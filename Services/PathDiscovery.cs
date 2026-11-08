using System.IO;

namespace RadmirMonitor.Services;

public static class PathDiscovery
{
    public static string FindChatLog()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            Path.Combine(home, "Documents", "RADMIR CRMP User Files", "SAMP", "chatlog.txt"),
            Path.Combine(home, "OneDrive", "Documents", "RADMIR CRMP User Files", "SAMP", "chatlog.txt"),
            Path.Combine(home, "OneDrive", "Документы", "RADMIR CRMP User Files", "SAMP", "chatlog.txt")
        };
        return candidates.FirstOrDefault(File.Exists) ?? "";
    }

}
