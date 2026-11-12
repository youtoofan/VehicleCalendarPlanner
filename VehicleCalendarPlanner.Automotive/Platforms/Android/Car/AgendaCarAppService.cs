using Android.App;
using Android.Content;
using Android.Content.PM;
using AndroidX.Car.App;
using AndroidX.Car.App.Validation;

namespace VehicleCalendarPlanner.Automotive.Car;

// There is no Car App Library category for calendars. IOT allows the list/message templates used here and is
// fine for personal use and emulator testing, but Google Play review would likely reject that category.
[Service(Exported = true, Name = "com.companyname.vehiclecalendarplanner.AgendaCarAppService")]
[IntentFilter(["androidx.car.app.CarAppService"], Categories = ["androidx.car.app.category.IOT"])]
public sealed class AgendaCarAppService : CarAppService
{
    // Debug builds accept any host (emulators, desktop head units); release builds only Google's known car hosts.
    public override HostValidator CreateHostValidator() =>
        (ApplicationInfo?.Flags ?? 0).HasFlag(ApplicationInfoFlags.Debuggable)
            ? HostValidator.AllowAllHostsValidator!
            : new HostValidator.Builder(this)
                .AddAllowedHosts(Resource.Array.hosts_allowlist_sample)!
                .Build()!;

    public override Session OnCreateSession() => new AgendaSession();
}

public sealed class AgendaSession : Session
{
    public override Screen OnCreateScreen(Intent? intent) => new AgendaScreen(CarContext!);
}
