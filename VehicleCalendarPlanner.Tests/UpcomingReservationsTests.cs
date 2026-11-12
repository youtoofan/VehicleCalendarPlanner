using VehicleCalendarPlanner.Core;
using Xunit;

namespace VehicleCalendarPlanner.Tests;

public sealed class UpcomingReservationsTests
{
    private static readonly DateOnly Start = new(2026, 11, 12);

    private static CalendarEntry Entry(string vehicle, DateOnly first, int days, bool damaged = false, bool reservation = true) =>
        new($"{vehicle}-{first}", vehicle, first.ToDateTime(TimeOnly.MinValue), first.AddDays(days).ToDateTime(TimeOnly.MinValue), true,
            reservation ? new ReservationDetails(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), vehicle, "Wim") : null, damaged);

    [Fact]
    public void ListsReservationsPerDayInRangeSortedByVehicle()
    {
        var entries = new[]
        {
            Entry("Volvo", Start.AddDays(-1), 3),
            Entry("Bike", Start, 1),
            Entry("Out of range", Start.AddDays(2), 1),
            Entry("Edited", Start, 1, damaged: true),
            Entry("Birthday", Start, 1, reservation: false),
        };

        var result = UpcomingReservations.From(entries, Start, Start.AddDays(2));

        Assert.Equal(
            [(Start, "Bike"), (Start, "Volvo"), (Start.AddDays(1), "Volvo")],
            result.Select(x => (x.Date, x.Reservation.VehicleName)));
    }
}
