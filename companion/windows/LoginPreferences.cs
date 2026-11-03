using System.Text;

namespace UpscaleLab.Companion;

internal sealed class LoginPreferences(string file)
{
    internal static LoginPreferences Current => new(Path.Combine(SecureStore.Root, "profiles", CompanionConfiguration.ServerId, "identifier.bin"));
    internal string Read() {
        try { return Encoding.UTF8.GetString(SecureStore.Transform(File.ReadAllBytes(file), false)); }
        catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException) { return ""; }
    }
    internal void Save(bool remember, string identifier) {
        if (!remember || string.IsNullOrWhiteSpace(identifier)) { File.Delete(file); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file + ".tmp", SecureStore.Transform(Encoding.UTF8.GetBytes(identifier.Trim()), true));
        File.Move(file + ".tmp", file, true);
    }
}
