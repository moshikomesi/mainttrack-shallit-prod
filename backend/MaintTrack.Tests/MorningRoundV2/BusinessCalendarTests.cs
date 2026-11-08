using MaintTrack.Infrastructure.Calendar;

namespace MaintTrack.Tests.MorningRoundV2;

public sealed class BusinessCalendarTests
{
    [Theory]
    // Israel is UTC+3 until DST ends on 25 Oct 2026, UTC+2 afterwards.
    [InlineData("2026-10-19T20:59:59Z", "2026-10-19")]
    [InlineData("2026-10-19T21:00:00Z", "2026-10-20")]
    [InlineData("2026-10-20T03:00:00Z", "2026-10-20")]
    [InlineData("2026-11-02T21:59:59Z", "2026-11-02")]
    [InlineData("2026-11-02T22:00:00Z", "2026-11-03")]
    public void BusinessDate_IsTheLocalDateInTheBusinessTimeZone(string utcInstant, string expectedDate)
    {
        var instant = DateTimeOffset.Parse(utcInstant).UtcDateTime;
        var calendar = TestBusinessCalendar.Create(new MutableTimeProvider(instant));

        Assert.Equal(DateOnly.Parse(expectedDate), calendar.GetBusinessDate(instant));
    }

    [Fact]
    public void UtcNow_ComesFromTheTimeProvider()
    {
        var clock = new MutableTimeProvider(TestBusinessCalendar.MorningOf(20));
        var calendar = TestBusinessCalendar.Create(clock);

        Assert.Equal(clock.UtcNow.UtcDateTime, calendar.UtcNow);
        Assert.Equal(DateTimeKind.Utc, calendar.UtcNow.Kind);
    }

    [Fact]
    public void NonUtcInstant_IsRejected()
    {
        var calendar = TestBusinessCalendar.Create(new MutableTimeProvider(TestBusinessCalendar.MorningOf(20)));

        Assert.Throws<ArgumentException>(() => calendar.GetBusinessDate(new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Local)));
        Assert.Throws<ArgumentException>(() => calendar.GetBusinessDate(new DateTime(2026, 10, 20, 6, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void DefaultTimeZone_IsResolvable()
    {
        Assert.NotNull(TimeZoneInfo.FindSystemTimeZoneById(BusinessCalendar.DefaultTimeZoneId));
    }
}
