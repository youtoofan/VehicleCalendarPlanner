using VehicleCalendarPlanner.Core;

namespace VehicleCalendarPlanner.Services;

public sealed class PlannerSession(JsonSettingsStore store, MicrosoftGraphCalendar microsoftCalendar)
{
    private DemoCalendar? _demo;
    public PlannerSettings Settings { get; private set; } = new();
    public bool IsDemo => _demo is not null;
    public IPlannerCalendar Calendar => _demo is not null ? _demo : microsoftCalendar;

    public async Task LoadAsync() => Settings = await store.LoadAsync();

    public Task SaveAsync() => IsDemo ? Task.CompletedTask : store.SaveAsync(Settings);

    public void StartDemo()
    {
        _demo = new DemoCalendar();
        Settings = DemoCalendar.CreateSettings();
    }

    public async Task LeaveDemoAsync()
    {
        var savedSettings = await store.LoadAsync();
        _demo = null;
        Settings = savedSettings;
    }
}
