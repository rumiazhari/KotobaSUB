namespace KotobaSUB.Windows;

internal static class StartupSmoke
{
    public static int Run()
    {
        var startup = new StartupRegistration($"KotobaSUB.Smoke.{Guid.NewGuid():N}");
        const string executablePath = @"C:\Program Files\KotobaSUB\KotobaSUB.exe";
        string expected = StartupRegistration.Quote(executablePath);
        try
        {
            startup.Set(true, executablePath);
            bool enabled = startup.Enabled;
            string? command = startup.Command;
            startup.Set(false, executablePath);
            bool removed = !startup.Enabled;
            string directory = Path.GetFullPath("artifacts/startup-smoke");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "result.txt"), $"Enabled={enabled}\nCommand={command}\nRemoved={removed}\n");
            return enabled && command == expected && removed ? 0 : 1;
        }
        finally
        {
            try { startup.Set(false, executablePath); }
            catch { }
        }
    }
}