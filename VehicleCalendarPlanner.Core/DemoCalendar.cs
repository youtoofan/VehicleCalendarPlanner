namespace VehicleCalendarPlanner.Core;

public sealed class DemoCalendar : IPlannerCalendar
{
    public const string CalendarId = "family-wheels-sample";
    private readonly List<CalendarEntry> _entries = [];

    public static PlannerSettings CreateSettings() => new()
    {
        CalendarId = CalendarId,
        CalendarName = "Sample family calendar",
        People =
        [
            new() { Name = "Alex", Color = "#467A76" },
            new() { Name = "Sam", Color = "#AD735D" },
            new() { Name = "Jamie", Color = "#647BA4" },
            new() { Name = "Taylor", Color = "#A679A0" }
        ],
        Vehicles =
        [
            new() { Name = "Car 1", Type = "Car", Icon = "car", Color = "#D9ECE7" },
            new() { Name = "Car 2", Type = "Car", Icon = "car", Color = "#E8E8F7" },
            new() { Name = "Car 3", Type = "Car", Icon = "car", Color = "#F8E9DB" },
            new() { Name = "E-bike 1", Type = "Electric bike", Icon = "bike", Color = "#F7E5D8" },
            new() { Name = "E-bike 2", Type = "Electric bike", Icon = "bike", Color = "#E5EBD7" }
        ]
    };

    public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CalendarInfo>>([new(CalendarId, "Sample family calendar", true, null)]);

    public Task<IReadOnlyList<CalendarEntry>> GetEventsAsync(
        string calendarId, DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken = default)
    {
        CheckCalendar(calendarId);
        var entries = _entries.Where(x =>
            DateOnly.FromDateTime(x.Start) < endExclusive && DateOnly.FromDateTime(x.End.AddTicks(-1)) >= start).ToList();
        return Task.FromResult<IReadOnlyList<CalendarEntry>>(entries);
    }

    public Task<CalendarEntry?> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
    {
        CheckCalendar(calendarId);
        return Task.FromResult(_entries.SingleOrDefault(x => x.Id == eventId));
    }

    public Task<CalendarEntry> CreateReservationAsync(
        string calendarId, DateOnly date, ReservationDetails reservation, CancellationToken cancellationToken = default)
    {
        CheckCalendar(calendarId);
        var start = date.ToDateTime(TimeOnly.MinValue);
        var entry = new CalendarEntry(Guid.NewGuid().ToString(), ReservationMarker.Subject(reservation),
            start, start.AddDays(1), true, reservation, false);
        _entries.Add(entry);
        return Task.FromResult(entry);
    }

    public Task DeleteEventAsync(string calendarId, CalendarEntry entry, CancellationToken cancellationToken = default)
    {
        CheckCalendar(calendarId);
        _entries.RemoveAll(x => x.Id == entry.Id);
        return Task.CompletedTask;
    }

    private static void CheckCalendar(string calendarId)
    {
        if (calendarId != CalendarId)
            throw new InvalidOperationException("The sample calendar is not selected.");
    }
}
