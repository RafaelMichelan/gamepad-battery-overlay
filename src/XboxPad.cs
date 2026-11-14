// Controles Xbox (Series X|S, One S, Elite 2) usando só APIs nativas do Windows. Apenas LÊ, nunca abre o controle.
//  - Bluetooth: o controle aparece como dispositivo HID (VID 045E). A bateria vem da mesma propriedade que a tela
//    "Bluetooth e dispositivos" das Configurações usa (DEVPKEY_Bluetooth_Battery, no nó BTHLE\DEV_<MAC>).
//    O Windows.Gaming.Input e o XInput não servem: informam um valor errado (10%) ou nenhum.
//  - USB: o controle aparece no XInput como "com fio". O Windows não informa a bateria no cabo, então
//    a linha do overlay mostra "USB mode" no lugar da bateria.
//  - O controle Xbox 360 virtual criado pelo DS4Windows / ViGEm (PID 028E) é ignorado.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>Um controle Xbox encontrado agora.</summary>
public class XboxPad
{
    public string Key;        // MAC no Bluetooth; "xbox-usb-<slot>" no USB
    public string Path;       // caminho HID (Bluetooth) ou "xinput:<slot>" (USB)
    public ushort Pid;
    public bool Bluetooth;
    public int Slot = -1;     // posição no XInput (USB)
    public int Percent = -1;  // -1 = sem informação de bateria
    public string Name { get { return XboxHid.ModelName(Pid); } }

    public BatteryState ToState()
    {
        var s = new BatteryState { Key = Key, Path = Path, Model = PadModel.Xbox, Name = Name, Tag = "XBOX", Bluetooth = Bluetooth, Ok = true };
        if (Percent >= 0 && Percent <= 100) s.Percent = Percent;
        else s.NoBattery = true;
        return s;
    }
}

/// <summary>Regras que valem para todos os controles (PS e Xbox).</summary>
public static class BatteryRules
{
    public const int CriticalPct = 10;

    /// <summary>A linha mostra ícone e porcentagem? (sem informação de bateria = "USB mode")</summary>
    public static bool ShowsBattery(BatteryState s) { return s.Ok && !s.NoBattery; }

    /// <summary>Bateria crítica (alerta obrigatório): só para quem informa a bateria e não está carregando.</summary>
    public static bool IsCritical(BatteryState s) { return ShowsBattery(s) && s.Percent < CriticalPct && !s.Charging && !s.Full; }

    /// <summary>"Só mostrar com bateria baixa": só conta quem informa a bateria.</summary>
    public static bool IsLow(BatteryState s) { return ShowsBattery(s) && s.Percent <= 20 && !s.Charging; }

    /// <summary>Texto no lugar da bateria quando o controle não informa a bateria.</summary>
    public static string NoBatteryLabel(BatteryState s) { return s.Bluetooth ? Lang.T("sem info", "no info") : "USB mode"; }
}

public static class XboxHid
{
    public const ushort MS_VID = 0x045E;
    public const ushort VirtualX360Pid = 0x028E; // Xbox 360 com fio — é o que o ViGEm (DS4Windows/DSX) cria

    public static bool IsVirtual(ushort pid) { return pid == VirtualX360Pid; }

    public static string ModelName(ushort pid)
    {
        switch (pid)
        {
            case 0x0B12: case 0x0B13: case 0x0B20: case 0x0B21: return "Xbox Series";
            case 0x0B00: case 0x0B05: case 0x0B22: return "Xbox Elite 2";
            case 0x02EA: case 0x02E0: case 0x02FD: return "Xbox One";
            default: return Lang.T("Controle Xbox", "Xbox controller");
        }
    }

    // ---------------------------------------------------------------- Bluetooth (HID)

    static readonly Regex RxVidPid = new Regex(@"vid_045e&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase);
    static readonly Regex RxMac = new Regex(@"&([0-9a-f]{12})&", RegexOptions.IgnoreCase);

    /// <summary>É o caminho HID de um controle Xbox via Bluetooth (LE ou clássico)?</summary>
    public static bool IsBluetoothXboxPath(string path, out ushort pid)
    {
        pid = 0;
        if (path == null) return false;
        string p = path.ToLowerInvariant();
        if (!p.Contains("{00001812-") && !p.Contains("{00001124-")) return false; // HID sobre BLE / BT clássico
        Match m = RxVidPid.Match(p);
        if (!m.Success) return false;
        pid = Convert.ToUInt16(m.Groups[1].Value, 16);
        return !IsVirtual(pid);
    }

    /// <summary>MAC que o Windows coloca no caminho HID do Bluetooth (ex.: ...&REV_0524&A1B2C3D4E5F6&IG_00...).</summary>
    public static string MacFromPath(string path)
    {
        if (path == null) return null;
        Match m = RxMac.Match(path);
        return m.Success ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static List<XboxPad> EnumerateBluetooth()
    {
        var list = new List<XboxPad>();
        var seen = new HashSet<string>();
        foreach (string path in PadHid.HidPaths())
        {
            ushort pid;
            if (!IsBluetoothXboxPath(path, out pid)) continue;
            string mac = MacFromPath(path) ?? path.ToLowerInvariant();
            if (!seen.Add(mac)) continue; // o mesmo controle pode ter mais de uma coleção HID
            list.Add(new XboxPad { Key = mac, Path = path, Pid = pid, Bluetooth = true });
        }
        return list;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DEVPROPKEY { public Guid fmtid; public uint pid; public DEVPROPKEY(Guid g, uint p) { fmtid = g; pid = p; } }

    static DEVPROPKEY KeyInstanceId = new DEVPROPKEY(new Guid("78C34FC8-104A-4ACA-9EA4-524D52996E57"), 256);
    static DEVPROPKEY KeyBtBattery = new DEVPROPKEY(new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5"), 2);
    const uint DEVPROP_TYPE_BYTE = 0x03;

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_Interface_PropertyW(string iface, ref DEVPROPKEY key, out uint type, byte[] buf, ref int size, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Locate_DevNodeW(out int devInst, string id, int flags);
    [DllImport("cfgmgr32.dll")]
    static extern int CM_Get_Parent(out int parent, int devInst, int flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_DevNode_PropertyW(int devInst, ref DEVPROPKEY key, out uint type, byte[] buf, ref int size, int flags);

    /// <summary>
    /// Bateria (0-100) do controle Bluetooth dono deste caminho HID, ou -1 se o Windows não informar.
    /// Sobe da interface HID pelos "pais" do dispositivo até o nó Bluetooth que tem a propriedade.
    /// </summary>
    public static int ReadBluetoothBattery(string hidPath)
    {
        try
        {
            byte[] buf = new byte[512];
            int size = buf.Length;
            uint type;
            if (CM_Get_Device_Interface_PropertyW(hidPath, ref KeyInstanceId, out type, buf, ref size, 0) != 0) return -1;
            string id = Encoding.Unicode.GetString(buf, 0, Math.Max(0, size - 2)).TrimEnd('\0');
            int dev;
            if (CM_Locate_DevNodeW(out dev, id, 0) != 0) return -1;
            for (int depth = 0; depth < 8; depth++)
            {
                byte[] b = new byte[8];
                int sz = b.Length;
                if (CM_Get_DevNode_PropertyW(dev, ref KeyBtBattery, out type, b, ref sz, 0) == 0 && type == DEVPROP_TYPE_BYTE && sz >= 1)
                    return b[0] <= 100 ? b[0] : -1;
                int parent;
                if (CM_Get_Parent(out parent, dev, 0) != 0) break;
                dev = parent;
            }
        }
        catch { }
        return -1;
    }

    // ---------------------------------------------------------------- USB (XInput)

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_CAPABILITIES_EX
    {
        public byte Type, SubType; public ushort Flags;
        public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY;
        public ushort VibL, VibR;
        public ushort VendorId, ProductId, VersionNumber, Unk1; public uint Unk2;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_BATTERY_INFORMATION { public byte BatteryType, BatteryLevel; }

    // XInputGetCapabilitiesEx (ordinal 108 do xinput1_4.dll, Windows 8+): igual ao XInputGetCapabilities, mais VID/PID.
    [DllImport("xinput1_4.dll", EntryPoint = "#108")]
    static extern int XInputGetCapabilitiesEx(int one, int index, int flags, out XINPUT_CAPABILITIES_EX caps);
    [DllImport("xinput1_4.dll")]
    static extern int XInputGetBatteryInformation(int index, byte devType, out XINPUT_BATTERY_INFORMATION info);

    public const byte BATTERY_TYPE_WIRED = 0x01;

    [StructLayout(LayoutKind.Sequential)]
    struct XINPUT_STATE { public uint Packet; public ushort Buttons; public byte LT, RT; public short LX, LY, RX, RY; }
    [DllImport("xinput1_4.dll")]
    static extern int XInputGetState(int index, out XINPUT_STATE state);

    public const ushort BTN_LEFT_THUMB = 0x0040, BTN_RIGHT_THUMB = 0x0080;

    /// <summary>Atalho do overlay no Xbox: L3 + R3 (afundar os dois analógicos) juntos, igual ao DualShock 4.</summary>
    public static bool TogglePressed(ushort buttons)
    {
        const ushort both = BTN_LEFT_THUMB | BTN_RIGHT_THUMB;
        return (buttons & both) == both;
    }

    /// <summary>Posições do XInput ocupadas por um Xbox de verdade (Bluetooth ou USB), sem o 360 virtual.</summary>
    public static int[] RealXboxSlots()
    {
        var list = new List<int>();
        if (xinputMissing) return list.ToArray();
        try
        {
            for (int i = 0; i < 4; i++)
            {
                XINPUT_CAPABILITIES_EX c;
                if (XInputGetCapabilitiesEx(1, i, 0, out c) != 0) continue;
                if (c.VendorId == MS_VID && !IsVirtual(c.ProductId)) list.Add(i);
            }
        }
        catch (DllNotFoundException) { xinputMissing = true; }
        catch (EntryPointNotFoundException) { xinputMissing = true; }
        catch { }
        return list.ToArray();
    }

    /// <summary>Botões do controle nesta posição do XInput, ou -1 se não estiver conectado. Só leitura.</summary>
    public static int GetButtons(int slot)
    {
        try
        {
            XINPUT_STATE s;
            if (XInputGetState(slot, out s) != 0) return -1;
            return s.Buttons;
        }
        catch { return -1; }
    }

    /// <summary>Regra do XInput: é um Xbox de verdade ligado no cabo? (não é o 360 virtual e o XInput diz "com fio")</summary>
    public static bool IsWiredXbox(ushort vid, ushort pid, byte batteryType)
    {
        return vid == MS_VID && !IsVirtual(pid) && batteryType == BATTERY_TYPE_WIRED;
    }

    static bool xinputMissing;

    public static List<XboxPad> EnumerateWired()
    {
        var list = new List<XboxPad>();
        if (xinputMissing) return list;
        try
        {
            for (int i = 0; i < 4; i++)
            {
                XINPUT_CAPABILITIES_EX c;
                if (XInputGetCapabilitiesEx(1, i, 0, out c) != 0) continue;
                XINPUT_BATTERY_INFORMATION b;
                if (XInputGetBatteryInformation(i, 0, out b) != 0) continue;
                if (!IsWiredXbox(c.VendorId, c.ProductId, b.BatteryType)) continue;
                list.Add(new XboxPad { Key = "xbox-usb-" + i, Path = "xinput:" + i, Pid = c.ProductId, Bluetooth = false, Slot = i });
            }
        }
        catch (DllNotFoundException) { xinputMissing = true; }
        catch (EntryPointNotFoundException) { xinputMissing = true; }
        catch { }
        return list;
    }

    /// <summary>Todos os controles Xbox conectados agora. readBattery=false reaproveita a bateria de 'previous'.</summary>
    public static List<XboxPad> Enumerate(bool readBattery, Dictionary<string, XboxPad> previous)
    {
        var all = EnumerateBluetooth();
        foreach (XboxPad x in all)
        {
            XboxPad old;
            if (!readBattery && previous != null && previous.TryGetValue(x.Key, out old) && old.Percent >= 0) x.Percent = old.Percent;
            else x.Percent = ReadBluetoothBattery(x.Path);
        }
        all.AddRange(EnumerateWired());
        return all;
    }
}
