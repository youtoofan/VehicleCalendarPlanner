using VehicleCalendarPlanner.Core;
using Xunit;

namespace VehicleCalendarPlanner.Tests;

public sealed class ReservationMarkerTests
{
    [Fact]
    public void MarkerSurvivesOutlookHtmlAndSubjectEdits()
    {
        var reservation = new ReservationDetails(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Blue e-bike", "Jamie");
        var html = $"<div>{ReservationMarker.Body(reservation).Replace("\n", "<br>")}</div>";
        var parsed = ReservationMarker.Read("A different title", html);
        Assert.Equal(reservation, parsed.Reservation);
        Assert.False(parsed.IsDamaged);
    }

    [Fact]
    public void MissingOrCorruptMarkerIsNotMistakenForAnAvailableVehicle()
    {
        Assert.True(ReservationMarker.Read("[Family Wheels] Bike - Jamie", "edited").IsDamaged);
        Assert.True(ReservationMarker.Read("Changed title", "[FamilyWheels:v1:bad]").IsDamaged);
        Assert.False(ReservationMarker.Read("Family dinner", "Bring dessert").IsDamaged);
    }

    [Fact]
    public void MultiDayEventDoesNotOccupyItsExclusiveEndDate()
    {
        var entry = new CalendarEntry("id", "Trip", new DateTime(2026, 11, 10), new DateTime(2026, 11, 12),
            true, null, false);
        Assert.True(entry.OccursOn(new DateOnly(2026, 11, 10)));
        Assert.True(entry.OccursOn(new DateOnly(2026, 11, 11)));
        Assert.False(entry.OccursOn(new DateOnly(2026, 11, 12)));
    }
}
