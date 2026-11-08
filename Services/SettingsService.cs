using System.IO;
using System.Text.Json;
using RadmirMonitor.Models;

namespace RadmirMonitor.Services;

public sealed class SettingsService
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public MonitorSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.Settings))
            {
                var loaded = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(AppPaths.Settings), Json) ?? new();
                if (loaded.SettingsSchemaVersion < 303)
                {
                    loaded.OverlayEnabled = false;
                    loaded.SettingsSchemaVersion = 303;
                    Save(loaded);
                }
                return loaded;
            }
        }
        catch { }

        var migrated = new MonitorSettings();
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(exeDir))
            {
                var oldConfig = Path.Combine(exeDir, "config.txt");
                if (File.Exists(oldConfig))
                {
                    var lines = File.ReadAllLines(oldConfig);
                    if (lines.Length > 0 && !string.IsNullOrWhiteSpace(lines[0])) migrated.Nick = lines[0].Trim();
                    if (lines.Length > 1 && File.Exists(lines[1].Trim())) migrated.ChatLogPath = lines[1].Trim();
                }
            }
        }
        catch { }
        return migrated;
    }

    public void Save(MonitorSettings settings)
    {
        Directory.CreateDirectory(AppPaths.Root);
        var tmp = AppPaths.Settings + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Json));
        File.Move(tmp, AppPaths.Settings, true);
    }
}
