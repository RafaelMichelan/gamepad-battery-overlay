// Integração opcional com o RivaTuner Statistics Server (RTSS): mostra a bateria DENTRO do jogo, inclusive em
// tela cheia exclusiva (onde nenhuma janela do Windows aparece). Usa o canal oficial de texto do RTSS (a memória
// compartilhada "RTSSSharedMemoryV2", o mesmo que HWiNFO e AIDA64 usam): o programa só escreve um texto no
// espaço dele, e quem desenha no jogo é o próprio RTSS. Nada é injetado no jogo por este programa.
// Sem o RTSS aberto, nada acontece.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public class RtssOsd
{
    public const string DefaultMapping = "RTSSSharedMemoryV2";
    public const string Owner = "GamepadBatteryOverlay";
    const int Signature = 0x52545353;              // "RTSS"
    const int OffVersion = 4, OffOsdEntrySize = 20, OffOsdArrOffset = 24, OffOsdArrSize = 28, OffOsdFrame = 32, OffBusy = 36;
    const int EntryOsd = 0, EntryOwner = 256, EntryOsdEx = 512;   // szOSD[256], szOSDOwner[256], szOSDEx[4096]
    const int OsdLen = 256, OwnerLen = 256, OsdExLen = 4096;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenFileMapping(uint access, bool inherit, string name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr MapViewOfFile(IntPtr h, uint access, uint offHigh, uint offLow, UIntPtr bytes);
    [DllImport("kernel32.dll")] static extern bool UnmapViewOfFile(IntPtr p);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    const uint FILE_MAP_ALL_ACCESS = 0xF001F;

    readonly string mapping;
    string lastText;

    public RtssOsd() : this(DefaultMapping) { }
    public RtssOsd(string mappingName) { mapping = mappingName; }

    /// <summary>O RTSS está aberto (a memória compartilhada existe e é válida)?</summary>
    public bool IsAvailable()
    {
        bool ok = false;
        With(p => { ok = true; return true; });
        return ok;
    }

    /// <summary>Escreve (ou atualiza) o nosso texto no RTSS. Devolve false se o RTSS não estiver aberto ou estiver ocupado.</summary>
    public bool Update(string text)
    {
        text = text ?? "";
        bool ok = With(p =>
        {
            IntPtr e = FindEntry(p, true);
            if (e == IntPtr.Zero) return false;
            int ver = Marshal.ReadInt32(p, OffVersion);
            if (ver >= 0x00020007) WriteString(e, EntryOsdEx, OsdExLen, text);
            else WriteString(e, EntryOsd, OsdLen, text);
            Marshal.WriteInt32(p, OffOsdFrame, Marshal.ReadInt32(p, OffOsdFrame) + 1);
            return true;
        });
        if (ok) lastText = text;
        return ok;
    }

    /// <summary>Só escreve se o texto mudou (o RTSS redesenha a cada quadro, não precisa ficar reescrevendo).</summary>
    public bool UpdateIfChanged(string text)
    {
        if (text == lastText && lastText != null) return true;
        return Update(text);
    }

    /// <summary>Apaga o nosso espaço no RTSS (ao sair, ou quando a integração é desligada).</summary>
    public void Release()
    {
        With(p =>
        {
            IntPtr e;
            while ((e = FindEntry(p, false)) != IntPtr.Zero)
            {
                int size = Marshal.ReadInt32(p, OffOsdEntrySize);
                int clear = Math.Min(size, EntryOsdEx + OsdExLen);
                for (int i = 0; i < clear; i += 4) Marshal.WriteInt32(e, i, 0);
                Marshal.WriteInt32(p, OffOsdFrame, Marshal.ReadInt32(p, OffOsdFrame) + 1);
            }
            return true;
        });
        lastText = null;
    }

    // Abre a memória do RTSS, confere a assinatura, segura a trava "ocupado" (RTSS 2.14+) e roda a ação.
    bool With(Func<IntPtr, bool> action)
    {
        IntPtr h = OpenFileMapping(FILE_MAP_ALL_ACCESS, false, mapping);
        if (h == IntPtr.Zero) return false;
        IntPtr p = MapViewOfFile(h, FILE_MAP_ALL_ACCESS, 0, 0, UIntPtr.Zero);
        try
        {
            if (p == IntPtr.Zero) return false;
            if (Marshal.ReadInt32(p, 0) != Signature) return false;
            int ver = Marshal.ReadInt32(p, OffVersion);
            if (ver < 0x00020000) return false;
            bool locked = false;
            if (ver >= 0x0002000E)
            {
                if (!TryLock(p)) return false; // o RTSS está mexendo agora: tenta de novo no próximo ciclo
                locked = true;
            }
            try { return action(p); }
            finally { if (locked) Unlock(p); }
        }
        catch { return false; }
        finally
        {
            if (p != IntPtr.Zero) UnmapViewOfFile(p);
            CloseHandle(h);
        }
    }

    // Trava "ocupado" do RTSS (campo dwBusy): o mesmo bit que o exemplo oficial marca com uma operação atômica.
    static unsafe bool TryLock(IntPtr p)
    {
        int* busy = (int*)((byte*)p.ToPointer() + OffBusy);
        return Interlocked.CompareExchange(ref *busy, 1, 0) == 0;
    }

    static unsafe void Unlock(IntPtr p)
    {
        int* busy = (int*)((byte*)p.ToPointer() + OffBusy);
        Thread.VolatileWrite(ref *busy, 0);
    }

    // Procura o espaço que é nosso; com claim=true, pega o primeiro livre se ainda não tivermos um.
    // O espaço 0 é do próprio RTSS e nunca é usado (igual ao exemplo oficial).
    static IntPtr FindEntry(IntPtr p, bool claim)
    {
        int size = Marshal.ReadInt32(p, OffOsdEntrySize);
        int arr = Marshal.ReadInt32(p, OffOsdArrOffset);
        int count = Marshal.ReadInt32(p, OffOsdArrSize);
        if (size < EntryOwner + OwnerLen || count <= 1) return IntPtr.Zero;
        for (int pass = 0; pass < (claim ? 2 : 1); pass++)
            for (int i = 1; i < count; i++)
            {
                IntPtr e = new IntPtr(p.ToInt64() + arr + (long)i * size);
                string owner = ReadString(e, EntryOwner, OwnerLen);
                if (pass == 1 && owner.Length == 0) { WriteString(e, EntryOwner, OwnerLen, Owner); owner = Owner; }
                if (owner == Owner) return e;
            }
        return IntPtr.Zero;
    }

    static string ReadString(IntPtr e, int offset, int max)
    {
        var b = new byte[max];
        Marshal.Copy(new IntPtr(e.ToInt64() + offset), b, 0, max);
        int n = Array.IndexOf(b, (byte)0);
        return Encoding.Default.GetString(b, 0, n < 0 ? max : n);
    }

    static void WriteString(IntPtr e, int offset, int max, string s)
    {
        byte[] src = Encoding.Default.GetBytes(s);
        var b = new byte[max];
        Array.Copy(src, b, Math.Min(src.Length, max - 1)); // sempre termina com zero
        Marshal.Copy(b, 0, new IntPtr(e.ToInt64() + offset), max);
    }

    // ------------------------------------------------------------------ texto mostrado no jogo

    /// <summary>
    /// Monta o texto do RTSS a partir das linhas do overlay: "PS5 75%   XBOX 77%", com a % na cor do nível
    /// (tags de cor do próprio RTSS), mais o último aviso por alguns segundos. Sem linhas e sem aviso = vazio.
    /// </summary>
    public static string Format(IList<BatteryState> lines, string notice)
    {
        var parts = new List<string>();
        foreach (BatteryState s in lines)
        {
            if (!s.Ok) continue;
            if (s.NoBattery) parts.Add(s.Tag + " " + BatteryRules.NoBatteryLabel(s));
            else parts.Add(s.Tag + " <C=" + Hex(LevelRgb(s)) + ">" + s.Percent + "%" + (s.Charging ? "+" : "") + "<C>");
        }
        string text = string.Join("   ", parts.ToArray());
        if (!string.IsNullOrEmpty(notice)) text = text.Length > 0 ? text + "\n" + notice : notice;
        return text;
    }

    /// <summary>Mesmas cores do overlay (verde, amarelo, vermelho; azul carregando).</summary>
    public static int LevelRgb(BatteryState s)
    {
        if (s.Charging || s.Full) return 0x50AAFF;
        if (s.Percent > 50) return 0x5AD26E;
        if (s.Percent > 20) return 0xF0BE3C;
        return 0xF05046;
    }

    static string Hex(int rgb) { return rgb.ToString("X6"); }
}

/// <summary>O Windows diz que um jogo Direct3D está em tela cheia exclusiva? (onde janelas não aparecem)</summary>
public static class Fullscreen
{
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

    public static bool D3DExclusive()
    {
        try { int s; return SHQueryUserNotificationState(out s) == 0 && s == QUNS_RUNNING_D3D_FULL_SCREEN; }
        catch { return false; }
    }
}
