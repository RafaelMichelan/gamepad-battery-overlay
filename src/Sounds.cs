// Efeitos sonoros das notificações, gerados por síntese (nenhum arquivo de som externo).
// Cada som é uma sequência de notas curtas tipo "sino": tom fundamental + harmônicos que somem rápido,
// ataque de 4 ms e decaimento exponencial. Volume baixo de propósito (sutil).
using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

public enum Sfx { Connect, Disconnect, Charging, Full, Unplug, Low, Hide, Show }

public static class SoundFx
{
    const int Rate = 44100;
    /// <summary>Pico do som mais alto (0..1). 0.16 ≈ -16 dBFS: perceptível, mas discreto.</summary>
    public const double MaxPeak = 0.16;

    static readonly Dictionary<Sfx, byte[]> cache = new Dictionary<Sfx, byte[]>();
    static readonly object sync = new object();
    static SoundPlayer player;          // mantém o som vivo enquanto toca
    static MemoryStream stream;
    static DateTime lastAt = DateTime.MinValue;
    static Sfx lastKind;

    struct Note
    {
        public double Start, Freq, Decay, Amp;
        public Note(double start, double freq, double decay, double amp) { Start = start; Freq = freq; Decay = decay; Amp = amp; }
    }

    // Frequências (Hz) das notas usadas
    const double A4 = 440.00, E5 = 659.25, A5 = 880.00, B5 = 987.77, C6 = 1046.50, D6 = 1174.66,
                 E6 = 1318.51, G6 = 1567.98, A6 = 1760.00, C7 = 2093.00;

    static Note[] Design(Sfx k, out double gain)
    {
        gain = 1.0;
        switch (k)
        {
            case Sfx.Connect:      // subindo: "plim-plim"
                return new[] { new Note(0, E5, .13, 1), new Note(.085, B5, .26, 1) };
            case Sfx.Disconnect:   // descendo (espelho do conectar)
                gain = 0.9;
                return new[] { new Note(0, B5, .13, 1), new Note(.095, E5, .28, .9) };
            case Sfx.Charging:     // arpejo rápido subindo (energia entrando)
                gain = 0.85;
                return new[] { new Note(0, C6, .10, .8), new Note(.06, E6, .10, .8), new Note(.12, G6, .22, .9) };
            case Sfx.Full:         // acorde aberto, mais longo
                gain = 0.8;
                return new[] { new Note(0, C6, .30, .6), new Note(.03, E6, .30, .55), new Note(.06, G6, .34, .55), new Note(.09, C7, .38, .45) };
            case Sfx.Unplug:       // duas notas caindo, curtas
                gain = 0.8;
                return new[] { new Note(0, D6, .09, .7), new Note(.07, A5, .20, .8) };
            case Sfx.Low:          // dois toques graves: alerta, mas sem susto
                return new[] { new Note(0, A4, .11, 1), new Note(.22, A4, .18, 1) };
            case Sfx.Hide:         // "tic" descendo
                gain = 0.55;
                return new[] { new Note(0, A6, .045, .5), new Note(.035, E6, .07, .45) };
            case Sfx.Show:         // "tic" subindo
                gain = 0.55;
                return new[] { new Note(0, E6, .045, .45), new Note(.035, A6, .07, .5) };
        }
        return new Note[0];
    }

    /// <summary>Gera o som como um arquivo WAV (PCM 16 bits, mono, 44,1 kHz) em memória.</summary>
    public static byte[] Wav(Sfx k)
    {
        double gain;
        Note[] notes = Design(k, out gain);
        double len = 0;
        foreach (Note n in notes) len = Math.Max(len, n.Start + n.Decay * 7);
        len = Math.Min(len, 3.0);
        int total = (int)(len * Rate);
        var buf = new double[total];

        foreach (Note n in notes)
        {
            int start = (int)(n.Start * Rate);
            for (int i = 0; start + i < total; i++)
            {
                double t = (double)i / Rate;
                double env = (1 - Math.Exp(-t / 0.004)) * Math.Exp(-t / n.Decay);
                if (env < 1e-5 && t > 0.01) break;
                double w = 2 * Math.PI * n.Freq * t;
                double s = Math.Sin(w)
                         + 0.25 * Math.Sin(2 * w) * Math.Exp(-t / (n.Decay * 0.5))
                         + 0.08 * Math.Sin(3 * w) * Math.Exp(-t / (n.Decay * 0.3));
                buf[start + i] += n.Amp * env * s;
            }
        }

        // normaliza para o volume-alvo
        double peak = 1e-9;
        foreach (double v in buf) peak = Math.Max(peak, Math.Abs(v));
        double scale = MaxPeak * gain / peak;
        // corta o silêncio do fim: onde o som já caiu abaixo de -30 dB do pico (com o volume já baixo, inaudível)
        int end = total - 1;
        while (end > 0 && Math.Abs(buf[end]) < peak * 0.03) end--;
        total = Math.Min(total, end + Rate / 50);
        // e suaviza os últimos 15 ms para não estalar
        int fade = Math.Min(total, Rate * 15 / 1000);
        for (int i = 0; i < total; i++)
        {
            buf[i] *= scale;
            if (i >= total - fade) buf[i] *= (double)(total - i) / fade;
        }

        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            int dataLen = total * 2;
            bw.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); bw.Write(36 + dataLen);
            bw.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
            bw.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' }); bw.Write(16);
            bw.Write((short)1); bw.Write((short)1); bw.Write(Rate); bw.Write(Rate * 2); bw.Write((short)2); bw.Write((short)16);
            bw.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); bw.Write(dataLen);
            for (int i = 0; i < total; i++) bw.Write((short)Math.Round(Math.Max(-1, Math.Min(1, buf[i])) * 32767));
            bw.Flush();
            return ms.ToArray();
        }
    }

    static byte[] Get(Sfx k)
    {
        lock (sync)
        {
            byte[] w;
            if (!cache.TryGetValue(k, out w)) cache[k] = w = Wav(k);
            return w;
        }
    }

    /// <summary>Toca o som sem bloquear. Um som novo interrompe o anterior.</summary>
    public static void Play(Sfx k)
    {
        try
        {
            lock (sync)
            {
                // o mesmo som repetido em menos de 60 ms (rajada) não toca de novo
                if (k == lastKind && (DateTime.UtcNow - lastAt).TotalMilliseconds < 60) return;
                lastKind = k; lastAt = DateTime.UtcNow;
                var ms = new MemoryStream(Get(k), false);
                var p = new SoundPlayer(ms);
                p.Play();
                if (player != null) player.Dispose();
                if (stream != null) stream.Dispose();
                player = p; stream = ms;
            }
        }
        catch { } // sem placa de som / áudio indisponível: segue sem som
    }

    /// <summary>Gera os sons na hora em que o programa abre (assim o primeiro aviso não atrasa).</summary>
    public static void Preload()
    {
        foreach (Sfx k in Enum.GetValues(typeof(Sfx))) Get(k);
    }

    /// <summary>Salva todos os sons como .wav numa pasta (para ouvir / documentação).</summary>
    public static void Export(string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (Sfx k in Enum.GetValues(typeof(Sfx)))
            File.WriteAllBytes(Path.Combine(dir, k.ToString().ToLowerInvariant() + ".wav"), Get(k));
    }
}
