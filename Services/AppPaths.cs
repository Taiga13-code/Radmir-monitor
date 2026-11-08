using System.IO;

namespace RadmirMonitor.Services;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadmirMonitor");
    public static readonly string Settings = Path.Combine(Root, "settings.json");
    public static readonly string Data = Path.Combine(Root, "local_data.json");
    public static readonly string Log = Path.Combine(Root, "monitor.log");

    static AppPaths()
    {
        Directory.CreateDirectory(Root);
    }
}
