// Ícone da bandeja (opção "B"): um controle desenhado pixel a pixel com uma barra de bateria embaixo,
// que enche conforme a % e muda de cor. Desenho próprio para 16 px e para 24 px (escala de tela maior),
// nítido nos dois. Na barra de tarefas clara o controle fica escuro; na escura, claro.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Win32;

public static class TrayIcon
{
    static readonly string[] Pad16 = {
        "..XXX......XXX..",
        ".XXXXXXXXXXXXXX.",
        "XXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXX",
        "XXXXXX....XXXXXX",
        "XXXX........XXXX",
        ".XX..........XX." };
    static readonly string[] Pad24 = {
        "...XXXXX........XXXXX...",
        "..XXXXXXXXXXXXXXXXXXXX..",
        ".XXXXXXXXXXXXXXXXXXXXXX.",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
        "XXXXXXXXXXXXXXXXXXXXXXXX",
        "XXXXXXXX........XXXXXXXX",
        "XXXXXX............XXXXXX",
        ".XXXXX............XXXXX.",
        "..XXX..............XXX.." };

    public static readonly Color DarkInk = Color.FromArgb(32, 32, 36);     // controle na barra clara
    public static readonly Color LightInk = Color.FromArgb(240, 242, 246); // controle na barra escura

    /// <summary>A barra de tarefas do Windows está no tema claro?</summary>
    public static bool TaskbarIsLight()
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                return v is int && (int)v == 1;
            }
        }
        catch { return false; }
    }

    /// <summary>Largura da parte colorida da barra (em pixels) para uma porcentagem: 0% = vazia, qualquer valor acima de 0 = pelo menos 1 px.</summary>
    public static int FillWidth(int innerWidth, int pct)
    {
        pct = Math.Max(0, Math.Min(100, pct));
        int w = (int)Math.Round(innerWidth * pct / 100.0);
        if (pct > 0 && w == 0) w = 1;
        return w;
    }

    /// <summary>
    /// Desenha o ícone. size = tamanho pedido pelo Windows (16, 20, 24...). pct &lt; 0 = sem controle com bateria
    /// (desenha a barra vazia, com o controle apagado). fill = cor da barra (cor do nível).
    /// </summary>
    public static Bitmap Draw(int size, int pct, Color fill, bool lightBar)
    {
        if (size < 16) size = 16;
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        bool big = size >= 24;
        int box = big ? 24 : 16;
        int ox = (size - box) / 2, oy = (size - box) / 2;
        Color ink = lightBar ? DarkInk : LightInk;
        if (pct < 0) ink = Color.FromArgb(150, ink); // nenhum controle: tudo mais apagado

        string[] pad = big ? Pad24 : Pad16;
        for (int y = 0; y < pad.Length; y++)
            for (int x = 0; x < pad[y].Length; x++)
                if (pad[y][x] == 'X') bmp.SetPixel(ox + x, oy + y, ink);

        // barra de bateria: contorno, "polo" à direita e preenchimento
        int top = big ? 15 : 10, bot = box - 1, right = big ? 20 : 13;
        Rect(bmp, ox, oy, 0, top, right, top, ink);
        Rect(bmp, ox, oy, 0, bot, right, bot, ink);
        Rect(bmp, ox, oy, 0, top, 0, bot, ink);
        Rect(bmp, ox, oy, right, top, right, bot, ink);
        Rect(bmp, ox, oy, right + 1, big ? 17 : 12, box - 1, big ? 21 : 13, ink);
        if (pct > 0)
        {
            int x0 = big ? 2 : 1, x1 = big ? right - 2 : right - 1;
            int y0 = big ? top + 2 : top + 1, y1 = big ? bot - 2 : bot - 1;
            int w = FillWidth(x1 - x0 + 1, pct);
            Rect(bmp, ox, oy, x0, y0, x0 + w - 1, y1, fill);
        }
        return bmp;
    }

    static void Rect(Bitmap b, int ox, int oy, int x0, int y0, int x1, int y1, Color c)
    {
        for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++) b.SetPixel(ox + x, oy + y, c);
    }
}
