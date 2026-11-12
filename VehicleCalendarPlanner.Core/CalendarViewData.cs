namespace VehicleCalendarPlanner.Core;

public sealed class CalendarViewData
{
    private CancellationTokenSource? _request;
    private IPlannerCalendar? _calendar;
    private string? _calendarId;
    private (DateOnly Start, DateOnly End) _range;

    public IReadOnlyList<CalendarEntry> Entries { get; private set; } = [];
    public bool IsLoading => _request is not null;
    public bool IsReady { get; private set; }
    public Exception? Error { get; private set; }

    // Returns false when a newer request (or leaving the page) supersedes this load.
    public async Task<bool> LoadAsync(
        IPlannerCalendar calendar, string calendarId, DateOnly start, DateOnly end)
    {
        Cancel();
        using var request = new CancellationTokenSource();
        _request = request;
        if (!ReferenceEquals(calendar, _calendar) || calendarId != _calendarId || _range != (start, end))
            Entries = [];
        _calendar = calendar;
        _calendarId = calendarId;
        _range = (start, end);
        IsReady = false;
        Error = null;
        try
        {
            // Let the caller draw the date grid before even a synchronous provider does work.
            await Task.Yield();
            request.Token.ThrowIfCancellationRequested();
            var entries = await calendar.GetEventsAsync(calendarId, start, end, request.Token);
            if (!ReferenceEquals(_request, request))
                return false;
            Entries = entries;
            IsReady = true;
            return true;
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_request, request))
                return false;
            Error = ex;
            return true;
        }
        finally
        {
            if (ReferenceEquals(_request, request))
                _request = null;
        }
    }

    public void Cancel()
    {
        var request = _request;
        _request = null;
        IsReady = false;
        request?.Cancel();
    }

    public void Clear()
    {
        Cancel();
        Entries = [];
        Error = null;
        _calendar = null;
        _calendarId = null;
    }
}
