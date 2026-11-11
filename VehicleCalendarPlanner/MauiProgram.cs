using Microsoft.Extensions.Logging;
using VehicleCalendarPlanner.Core;
using VehicleCalendarPlanner.Services;

namespace VehicleCalendarPlanner
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            builder.Services.AddSingleton(_ => new HttpClient
            {
                BaseAddress = new Uri("https://graph.microsoft.com/v1.0/"),
                Timeout = TimeSpan.FromSeconds(25)
            });
            builder.Services.AddSingleton(_ => new JsonSettingsStore(
                Path.Combine(FileSystem.Current.AppDataDirectory, "family-wheels.json")));
            builder.Services.AddSingleton<MicrosoftSignIn>();
            builder.Services.AddSingleton(sp => new MicrosoftGraphCalendar(
                sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<MicrosoftSignIn>(), TimeZoneInfo.Local));
            builder.Services.AddSingleton<PlannerSession>();
            builder.Services.AddSingleton<PlannerService>();
            builder.Services.AddTransient<SettingsPage>();
            builder.Services.AddTransient<MainPage>();

#if DEBUG
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
