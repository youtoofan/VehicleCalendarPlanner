using System.Text.Json;

namespace VehicleCalendarPlanner.Core;

public sealed class PlannerService
{
    private readonly SemaphoreSlim _bookingGate = new(1, 1);

    public async Task<BookingResult> BookAsync(
        IPlannerCalendar calendar,
        PlannerSettings settings,
        Guid vehicleId,
        Guid personId,
        DateOnly date,
        CancellationToken cancellationToken = default)
    {
        await _bookingGate.WaitAsync(cancellationToken);
        try
        {
            var calendarId = RequireCalendar(settings);
            var vehicle = settings.Vehicles.SingleOrDefault(x => x.Id == vehicleId && x.IsActive)
                ?? throw new InvalidOperationException("That vehicle is no longer available. Open Settings to check it.");
            var person = settings.People.SingleOrDefault(x => x.Id == personId && x.IsActive)
                ?? throw new InvalidOperationException("That family member is no longer active. Open Settings to check them.");

            var entries = await calendar.GetEventsAsync(calendarId, date, date.AddDays(1), cancellationToken);
            EnsureAvailable(entries, vehicle, date);

            var reservation = new ReservationDetails(Guid.NewGuid(), vehicle.Id, person.Id, vehicle.Name, person.Name);
            var created = await calendar.CreateReservationAsync(calendarId, date, reservation, cancellationToken);

            try
            {
                var updated = await calendar.GetEventsAsync(calendarId, date, date.AddDays(1), cancellationToken);
                var matching = updated.Where(x => x.OccursOn(date) && MatchesVehicle(x.Reservation, vehicle)).ToList();
                var foundCreated = matching.Any(x => x.Reservation!.ReservationId == reservation.ReservationId);
                var hasConflict = matching.Count > 1 ||
                    matching.Any(x => x.Reservation!.ReservationId != reservation.ReservationId) ||
                    updated.Any(x => x.OccursOn(date) && x.IsDamagedReservation);
                return new BookingResult(created, hasConflict, !foundCreated);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or
                                       JsonException or AuthenticationRequiredException or TaskCanceledException)
            {
                return new BookingResult(created, false, true);
            }
        }
        finally
        {
            _bookingGate.Release();
        }
    }

    public async Task CancelAsync(
        IPlannerCalendar calendar,
        PlannerSettings settings,
        CalendarEntry entry,
        CancellationToken cancellationToken = default)
    {
        await _bookingGate.WaitAsync(cancellationToken);
        try
        {
            var calendarId = RequireCalendar(settings);
            var current = await calendar.GetEventAsync(calendarId, entry.Id, cancellationToken);
            if (current is null)
                return;
            if (current.Reservation is null || current.Reservation.ReservationId != entry.Reservation?.ReservationId)
                throw new InvalidOperationException("This event changed in Outlook. Refresh before removing it.");

            await calendar.DeleteEventAsync(calendarId, current, cancellationToken);
        }
        finally
        {
            _bookingGate.Release();
        }
    }

    public static bool MatchesVehicle(ReservationDetails? reservation, Vehicle vehicle) =>
        reservation is not null &&
        (reservation.VehicleId == vehicle.Id ||
         string.Equals(reservation.VehicleName.Trim(), vehicle.Name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static void EnsureAvailable(IEnumerable<CalendarEntry> entries, Vehicle vehicle, DateOnly date)
    {
        var onDay = entries.Where(x => x.OccursOn(date)).ToList();
        if (onDay.Any(x => x.IsDamagedReservation))
            throw new BookingConflictException("A vehicle reservation on this day has been edited in Outlook and cannot be checked. Restore its Family Wheels code or remove that event in Outlook first.");

        var existing = onDay.FirstOrDefault(x => MatchesVehicle(x.Reservation, vehicle));
        if (existing?.Reservation is { } reservation)
            throw new BookingConflictException($"{vehicle.Name} is already reserved for {reservation.PersonName} on {date:D}.");
    }

    private static string RequireCalendar(PlannerSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.CalendarId)
            ? settings.CalendarId
            : throw new InvalidOperationException("Choose the family calendar in Settings first.");
}
