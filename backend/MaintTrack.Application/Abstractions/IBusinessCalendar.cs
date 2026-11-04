using System;

namespace MaintTrack.Application.Abstractions;

/// <summary>
/// Server-authoritative clock and business day. The business day is the calendar date in the
/// configured business time zone (the factory's local time), never a date supplied by the client.
/// </summary>
public interface IBusinessCalendar
{
    DateTime UtcNow { get; }

    DateOnly GetBusinessDate(DateTime utcInstant);
}
