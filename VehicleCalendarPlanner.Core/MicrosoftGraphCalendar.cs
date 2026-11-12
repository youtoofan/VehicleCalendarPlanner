using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace VehicleCalendarPlanner.Core;

public sealed class MicrosoftGraphCalendar(HttpClient http, IAccessTokenProvider tokenProvider, TimeZoneInfo timeZone) : IPlannerCalendar
{
    private readonly string _graphTimeZone = TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZone.Id, out var windowsId)
        ? windowsId
        : timeZone.Id;

    public async Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default)
    {
        var items = await GetCollectionAsync("me/calendars?$top=100", cancellationToken);
        return items.Select(item =>
        {
            var owner = item.TryGetProperty("owner", out var ownerJson) && ownerJson.ValueKind == JsonValueKind.Object
                ? OptionalString(ownerJson, "name") ?? OptionalString(ownerJson, "address")
                : null;
            return new CalendarInfo(RequiredString(item, "id"), OptionalString(item, "name") ?? CoreResources.UnnamedCalendar,
                item.TryGetProperty("canEdit", out var canEdit) && canEdit.ValueKind == JsonValueKind.True, owner);
        }).ToList();
    }

    public async Task<IReadOnlyList<CalendarEntry>> GetEventsAsync(
        string calendarId, DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken = default)
    {
        if (endExclusive <= start)
            throw new ArgumentException("The calendar range must have a positive length.", nameof(endExclusive));

        var startValue = Uri.EscapeDataString(ToOffset(start));
        var endValue = Uri.EscapeDataString(ToOffset(endExclusive));
        var route = $"me/calendars/{Uri.EscapeDataString(calendarId)}/calendarView" +
                    $"?startDateTime={startValue}&endDateTime={endValue}&$top=100" +
                    "&$select=id,subject,start,end,isAllDay,isCancelled,body";
        var items = await GetCollectionAsync(route, cancellationToken);
        return items.Where(x => !x.TryGetProperty("isCancelled", out var cancelled) || cancelled.ValueKind != JsonValueKind.True)
            .Select(ParseEvent).ToList();
    }

    public async Task<CalendarEntry?> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default)
    {
        var route = EventRoute(calendarId, eventId);
        using var request = await CreateRequestAsync(HttpMethod.Get, route, cancellationToken);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        return ParseEvent(document.RootElement);
    }

    public async Task<CalendarEntry> CreateReservationAsync(
        string calendarId, DateOnly date, ReservationDetails reservation, CancellationToken cancellationToken = default)
    {
        var start = date.ToDateTime(TimeOnly.MinValue);
        var payload = new
        {
            subject = ReservationMarker.Subject(reservation),
            body = new { contentType = "text", content = ReservationMarker.Body(reservation) },
            start = new { dateTime = start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), timeZone = _graphTimeZone },
            end = new { dateTime = start.AddDays(1).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture), timeZone = _graphTimeZone },
            isAllDay = true,
            isReminderOn = false,
            showAs = "free",
            transactionId = reservation.ReservationId.ToString()
        };
        using var request = await CreateRequestAsync(HttpMethod.Post,
            $"me/calendars/{Uri.EscapeDataString(calendarId)}/events", cancellationToken);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new ReservationWriteUncertainException(
                CoreResources.SaveInterrupted, ex);
        }

        using (response)
        {
            try
            {
                await EnsureSuccessAsync(response, cancellationToken);
            }
            catch (HttpRequestException ex) when (response.StatusCode == HttpStatusCode.RequestTimeout ||
                                                  (int)response.StatusCode >= 500)
            {
                throw new ReservationWriteUncertainException(
                    CoreResources.SaveNotConfirmed, ex);
            }
            try
            {
                using var document = await ReadJsonAsync(response, cancellationToken);
                var entry = ParseEvent(document.RootElement);
                if (entry.Reservation?.ReservationId != reservation.ReservationId)
                    throw new InvalidDataException(CoreResources.SavedEventMissingDetails);
                return entry;
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or
                                       HttpRequestException or TaskCanceledException)
            {
                throw new ReservationWriteUncertainException(
                    CoreResources.SaveResultUnverified, ex);
            }
        }
    }

    public async Task DeleteEventAsync(string calendarId, CalendarEntry entry, CancellationToken cancellationToken = default)
    {
        using var request = await CreateRequestAsync(HttpMethod.Delete, EventRoute(calendarId, entry.Id), cancellationToken);
        if (!string.IsNullOrWhiteSpace(entry.ETag))
            request.Headers.TryAddWithoutValidation("If-Match", entry.ETag);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return;
        if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            throw new InvalidOperationException(CoreResources.EventChangedInOutlook);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<List<JsonElement>> GetCollectionAsync(string route, CancellationToken cancellationToken)
    {
        var results = new List<JsonElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? next = route;
        while (next is not null)
        {
            var uri = ResolveGraphUri(next);
            if (!seen.Add(uri.AbsoluteUri))
                throw new InvalidDataException(CoreResources.GraphRepeatedPageLink);
            using var request = await CreateRequestAsync(HttpMethod.Get, uri.AbsoluteUri, cancellationToken);
            using var response = await http.SendAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("value", out var values) || values.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException(CoreResources.GraphIncompleteResponse);
            foreach (var value in values.EnumerateArray())
            {
                if (value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException(CoreResources.GraphInvalidItem);
                results.Add(value.Clone());
            }
            if (document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink))
            {
                next = nextLink.ValueKind == JsonValueKind.String ? nextLink.GetString() : null;
                if (string.IsNullOrWhiteSpace(next))
                    throw new InvalidDataException(CoreResources.GraphInvalidPageLink);
            }
            else
                next = null;
        }
        return results;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(HttpMethod method, string route, CancellationToken cancellationToken)
    {
        var uri = ResolveGraphUri(route);
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("Prefer",
            $"outlook.timezone=\"{_graphTimeZone}\", outlook.body-content-type=\"text\"");
        return request;
    }

    private Uri ResolveGraphUri(string route)
    {
        var baseAddress = http.BaseAddress ?? throw new InvalidOperationException("The Microsoft Graph address is not configured.");
        var uri = new Uri(baseAddress, route);
        if (uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, baseAddress.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != baseAddress.Port ||
            !uri.AbsolutePath.StartsWith(baseAddress.AbsolutePath, StringComparison.Ordinal))
            throw new InvalidDataException(CoreResources.GraphUnsafePageLink);
        return uri;
    }

    private string ToOffset(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
    }

    private static string EventRoute(string calendarId, string eventId) =>
        $"me/calendars/{Uri.EscapeDataString(calendarId)}/events/{Uri.EscapeDataString(eventId)}";

    private static CalendarEntry ParseEvent(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(CoreResources.GraphInvalidEvent);
        var id = RequiredString(item, "id");
        var subject = OptionalString(item, "subject") ?? CoreResources.NoTitle;
        var start = ParseDateTime(item, "start");
        var end = ParseDateTime(item, "end");
        if (end <= start)
            throw new InvalidDataException(string.Format(CoreResources.EventInvalidDateRange, subject));
        var body = item.TryGetProperty("body", out var bodyJson) && bodyJson.ValueKind == JsonValueKind.Object
            ? OptionalString(bodyJson, "content")
            : null;
        var (reservation, damaged) = ReservationMarker.Read(subject, body);
        var allDay = item.TryGetProperty("isAllDay", out var isAllDay) && isAllDay.ValueKind == JsonValueKind.True;
        return new CalendarEntry(id, subject, start, end, allDay, reservation, damaged, OptionalString(item, "@odata.etag"));
    }

    private static DateTime ParseDateTime(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Object ||
            !DateTime.TryParse(OptionalString(value, "dateTime"), CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var dateTime))
            throw new InvalidDataException(string.Format(CoreResources.EventInvalidTime, property));
        return DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified);
    }

    private static string RequiredString(JsonElement item, string property) =>
        OptionalString(item, property) is { Length: > 0 } value
            ? value
            : throw new InvalidDataException(string.Format(CoreResources.GraphFieldMissing, property));

    private static string? OptionalString(JsonElement item, string property) =>
        item.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;
        var message = string.Format(CoreResources.GraphRequestFailed, (int)response.StatusCode);
        try
        {
            using var document = await ReadJsonAsync(response, cancellationToken);
            if (document.RootElement.TryGetProperty("error", out var error))
                message = OptionalString(error, "message") ?? message;
        }
        catch (JsonException)
        {
            // Some gateways return an HTML error page instead of a Graph error.
        }
        throw new HttpRequestException(message, null, response.StatusCode);
    }
}

public sealed class ReservationWriteUncertainException(string message, Exception? inner = null) : Exception(message, inner);
