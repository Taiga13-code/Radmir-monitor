using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RadmirMonitor.Models;

namespace RadmirMonitor.Services;

public sealed class LocalStore
{
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new() { WriteIndented = false };
    private LocalData _data = new();

    public LocalStore() => Load();

    private void Load()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(AppPaths.Data))
                    _data = JsonSerializer.Deserialize<LocalData>(File.ReadAllText(AppPaths.Data), _json) ?? new();
            }
            catch
            {
                _data = new LocalData();
            }
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(AppPaths.Root);
        var tmp = AppPaths.Data + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_data, _json));
        File.Move(tmp, AppPaths.Data, true);
    }

    public EventRecord AddEvent(string nick, ParsedAction action)
    {
        lock (_gate)
        {
            // A source chat line has a stable timestamp inside chatlog.txt. Build a deterministic
            // ID from that line so replaying the file cannot create a second local event.
            string? stableId = null;
            if (!string.IsNullOrWhiteSpace(action.RawLine))
            {
                stableId = StableEventId(nick, action.Action, action.RawLine);
                var existing = _data.Events.LastOrDefault(e =>
                    e.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(e.Id, stableId, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(e.RawLine, action.RawLine, StringComparison.Ordinal)));
                if (existing is not null) return Clone(existing);
            }

            var record = new EventRecord
            {
                Id = stableId ?? Guid.NewGuid().ToString("N"),
                Nick = nick,
                Action = action.Action,
                Target = action.Target,
                TargetId = action.TargetId,
                Reason = action.Reason,
                RawLine = action.RawLine,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _data.Events.Add(record);
            Trim();
            Save();
            return record;
        }
    }

    public void AddOnlineMinute(string nick, DateOnly? day = null)
    {
        var key = OnlineKey(nick, day ?? MoscowClock.Today);
        lock (_gate)
        {
            _data.OnlineByDay.TryGetValue(key, out var current);
            _data.OnlineByDay[key] = Math.Clamp(current + 1, 0, 1440);
            Save();
        }
    }

    public void SetOnline(string nick, int minutes, DateOnly? day = null)
    {
        var key = OnlineKey(nick, day ?? MoscowClock.Today);
        lock (_gate)
        {
            _data.OnlineByDay[key] = Math.Clamp(minutes, 0, 1440);
            Save();
        }
    }

    public int GetOnline(string nick, DateOnly? day = null)
    {
        var key = OnlineKey(nick, day ?? MoscowClock.Today);
        lock (_gate) return _data.OnlineByDay.TryGetValue(key, out var v) ? v : 0;
    }

    public List<DailyStat> History(string nick, int days)
    {
        lock (_gate)
        {
            var result = new List<DailyStat>();
            var today = MoscowClock.Today;
            for (var i = 0; i < days; i++)
            {
                var day = today.AddDays(-i);
                var startUtc = MoscowClock.StartOfDayUtc(day);
                var endUtc = MoscowClock.StartOfDayUtc(day.AddDays(1));
                var events = _data.Events.Where(e =>
                    e.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase) &&
                    e.CreatedAt >= startUtc && e.CreatedAt < endUtc).ToList();
                var stat = new DailyStat
                {
                    Day = day.ToString("yyyy-MM-dd"),
                    Report = Count(events, "report"),
                    Ban = Count(events, "ban"),
                    Mute = Count(events, "mute"),
                    Kick = Count(events, "kick"),
                    Warn = Count(events, "warn"),
                    Jail = Count(events, "jail"),
                    OnlineMinutes = _data.OnlineByDay.TryGetValue(OnlineKey(nick, day), out var m) ? m : 0
                };
                result.Add(stat);
            }
            return result;
        }
    }

    public List<EventRecord> SearchPunishments(string nick, string query, int limit = 300)
    {
        query = (query ?? "").Trim();
        lock (_gate)
        {
            return _data.Events
                .Where(e => e.Nick.Equals(nick, StringComparison.OrdinalIgnoreCase) && e.Action != "report")
                .Where(e => string.IsNullOrWhiteSpace(query) || Haystack(e).Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(e => e.CreatedAt)
                .Take(limit)
                .Select(Clone)
                .ToList();
        }
    }

    public (int Reports, int Punishments, int Online) Today(string nick)
    {
        var h = History(nick, 1)[0];
        return (h.Report, h.Ban + h.Mute + h.Kick + h.Warn + h.Jail, h.OnlineMinutes);
    }

    private static string StableEventId(string nick, string action, string rawLine)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{nick.Trim().ToLowerInvariant()}|{action.Trim().ToLowerInvariant()}|{rawLine}"));
        return "chat-" + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int Count(IEnumerable<EventRecord> source, string action) => source.Count(x => x.Action == action);
    private static string OnlineKey(string nick, DateOnly day) => $"{nick}|{day:yyyy-MM-dd}";
    private static string Haystack(EventRecord e) => $"{e.Action} {e.Target} {e.TargetId} {e.Reason}";
    private static EventRecord Clone(EventRecord e) => new()
    {
        Id = e.Id, Nick = e.Nick, Action = e.Action, Target = e.Target, TargetId = e.TargetId,
        Reason = e.Reason, RawLine = e.RawLine, CreatedAt = e.CreatedAt
    };

    private void Trim()
    {
        const int max = 25000;
        if (_data.Events.Count > max)
            _data.Events = _data.Events.OrderByDescending(x => x.CreatedAt).Take(max).OrderBy(x => x.CreatedAt).ToList();
    }
}
