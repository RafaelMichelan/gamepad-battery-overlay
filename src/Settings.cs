// Migração das configurações do nome antigo do programa ("DualSense Battery Overlay", até a v1.5)
// para o nome novo ("Gamepad Battery Overlay", v1.6+). Quem já usava não perde nada: posição, idioma,
// escala, sons, atalho e o "Iniciar com o Windows" passam para o nome novo na primeira vez que abre.
using System;
using Microsoft.Win32;

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
