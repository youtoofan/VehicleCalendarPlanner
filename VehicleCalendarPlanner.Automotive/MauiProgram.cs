using VehicleCalendarPlanner.Services;

namespace VehicleCalendarPlanner.Automotive;

public static class MauiProgram
{
    // Cars rarely have a browser for the Microsoft sign-in page, so sign in on a phone with a code instead.
    public static MauiApp CreateMauiApp() =>
        MauiApp.CreateBuilder()
            .UseVehicleCalendarPlanner(SignInMode.DeviceCode)
            .Build();
}
