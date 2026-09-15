using Microsoft.Win32;

namespace KotobaSUB.Windows;

internal sealed class StartupRegistration(string valueName = "KotobaSUB")
{
    private const string RunPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    internal string? Command => Registry.CurrentUser.OpenSubKey(RunPath, false)?.GetValue(valueName) as string;
    public bool Enabled => !string.IsNullOrWhiteSpace(Command);
    public void Set(bool enabled, string executablePath)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunPath, true)
            ?? throw new InvalidOperationException("Current-user startup registry key is unavailable.");
        if (enabled) key.SetValue(valueName, Quote(Path.GetFullPath(executablePath)), RegistryValueKind.String);
        else key.DeleteValue(valueName, false);
    }
    internal static string Quote(string path) => '"' + path.Replace("\"", "") + '"';
}