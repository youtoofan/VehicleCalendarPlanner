namespace VehicleCalendarPlanner.Core;

public sealed class PlannerSettings
{
    public string? CalendarId { get; set; }
    public string? CalendarName { get; set; }
    public List<FamilyMember> People { get; set; } = [];
    public List<Vehicle> Vehicles { get; set; } = [];
}

public sealed class FamilyMember
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#527C79";
    public bool IsActive { get; set; } = true;
}

public sealed class Vehicle
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string Icon { get; set; } = "car";
    public string Color { get; set; } = "#DDEDE8";
    public bool IsActive { get; set; } = true;
}

public sealed record CalendarInfo(string Id, string Name, bool CanEdit, string? Owner);

public sealed record ReservationDetails(
    Guid ReservationId,
    Guid VehicleId,
    Guid PersonId,
    string VehicleName,
    string PersonName);

public sealed record CalendarEntry(
    string Id,
    string Subject,
    DateTime Start,
    DateTime End,
    bool IsAllDay,
    ReservationDetails? Reservation,
    bool IsDamagedReservation,
    string? ETag = null)
{
    public bool OccursOn(DateOnly date)
    {
        var first = DateOnly.FromDateTime(Start);
        var last = DateOnly.FromDateTime(End.AddTicks(-1));
        return date >= first && date <= last;
    }
}

public sealed record BookingResult(CalendarEntry Entry, bool HasConflict, bool VerificationFailed);
