using System.Globalization;
using AndroidX.Car.App;
using AndroidX.Car.App.Model;
using Microsoft.Extensions.Logging;
using VehicleCalendarPlanner.Core;
using VehicleCalendarPlanner.Services;
using CarAction = AndroidX.Car.App.Model.Action;

// The Car App Library binding marks builder results as nullable, but the builders always return themselves or a template.
#pragma warning disable CS8602, CS8603

namespace VehicleCalendarPlanner.Automotive.Car;

/// <summary>Read-only, driving-safe list of upcoming reservations. Booking stays in the parked MAUI planner.</summary>
public sealed class AgendaScreen : Screen
{
    private const int DaysAhead = 7;
    // Hosts cap list length while driving; six is the common minimum guaranteed limit.
    private const int MaxRows = 6;

    private readonly IServiceProvider _services;
    private IReadOnlyList<UpcomingReservation>? _reservations;
    private string? _message;
    private bool _isSample;
    private bool _loading;

    public AgendaScreen(CarContext carContext) : base(carContext)
    {
        _services = IPlatformApplication.Current?.Services
            ?? throw new InvalidOperationException("The MAUI application has not started.");
        _ = LoadAsync();
    }

    public override ITemplate OnGetTemplate()
    {
        var header = new Header.Builder()
            .SetTitle(AppResources.CarAgendaTitle)
            .SetStartHeaderAction(CarAction.AppIcon)
            .AddEndHeaderAction(new CarAction.Builder()
                .SetTitle(AppResources.CarRefresh)
                .SetOnClickListener(new ClickListener(() => _ = LoadAsync()))
                .Build())
            .Build();

        if (_loading && _reservations is null)
            return new ListTemplate.Builder().SetHeader(header).SetLoading(true).Build();

        if (_message is not null)
            return new MessageTemplate.Builder(_message).SetHeader(header).Build();

        var items = new ItemList.Builder().SetNoItemsMessage(AppResources.CarNoReservations);
        foreach (var item in _reservations!.Take(MaxRows))
        {
            var title = string.Format(AppResources.CarReservationTitle, item.Reservation.VehicleName, item.Reservation.PersonName);
            items.AddItem(new Row.Builder()
                .SetTitle(_isSample ? string.Format(AppResources.CarSampleSuffix, title) : title)
                .AddText(FormatDate(item.Date))
                .Build());
        }
        return new ListTemplate.Builder().SetHeader(header).SetSingleList(items.Build()).Build();
    }

    private async Task LoadAsync()
    {
        if (_loading)
            return;
        _loading = true;
        Invalidate();
        try
        {
            var session = _services.GetRequiredService<PlannerSession>();
            // Read saved settings without touching the shared session, which the parked UI may be editing.
            var settings = session.IsDemo ? session.Settings : await _services.GetRequiredService<JsonSettingsStore>().LoadAsync();
            _isSample = session.IsDemo;
            if (string.IsNullOrWhiteSpace(settings.CalendarId))
            {
                Show(null, AppResources.CarSetupRequired);
                return;
            }
            var today = DateOnly.FromDateTime(DateTime.Now);
            Show(await UpcomingReservations.LoadAsync(session.Calendar, settings.CalendarId, today, DaysAhead), null);
        }
        catch (AuthenticationRequiredException)
        {
            Show(null, AppResources.CarSetupRequired);
        }
        catch (Exception ex)
        {
            _services.GetService<ILogger<AgendaScreen>>()?.LogWarning(ex, "Loading the car agenda failed");
            Show(null, AppResources.CarLoadFailed);
        }
        finally
        {
            _loading = false;
            Invalidate();
        }
    }

    private void Show(IReadOnlyList<UpcomingReservation>? reservations, string? message)
    {
        _reservations = reservations;
        _message = message;
    }

    private new void Invalidate() => MainThread.BeginInvokeOnMainThread(base.Invalidate);

    private static string FormatDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var text = date.ToString("dddd d MMMM", CultureInfo.CurrentCulture);
        return date == today ? $"{AppResources.Today} · {text}" : text;
    }

    private sealed class ClickListener(System.Action onClick) : Java.Lang.Object, IOnClickListener
    {
        public void OnClick() => onClick();
    }
}
