using System.Threading.Channels;
using VehicleCalendarPlanner.Core;
using Xunit;

namespace VehicleCalendarPlanner.Tests;

public sealed class CalendarViewDataTests
{
    private static readonly DateOnly Start = new(2026, 11, 9);
    private static readonly DateOnly End = Start.AddDays(7);
    private static readonly CalendarEntry Entry = new(
        "event", "Family trip", Start.ToDateTime(TimeOnly.MinValue),
        End.ToDateTime(TimeOnly.MinValue), true, null, false);

    [Fact]
    public async Task DatesCanRenderWhileDataIsPendingAndBookingsWaitForSuccess()
    {
        var data = new CalendarViewData();
        var calendar = new ControlledCalendar();
        var load = data.LoadAsync(calendar, "calendar", Start, End);

        Assert.True(data.IsLoading);
        Assert.False(data.IsReady);
        Assert.Empty(data.Entries);
        var request = await calendar.NextAsync();
        Assert.Equal(("calendar", Start, End), (request.CalendarId, request.Start, request.End));
        request.Completion.SetResult([Entry]);

        Assert.True(await load);
        Assert.False(data.IsLoading);
        Assert.True(data.IsReady);
        Assert.Equal(Entry, Assert.Single(data.Entries));
        Assert.Null(data.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededRequestsCannotPublishDataOrErrorsEvenIfCancellationIsIgnored(bool fail)
    {
        var data = new CalendarViewData();
        var calendar = new ControlledCalendar();
        var oldLoad = data.LoadAsync(calendar, "calendar", Start, End);
        var oldRequest = await calendar.NextAsync();
        var latestLoad = data.LoadAsync(calendar, "calendar", End, End.AddDays(7));
        var latestRequest = await calendar.NextAsync();
        Assert.True(oldRequest.Token.IsCancellationRequested);

        if (fail)
            oldRequest.Completion.SetException(new IOException("Old request failed"));
        else
            oldRequest.Completion.SetResult([Entry]);
        Assert.False(await oldLoad);
        Assert.True(data.IsLoading);
        Assert.False(data.IsReady);
        Assert.Empty(data.Entries);
        Assert.Null(data.Error);

        latestRequest.Completion.SetResult([]);
        Assert.True(await latestLoad);
        Assert.True(data.IsReady);
        Assert.Empty(data.Entries);
        Assert.Null(data.Error);
    }

    [Fact]
    public async Task LateOldResponseCannotOverwriteAnAlreadyCompletedNewRange()
    {
        var data = new CalendarViewData();
        var calendar = new ControlledCalendar();
        var oldLoad = data.LoadAsync(calendar, "calendar", Start, End);
        var oldRequest = await calendar.NextAsync();
        var latestLoad = data.LoadAsync(calendar, "calendar", End, End.AddDays(7));
        var latestRequest = await calendar.NextAsync();
        latestRequest.Completion.SetResult([]);
        Assert.True(await latestLoad);

        oldRequest.Completion.SetResult([Entry]);
        Assert.False(await oldLoad);
        Assert.Empty(data.Entries);
        Assert.True(data.IsReady);
        Assert.False(data.IsLoading);
    }

    [Fact]
    public async Task SameRangeRefreshRetainsEventsButDisablesBookingsOnFailureAndCanRecover()
    {
        var data = new CalendarViewData();
        var calendar = new ControlledCalendar();
        await LoadEntryAsync(data, calendar);

        var refresh = data.LoadAsync(calendar, "calendar", Start, End);
        Assert.Equal(Entry, Assert.Single(data.Entries));
        Assert.False(data.IsReady);
        var failure = new IOException("Offline");
        (await calendar.NextAsync()).Completion.SetException(failure);
        Assert.True(await refresh);
        Assert.Same(failure, data.Error);
        Assert.False(data.IsReady);
        Assert.False(data.IsLoading);
        Assert.Equal(Entry, Assert.Single(data.Entries));

        var retry = data.LoadAsync(calendar, "calendar", Start, End);
        (await calendar.NextAsync()).Completion.SetResult([]);
        Assert.True(await retry);
        Assert.True(data.IsReady);
        Assert.Null(data.Error);
        Assert.Empty(data.Entries);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("calendar")]
    [InlineData("provider")]
    public async Task NewRangeOrCalendarNeverShowsPreviousCalendarsEvents(string change)
    {
        var data = new CalendarViewData();
        var calendar = new ControlledCalendar();
        await LoadEntryAsync(data, calendar);

        var provider = change == "provider" ? new ControlledCalendar() : calendar;
        var start = change == "range" ? End : Start;
        var load = data.LoadAsync(provider, change == "calendar" ? "another" : "calendar", start, start.AddDays(7));
        Assert.Empty(data.Entries);
        Assert.False(data.IsReady);
        (await provider.NextAsync()).Completion.SetResult([]);
        Assert.True(await load);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LeavingOrClearingCalendarCancelsPendingLoadAndPreventsLateUpdates(bool clear)
    {
        var data = new CalendarViewData();
        var calendar = new ControlledCalendar();
        var load = data.LoadAsync(calendar, "calendar", Start, End);
        var request = await calendar.NextAsync();

        if (clear)
            data.Clear();
        else
            data.Cancel();
        Assert.True(request.Token.IsCancellationRequested);
        Assert.False(data.IsLoading);
        request.Completion.SetResult([Entry]);

        Assert.False(await load);
        Assert.False(data.IsReady);
        Assert.Empty(data.Entries);
    }

    private static async Task LoadEntryAsync(CalendarViewData data, ControlledCalendar calendar)
    {
        var load = data.LoadAsync(calendar, "calendar", Start, End);
        (await calendar.NextAsync()).Completion.SetResult([Entry]);
        Assert.True(await load);
    }

    private sealed class ControlledCalendar : IPlannerCalendar
    {
        private readonly Channel<Request> _requests = Channel.CreateUnbounded<Request>();

        public Task<Request> NextAsync() =>
            _requests.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public Task<IReadOnlyList<CalendarEntry>> GetEventsAsync(
            string calendarId, DateOnly start, DateOnly endExclusive, CancellationToken cancellationToken = default)
        {
            var request = new Request(calendarId, start, endExclusive, cancellationToken);
            Assert.True(_requests.Writer.TryWrite(request));
            return request.Completion.Task;
        }

        public Task<IReadOnlyList<CalendarInfo>> GetCalendarsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CalendarEntry?> GetEventAsync(string calendarId, string eventId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CalendarEntry> CreateReservationAsync(
            string calendarId, DateOnly date, ReservationDetails reservation, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteEventAsync(string calendarId, CalendarEntry entry, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed record Request(string CalendarId, DateOnly Start, DateOnly End, CancellationToken Token)
    {
        public TaskCompletionSource<IReadOnlyList<CalendarEntry>> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
