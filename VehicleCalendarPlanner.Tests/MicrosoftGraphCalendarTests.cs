using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VehicleCalendarPlanner.Core;
using Xunit;

namespace VehicleCalendarPlanner.Tests;

public sealed class MicrosoftGraphCalendarTests
{
    private static readonly DateOnly Date = new(2026, 11, 11);

    [Fact]
    public async Task CreatesAnAllDayReservationWithItsRecoverableMarker()
    {
        var reservation = NewReservation();
        using var http = CreateClient(async request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/v1.0/me/calendars/calendar%2Fid/events", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);

            var item = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
            Assert.True((bool)item["isAllDay"]!);
            Assert.Equal("free", (string?)item["showAs"]);
            Assert.Equal("2026-11-11T00:00:00", (string?)item["start"]!["dateTime"]);
            Assert.Equal("2026-11-12T00:00:00", (string?)item["end"]!["dateTime"]);
            Assert.Equal("UTC", (string?)item["start"]!["timeZone"]);
            Assert.Equal(reservation.ReservationId.ToString(), (string?)item["transactionId"]);
            item["id"] = "created-event";
            return JsonResponse(item, HttpStatusCode.Created);
        });

        var result = await Calendar(http).CreateReservationAsync("calendar/id", Date, reservation);

        Assert.Equal("created-event", result.Id);
        Assert.Equal(reservation, result.Reservation);
        Assert.True(result.IsAllDay);
        Assert.True(result.OccursOn(Date));
        Assert.False(result.OccursOn(Date.AddDays(1)));
    }

    [Fact]
    public async Task ReadsEveryPageAndDistinguishesReservationsFromOtherEvents()
    {
        var reservation = NewReservation();
        var requests = 0;
        using var http = CreateClient(request =>
        {
            requests++;
            Assert.Equal("test-token", request.Headers.Authorization?.Parameter);
            if (requests == 1)
            {
                var query = Uri.UnescapeDataString(request.RequestUri!.Query);
                Assert.Contains("startDateTime=2026-11-11T00:00:00+00:00", query);
                Assert.Contains("endDateTime=2026-11-12T00:00:00+00:00", query);
                return Task.FromResult(JsonResponse(new Dictionary<string, object>
                {
                    ["value"] = new[] { Event("booking", reservation) },
                    ["@odata.nextLink"] = "https://graph.microsoft.com/v1.0/me/calendars/a/calendarView?$skiptoken=next"
                }));
            }
            Assert.Contains("$skiptoken=next", request.RequestUri!.Query);
            return Task.FromResult(JsonResponse(new
            {
                value = new[] { Event("ordinary"), Event("damaged", damaged: true), Event("cancelled", cancelled: true) }
            }));
        });

        var entries = await Calendar(http).GetEventsAsync("a", Date, Date.AddDays(1));

        Assert.Equal(2, requests);
        Assert.Equal(3, entries.Count);
        Assert.Equal(reservation, entries.Single(x => x.Id == "booking").Reservation);
        Assert.False(entries.Single(x => x.Id == "ordinary").IsDamagedReservation);
        Assert.True(entries.Single(x => x.Id == "damaged").IsDamagedReservation);
    }

    [Theory]
    [InlineData("https://example.com/v1.0/me/calendars")]
    [InlineData("http://graph.microsoft.com/v1.0/me/calendars")]
    [InlineData("https://graph.microsoft.com/v1.0-other/me/calendars")]
    public async Task RefusesUnsafePaginationLinksBeforeSendingTheToken(string nextLink)
    {
        var requests = 0;
        using var http = CreateClient(_ =>
        {
            requests++;
            return Task.FromResult(JsonResponse(new Dictionary<string, object>
            {
                ["value"] = Array.Empty<object>(),
                ["@odata.nextLink"] = nextLink
            }));
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => Calendar(http).GetCalendarsAsync());
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task DamagedReservationBlocksBookingWithoutPosting()
    {
        var settings = DemoCalendar.CreateSettings();
        var requests = 0;
        using var http = CreateClient(request =>
        {
            requests++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(JsonResponse(new { value = new[] { Event("damaged", damaged: true) } }));
        });

        var error = await Assert.ThrowsAsync<BookingConflictException>(() =>
            new PlannerService().BookAsync(Calendar(http), settings, settings.Vehicles[3].Id, settings.People[2].Id, Date));

        Assert.Contains("cannot be checked", error.Message);
        Assert.Equal(1, requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedReservationIsReportedAsUnverifiedWhenFollowUpCannotConfirmIt(bool invalidJson)
    {
        var settings = DemoCalendar.CreateSettings();
        var reads = 0;
        using var http = CreateClient(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var item = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
                item["id"] = "created-event";
                return JsonResponse(item, HttpStatusCode.Created);
            }
            reads++;
            if (reads > 1 && invalidJson)
                return RawResponse("{");
            return JsonResponse(new { value = Array.Empty<object>() });
        });

        var result = await new PlannerService().BookAsync(
            Calendar(http), settings, settings.Vehicles[3].Id, settings.People[2].Id, Date);

        Assert.True(result.VerificationFailed);
        Assert.False(result.HasConflict);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task UnreadableSuccessfulWriteIsReportedAsUncertain()
    {
        using var http = CreateClient(_ => Task.FromResult(RawResponse("{", HttpStatusCode.Created)));
        var error = await Assert.ThrowsAsync<ReservationWriteUncertainException>(() =>
            Calendar(http).CreateReservationAsync("calendar", Date, NewReservation()));
        Assert.Contains("may have been created", error.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ServerFailureDuringWriteIsReportedAsUncertain(HttpStatusCode status)
    {
        using var http = CreateClient(_ => Task.FromResult(JsonResponse(
            new { error = new { message = "Could not complete the request" } }, status)));
        var error = await Assert.ThrowsAsync<ReservationWriteUncertainException>(() =>
            Calendar(http).CreateReservationAsync("calendar", Date, NewReservation()));
        Assert.Contains("may have been created", error.Message);
    }

    [Fact]
    public async Task RivalBookingDetectedAfterSaveIsReportedEvenIfTheNewEventIsNotVisibleYet()
    {
        var settings = DemoCalendar.CreateSettings();
        var rival = new ReservationDetails(Guid.NewGuid(), settings.Vehicles[3].Id, settings.People[3].Id,
            settings.Vehicles[3].Name, settings.People[3].Name);
        var reads = 0;
        using var http = CreateClient(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                var item = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!.AsObject();
                item["id"] = "created-event";
                return JsonResponse(item, HttpStatusCode.Created);
            }
            reads++;
            return JsonResponse(new { value = reads == 1 ? Array.Empty<object>() : new[] { Event("rival", rival) } });
        });

        var result = await new PlannerService().BookAsync(
            Calendar(http), settings, settings.Vehicles[3].Id, settings.People[2].Id, Date);

        Assert.True(result.HasConflict);
        Assert.True(result.VerificationFailed);
    }

    [Fact]
    public async Task CalendarErrorsAreShownAndChangedEventsCannotBeDeleted()
    {
        using var http = CreateClient(request =>
        {
            if (request.Method == HttpMethod.Delete)
            {
                Assert.Equal("W/\"version-1\"", request.Headers.GetValues("If-Match").Single());
                return Task.FromResult(RawResponse("", HttpStatusCode.PreconditionFailed));
            }
            return Task.FromResult(JsonResponse(new { error = new { message = "Access denied" } }, HttpStatusCode.Forbidden));
        });
        var calendar = Calendar(http);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => calendar.GetCalendarsAsync());
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Contains("Access denied", error.Message);

        var entry = new CalendarEntry("id", "booking", Date.ToDateTime(TimeOnly.MinValue),
            Date.AddDays(1).ToDateTime(TimeOnly.MinValue), true, NewReservation(), false, "W/\"version-1\"");
        var changed = await Assert.ThrowsAsync<InvalidOperationException>(() => calendar.DeleteEventAsync("calendar", entry));
        Assert.Contains("changed", changed.Message);
    }

    private static ReservationDetails NewReservation() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Blue e-bike", "Jamie");

    private static object Event(string id, ReservationDetails? reservation = null, bool damaged = false, bool cancelled = false) => new
    {
        id,
        subject = reservation is not null ? ReservationMarker.Subject(reservation)
            : damaged ? "[Family Wheels] edited reservation" : "Family dinner",
        start = new { dateTime = "2026-11-11T00:00:00", timeZone = "UTC" },
        end = new { dateTime = "2026-11-12T00:00:00", timeZone = "UTC" },
        isAllDay = true,
        isCancelled = cancelled,
        body = new { contentType = "text", content = reservation is not null ? ReservationMarker.Body(reservation) : "Edited" }
    };

    private static MicrosoftGraphCalendar Calendar(HttpClient http) => new(http, new TokenProvider(), TimeZoneInfo.Utc);

    private static HttpClient CreateClient(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new StubHandler(send)) { BaseAddress = new Uri("https://graph.microsoft.com/v1.0/") };

    private static HttpResponseMessage JsonResponse(object content, HttpStatusCode status = HttpStatusCode.OK) =>
        RawResponse(JsonSerializer.Serialize(content), status);

    private static HttpResponseMessage RawResponse(string content, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class TokenProvider : IAccessTokenProvider
    {
        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("test-token");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            send(request);
    }
}
