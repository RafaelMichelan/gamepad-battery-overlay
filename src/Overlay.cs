// Overlay da bateria (DualSense e DualShock 4): janelinha sempre no topo, uma linha por controle,
// com o clique passando através dela, mais o ícone na bandeja com o menu de opções.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

public class OverlayForm : Form
{
    const string RegKey = @"Software\GamepadBatteryOverlay";   // até a v1.5: Software\DualSenseOverlay (migrado, ver Settings.cs)
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "GamepadBatteryOverlay";
    const int LineH = 32, OverlayW = 128;

    static readonly Color AccentBlue = Color.FromArgb(0x3D, 0x8B, 0xFF);
    static readonly Color AccentRed = Color.FromArgb(0xFF, 0x5A, 0x5F);
    static readonly Color AccentGreen = Color.FromArgb(0x34, 0xC7, 0x59);
    static readonly Color AccentAmber = Color.FromArgb(0xF5, 0xA6, 0x23);
    static readonly Color AccentGray = Color.FromArgb(0x8A, 0x8F, 0x98);

    [StructLayout(LayoutKind.Sequential)]
    struct DEV_BROADCAST_DEVICEINTERFACE { public int dbcc_size; public int dbcc_devicetype; public int dbcc_reserved; public Guid dbcc_classguid; public short dbcc_name; }
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr RegisterDeviceNotification(IntPtr h, ref DEV_BROADCAST_DEVICEINTERFACE f, int flags);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

    // ---- estado (acessado só na thread da interface, exceto activePaths) ----
    readonly object sync = new object();
    readonly HashSet<string> activePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // leitores rodando
    readonly Dictionary<string, BatteryState> devices = new Dictionary<string, BatteryState>(StringComparer.OrdinalIgnoreCase); // por caminho HID
    Dictionary<string, BatteryState> current = new Dictionary<string, BatteryState>(); // por controle (MAC)
    readonly Dictionary<string, BatteryState> known = new Dictionary<string, BatteryState>(); // último estado avisado
    readonly Dictionary<string, System.Windows.Forms.Timer> pendingDisconnect = new Dictionary<string, System.Windows.Forms.Timer>();
    readonly HashSet<string> lowWarned = new HashSet<string>();
    readonly List<string> order = new List<string>();   // ordem de conexão = ordem das linhas
    List<BatteryState> lines = new List<BatteryState>();
    volatile int removalGen;

    // ---- opções ----
    string corner;
    bool onlyLow, notify, forceLow, userHidden, allowShow, sounds;
    static bool mute;          // --mudo: sem som (usado pela suíte de testes)
    ToolStripMenuItem miSounds;
    volatile bool btnToggle;
    static bool simulateLow;   // --teste-bateria-baixa: finge 5% e começa com o overlay desligado

    NotifyIcon tray;
    IntPtr trayIconHandle = IntPtr.Zero;
    ToolStripMenuItem miShow, miOnlyLow, miForceLow, miNotify, miBtn, miStartup;
    readonly ToolStripMenuItem[] cornerItems = new ToolStripMenuItem[4];
    static readonly string[] Corners = { "TR", "TL", "BR", "BL" };
    static string[] CornerNames
    {
        get
        {
            return Lang.En ? new[] { "Top right", "Top left", "Bottom right", "Bottom left" }
                           : new[] { "Superior direito", "Superior esquerdo", "Inferior direito", "Inferior esquerdo" };
        }
    }

    public OverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(20, 20, 24);
        Opacity = 0.85;
        Size = new Size(OverlayW, LineH);
        DoubleBuffered = true;

        using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
        {
            corner = (string)k.GetValue("Corner", "TR");
            onlyLow = (int)k.GetValue("OnlyLow", 0) == 1;
            notify = (int)k.GetValue("Notify", 1) == 1;
            sounds = (int)k.GetValue("Sounds", 1) == 1;
            startupNotice = (int)k.GetValue("StartupNotice", 1) == 1;
            btnToggle = (int)k.GetValue("MuteToggle", 1) == 1;
            forceLow = (int)k.GetValue("ForceLow", 1) == 1;
            PadHid.LegacyScale = (int)k.GetValue("BatteryScale", 0) == 1; // 0 = corrigida (0-8), 1 = antiga (0-10)
            Lang.Apply(langOverride ?? (k.GetValue("Language", "") as string)); // "pt", "en" ou vazio (= idioma do Windows)
            rtssMode = rtssModeOverride >= 0 ? rtssModeOverride : (int)k.GetValue("Rtss", RtssFullscreenOnly); // 0 desligado, 1 só tela cheia exclusiva, 2 sempre
            if (rtssMode < RtssOff || rtssMode > RtssAlways) rtssMode = RtssFullscreenOnly;
        }

        BuildTray();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged; // tema claro/escuro: redesenha o ícone
        ApplySize(1);

        var topTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        topTimer.Tick += (s, e) => { if (Visible) SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, 0x0013 /* NOSIZE|NOMOVE|NOACTIVATE */); };
        topTimer.Start();

        rtss = new RtssOsd(rtssMappingOverride ?? RtssOsd.DefaultMapping);
        var rtssTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        rtssTimer.Tick += (s, e) => RtssTick();
        rtssTimer.Start();

        CreateHandle(); // precisa existir antes das threads usarem BeginInvoke

        // varredura de garantia (a principal vem do aviso WM_DEVICECHANGE)
        var scanTimer = new System.Windows.Forms.Timer { Interval = 10000 };
        scanTimer.Tick += (s, e) => Scan();
        scanTimer.Start();
        watchdog = new System.Threading.Timer(Watchdog, null, 250, 250);
        SoundFx.Preload(); // gera os sons agora (poucos ms) para o primeiro aviso não atrasar
        new Thread(XboxLoop) { IsBackground = true, Name = "xbox" }.Start();
        new Thread(XboxButtonsLoop) { IsBackground = true, Name = "xbox botões" }.Start();
        Scan();
    }

    // Janela que não rouba foco, não aparece no Alt+Tab e deixa o mouse passar.
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

    protected override void SetVisibleCore(bool value)
    {
        if (!allowShow) value = false; // começa escondido até a primeira leitura
        base.SetVisibleCore(value);
    }

    void Post(Action a) { try { if (!IsDisposed) BeginInvoke(a); } catch { } }

    // =====================================================================================
    //  Leitura dos controles: uma thread por controle conectado, lendo sem parar.
    //  A leitura fica bloqueada esperando o próximo pacote, então o custo de CPU é desprezível.
    // =====================================================================================

    void Scan()
    {
        xboxKick.Set(); // Xbox: confere na hora também
        ThreadPool.QueueUserWorkItem(_ =>
        {
            List<PadDevice> list;
            try { list = PadHid.Enumerate(); } catch { return; }
            foreach (PadDevice d in list)
            {
                lock (sync) { if (!activePaths.Add(d.Path)) continue; }
                PadDevice dev = d;
                new Thread(() => ReaderLoop(dev)) { IsBackground = true, Name = "pad " + dev.Tag }.Start();
            }
        });
    }

    // ---- vigia de desconexão: roda 4x por segundo, fora das threads de leitura ----
    class ReaderInfo
    {
        public string Path;
        public PadDevice Dev;
        public IntPtr Thread;          // handle nativo da thread de leitura (para cancelar a leitura)
        public long LastTick;          // último pacote recebido (ms do relógio)
        public int SeenGen;
        public volatile bool Gone;
    }
    readonly List<ReaderInfo> readers = new List<ReaderInfo>();
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    System.Threading.Timer watchdog;
    int watchdogBusy;

    [DllImport("kernel32.dll")] static extern IntPtr OpenThread(int access, bool inherit, int id);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    [DllImport("kernel32.dll")] static extern bool CancelSynchronousIo(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    void Watchdog(object _)
    {
        if (Interlocked.Exchange(ref watchdogBusy, 1) == 1) return;
        try
        {
            ReaderInfo[] list;
            lock (sync) list = readers.ToArray();
            long now = clock.ElapsedMilliseconds;
            int gen = removalGen;
            foreach (ReaderInfo r in list)
            {
                if (r.Gone) { CancelSynchronousIo(r.Thread); continue; }
                long silent = now - Interlocked.Read(ref r.LastTick);
                bool removalHint = r.SeenGen != gen;
                if ((removalHint && silent > 250) || silent > 3000)
                {
                    r.SeenGen = gen;
                    if (!PadHid.IsPresent(r.Path)) { r.Gone = true; CancelSynchronousIo(r.Thread); }
                    else if (silent > 3000)
                    {
                        // presente mas mudo: pede o relatório completo de novo (por um handle à parte)
                        using (SafeFileHandle h2 = PadHid.Open(r.Path, false)) if (h2 != null) PadHid.EnableFullReports(h2, r.Dev);
                        Interlocked.Exchange(ref r.LastTick, now);
                    }
                }
            }
        }
        catch { }
        finally { Interlocked.Exchange(ref watchdogBusy, 0); }
    }

    void ReaderLoop(PadDevice d)
    {
        var info = new ReaderInfo
        {
            Path = d.Path, Dev = d, SeenGen = removalGen, LastTick = clock.ElapsedMilliseconds,
            Thread = OpenThread(0x0001 /* THREAD_TERMINATE: exigido por CancelSynchronousIo */, false, GetCurrentThreadId())
        };
        lock (sync) readers.Add(info);
        try
        {
            SafeFileHandle h = PadHid.Open(d.Path, false);
            if (h == null) return;
            PadHid.EnableFullReports(h, d);
            string key = PadHid.GetIdentity(h, d) ?? d.Path;
            using (var fs = new FileStream(h, FileAccess.Read, d.InLen, false))
            {
                byte[] buf = new byte[d.InLen];
                bool prevBtn = true;              // ignora se já estiver apertado ao conectar
                int lastBat = -1;
                BatteryState posted = null, cand = null;
                DateTime candSince = DateTime.MinValue;
                int wrong = 0;
                while (!info.Gone)
                {
                    int n = fs.Read(buf, 0, buf.Length);   // bloqueia até o próximo pacote (sem gastar CPU)
                    Interlocked.Exchange(ref info.LastTick, clock.ElapsedMilliseconds);
                    if (n < d.MinLength || buf[0] != d.ReportId)
                    {
                        if (++wrong % 250 == 0) PadHid.EnableFullReports(h, d); // voltou ao modo simples? pede de novo
                        continue;
                    }

                    // Pacote igual ao último (bateria e atalho iguais): nada a fazer, nem conferir o CRC.
                    // Os controles mandam centenas de pacotes por segundo; isso mantém o uso de CPU perto de zero.
                    int bat = buf[d.BatteryIndex];
                    bool pressed = PadHid.TogglePressed(d, buf);
                    if (bat == lastBat && pressed == prevBtn && cand == null) continue;
                    if (d.Bluetooth && !PadHid.CheckCrc(buf)) continue;
                    wrong = 0;

                    if (pressed && !prevBtn) Post(ToggleByButton);
                    prevBtn = pressed;
                    lastBat = bat;

                    BatteryState s = PadHid.Decode(d, buf);
                    s.Key = key;
                    if (simulateLow && s.Ok && s.Status == 0) s.Percent = 5;
                    if (posted == null) { posted = s; Post(() => OnReading(s)); continue; }
                    if (s.SameReading(posted)) { cand = null; continue; }
                    // só aceita uma mudança se ela se mantiver por 1 s (evita oscilação entre dois níveis)
                    if (cand == null || !s.SameReading(cand)) { cand = s; candSince = DateTime.UtcNow; continue; }
                    if ((DateTime.UtcNow - candSince).TotalMilliseconds >= 1000)
                    {
                        posted = s; cand = null;
                        Post(() => OnReading(s));
                    }
                }
            }
        }
        catch { }
        finally
        {
            lock (sync) { readers.Remove(info); activePaths.Remove(d.Path); }
            if (info.Thread != IntPtr.Zero) CloseHandle(info.Thread);
            Post(() => OnRemoved(d.Path));
        }
    }

    // =====================================================================================
    //  Controles Xbox: uma thread só, que confere a cada 2 s (e na hora, quando o Windows avisa
    //  que um dispositivo entrou ou saiu). A bateria do Bluetooth é lida a cada 30 s: o próprio
    //  controle só atualiza esse valor de vez em quando. Nada é aberto no controle.
    // =====================================================================================

    const int XboxPollMs = 2000, XboxBatteryMs = 30000;
    readonly AutoResetEvent xboxKick = new AutoResetEvent(false);
    static string langOverride; // --idioma pt|en
    static bool fakeXboxUsb;   // --teste-xbox-usb: finge um Xbox no cabo (para testar o "USB mode")

    void XboxLoop()
    {
        var last = new Dictionary<string, XboxPad>();
        long lastBattery = long.MinValue / 2;
        while (!IsDisposed)
        {
            try
            {
                long now = clock.ElapsedMilliseconds;
                bool readBat = now - lastBattery >= XboxBatteryMs;
                List<XboxPad> pads = XboxHid.Enumerate(readBat, last);
                if (readBat) lastBattery = now;
                xboxSlots = XboxHid.RealXboxSlots();
                if (fakeXboxUsb) pads.Add(new XboxPad { Key = "xbox-usb-teste", Path = "xinput:teste", Pid = 0x0B12, Bluetooth = false, Slot = 9 });
                var next = new Dictionary<string, XboxPad>();
                var states = new List<BatteryState>();
                foreach (XboxPad x in pads)
                {
                    next[x.Key] = x;
                    BatteryState s = x.ToState();
                    if (simulateLow && BatteryRules.ShowsBattery(s)) s.Percent = 5;
                    states.Add(s);
                }
                last = next;
                Post(() => OnXbox(states));
            }
            catch { }
            xboxKick.WaitOne(XboxPollMs);
        }
    }

    // Atalho L3+R3 no Xbox: lê os botões pelo XInput (só leitura) 20x por segundo, só nas posições
    // que têm um Xbox de verdade. Sem Xbox conectado, só confere de novo a cada 1 s.
    volatile int[] xboxSlots = new int[0];

    void XboxButtonsLoop()
    {
        var prev = new Dictionary<int, bool>();
        while (!IsDisposed)
        {
            int[] slots = xboxSlots;
            if (slots.Length == 0 || !btnToggle) { prev.Clear(); Thread.Sleep(1000); continue; }
            foreach (int slot in slots)
            {
                int b = XboxHid.GetButtons(slot);
                bool pressed = b >= 0 && XboxHid.TogglePressed((ushort)b);
                bool was;
                if (!prev.TryGetValue(slot, out was)) was = true; // ignora se já estiver apertado ao conectar
                if (pressed && !was) Post(ToggleByButton);
                prev[slot] = pressed;
            }
            foreach (int k in new List<int>(prev.Keys)) if (Array.IndexOf(slots, k) < 0) prev.Remove(k);
            Thread.Sleep(50);
        }
    }

    void OnXbox(List<BatteryState> list)
    {
        bool changed = false;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BatteryState s in list) paths.Add(s.Path);
        foreach (KeyValuePair<string, BatteryState> kv in new List<KeyValuePair<string, BatteryState>>(devices))
            if (kv.Value.IsXbox && !paths.Contains(kv.Key)) { devices.Remove(kv.Key); changed = true; }
        foreach (BatteryState s in list)
        {
            BatteryState old;
            if (!devices.TryGetValue(s.Path, out old) || !old.SameReading(s)) { devices[s.Path] = s; changed = true; }
        }
        if (changed) Rebuild();
    }

    // Aviso instantâneo do Windows quando um dispositivo HID aparece ou some
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        var f = new DEV_BROADCAST_DEVICEINTERFACE();
        f.dbcc_size = Marshal.SizeOf(f);
        f.dbcc_devicetype = 5; // DBT_DEVTYP_DEVICEINTERFACE
        f.dbcc_classguid = HidGuid;
        RegisterDeviceNotification(Handle, ref f, 0);
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_DEVICECHANGE = 0x0219, DBT_ARRIVAL = 0x8000, DBT_REMOVE = 0x8004;
        if (m.Msg == WM_DEVICECHANGE && m.LParam != IntPtr.Zero)
        {
            int ev = m.WParam.ToInt32();
            if (ev == DBT_ARRIVAL || ev == DBT_REMOVE)
            {
                string name = Marshal.PtrToStringUni(new IntPtr(m.LParam.ToInt64() + 28)) ?? "";
                string ln = name.ToLowerInvariant();
                if (ln.Contains("054c") || ln.Contains("045e")) // Sony ou Microsoft (Xbox)
                {
                    if (ev == DBT_REMOVE) removalGen++;
                    Scan();
                    // o driver pode demorar um instante para liberar o relatório: tenta de novo logo depois
                    var t = new System.Windows.Forms.Timer { Interval = 1500 };
                    t.Tick += (s, a) => { t.Dispose(); Scan(); };
                    t.Start();
                }
            }
        }
        base.WndProc(ref m);
    }

    // =====================================================================================
    //  Estado dos controles e avisos (thread da interface)
    // =====================================================================================

    void OnReading(BatteryState s) { devices[s.Path] = s; Rebuild(); }
    void OnRemoved(string path) { if (devices.Remove(path)) Rebuild(); }

    void Rebuild()
    {
        queuedToasts = new List<Action>();
        try { RebuildCore(); }
        finally
        {
            var q = queuedToasts;
            queuedToasts = null;
            foreach (Action a in q) a();
        }
    }

    void RebuildCore()
    {
        // Junta USB + Bluetooth do mesmo controle (mesmo MAC) numa coisa só
        var logical = new Dictionary<string, BatteryState>();
        foreach (BatteryState s in devices.Values)
        {
            BatteryState cur;
            if (!logical.TryGetValue(s.Key, out cur) || (!cur.Ok && s.Ok) || (s.Ok && !s.Bluetooth && cur.Bluetooth)) logical[s.Key] = s;
        }
        current = logical;

        foreach (KeyValuePair<string, BatteryState> kv in logical)
        {
            string key = kv.Key;
            BatteryState s = kv.Value, prev;
            known.TryGetValue(key, out prev);
            System.Windows.Forms.Timer pend;
            if (pendingDisconnect.TryGetValue(key, out pend))
            {
                pend.Dispose(); pendingDisconnect.Remove(key); // voltou rápido (ex.: troca USB <-> Bluetooth)
                Transitions(prev, s);
            }
            else if (prev == null)
            {
                if (!order.Contains(key)) order.Add(key);
                if (s.Ok) Toast(ToastWindow.GlyphGamepad, AccentBlue, s.Name + Lang.T(" conectado", " connected"),
                    "via " + s.Conn + (s.NoBattery ? "  ·  " + BatteryRules.NoBatteryLabel(s) : s.Charging ? Lang.T("  ·  carregando", "  ·  charging") : s.Full ? Lang.T("  ·  carga completa", "  ·  fully charged") : ""),
                    s.NoBattery ? -1 : s.Percent, s.Charging, "pad:" + key, Sfx.Connect);
            }
            else Transitions(prev, s);
            known[key] = s;
            CheckCritical(key, s);
        }

        // Controles que sumiram: confirma depois de 1,5 s antes de avisar
        foreach (string key in new List<string>(known.Keys))
        {
            if (logical.ContainsKey(key) || pendingDisconnect.ContainsKey(key)) continue;
            string k = key;
            var t = new System.Windows.Forms.Timer { Interval = 1500 };
            t.Tick += (o, e) =>
            {
                t.Dispose();
                pendingDisconnect.Remove(k);
                if (current.ContainsKey(k)) return;
                BatteryState last;
                if (known.TryGetValue(k, out last))
                    Toast(ToastWindow.GlyphGamepad, AccentRed, last.Name + Lang.T(" desconectado", " disconnected"),
                        last.NoBattery ? Lang.T("Estava via ", "Was on ") + last.Conn + "  ·  " + BatteryRules.NoBatteryLabel(last)
                                       : Lang.T("Última leitura de bateria: ", "Last battery reading: ") + last.Percent + "%",
                        -1, false, "pad:" + k, Sfx.Disconnect);
                known.Remove(k); order.Remove(k); lowWarned.Remove(k);
                UpdateVisuals();
            };
            pendingDisconnect[k] = t;
            t.Start();
        }
        UpdateVisuals();
    }

    void Transitions(BatteryState prev, BatteryState s)
    {
        if (prev == null || !prev.Ok || !s.Ok || prev.Status == s.Status) return;
        string g = "pad:" + s.Key;
        if (s.Charging)
            Toast(ToastWindow.GlyphBolt, AccentGreen, Lang.T("Carregando", "Charging"), s.Name + Lang.T("  ·  cabo conectado", "  ·  cable connected"), s.Percent, true, g, Sfx.Charging);
        else if (s.Full)
            Toast(ToastWindow.GlyphBatteryFull, AccentGreen, Lang.T("Carga completa", "Fully charged"), s.Name + Lang.T("  ·  já pode desconectar o cabo", "  ·  you can unplug the cable"), 100, false, g, Sfx.Full);
        else
            Toast(ToastWindow.GlyphGamepad, AccentAmber, Lang.T("Carregamento parado", "Charging stopped"), s.Name + Lang.T("  ·  usando a bateria", "  ·  running on battery"), s.Percent, false, g, Sfx.Unplug);
    }

    static bool IsCritical(BatteryState s) { return BatteryRules.IsCritical(s); } // sem informação de bateria nunca é crítico

    void CheckCritical(string key, BatteryState s)
    {
        if (IsCritical(s))
        {
            if (lowWarned.Add(key))
                Toast(ToastWindow.GlyphWarning, AccentRed, Lang.T("Bateria muito baixa", "Battery very low"), s.Name + Lang.T("  ·  carregue o controle", "  ·  charge the controller"), s.Percent, false, "low:" + key, Sfx.Low);
        }
        else lowWarned.Remove(key);
    }

    // Durante o Rebuild as notificações esperam o overlay ser redimensionado (para aparecerem logo abaixo dele)
    List<Action> queuedToasts;

    // group: notificações do mesmo grupo se substituem em vez de empilhar
    // ("pad:MAC" = estado de um controle, "low:MAC" = bateria crítica, "toggle" = atalho)
    void Toast(string glyph, Color accent, string title, string sub, int pct, bool charging, string group, Sfx sfx)
    {
        if (!notify) return;
        Action a = () => { Card(glyph, accent, title, sub, pct, charging, group); PlaySfx(sfx); };
        if (queuedToasts != null) queuedToasts.Add(a); else a();
    }

    // Mostra um aviso: cartão na tela normalmente; dentro do jogo (pelo RivaTuner) quando um jogo está em
    // tela cheia exclusiva, onde o cartão não apareceria.
    void Card(string glyph, Color accent, string title, string sub, int pct, bool charging, string group)
    {
        if (rtssInGame)
        {
            rtssNotice = "<C=" + (accent.ToArgb() & 0xFFFFFF).ToString("X6") + ">" + title + "<C>" + (string.IsNullOrEmpty(sub) ? "" : "  " + sub);
            rtssNoticeUntil = DateTime.UtcNow.AddSeconds(5);
            RtssPush();
            return;
        }
        ToastWindow.ShowToast(glyph, accent, title, sub, pct, charging, corner, group);
    }

    // =====================================================================================
    //  RivaTuner (RTSS): mostra a bateria dentro do jogo, inclusive em tela cheia exclusiva
    // =====================================================================================

    const int RtssOff = 0, RtssFullscreenOnly = 1, RtssAlways = 2;
    int rtssMode = RtssFullscreenOnly;
    static string rtssMappingOverride;  // --rtss-mapa <nome>: usado pela suíte (RivaTuner simulado)
    static int rtssModeOverride = -1;   // --rtss-modo desligado|tela-cheia|sempre: só nesta execução
    RtssOsd rtss;
    bool rtssWriting;      // nosso texto está no RTSS agora
    bool rtssInGame;       // jogo com posse exclusiva da tela + RTSS mostrando: a janela do overlay fica escondida
    string rtssNotice;
    DateTime rtssNoticeUntil;
    ToolStripMenuItem[] rtssItems;

    // Roda 1x por segundo: vê se há jogo em tela cheia exclusiva e se o RTSS está aberto
    void RtssTick()
    {
        // Só quando o jogo tem a posse exclusiva da tela (o overlay em janela não apareceria). Jogo em tela cheia
        // com as otimizações ligadas, ou DX12, continua com o overlay normal (bug #12).
        bool hidden = Fullscreen.OverlayCannotShow();
        if (RtssRules.ShouldWrite(rtssMode, hidden)) rtssWriting = rtss.UpdateIfChanged(RtssText());
        else if (rtssWriting) { rtss.Release(); rtssWriting = false; }
        bool inGame = RtssRules.HideWindow(rtssWriting, hidden);
        if (inGame != rtssInGame) { rtssInGame = inGame; UpdateVisuals(); }
    }

    string RtssText()
    {
        string notice = rtssNotice != null && DateTime.UtcNow < rtssNoticeUntil ? rtssNotice : null;
        return RtssOsd.Format(lines, notice);
    }

    // Atualiza o texto na hora (sem esperar o próximo segundo) quando algo muda
    void RtssPush()
    {
        if (rtssWriting) rtssWriting = rtss.UpdateIfChanged(RtssText());
    }

    void SetRtssMode(int mode)
    {
        rtssMode = mode;
        Save("Rtss", mode);
        for (int i = 0; i < rtssItems.Length; i++) rtssItems[i].Checked = i == mode;
        RtssTick();
    }

    public void ReleaseRtss()
    {
        if (rtss != null) rtss.Release();
        rtssWriting = false;
    }

    // Som da notificação (se a opção "Sons" estiver ligada e o programa não estiver em modo --mudo)
    void PlaySfx(Sfx k)
    {
        if (sounds && !mute) SoundFx.Play(k);
    }

    void ToggleByButton()
    {
        if (!btnToggle) return;
        userHidden = !userHidden;
        miShow.Checked = !userHidden;
        UpdateVisuals();
        PlaySfx(userHidden ? Sfx.Hide : Sfx.Show);
        if (userHidden)
            Card("", AccentGray, Lang.T("Overlay oculto", "Overlay hidden"), Lang.T("Mute (PS5) ou L3+R3 (PS4/Xbox) mostra de novo", "Mute (PS5) or L3+R3 (PS4/Xbox) shows it again"), -1, false, "toggle");
        else
            Card("", AccentBlue, Lang.T("Overlay visível", "Overlay visible"), current.Count + (current.Count == 1 ? Lang.T(" controle conectado", " controller connected") : Lang.T(" controles conectados", " controllers connected")), -1, false, "toggle");
    }

    // =====================================================================================
    //  Overlay
    // =====================================================================================

    void UpdateVisuals()
    {
        var ls = new List<BatteryState>();
        foreach (string key in order)
        {
            BatteryState s;
            if (!current.TryGetValue(key, out s)) continue;
            bool normal = !userHidden && (!onlyLow || BatteryRules.IsLow(s));
            bool forced = forceLow && IsCritical(s);
            if (normal || forced) ls.Add(s);
        }
        lines = ls;
        ApplySize(Math.Max(1, ls.Count));
        allowShow = true;
        bool showWindow = ls.Count > 0 && !rtssInGame; // em jogo com tela cheia exclusiva, quem mostra é o RivaTuner
        if (showWindow && !Visible) Show();
        else if (!showWindow && Visible) Hide();
        ToastWindow.OverlayHeight = showWindow ? Height : 0;
        RtssPush();
        Invalidate();
        UpdateTray();
    }

    void ApplySize(int n)
    {
        int h = n * LineH;
        if (Height != h || Region == null)
        {
            Size = new Size(OverlayW, h);
            using (var path = Rounded(new Rectangle(0, 0, OverlayW, h), 10)) Region = new Region(path);
        }
        PlaceWindow();
    }

    void PlaceWindow()
    {
        Rectangle wa = Screen.PrimaryScreen.WorkingArea;
        const int m = 12;
        int x = corner.EndsWith("L") ? wa.Left + m : wa.Right - Width - m;
        int y = corner.StartsWith("B") ? wa.Bottom - Height - m : wa.Top + m;
        Location = new Point(x, y);
    }

    static Color LevelColor(BatteryState s)
    {
        if (s.Charging || s.Full) return Color.FromArgb(80, 170, 255);
        if (s.Percent > 50) return Color.FromArgb(90, 210, 110);
        if (s.Percent > 20) return Color.FromArgb(240, 190, 60);
        return Color.FromArgb(240, 80, 70);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using (var tagFont = new Font("Segoe UI Semibold", 8f))
        using (var tagFontSmall = new Font("Segoe UI Semibold", 6.75f)) // "XBOX" (4 letras) não pode encostar no ícone
        using (var pctFont = new Font("Segoe UI Semibold", 11f))
        using (var tagBrush = new SolidBrush(Color.FromArgb(150, 155, 165)))
        using (var white = new SolidBrush(Color.White))
        using (var outline = new Pen(Color.FromArgb(220, 220, 225), 1.6f))
        using (var tipBrush = new SolidBrush(Color.FromArgb(220, 220, 225)))
        using (var sep = new Pen(Color.FromArgb(30, 255, 255, 255)))
        {
            var left = new StringFormat { LineAlignment = StringAlignment.Center };
            var right = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
            for (int i = 0; i < lines.Count; i++)
            {
                BatteryState s = lines[i];
                int y = i * LineH;
                if (i > 0) g.DrawLine(sep, 10, y, Width - 10, y);

                g.DrawString(s.Tag, s.Tag.Length > 3 ? tagFontSmall : tagFont, tagBrush, new RectangleF(s.Tag.Length > 3 ? 7 : 9, y, 32, LineH), left);

                if (s.Ok && !BatteryRules.ShowsBattery(s))
                {
                    // sem informação de bateria (ex.: Xbox no cabo): texto no lugar do ícone e da porcentagem
                    using (var modeFont = new Font("Segoe UI Semibold", 9f))
                    using (var modeBrush = new SolidBrush(Color.FromArgb(200, 205, 215)))
                        g.DrawString(BatteryRules.NoBatteryLabel(s), modeFont, modeBrush, new RectangleF(40, y, Width - 48, LineH), right);
                    continue;
                }

                var body = new Rectangle(38, y + 9, 30, 14);
                using (var bp = Rounded(body, 3)) g.DrawPath(outline, bp);
                g.FillRectangle(tipBrush, 69, y + 13, 2, 6);
                if (s.Ok)
                {
                    int fill = (int)Math.Round((body.Width - 4) * Math.Max(0, Math.Min(100, s.Percent)) / 100.0);
                    if (fill > 0) using (var fb = new SolidBrush(LevelColor(s))) g.FillRectangle(fb, body.X + 2, body.Y + 2, fill, body.Height - 3);
                    if (s.Charging)
                    {
                        float bx = 0, by = y;
                        var bolt = new PointF[] { new PointF(bx + 55, by + 10), new PointF(bx + 48, by + 17), new PointF(bx + 52.5f, by + 17),
                                                  new PointF(bx + 50, by + 23), new PointF(bx + 57, by + 15), new PointF(bx + 52.5f, by + 15) };
                        g.FillPolygon(white, bolt);
                    }
                }
                g.DrawString(s.Ok ? s.Percent + "%" : "?", pctFont, white, new RectangleF(72, y, Width - 80, LineH), right);
            }
        }
    }

    static GraphicsPath Rounded(Rectangle r, int rad)
    {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // =====================================================================================
    //  Bandeja
    // =====================================================================================

    void BuildTray()
    {
        tray = new NotifyIcon { ContextMenuStrip = BuildMenu(), Visible = true };
        UpdateTray();
    }

    // O menu é montado de novo quando o idioma muda (todos os textos passam por Lang.T)
    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem(AppInfo.Name + " v" + AppInfo.Version + "  —  " + AppInfo.Author) { Enabled = false });
        menu.Items.Add(new ToolStripSeparator());

        // Organização do menu (v1.7.1): o que mais se usa fica no topo; o resto em grupos.
        //   Mostrar overlay
        //   Overlay ▸        quando aparece, posição, escala da bateria
        //   Notificações ▸   avisos, sons, aviso ao iniciar com o Windows, testar
        //   Controle ▸       atalho no controle, procurar controles
        //   RivaTuner (RTSS) ▸
        //   Idioma / Language ▸
        //   Iniciar com o Windows
        //   Sair
        miShow = new ToolStripMenuItem(Lang.T("Mostrar overlay", "Show overlay"), null, (s, e) => { userHidden = !userHidden; miShow.Checked = !userHidden; UpdateVisuals(); }) { Checked = !userHidden };
        menu.Items.Add(miShow);

        // ---- Overlay ----
        var ov = new ToolStripMenuItem(Lang.T("Overlay", "Overlay"));
        miOnlyLow = new ToolStripMenuItem(Lang.T("Só mostrar com bateria baixa (≤20%)", "Only show when battery is low (≤20%)"), null, (s, e) =>
        {
            onlyLow = !onlyLow; miOnlyLow.Checked = onlyLow; Save("OnlyLow", onlyLow ? 1 : 0); UpdateVisuals();
        }) { Checked = onlyLow };
        miForceLow = new ToolStripMenuItem(Lang.T("Sempre mostrar com bateria abaixo de 10%", "Always show when battery is below 10%"), null, (s, e) =>
        {
            forceLow = !forceLow; miForceLow.Checked = forceLow; Save("ForceLow", forceLow ? 1 : 0); UpdateVisuals();
        }) { Checked = forceLow };
        var pos = new ToolStripMenuItem(Lang.T("Posição na tela", "Screen position"));
        string[] names = CornerNames;
        for (int i = 0; i < 4; i++)
        {
            string code = Corners[i];
            cornerItems[i] = new ToolStripMenuItem(names[i], null, (s, e) =>
            {
                corner = code; Save("Corner", code); PlaceWindow();
                foreach (var it in cornerItems) it.Checked = false;
                ((ToolStripMenuItem)s).Checked = true;
            }) { Checked = corner == code };
            pos.DropDownItems.Add(cornerItems[i]);
        }
        // Escala da bateria: a corrigida (0-8) é o padrão; a antiga (0-10) fica para quem quiser voltar
        var scale = new ToolStripMenuItem(Lang.T("Escala da bateria", "Battery scale"));
        miScaleNew = new ToolStripMenuItem(Lang.T("0 a 8 — corrigida (cheio = 100%)", "0 to 8 — corrected (full = 100%)"), null, (s, e) => ApplyScale(false)) { Checked = !PadHid.LegacyScale };
        miScaleOld = new ToolStripMenuItem(Lang.T("0 a 10 — antiga (igual ao DSX)", "0 to 10 — old (same as DSX)"), null, (s, e) => ApplyScale(true)) { Checked = PadHid.LegacyScale };
        scale.DropDownItems.Add(miScaleNew);
        scale.DropDownItems.Add(miScaleOld);
        ov.DropDownItems.Add(miOnlyLow);
        ov.DropDownItems.Add(miForceLow);
        ov.DropDownItems.Add(new ToolStripSeparator());
        ov.DropDownItems.Add(pos);
        ov.DropDownItems.Add(scale);
        menu.Items.Add(ov);

        // ---- Notificações ----
        var nt = new ToolStripMenuItem(Lang.T("Notificações", "Notifications"));
        miNotify = new ToolStripMenuItem(Lang.T("Mostrar notificações (conexão, carga, bateria baixa)", "Show notifications (connection, charging, low battery)"), null, (s, e) =>
        {
            notify = !notify; miNotify.Checked = notify; Save("Notify", notify ? 1 : 0);
        }) { Checked = notify };
        miSounds = new ToolStripMenuItem(Lang.T("Sons das notificações", "Notification sounds"), null, (s, e) =>
        {
            sounds = !sounds; miSounds.Checked = sounds; Save("Sounds", sounds ? 1 : 0);
            if (sounds && !mute) SoundFx.Play(Sfx.Show); // amostra ao ligar
        }) { Checked = sounds };
        var miStartNotice = new ToolStripMenuItem(Lang.T("Aviso ao iniciar com o Windows", "Notice when starting with Windows")) { Checked = startupNotice };
        miStartNotice.Click += (s, e) => { startupNotice = !startupNotice; miStartNotice.Checked = startupNotice; Save("StartupNotice", startupNotice ? 1 : 0); };
        nt.DropDownItems.Add(miNotify);
        nt.DropDownItems.Add(miSounds);
        nt.DropDownItems.Add(miStartNotice);
        nt.DropDownItems.Add(new ToolStripSeparator());
        nt.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Testar notificações", "Test notifications"), null, (s, e) => DemoToasts()));
        menu.Items.Add(nt);

        // ---- Controle ----
        var ct = new ToolStripMenuItem(Lang.T("Controle", "Controller"));
        miBtn = new ToolStripMenuItem(Lang.T("Atalho no controle (Mute no PS5 · L3+R3 no PS4 e Xbox)", "Controller shortcut (Mute on PS5 · L3+R3 on PS4 and Xbox)"), null, (s, e) =>
        {
            btnToggle = !btnToggle; miBtn.Checked = btnToggle; Save("MuteToggle", btnToggle ? 1 : 0);
        }) { Checked = btnToggle };
        ct.DropDownItems.Add(miBtn);
        ct.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Procurar controles agora", "Search for controllers now"), null, (s, e) => Scan()));
        menu.Items.Add(ct);

        // ---- RivaTuner: a bateria dentro do jogo, inclusive em tela cheia exclusiva (precisa do RTSS aberto) ----
        var rt = new ToolStripMenuItem("RivaTuner (RTSS)");
        var rtStatus = new ToolStripMenuItem("") { Enabled = false };
        rtssItems = new[]
        {
            new ToolStripMenuItem(Lang.T("Desligado", "Off"), null, (s, e) => SetRtssMode(RtssOff)),
            new ToolStripMenuItem(Lang.T("Só quando o jogo esconde o overlay (padrão)", "Only when the game hides the overlay (default)"), null, (s, e) => SetRtssMode(RtssFullscreenOnly)),
            new ToolStripMenuItem(Lang.T("Sempre (pode aparecer junto com o overlay)", "Always (may show alongside the overlay)"), null, (s, e) => SetRtssMode(RtssAlways)),
        };
        for (int i = 0; i < rtssItems.Length; i++) rtssItems[i].Checked = i == rtssMode;
        rt.DropDownItems.Add(rtStatus);
        rt.DropDownItems.Add(new ToolStripSeparator());
        rt.DropDownItems.AddRange(rtssItems);
        rt.DropDownOpening += (s, e) => rtStatus.Text = rtss.IsAvailable()
            ? Lang.T("RivaTuner aberto: a bateria aparece dentro do jogo", "RivaTuner running: battery shows inside the game")
            : Lang.T("RivaTuner não encontrado (abra o RTSS para usar)", "RivaTuner not found (open RTSS to use this)");
        menu.Items.Add(rt);

        // ---- Idioma: fica nos dois idiomas, para qualquer pessoa achar ----
        var lang = new ToolStripMenuItem("Idioma / Language");
        lang.DropDownItems.Add(new ToolStripMenuItem("Português (Brasil)", null, (s, e) => SetLanguage("pt")) { Checked = !Lang.En });
        lang.DropDownItems.Add(new ToolStripMenuItem("English", null, (s, e) => SetLanguage("en")) { Checked = Lang.En });
        menu.Items.Add(lang);
        menu.Items.Add(new ToolStripSeparator());

        miStartup = new ToolStripMenuItem(StartupMenuText(), null, (s, e) => ToggleStartup()) { Checked = IsStartup() };
        menu.Opening += (s, e) => { miStartup.Checked = IsStartup(); miStartup.Text = StartupMenuText(); }; // reflete mudanças feitas fora (Gerenciador de Tarefas)
        menu.Items.Add(miStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem(Lang.T("Sair", "Exit"), null, (s, e) => { tray.Visible = false; Application.Exit(); }));
        return menu;
    }

    // Troca o idioma na hora: salva, remonta o menu e atualiza o overlay e a dica do ícone
    void SetLanguage(string code)
    {
        Lang.Apply(code);
        Save("Language", Lang.Code);
        ContextMenuStrip old = tray.ContextMenuStrip;
        tray.ContextMenuStrip = BuildMenu();
        if (old != null) BeginInvoke(new Action(old.Dispose)); // o menu antigo ainda está fechando
        UpdateVisuals();
        ToastWindow.ShowToast("", AccentBlue, Lang.T("Idioma: Português (Brasil)", "Language: English"),
            Lang.T("Os textos do programa mudaram", "The program's texts have changed"), -1, false, corner, "toggle");
    }

    void UpdateTray()
    {
        var parts = new List<string>();
        BatteryState lowest = null;
        foreach (string key in order)
        {
            BatteryState s;
            if (!current.TryGetValue(key, out s) || !s.Ok) continue;
            if (s.NoBattery) { parts.Add(s.Tag + " " + BatteryRules.NoBatteryLabel(s)); continue; }
            parts.Add(s.Tag + " " + s.Percent + "%" + (s.Charging ? Lang.T(" (carregando)", " (charging)") : ""));
            if (lowest == null || s.Percent < lowest.Percent) lowest = s;
        }
        string tip = parts.Count > 0 ? string.Join("  ·  ", parts.ToArray()) : Lang.T("Nenhum controle conectado", "No controller connected");
        tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;

        // Ícone: controle + barra de bateria do controle MAIS descarregado (cor do nível). Segue o tema da barra de tarefas.
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        using (Bitmap bmp = TrayIcon.Draw(size, lowest != null ? lowest.Percent : -1, lowest != null ? LevelColor(lowest) : Color.Gray, TrayIcon.TaskbarIsLight()))
        {
            IntPtr h = bmp.GetHicon();
            var old = tray.Icon;
            tray.Icon = Icon.FromHandle(h);
            if (old != null) old.Dispose();
            if (trayIconHandle != IntPtr.Zero) DestroyIcon(trayIconHandle);
            trayIconHandle = h;
        }
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) { Post(UpdateTray); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.Dispose(disposing);
    }

    ToolStripMenuItem miScaleNew, miScaleOld;

    // Troca a escala e recalcula na hora os controles já conectados (a partir do byte original)
    void ApplyScale(bool legacy)
    {
        PadHid.LegacyScale = legacy;
        Save("BatteryScale", legacy ? 1 : 0);
        miScaleNew.Checked = !legacy;
        miScaleOld.Checked = legacy;
        foreach (BatteryState s in new List<BatteryState>(devices.Values))
        {
            if (s.IsXbox) continue; // a bateria do Xbox já vem em %, a escala não se aplica
            int p, st;
            s.Ok = PadHid.DecodeBattery(s.Model, s.Raw, out p, out st);
            s.Percent = p; s.Status = st;
            if (simulateLow && s.Ok && s.Status == 0) s.Percent = 5;
        }
        Rebuild();
    }

    // Mostra as notificações principais em sequência, para ver como ficam
    void DemoToasts()
    {
        var steps = new Action[]
        {
            () => { ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentBlue, "DualSense" + Lang.T(" conectado", " connected"), "via Bluetooth", 65, false, corner); PlaySfx(Sfx.Connect); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentBlue, "DualShock 4" + Lang.T(" conectado", " connected"), "via Bluetooth", 45, false, corner); PlaySfx(Sfx.Connect); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentBlue, "Xbox Series" + Lang.T(" conectado", " connected"), "via USB  ·  USB mode", -1, false, corner); PlaySfx(Sfx.Connect); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphBolt, AccentGreen, Lang.T("Carregando", "Charging"), "DualShock 4" + Lang.T("  ·  cabo conectado", "  ·  cable connected"), 45, true, corner); PlaySfx(Sfx.Charging); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphBatteryFull, AccentGreen, Lang.T("Carga completa", "Fully charged"), "DualSense" + Lang.T("  ·  já pode desconectar o cabo", "  ·  you can unplug the cable"), 100, false, corner); PlaySfx(Sfx.Full); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentAmber, Lang.T("Carregamento parado", "Charging stopped"), "DualSense" + Lang.T("  ·  usando a bateria", "  ·  running on battery"), 95, false, corner); PlaySfx(Sfx.Unplug); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphWarning, AccentRed, Lang.T("Bateria muito baixa", "Battery very low"), "DualShock 4" + Lang.T("  ·  carregue o controle", "  ·  charge the controller"), 5, false, corner); PlaySfx(Sfx.Low); },
            () => { ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentRed, "DualSense" + Lang.T(" desconectado", " disconnected"), Lang.T("Última leitura de bateria: ", "Last battery reading: ") + "95%", -1, false, corner); PlaySfx(Sfx.Disconnect); },
        };
        int i = 0;
        var t = new System.Windows.Forms.Timer { Interval = 1800 };
        t.Tick += (s, e) => { if (i < steps.Length) steps[i++](); else t.Dispose(); };
        steps[i++]();
        t.Start();
    }

    // --teste-carga-completa: com o overlay OCULTO, finge um DualSense carregando que completa a carga 2 s depois.
    // O aviso "Carga completa" tem que aparecer mesmo assim (ele não depende do overlay estar visível).
    void SimulateFullCharge()
    {
        Func<int, int, BatteryState> fake = (pct, st) => new BatteryState
        { Key = "teste-carga", Path = "teste:carga", Model = PadModel.DualSense, Name = "DualSense", Tag = "PS5", Ok = true, Percent = pct, Status = st };
        devices["teste:carga"] = fake(87, 1); Rebuild();
        var t = new System.Windows.Forms.Timer { Interval = 2000 };
        t.Tick += (s, e) => { t.Dispose(); devices["teste:carga"] = fake(100, 2); Rebuild(); };
        t.Start();
    }

    // --teste-rajada: aperta o atalho várias vezes seguidas e dispara vários avisos (teste de sobreposição)
    void StressTest()
    {
        var steps = new List<Action>();
        for (int i = 0; i < 12; i++) steps.Add(ToggleByButton);
        steps.Add(() => ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentBlue, "Teste A", "sem grupo", 50, false, corner));
        steps.Add(() => ToastWindow.ShowToast(ToastWindow.GlyphBolt, AccentGreen, "Teste B", "sem grupo", 60, true, corner));
        steps.Add(() => ToastWindow.ShowToast(ToastWindow.GlyphWarning, AccentRed, "Teste C", "sem grupo", 5, false, corner));
        steps.Add(() => ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentAmber, "Teste D", "sem grupo", 70, false, corner));
        steps.Add(() => ToastWindow.ShowToast(ToastWindow.GlyphGamepad, AccentGray, "Teste E", "sem grupo", -1, false, corner));
        for (int i = 0; i < 7; i++) steps.Add(ToggleByButton);
        int n = 0;
        var t = new System.Windows.Forms.Timer { Interval = 130 };
        t.Tick += (s, e) => { if (n < steps.Count) steps[n++](); else t.Dispose(); };
        t.Start();
    }

    static void Save(string name, object v)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RegKey)) k.SetValue(name, v);
    }

    // ---- Iniciar com o Windows (v1.7.1): escolha salva + entrada conferida e consertada a cada abertura ----
    static string startupStatus = "";   // resultado do autoconserto desta abertura (vai para o registro de inicializações)

    static bool StartupWanted()
    {
        using (var k = Registry.CurrentUser.CreateSubKey(RegKey))
        {
            object v = k.GetValue("StartWithWindows");
            if (v is int) return (int)v == 1;
            // primeira vez na v1.7.1: herda o que já estava ligado (entrada antiga sem o --inicio-windows)
            bool had = StartupEntry.Current(StartupEntry.RunKey, RunName) != null;
            k.SetValue("StartWithWindows", had ? 1 : 0);
            return had;
        }
    }

    static bool IsStartup()
    {
        return StartupEntry.IsActive(StartupEntry.RunKey, StartupEntry.ApprovedKey, RunName, Application.ExecutablePath);
    }

    string StartupMenuText()
    {
        string t = Lang.T("Iniciar com o Windows", "Start with Windows");
        if (StartupWanted() && StartupEntry.DisabledInTaskManager(StartupEntry.ApprovedKey, RunName))
            t += Lang.T("  (desativado no Gerenciador de Tarefas)", "  (disabled in Task Manager)");
        return t;
    }

    void ToggleStartup()
    {
        if (IsStartup())
        {
            StartupEntry.Disable(StartupEntry.RunKey, StartupEntry.ApprovedKey, RunName);
            Save("StartWithWindows", 0);
        }
        else
        {
            StartupEntry.Enable(StartupEntry.RunKey, StartupEntry.ApprovedKey, RunName, Application.ExecutablePath);
            Save("StartWithWindows", 1);
        }
        miStartup.Checked = IsStartup();
        miStartup.Text = StartupMenuText();
    }

    // Registro das últimas 20 aberturas, para saber se o Windows abriu o programa:
    // %LOCALAPPDATA%\GamepadBatteryOverlay\inicializacoes.log
    static string startReason = "", startDetail = "";

    static void LogStart()
    {
        string line = string.Format("{0:yyyy-MM-dd HH:mm:ss}  {1,-34}  v{2}  iniciar com o Windows: {3}  ({4})",
            DateTime.Now, startReason, AppInfo.Version, startupStatus, startDetail);
        Exception last = null;
        for (int attempt = 0; attempt < 5; attempt++) // no boot o disco pode estar ocupado: tenta algumas vezes
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GamepadBatteryOverlay");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "inicializacoes.log");
                var lines = new List<string>(File.Exists(file) ? File.ReadAllLines(file) : new string[0]);
                lines.Add(line);
                if (lines.Count > 20) lines.RemoveRange(0, lines.Count - 20);
                File.WriteAllLines(file, lines.ToArray(), new System.Text.UTF8Encoding(true)); // com marca UTF-8: acentos certos em qualquer visualizador
                return;
            }
            catch (Exception ex) { last = ex; Thread.Sleep(300); }
        }
        // não conseguiu: anota o erro num arquivo de reserva, para dar para descobrir o motivo
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "GamepadBatteryOverlay-erro.log"), line + "  ERRO ao gravar o registro: " + last + Environment.NewLine, new System.Text.UTF8Encoding(true)); }
        catch { }
    }

    // Aviso de que o programa abriu com o Windows (sem controle conectado ele fica só na bandeja, invisível)
    bool startupNotice = true; // "Aviso ao iniciar com o Windows" (bandeja > Notificações)

    void StartedWithWindowsToast()
    {
        if (!notify || !startupNotice) return;
        Card(ToastWindow.GlyphGamepad, AccentBlue, Lang.T("Gamepad Battery Overlay ativo", "Gamepad Battery Overlay is running"),
            Lang.T("Iniciado com o Windows  ·  ícone na bandeja (setinha ^)", "Started with Windows  ·  tray icon (^ arrow)"), -1, false, "start");
        PlaySfx(Sfx.Show);
    }

    // A versão antiga (DualSenseOverlay.exe, até a v1.5) aberta junto mostraria tudo em dobro: fecha ela.
    // Só age se o "sinal" de instância da versão antiga existir, ou seja, se ela estiver rodando de verdade.
    static void CloseOldVersion()
    {
        try
        {
            Mutex old;
            if (!Mutex.TryOpenExisting(SettingsMigration.OldMutex, out old)) return;
            old.Dispose();
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(SettingsMigration.OldProcess))
            {
                try { p.Kill(); p.WaitForExit(2000); } catch { }
                p.Dispose();
            }
        }
        catch { }
    }

    [STAThread]
    static void Main(string[] args)
    {
        bool demo = Array.IndexOf(args, "--demo") >= 0;
        simulateLow = Array.IndexOf(args, "--teste-bateria-baixa") >= 0;
        mute = Array.IndexOf(args, "--mudo") >= 0;
        fakeXboxUsb = Array.IndexOf(args, "--teste-xbox-usb") >= 0;
        int ri = Array.IndexOf(args, "--rtss-mapa");  // --rtss-mapa <nome>: RivaTuner simulado (suíte de testes)
        if (ri >= 0 && ri + 1 < args.Length) rtssMappingOverride = args[ri + 1];
        int rm = Array.IndexOf(args, "--rtss-modo");  // --rtss-modo desligado|tela-cheia|sempre (não salva)
        if (rm >= 0 && rm + 1 < args.Length)
            rtssModeOverride = args[rm + 1] == "sempre" ? RtssAlways : args[rm + 1] == "desligado" ? RtssOff : RtssFullscreenOnly;
        int li = Array.IndexOf(args, "--idioma"); // --idioma pt|en: força o idioma só nesta execução (não salva)
        if (li >= 0 && li + 1 < args.Length) langOverride = args[li + 1].ToLowerInvariant();
        int exp = Array.IndexOf(args, "--exportar-sons");
        if (exp >= 0)
        {
            // salva os efeitos sonoros como .wav e sai (sem abrir o programa)
            SoundFx.Export(exp + 1 < args.Length ? args[exp + 1] : "sons");
            return;
        }
        bool created;
        using (new Mutex(true, "GamepadBatteryOverlay_SingleInstance", out created))
        {
            if (!created) return;
            CloseOldVersion();
            // nome novo na v1.6: traz as configurações e o "Iniciar com o Windows" do nome antigo
            SettingsMigration.CopyKey(SettingsMigration.OldRegKey, RegKey);
            SettingsMigration.MoveRunEntry(RunKey, SettingsMigration.OldRunName, RunName, Application.ExecutablePath);
            // Iniciar com o Windows: se o usuário quer, garante que a entrada existe e aponta para este .exe (v1.7.1)
            bool hasArg = Array.IndexOf(args, StartupEntry.AutoArg) >= 0;
            double sinceExplorer = LogonStart.SecondsSinceExplorer(), uptime = LogonStart.UptimeSeconds();
            bool fromWindows = LogonStart.Decide(hasArg, sinceExplorer, uptime);
            string previousEntry;
            startupStatus = StartupEntry.Heal(StartupEntry.RunKey, StartupEntry.ApprovedKey, RunName, Application.ExecutablePath, StartupWanted(), out previousEntry);
            startReason = !fromWindows ? "aberto manualmente" : hasArg ? "aberto pelo Windows" : "aberto pelo Windows (sem a marca)";
            startDetail = string.Format("área de trabalho há {0:0}s, PC ligado há {1:0}s, entrada antes: {2}", sinceExplorer, uptime, previousEntry ?? "(nenhuma)");
            LogStart();
            Application.EnableVisualStyles();
            var form = new OverlayForm();
            if (fromWindows)
            {
                var ws = new System.Windows.Forms.Timer { Interval = 8000 }; // espera a área de trabalho terminar de abrir (no boot ela demora)
                ws.Tick += (s, e) => { ws.Dispose(); form.StartedWithWindowsToast(); };
                ws.Start();
            }
            if (simulateLow) { form.userHidden = true; form.miShow.Checked = false; }
            if (Array.IndexOf(args, "--teste-carga-completa") >= 0)
            {
                form.userHidden = true; form.miShow.Checked = false;
                var tc = new System.Windows.Forms.Timer { Interval = 1500 };
                tc.Tick += (s, e) => { tc.Dispose(); form.SimulateFullCharge(); };
                tc.Start();
            }
            if (Array.IndexOf(args, "--teste-rajada") >= 0)
            {
                form.btnToggle = true;
                var st = new System.Windows.Forms.Timer { Interval = 2500 };
                st.Tick += (s, e) => { st.Dispose(); form.StressTest(); };
                st.Start();
            }
            if (demo)
            {
                form.notify = false; // no modo demo, só a sequência de exemplo
                var t = new System.Windows.Forms.Timer { Interval = 800 };
                t.Tick += (s, e) => { t.Dispose(); form.DemoToasts(); };
                t.Start();
            }
            Application.Run(form);
            form.ReleaseRtss(); // ao sair, tira o nosso texto do RivaTuner (não fica nada preso no jogo)
        }
    }
}
