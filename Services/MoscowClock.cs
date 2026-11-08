namespace RadmirMonitor.Services;

public static class MoscowClock
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(Offset);

    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public static DateOnly DayOf(DateTimeOffset value) =>
        DateOnly.FromDateTime(value.ToOffset(Offset).DateTime);

    public static DateTimeOffset StartOfDayUtc(DateOnly day)
    {
        var local = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), Offset);
        return local.ToUniversalTime();
    }
}
