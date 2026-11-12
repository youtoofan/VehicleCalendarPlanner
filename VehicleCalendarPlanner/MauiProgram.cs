using VehicleCalendarPlanner.Services;

namespace VehicleCalendarPlanner
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp() =>
            MauiApp.CreateBuilder()
                .UseVehicleCalendarPlanner(SignInMode.Interactive)
                .Build();
    }
}
