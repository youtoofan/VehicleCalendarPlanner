using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VehicleCalendarPlanner.Core;

public static partial class ReservationMarker
{
    public const string SubjectPrefix = "[Family Wheels] ";
    private const string MarkerPrefix = "[FamilyWheels:v1:";

    public static string Subject(ReservationDetails reservation) =>
        $"{SubjectPrefix}{reservation.VehicleName} - {reservation.PersonName}";

    public static string Body(ReservationDetails reservation)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(reservation);
        var encoded = Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"Vehicle: {reservation.VehicleName}\nReserved for: {reservation.PersonName}\n\n" +
               "Managed by Family Wheels. Please leave the code below intact if you edit this event.\n" +
               $"{MarkerPrefix}{encoded}]";
    }

    public static (ReservationDetails? Reservation, bool IsDamaged) Read(string? subject, string? body)
    {
        var text = WebUtility.HtmlDecode(HtmlTag().Replace(body ?? "", ""));
        var match = MarkerPattern().Match(text);
        if (match.Success)
        {
            try
            {
                var encoded = match.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
                var reservation = JsonSerializer.Deserialize<ReservationDetails>(Convert.FromBase64String(encoded));
                if (reservation is not null && reservation.ReservationId != Guid.Empty &&
                    reservation.VehicleId != Guid.Empty && reservation.PersonId != Guid.Empty &&
                    !string.IsNullOrWhiteSpace(reservation.VehicleName) && !string.IsNullOrWhiteSpace(reservation.PersonName))
                    return (reservation, false);
            }
            catch (Exception ex) when (ex is FormatException or JsonException)
            {
                return (null, true);
            }
            return (null, true);
        }

        return (null, (subject?.StartsWith(SubjectPrefix, StringComparison.OrdinalIgnoreCase) ?? false) ||
                      text.Contains(MarkerPrefix, StringComparison.Ordinal));
    }

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\[FamilyWheels:v1:([A-Za-z0-9_-]+)\]")]
    private static partial Regex MarkerPattern();
}
