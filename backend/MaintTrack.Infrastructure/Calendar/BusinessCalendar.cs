using System;
using MaintTrack.Application.Abstractions;

namespace MaintTrack.Infrastructure.Calendar;

public sealed class BusinessCalendar : IBusinessCalendar
{
    /// <summary>Used when BusinessCalendar:TimeZoneId is not configured.</summary>
    public const string DefaultTimeZoneId = "Asia/Jerusalem";

    private readonly TimeProvider _timeProvider;
    private readonly TimeZoneInfo _timeZone;

    public BusinessCalendar(TimeProvider timeProvider, TimeZoneInfo timeZone)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _timeZone = timeZone ?? throw new ArgumentNullException(nameof(timeZone));
    }

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public DateOnly GetBusinessDate(DateTime utcInstant)
    {
        if (utcInstant.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The instant must be in UTC.", nameof(utcInstant));
        }

        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcInstant, _timeZone));
    }
}
