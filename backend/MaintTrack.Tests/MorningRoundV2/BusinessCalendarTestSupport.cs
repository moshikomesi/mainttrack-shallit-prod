using MaintTrack.Infrastructure.Calendar;

namespace MaintTrack.Tests.MorningRoundV2;

internal sealed class MutableTimeProvider : TimeProvider
{
    public MutableTimeProvider(DateTimeOffset utcNow)
    {
        UtcNow = utcNow;
    }

    public DateTimeOffset UtcNow { get; set; }

    public override DateTimeOffset GetUtcNow() => UtcNow;
}

internal static class TestBusinessCalendar
{
    public static readonly TimeZoneInfo Israel = TimeZoneInfo.FindSystemTimeZoneById(BusinessCalendar.DefaultTimeZoneId);

    /// <summary>06:00 Israel time (UTC+3 in October 2026) on the given October 2026 day.</summary>
    public static DateTimeOffset MorningOf(int octoberDay) => new(2026, 10, octoberDay, 3, 0, 0, TimeSpan.Zero);

    public static BusinessCalendar Create(MutableTimeProvider clock) => new(clock, Israel);
}
