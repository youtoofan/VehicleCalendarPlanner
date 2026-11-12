namespace VehicleCalendarPlanner.Core;

public sealed record UpcomingReservation(DateOnly Date, ReservationDetails Reservation);

public static class UpcomingReservations
{
    public static async Task<IReadOnlyList<UpcomingReservation>> LoadAsync(
        IPlannerCalendar calendar, string calendarId, DateOnly start, int days, CancellationToken cancellationToken = default)
    {
        var end = start.AddDays(days);
        var entries = await calendar.GetEventsAsync(calendarId, start, end, cancellationToken);
        return From(entries, start, end);
    }

    // One item per reserved day, so multi-day reservations show up on every day they cover.
    public static IReadOnlyList<UpcomingReservation> From(
        IEnumerable<CalendarEntry> entries, DateOnly start, DateOnly endExclusive)
    {
        var reservations = entries.Where(x => x.Reservation is not null && !x.IsDamagedReservation).ToList();
        var result = new List<UpcomingReservation>();
        for (var date = start; date < endExclusive; date = date.AddDays(1))
        {
            result.AddRange(reservations
                .Where(x => x.OccursOn(date))
                .Select(x => new UpcomingReservation(date, x.Reservation!))
                .OrderBy(x => x.Reservation.VehicleName, StringComparer.CurrentCultureIgnoreCase));
        }
        return result;
    }
}
