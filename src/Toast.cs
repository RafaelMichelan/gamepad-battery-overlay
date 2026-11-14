// Notificação estilo "card" moderno: cantos arredondados com transparência real (per-pixel alpha),
// sombra suave, animação de entrada deslizando e saída com fade. O clique passa através dela.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class ToastWindow : Form
{
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; public SIZE(int x, int y) { cx = x; cy = y; } }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);

    // Glifos da fonte de ícones do Windows (Segoe Fluent Icons / Segoe MDL2 Assets)
    public const string GlyphGamepad = "";
    public const string GlyphBolt = "";
    public const string GlyphBatteryFull = "";
    public const string GlyphWarning = "";

    const int W = 344, H = 84, SH = 18, Gap = 10;   // card, margem da sombra, espaço entre cards
    const int InMs = 320, HoldMs = 4000, OutMs = 380, QuickOutMs = 160, Slide = 18;
    const int MaxToasts = 4;

    // Pilha das notificações na tela (índice 0 = mais perto do overlay). A posição de cada uma é
    // recalculada a cada quadro: quando uma sai ou o overlay muda de altura, as outras deslizam
    // para o lugar certo, sem nunca ficarem uma por cima da outra.
    static readonly System.Collections.Generic.List<ToastWindow> stack = new System.Collections.Generic.List<ToastWindow>();
    static string iconFont;
    /// <summary>Altura atual do overlay (0 se oculto): as notificações aparecem logo depois dele.</summary>
    public static int OverlayHeight;

    readonly Timer timer = new Timer { Interval = 15 };
    readonly string group, corner;
    DateTime t0 = DateTime.Now;
    IntPtr screenDc, memDc, hBmp, oldBmp;
    double curY = double.NaN;
    bool closing, leaving;
    DateTime leaveStart;
    int leaveMs;

    /// <summary>
    /// Mostra uma notificação. Se já existir uma do mesmo <paramref name="group"/> na tela, ela é
    /// atualizada no lugar (em vez de empilhar outra) e o tempo de exibição recomeça.
    /// </summary>
    public static void ShowToast(string glyph, Color accent, string title, string subtitle, int percent, bool charging, string corner, string group = null)
    {
        if (group != null)
            foreach (ToastWindow existing in stack)
                if (existing.group == group && !existing.leaving)
                {
                    existing.Replace(glyph, accent, title, subtitle, percent, charging);
                    return;
                }

        // Limite de notificações: as mais antigas saem rapidinho
        int alive = 0;
        foreach (ToastWindow s in stack) if (!s.leaving) alive++;
        for (int i = 0; i < stack.Count && alive >= MaxToasts; i++)
            if (!stack[i].leaving) { stack[i].BeginLeave(QuickOutMs); alive--; }
        // Em rajadas, as que estão saindo podem se acumular: nunca passa de MaxToasts + 1 cartões na tela
        while (stack.Count > MaxToasts)
        {
            ToastWindow oldest = stack.Find(t => t.leaving) ?? stack[0];
            oldest.Close(); // OnFormClosed tira da pilha
            if (stack.Contains(oldest)) stack.Remove(oldest);
        }

        var toast = new ToastWindow(glyph, accent, title, subtitle, percent, charging, corner, group);
        stack.Add(toast);
        toast.Show();
    }

    ToastWindow(string glyph, Color accent, string title, string subtitle, int percent, bool charging, string corner, string group)
    {
        this.group = group;
        this.corner = corner;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Size = new Size(W + 2 * SH, H + 2 * SH);
        Location = new Point(BaseX(), (int)TargetY() + DirY * Slide);

        screenDc = GetDC(IntPtr.Zero);
        memDc = CreateCompatibleDC(screenDc);
        SetBitmap(Render(glyph, accent, title, subtitle, percent, charging));
        timer.Tick += Animate;
    }

    void SetBitmap(Bitmap bmp)
    {
        using (bmp)
        {
            IntPtr nb = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr prev = SelectObject(memDc, nb);
            if (hBmp == IntPtr.Zero) oldBmp = prev; else DeleteObject(hBmp);
            hBmp = nb;
        }
    }

    void Replace(string glyph, Color accent, string title, string subtitle, int percent, bool charging)
    {
        SetBitmap(Render(glyph, accent, title, subtitle, percent, charging));
        dirty = true;
        // continua visível pelo tempo inteiro, sem repetir a animação de entrada
        if ((DateTime.Now - t0).TotalMilliseconds > InMs) t0 = DateTime.Now.AddMilliseconds(-InMs);
    }

    void BeginLeave(int ms)
    {
        if (leaving) return;
        leaving = true;
        leaveStart = DateTime.Now;
        leaveMs = ms;
    }

    int DirY { get { return corner.StartsWith("B") ? 1 : -1; } }

    int BaseX()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        return corner.EndsWith("L") ? wa.Left + 12 - SH : wa.Right - 12 - W - SH;
    }

    // Posição de destino pela ordem na pilha (logo abaixo do overlay, ou acima nos cantos de baixo)
    double TargetY()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        int idx = Math.Max(0, stack.IndexOf(this));
        int gap = (OverlayHeight > 0 ? OverlayHeight + Gap : 0) + idx * (H + Gap);
        return corner.StartsWith("B") ? wa.Bottom - 12 - gap - H - SH : wa.Top + 12 + gap - SH;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x80000 /* LAYERED */ | 0x20 /* TRANSPARENT */ | 0x80 /* TOOLWINDOW */ | 0x08000000 /* NOACTIVATE */ | 0x8 /* TOPMOST */;
            return cp;
        }
    }
    protected override bool ShowWithoutActivation { get { return true; } }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Present(0, (int)TargetY() + DirY * Slide);
        timer.Start();
    }

    static double EaseOut(double t) { t = Math.Max(0, Math.Min(1, t)); return 1 - Math.Pow(1 - t, 3); }

    void Animate(object sender, EventArgs e)
    {
        // desliza suavemente até a posição da pilha
        double target = TargetY();
        if (double.IsNaN(curY)) curY = target;
        else
        {
            double d = target - curY;
            curY = Math.Abs(d) < 0.5 ? target : curY + d * 0.22;
        }

        double ms = (DateTime.Now - t0).TotalMilliseconds;
        if (!leaving && ms >= InMs + HoldMs) BeginLeave(OutMs);

        double alpha = 1, offset = 0;
        if (ms < InMs)
        {
            double p = EaseOut(ms / InMs);
            alpha = p;
            offset = DirY * Slide * (1 - p);
        }
        if (leaving)
        {
            double p = Math.Min(1, (DateTime.Now - leaveStart).TotalMilliseconds / leaveMs);
            if (p >= 1) { Close(); return; }
            alpha = Math.Min(alpha, 1 - p);
            offset += DirY * 6 * p;
        }
        Present((byte)(255 * alpha), (int)Math.Round(curY + offset));
    }

    int lastAlpha = -1, lastY = int.MinValue, lastX = int.MinValue;
    bool dirty = true;   // conteúdo trocado (Replace): precisa redesenhar mesmo parado

    void Present(byte alpha, int y)
    {
        if (closing || IsDisposed) return;
        int x = BaseX();
        if (!dirty && alpha == lastAlpha && y == lastY && x == lastX) return; // nada mudou: não redesenha
        dirty = false; lastAlpha = alpha; lastY = y; lastX = x;
        var dst = new POINT(x, y);
        var size = new SIZE(Width, Height);
        var src = new POINT(0, 0);
        var blend = new BLENDFUNCTION { BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = alpha, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
        UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2 /* ULW_ALPHA */);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        closing = true;
        timer.Stop();
        timer.Dispose();
        if (memDc != IntPtr.Zero) { SelectObject(memDc, oldBmp); DeleteDC(memDc); memDc = IntPtr.Zero; }
        if (hBmp != IntPtr.Zero) { DeleteObject(hBmp); hBmp = IntPtr.Zero; }
        if (screenDc != IntPtr.Zero) { ReleaseDC(IntPtr.Zero, screenDc); screenDc = IntPtr.Zero; }
        stack.Remove(this);
        base.OnFormClosed(e);
        Dispose();
    }

    static string IconFont()
    {
        if (iconFont != null) return iconFont;
        foreach (string name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
            using (var f = new Font(name, 10f)) if (f.Name == name) return iconFont = name;
        return iconFont = "Segoe UI Symbol";
    }

    static GraphicsPath Rounded(RectangleF r, float rad)
    {
        var p = new GraphicsPath();
        float d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    Bitmap Render(string glyph, Color accent, string title, string subtitle, int percent, bool charging)
    {
        var bmp = new Bitmap(W + 2 * SH, H + 2 * SH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            // Sombra suave (camadas concêntricas, um pouco deslocada para baixo)
            for (int i = SH; i >= 1; i--)
            {
                int a = (int)(16 * Math.Pow(1.0 - (double)i / SH, 2.2)) + 1;
                using (var sp = Rounded(new RectangleF(SH - i, SH - i + 5, W + 2 * i, H + 2 * i), 14 + i))
                using (var sb = new SolidBrush(Color.FromArgb(a, 0, 0, 0))) g.FillPath(sb, sp);
            }

            var card = new RectangleF(SH, SH, W, H);
            using (var cp = Rounded(card, 14))
            {
                using (var bg = new LinearGradientBrush(card, Color.FromArgb(250, 36, 37, 44), Color.FromArgb(250, 24, 25, 30), 90f))
                    g.FillPath(bg, cp);

                // Brilho da cor de destaque vindo da esquerda
                g.SetClip(cp);
                using (var glowPath = new GraphicsPath())
                {
                    glowPath.AddEllipse(SH - 70, SH - 40, 190, H + 80);
                    using (var glow = new PathGradientBrush(glowPath))
                    {
                        glow.CenterColor = Color.FromArgb(70, accent);
                        glow.SurroundColors = new[] { Color.FromArgb(0, accent) };
                        g.FillPath(glow, glowPath);
                    }
                }
                g.ResetClip();

                using (var border = new Pen(Color.FromArgb(38, 255, 255, 255), 1f)) g.DrawPath(border, cp);
            }

            // Círculo do ícone
            var circle = new RectangleF(SH + 18, SH + (H - 46) / 2f, 46, 46);
            using (var cb = new SolidBrush(Color.FromArgb(55, accent))) g.FillEllipse(cb, circle);
            using (var cpen = new Pen(Color.FromArgb(120, accent), 1.2f)) g.DrawEllipse(cpen, circle);
            using (var gf = new Font(IconFont(), 17f))
            using (var gb = new SolidBrush(Lighten(accent, 0.25f)))
            {
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(glyph, gf, gb, new RectangleF(circle.X, circle.Y + 1, circle.Width, circle.Height), sf);
            }

            float tx = SH + 80, tw = W - 80 - 18;
            bool bar = percent >= 0;
            float ty = bar ? SH + 13 : SH + 20;
            using (var tf = new Font("Segoe UI Semibold", 11.5f))
            using (var tb = new SolidBrush(Color.FromArgb(245, 246, 248)))
                g.DrawString(title, tf, tb, new RectangleF(tx, ty, tw, 24), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });
            using (var sf2 = new Font("Segoe UI", 9.25f))
            using (var sb2 = new SolidBrush(Color.FromArgb(165, 170, 182)))
                g.DrawString(subtitle, sf2, sb2, new RectangleF(tx, ty + 24, tw, 20), new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap });

            if (bar)
            {
                // Barra de bateria
                var track = new RectangleF(tx, SH + H - 19, tw - 44, 6);
                using (var tp = Rounded(track, 3))
                using (var tbg = new SolidBrush(Color.FromArgb(45, 255, 255, 255))) g.FillPath(tbg, tp);
                float fw = Math.Max(6, track.Width * Math.Min(100, percent) / 100f);
                var fillR = new RectangleF(track.X, track.Y, fw, track.Height);
                using (var fp = Rounded(fillR, 3))
                using (var fb = new LinearGradientBrush(new RectangleF(fillR.X - 1, fillR.Y, fillR.Width + 2, fillR.Height), Lighten(accent, 0.2f), accent, 0f))
                    g.FillPath(fb, fp);
                using (var pf = new Font("Segoe UI Semibold", 9f))
                using (var pb = new SolidBrush(Color.FromArgb(230, 232, 236)))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    g.DrawString((charging ? "⚡" : "") + percent + "%", pf, pb, new RectangleF(track.Right, track.Y - 8, 44, 22), sf);
                }
            }
        }
        return bmp;
    }

    static Color Lighten(Color c, float k)
    {
        return Color.FromArgb(c.A, (int)(c.R + (255 - c.R) * k), (int)(c.G + (255 - c.G) * k), (int)(c.B + (255 - c.B) * k));
    }
}
