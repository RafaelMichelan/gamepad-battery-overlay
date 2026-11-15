// Migração das configurações do nome antigo do programa ("DualSense Battery Overlay", até a v1.5)
// para o nome novo ("Gamepad Battery Overlay", v1.6+). Quem já usava não perde nada: posição, idioma,
// escala, sons, atalho e o "Iniciar com o Windows" passam para o nome novo na primeira vez que abre.
using System;
using Microsoft.Win32;

/// <summary>
/// "Iniciar com o Windows" (v1.7.1). A escolha do usuário fica salva na configuração do programa (StartWithWindows),
/// e a entrada em ...\CurrentVersion\Run é só o "meio". Toda vez que o programa abre ele confere a entrada e, se ela
/// sumiu (programa de limpeza, pasta movida) ou aponta para outro .exe, recria. Respeita a escolha feita no
/// Gerenciador de Tarefas > Inicializar (StartupApproved): o autoconserto não reativa o que foi desativado lá;
/// só marcar a opção na bandeja de novo reativa.
/// </summary>
public static class StartupEntry
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    public const string AutoArg = "--inicio-windows";

    /// <summary>A linha que o Windows roda ao entrar: "caminho do .exe" --inicio-windows</summary>
    public static string Command(string exePath) { return "\"" + exePath + "\" " + AutoArg; }

    public static string Current(string runKey, string name)
    {
        try { using (var k = Registry.CurrentUser.OpenSubKey(runKey)) return k == null ? null : k.GetValue(name) as string; }
        catch { return null; }
    }

    /// <summary>Desativado no Gerenciador de Tarefas? (StartupApproved: 1º byte ímpar = desativado, ex.: 03)</summary>
    public static bool DisabledInTaskManager(string approvedKey, string name)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(approvedKey))
            {
                byte[] b = k == null ? null : k.GetValue(name) as byte[];
                return b != null && b.Length > 0 && (b[0] & 1) == 1;
            }
        }
        catch { return false; }
    }

    /// <summary>Vai mesmo abrir com o Windows? (entrada certa e não desativada no Gerenciador de Tarefas)</summary>
    public static bool IsActive(string runKey, string approvedKey, string name, string exePath)
    {
        string cur = Current(runKey, name);
        return cur != null && string.Equals(cur, Command(exePath), StringComparison.OrdinalIgnoreCase)
            && !DisabledInTaskManager(approvedKey, name);
    }

    /// <summary>Liga: grava a entrada e, se estiver desativada no Gerenciador de Tarefas, reativa (pedido explícito do usuário).</summary>
    public static void Enable(string runKey, string approvedKey, string name, string exePath)
    {
        using (var k = Registry.CurrentUser.CreateSubKey(runKey)) k.SetValue(name, Command(exePath));
        if (DisabledInTaskManager(approvedKey, name))
            using (var a = Registry.CurrentUser.CreateSubKey(approvedKey))
                a.SetValue(name, new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, RegistryValueKind.Binary);
    }

    /// <summary>Desliga: tira a entrada e a marcação do Gerenciador de Tarefas.</summary>
    public static void Disable(string runKey, string approvedKey, string name)
    {
        using (var k = Registry.CurrentUser.OpenSubKey(runKey, true)) if (k != null) k.DeleteValue(name, false);
        using (var a = Registry.CurrentUser.OpenSubKey(approvedKey, true)) if (a != null) a.DeleteValue(name, false);
    }

    /// <summary>
    /// Autoconserto ao abrir: se o usuário quer iniciar com o Windows e a entrada sumiu ou aponta para outro lugar,
    /// recria (sem mexer na escolha do Gerenciador de Tarefas). Devolve "ok", "recriada", "corrigida", "desativada"
    /// (no Gerenciador de Tarefas) ou "desligada" (o usuário não quer).
    /// </summary>
    public static string Heal(string runKey, string approvedKey, string name, string exePath, bool wanted)
    {
        string previous;
        return Heal(runKey, approvedKey, name, exePath, wanted, out previous);
    }

    /// <summary>Igual ao Heal, devolvendo também o que estava na entrada antes (vai para o registro de inicializações).</summary>
    public static string Heal(string runKey, string approvedKey, string name, string exePath, bool wanted, out string previous)
    {
        previous = Current(runKey, name);
        if (!wanted) return "desligada";
        string cur = previous;
        string result = "ok";
        if (cur == null || !string.Equals(cur, Command(exePath), StringComparison.OrdinalIgnoreCase))
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(runKey)) k.SetValue(name, Command(exePath)); }
            catch { return "erro"; }
            result = cur == null ? "recriada" : "corrigida";
        }
        return DisabledInTaskManager(approvedKey, name) ? "desativada" : result;
    }
}

/// <summary>
/// O programa foi aberto junto com o Windows? (v1.7.1) Não depende só da marca --inicio-windows na entrada:
/// num teste real ela chegou no boot sem a marca (algo reescreveu a entrada), e o aviso não apareceu.
/// Também conta como "aberto com o Windows" abrir até 3 minutos depois de a área de trabalho (explorer) começar,
/// ou até 5 minutos depois de o PC ligar.
/// </summary>
public static class LogonStart
{
    public const double ExplorerWindowSec = 180, UptimeWindowSec = 300;

    public static bool Decide(bool hasArg, double secondsSinceExplorer, double uptimeSeconds)
    {
        if (hasArg) return true;
        if (secondsSinceExplorer >= 0 && secondsSinceExplorer < ExplorerWindowSec) return true;
        return uptimeSeconds >= 0 && uptimeSeconds < UptimeWindowSec;
    }

    /// <summary>Segundos desde que a área de trabalho (explorer.exe desta sessão) começou; -1 se não der para saber.</summary>
    public static double SecondsSinceExplorer()
    {
        try
        {
            int session = System.Diagnostics.Process.GetCurrentProcess().SessionId;
            DateTime first = DateTime.MaxValue;
            foreach (var p in System.Diagnostics.Process.GetProcessesByName("explorer"))
            {
                try { if (p.SessionId == session && p.StartTime < first) first = p.StartTime; } catch { }
                p.Dispose();
            }
            return first == DateTime.MaxValue ? -1 : (DateTime.Now - first).TotalSeconds;
        }
        catch { return -1; }
    }

    /// <summary>Segundos desde que o PC ligou.</summary>
    public static double UptimeSeconds() { return (uint)Environment.TickCount / 1000.0; }
}

public static class SettingsMigration
{
    public const string OldRegKey = @"Software\DualSenseOverlay";
    public const string OldRunName = "DualSenseOverlay";
    public const string OldMutex = "DualSenseOverlay_SingleInstance";
    public const string OldProcess = "DualSenseOverlay";

    /// <summary>
    /// Copia os valores de oldKey para newKey, mas só se newKey ainda não existir (a migração roda uma vez).
    /// A chave antiga é mantida (para quem quiser voltar para a versão antiga). Devolve true se copiou algo.
    /// </summary>
    public static bool CopyKey(string oldKey, string newKey)
    {
        try
        {
            using (var existing = Registry.CurrentUser.OpenSubKey(newKey))
                if (existing != null) return false;
            using (var old = Registry.CurrentUser.OpenSubKey(oldKey))
            {
                if (old == null) return false;
                string[] names = old.GetValueNames();
                if (names.Length == 0) return false;
                using (var nk = Registry.CurrentUser.CreateSubKey(newKey))
                    foreach (string n in names) nk.SetValue(n, old.GetValue(n), old.GetValueKind(n));
                return true;
            }
        }
        catch { return false; }
    }

    /// <summary>
    /// "Iniciar com o Windows": se existir a entrada antiga em runKey, remove e cria a nova apontando para exePath.
    /// Devolve true se moveu. Sem a entrada antiga, não mexe em nada.
    /// </summary>
    public static bool MoveRunEntry(string runKey, string oldName, string newName, string exePath)
    {
        try
        {
            using (var k = Registry.CurrentUser.OpenSubKey(runKey, true))
            {
                if (k == null || k.GetValue(oldName) == null) return false;
                k.DeleteValue(oldName, false);
                k.SetValue(newName, "\"" + exePath + "\"");
                return true;
            }
        }
        catch { return false; }
    }
}
