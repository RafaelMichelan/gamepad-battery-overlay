// Leitura de controles PlayStation (DualSense, DualSense Edge, DualShock 4) usando só APIs nativas
// do Windows (hid.dll / setupapi.dll). Apenas LÊ o controle, com acesso compartilhado.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

public enum PadModel { DualSense, DualSenseEdge, DualShock4, Xbox }

/// <summary>Um controle encontrado e o layout dos relatórios dele.</summary>
public class PadDevice
{
    public string Path;
    public PadModel Model;
    public ushort Pid;
    public bool Bluetooth;
    public int InLen, FeatLen;

    public bool IsDS4 { get { return Model == PadModel.DualShock4; } }
    public string Name { get { return IsDS4 ? "DualShock 4" : Model == PadModel.DualSenseEdge ? "DualSense Edge" : "DualSense"; } }
    public string Tag { get { return IsDS4 ? "PS4" : "PS5"; } }

    // Relatório completo esperado: Bluetooth DS4 = 0x11, Bluetooth DualSense = 0x31, USB = 0x01
    public byte ReportId { get { return Bluetooth ? (byte)(IsDS4 ? 0x11 : 0x31) : (byte)0x01; } }
    // Onde começam os dados comuns (analógicos) dentro do buffer
    public int Base { get { return Bluetooth ? (IsDS4 ? 3 : 2) : 1; } }
    public int BatteryIndex { get { return Base + (IsDS4 ? 29 : 52); } }
    public int TimestampIndex { get { return Base + (IsDS4 ? 9 : 27); } }
    public int MinLength { get { return Bluetooth ? 78 : 64; } }
    public string ToggleName { get { return IsDS4 ? "L3+R3" : "Mute"; } }
}

public class BatteryState
{
    public string Key;        // identidade do controle (MAC), junta USB + Bluetooth do mesmo controle
    public string Path;
    public PadModel Model;
    public string Name, Tag;
    public bool Found = true;
    public bool Ok;           // leitura válida
    public int Percent;       // 0-100
    public int Status;        // 0 descarregando, 1 carregando, 2 carga completa
    public bool Bluetooth;
    public string Error;
    public byte Raw;          // byte de status original (para recalcular se a escala mudar)
    public bool NoBattery;    // o controle não informa a bateria (ex.: Xbox no cabo) -> "USB mode", sem alerta
    public bool IsXbox { get { return Model == PadModel.Xbox; } }
    public bool Charging { get { return Status == 1; } }
    public bool Full { get { return Status == 2; } }
    public string Conn { get { return Bluetooth ? "Bluetooth" : "USB"; } }

    public bool SameReading(BatteryState o)
    {
        return o != null && o.Ok == Ok && o.Percent == Percent && o.Status == Status && o.Bluetooth == Bluetooth && o.NoBattery == NoBattery;
    }

    public override string ToString()
    {
        if (!Ok) return Name + ": erro " + Error;
        if (NoBattery) return Name + ": sem informação de bateria via " + Conn;
        string s = Status == 1 ? "carregando" : Status == 2 ? "carga completa" : "descarregando";
        return string.Format("{0}: {1}% ({2}) via {3}", Name, Percent, s, Conn);
    }
}

public static class PadHid
{
    const ushort SONY_VID = 0x054C;

    [StructLayout(LayoutKind.Sequential)]
    struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, [Out] ushort[] caps);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetSerialNumberString(SafeFileHandle h, byte[] buf, int len);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetupDiGetDeviceInterfaceDetailW")]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr devInfo);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_FLAG_OVERLAPPED = 0x40000000;

    public static PadModel? ModelFromPid(ushort pid)
    {
        switch (pid)
        {
            case 0x0CE6: return PadModel.DualSense;
            case 0x0DF2: return PadModel.DualSenseEdge;
            case 0x05C4: // DualShock 4 (1ª geração, CUH-ZCT1)
            case 0x09CC: // DualShock 4 (2ª geração, CUH-ZCT2)
                return PadModel.DualShock4;
            default: return null;
        }
    }

    public static List<string> HidPaths()
    {
        var paths = new List<string>();
        Guid g;
        HidD_GetHidGuid(out g);
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return paths;
        try
        {
            for (int i = 0; ; i++)
            {
                var did = new SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(did);
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref did)) break;
                int req;
                SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out req, IntPtr.Zero);
                if (req <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(req);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref did, buf, req, out req, IntPtr.Zero))
                        paths.Add(Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    /// <summary>Lista todos os controles PlayStation suportados que estão conectados.</summary>
    public static List<PadDevice> Enumerate()
    {
        var list = new List<PadDevice>();
        foreach (string path in HidPaths())
        {
            if (!path.ToLowerInvariant().Contains("054c")) continue; // filtro rápido: só Sony
            using (SafeFileHandle h = Open(path, false))
            {
                if (h == null) continue;
                var attr = new HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (!HidD_GetAttributes(h, ref attr) || attr.VendorID != SONY_VID) continue;
                PadModel? model = ModelFromPid(attr.ProductID);
                if (model == null) continue;
                ushort[] caps = new ushort[32];
                IntPtr pre;
                if (!HidD_GetPreparsedData(h, out pre)) continue;
                HidP_GetCaps(pre, caps);
                HidD_FreePreparsedData(pre);
                if (caps[2] == 0) continue;
                list.Add(new PadDevice
                {
                    Path = path, Model = model.Value, Pid = attr.ProductID,
                    InLen = caps[2], FeatLen = caps[4],
                    Bluetooth = caps[2] > 64 // USB = 64 bytes; Bluetooth = 78 (DualSense) ou 547 (DS4)
                });
            }
        }
        return list;
    }

    /// <summary>O dispositivo ainda está presente no Windows?</summary>
    public static bool IsPresent(string path)
    {
        foreach (string p in HidPaths()) if (string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static SafeFileHandle Open(string path, bool overlapped)
    {
        uint flags = overlapped ? FILE_FLAG_OVERLAPPED : 0;
        SafeFileHandle h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (h.IsInvalid) { h.Dispose(); h = CreateFile(path, GENERIC_READ, 3, IntPtr.Zero, 3, flags, IntPtr.Zero); }
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    /// <summary>
    /// No Bluetooth os controles começam no modo "simples" (relatório 0x01, sem bateria).
    /// Ler o feature report de calibração faz eles mandarem o relatório completo
    /// (DualSense: feature 0x05 -> relatório 0x31; DualShock 4: feature 0x02 -> relatório 0x11).
    /// É o mesmo que a Steam e os jogos fazem.
    /// </summary>
    public static void EnableFullReports(SafeFileHandle h, PadDevice d)
    {
        if (!d.Bluetooth || d.FeatLen <= 0) return;
        byte[] f = new byte[d.FeatLen];
        f[0] = d.IsDS4 ? (byte)0x02 : (byte)0x05;
        HidD_GetFeature(h, f, f.Length);
    }

    /// <summary>MAC do controle (igual no USB e no Bluetooth), para não mostrar o mesmo controle duas vezes.</summary>
    public static string GetIdentity(SafeFileHandle h, PadDevice d)
    {
        try
        {
            if (d.Bluetooth)
            {
                byte[] b = new byte[256];
                if (HidD_GetSerialNumberString(h, b, b.Length))
                {
                    string s = Encoding.Unicode.GetString(b).TrimEnd('\0');
                    var hex = new StringBuilder();
                    foreach (char c in s) if (Uri.IsHexDigit(c)) hex.Append(char.ToLowerInvariant(c));
                    if (hex.Length == 12) return hex.ToString();
                }
            }
            else if (d.FeatLen > 0)
            {
                // USB: DS4 feature 0x12, DualSense feature 0x09 — MAC nos bytes 1..6 (ordem invertida)
                byte[] f = new byte[d.FeatLen];
                f[0] = d.IsDS4 ? (byte)0x12 : (byte)0x09;
                if (HidD_GetFeature(h, f, f.Length))
                {
                    var hex = new StringBuilder();
                    for (int i = 6; i >= 1; i--) hex.Append(f[i].ToString("x2"));
                    string mac = hex.ToString();
                    if (mac != "000000000000") return mac;
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>Confere o CRC32 de um relatório Bluetooth (semente 0xA1 + bytes 0..73, CRC nos bytes 74..77).</summary>
    public static bool CheckCrc(byte[] r)
    {
        if (r.Length < 78) return false;
        uint crc = 0xFFFFFFFF;
        crc = Crc(crc, 0xA1);
        for (int i = 0; i < 74; i++) crc = Crc(crc, r[i]);
        crc = ~crc;
        uint got = (uint)(r[74] | (r[75] << 8) | (r[76] << 16) | (r[77] << 24));
        return crc == got;
    }

    static uint Crc(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        return crc;
    }

    /// <summary>Relatório válido e completo para este controle?</summary>
    public static bool IsFullReport(PadDevice d, byte[] buf, int n)
    {
        if (n < d.MinLength || buf[0] != d.ReportId) return false;
        return !d.Bluetooth || CheckCrc(buf);
    }

    /// <summary>
    /// Escala da bateria. false (padrão, v1.2.1+) = escala CORRIGIDA: na bateria os controles vão só
    /// até o nível 8 (medido: DualSense com carga completa manda 0x28 = completo + nível 8; DS4 e
    /// DualSense cheios fora do cabo mandam nível 8). true = escala ANTIGA 0-10 (v1.0-1.2, igual ao DSX).
    /// </summary>
    public static bool LegacyScale;

    /// <summary>
    /// Decodifica o byte de status da bateria.
    /// DualSense: nibble baixo = nível, nibble alto = 0 descarregando / 1 carregando / 2 completo.
    /// DualShock 4: nibble baixo = nível, bit 4 = cabo ligado (com cabo: 0-10 carregando, 11 = completo).
    /// Escala corrigida: na bateria 0-8 (8 = 100%); DS4 no cabo 0-11 (como o DS4Windows).
    /// </summary>
    public static bool DecodeBattery(PadModel m, byte st, out int pct, out int status)
    {
        if (LegacyScale) return DecodeBatteryLegacy(m, st, out pct, out status);
        int level = st & 0x0F;
        pct = 0; status = 0;
        if (m == PadModel.DualShock4)
        {
            bool cable = (st & 0x10) != 0;
            if (cable)
            {
                if (level <= 10) { pct = Math.Min(level * 100 / 11, 100); status = 1; }
                else if (level == 11) { pct = 100; status = 2; }
                else return false;
            }
            else pct = Math.Min(level * 100 / 8, 100);
            return true;
        }
        int chg = (st >> 4) & 0x0F;
        if (chg == 0 || chg == 1) { pct = Math.Min(level * 100 / 8, 100); status = chg; return true; }
        if (chg == 2) { pct = 100; status = 2; return true; }
        return false;
    }

    /// <summary>Escala antiga (v1.0 a v1.2): nível 0-10, mostrando o meio de cada faixa de 10%.</summary>
    public static bool DecodeBatteryLegacy(PadModel m, byte st, out int pct, out int status)
    {
        int level = st & 0x0F;
        pct = 0; status = 0;
        if (m == PadModel.DualShock4)
        {
            bool cable = (st & 0x10) != 0;
            if (cable)
            {
                if (level < 10) { pct = level * 10 + 5; status = 1; }
                else if (level == 10) { pct = 100; status = 1; }
                else if (level == 11) { pct = 100; status = 2; }
                else return false;
            }
            else pct = level < 10 ? level * 10 + 5 : 100;
            return true;
        }
        int chg = (st >> 4) & 0x0F;
        if (chg == 0 || chg == 1) { pct = Math.Min(level * 10 + 5, 100); status = chg; return true; }
        if (chg == 2) { pct = 100; status = 2; return true; }
        return false;
    }

    public static BatteryState Decode(PadDevice d, byte[] buf)
    {
        var s = new BatteryState { Path = d.Path, Model = d.Model, Name = d.Name, Tag = d.Tag, Bluetooth = d.Bluetooth };
        byte st = buf[d.BatteryIndex];
        s.Raw = st;
        s.Ok = DecodeBattery(d.Model, st, out s.Percent, out s.Status);
        if (!s.Ok) s.Error = "status 0x" + st.ToString("X2");
        return s;
    }

    /// <summary>Atalho de mostrar/ocultar: Mute no DualSense, L3+R3 no DualShock 4.</summary>
    public static bool TogglePressed(PadDevice d, byte[] buf)
    {
        if (d.IsDS4) return (buf[d.Base + 5] & 0xC0) == 0xC0;   // buttons[1]: L3 = 0x40, R3 = 0x80
        return (buf[d.Base + 9] & 0x04) != 0;                   // buttons[2]: Mute = 0x04
    }

    /// <summary>Diagnóstico: captura N relatórios brutos de um controle.</summary>
    public static List<byte[]> CaptureRaw(PadDevice d, int count)
    {
        var list = new List<byte[]>();
        SafeFileHandle h = Open(d.Path, false);
        if (h == null) return list;
        EnableFullReports(h, d);
        using (var fs = new FileStream(h, FileAccess.Read, d.InLen, false))
        {
            int tries = 0;
            while (list.Count < count && tries++ < count * 4)
            {
                byte[] buf = new byte[d.InLen];
                int n = fs.Read(buf, 0, buf.Length);
                if (n > 0) list.Add(buf);
            }
        }
        return list;
    }
}
