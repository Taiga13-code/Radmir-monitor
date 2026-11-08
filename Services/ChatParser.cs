using System.Text.RegularExpressions;
using RadmirMonitor.Models;

namespace RadmirMonitor.Services;

public static partial class ChatParser
{
    public static ParsedAction? ParseAdminAction(string rawLine, string nick)
    {
        var sourceLine = NormalizeSourceLine(rawLine);
        var line = Strip(rawLine);
        if (string.IsNullOrWhiteSpace(nick)) return null;

        var actor = ActorRegex().Match(line);
        if (!actor.Success || !actor.Groups[1].Value.Equals(nick, StringComparison.OrdinalIgnoreCase))
            return null;

        var report = ReportRegex().Match(line);
        if (report.Success)
        {
            return new ParsedAction
            {
                Action = "report",
                Target = report.Groups[1].Value,
                TargetId = int.TryParse(report.Groups[2].Value, out var id) ? id : null,
                Reason = CleanReason(report.Groups[3].Value),
                RawLine = sourceLine
            };
        }

        var low = line.Replace('ё', 'е').ToLowerInvariant();
        var action = DetectAction(low);
        if (action is null) return null;

        string? target = null;
        int? targetId = null;
        var targetMatch = TargetRegex().Match(line);
        if (targetMatch.Success)
        {
            target = targetMatch.Groups[1].Value;
            if (int.TryParse(targetMatch.Groups[2].Value, out var id)) targetId = id;
        }
        else
        {
            var rest = line[(actor.Index + actor.Length)..];
            var fallback = NameIdRegex().Match(rest);
            if (fallback.Success)
            {
                target = fallback.Groups[1].Value;
                if (int.TryParse(fallback.Groups[2].Value, out var id)) targetId = id;
            }
        }

        string? reason = null;
        var rm = ReasonRegex().Match(line);
        if (rm.Success) reason = CleanReason(rm.Groups[1].Value);
        if (reason is null)
        {
            var za = ZaRegex().Match(line);
            if (za.Success) reason = CleanReason(za.Groups[1].Value);
        }

        return new ParsedAction { Action = action, Target = target, TargetId = targetId, Reason = reason, RawLine = sourceLine };
    }

    public static int? ParseOnlineMinutes(string rawLine)
    {
        var line = Strip(rawLine);
        var low = line.Replace('ё', 'е').ToLowerInvariant();
        if (!(low.Contains("онлайн") || low.Contains("отыграл") || low.Contains("отыграно") || low.Contains("время в игре") || low.Contains("времени в игре")))
            return null;

        var colon = Regex.Match(low, @"(?<!\d)(\d{1,2})\s*[:.]\s*([0-5]?\d)(?!\d)");
        if (colon.Success) return int.Parse(colon.Groups[1].Value) * 60 + int.Parse(colon.Groups[2].Value);

        var h = Regex.Match(low, @"(\d{1,2})\s*(?:ч|час|часа|часов|h)");
        var m = Regex.Match(low, @"(\d{1,3})\s*(?:м|мин|минута|минуты|минут|m)(?![a-zа-я])");
        if (h.Success || m.Success)
            return (h.Success ? int.Parse(h.Groups[1].Value) * 60 : 0) + (m.Success ? int.Parse(m.Groups[1].Value) : 0);
        return null;
    }


    private static string NormalizeSourceLine(string input)
    {
        var text = Regex.Replace(input ?? "", @"\{[0-9A-Fa-f]{6,8}\}", "");
        return text.Replace("\0", "").Trim();
    }

    public static string Strip(string input)
    {
        var text = Regex.Replace(input ?? "", @"\{[0-9A-Fa-f]{6,8}\}", "");
        text = Regex.Replace(text, @"^\s*\[\d{1,2}:\d{2}:\d{2}\]\s*", "");
        return text.Replace("\0", "").Trim();
    }

    private static string? DetectAction(string low)
    {
        if (ContainsAny(low, "забанил", "заблокировал аккаунт", "заблокировал игрока", "выдал блокировку", "бан игрок", "ban ")) return "ban";
        if (ContainsAny(low, "кикнул", "выгнал игрока", "выгнал с сервера", "kick ")) return "kick";
        if (ContainsAny(low, "замутил", "выдал мут", "заблокировал чат", "блокировка чата", "заблокировал голосовой чат", "заблокировал репорт", "блокировку репорта", "mute ")) return "mute";
        if (ContainsAny(low, "выдал предупреждение", "предупредил игрока", "выдал варн", "варн игрок", "warn ")) return "warn";
        if (ContainsAny(low, "посадил в тюрьму", "посадил игрока", "поместил в деморган", "деморган", "demorgan", "jail ")) return "jail";
        return null;
    }

    private static bool ContainsAny(string s, params string[] values) => values.Any(v => s.Contains(v, StringComparison.Ordinal));
    private static string? CleanReason(string? s)
    {
        var v = (s ?? "").Trim().Trim(' ', '.', ';', '|');
        return string.IsNullOrWhiteSpace(v) ? null : v;
    }

    [GeneratedRegex(@"(?:\[A\]\s*)?Администратор\s+([A-Za-zА-Яа-яЁё0-9_]{2,32})(?:\[(\d{1,6})\])?", RegexOptions.IgnoreCase)]
    private static partial Regex ActorRegex();
    [GeneratedRegex(@"Администратор\s+[A-Za-zА-Яа-яЁё0-9_]{2,32}(?:\[\d{1,6}\])?\s+для\s+([A-Za-zА-Яа-яЁё0-9_]{2,32})\[(\d{1,6})\]\s*:\s*(.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex ReportRegex();
    [GeneratedRegex(@"(?:игрока|игроку|пользователя)\s+([A-Za-zА-Яа-яЁё0-9_]{2,32})(?:\[(\d{1,6})\])?", RegexOptions.IgnoreCase)]
    private static partial Regex TargetRegex();
    [GeneratedRegex(@"([A-Za-zА-Яа-яЁё0-9_]{2,32})\s*\[(\d{1,6})\]", RegexOptions.IgnoreCase)]
    private static partial Regex NameIdRegex();
    [GeneratedRegex(@"(?:причина|причине)\s*[:\-]?\s*(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReasonRegex();
    [GeneratedRegex(@"\sза\s+(.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ZaRegex();
}
