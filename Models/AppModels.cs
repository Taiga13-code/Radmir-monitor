using System.Text.Json.Serialization;

namespace RadmirMonitor.Models;

public sealed class MonitorSettings
{
    public int SettingsSchemaVersion { get; set; } = 305;
    public string Nick { get; set; } = "";
    public string ChatLogPath { get; set; } = "";
    public bool Autostart { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool StartMonitoringWithApp { get; set; } = true;
    public bool OverlayEnabled { get; set; } = false;
    public bool OverlayClickThrough { get; set; }
    public double OverlayOpacity { get; set; } = 0.92;
    public double OverlayScale { get; set; } = 1.0;
    public double OverlayLeft { get; set; } = 80;
    public double OverlayTop { get; set; } = 80;
    public bool MobileEnabled { get; set; } = true;
    public string MobileToken { get; set; } = Guid.NewGuid().ToString("N");
    public int ReportGoal { get; set; } = 0;
    public int OnlineGoalMinutes { get; set; } = 0;
}

public sealed class EventRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Nick { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Target { get; set; }
    public int? TargetId { get; set; }
    public string? Reason { get; set; }
    public string? RawLine { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LocalData
{
    public List<EventRecord> Events { get; set; } = [];
    public Dictionary<string, int> OnlineByDay { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class DailyStat
{
    public string Day { get; set; } = "";
    public int Report { get; set; }
    public int Ban { get; set; }
    public int Mute { get; set; }
    public int Kick { get; set; }
    public int Warn { get; set; }
    public int Jail { get; set; }
    public int OnlineMinutes { get; set; }
    [JsonIgnore] public string OnlineText => $"{OnlineMinutes / 60}ч {OnlineMinutes % 60:00}м";
}

public sealed class HistoryRow
{
    public string Time { get; set; } = "";
    public string Action { get; set; } = "";
    public string Player { get; set; } = "—";
    public string Id { get; set; } = "—";
    public string Reason { get; set; } = "—";
    public string Source { get; set; } = "Локально";
}

public sealed class ParsedAction
{
    public string Action { get; init; } = "";
    public string? Target { get; init; }
    public int? TargetId { get; init; }
    public string? Reason { get; init; }
    public string RawLine { get; init; } = "";
}
