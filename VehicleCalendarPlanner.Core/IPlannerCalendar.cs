namespace VehicleCalendarPlanner.Core;

public interface IPlannerCalendar
{
    Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CalendarEntry>> GetEventsAsync(
        string calendarId, DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken = default);
    Task<CalendarEntry?> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default);
    Task<CalendarEntry> CreateReservationAsync(
        string calendarId, DateOnly date, ReservationDetails reservation, CancellationToken cancellationToken = default);
    Task DeleteEventAsync(string calendarId, CalendarEntry entry, CancellationToken cancellationToken = default);
}

public interface IAccessTokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthenticationRequiredException : Exception
{
    public AuthenticationRequiredException() : base("Connect the shared Microsoft account in Settings first.") { }
}

public sealed class BookingConflictException(string message) : Exception(message);
