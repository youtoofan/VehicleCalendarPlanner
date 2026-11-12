using Android.App;
using Android.Content.PM;

namespace VehicleCalendarPlanner.Automotive;

// The full planner. It is not marked distractionOptimized, so Android Automotive only allows it while parked.
// Landscape-only; on portrait head units the system letterboxes it.
[Activity(Label = "Family Wheels Planner", Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop,
    ScreenOrientation = ScreenOrientation.SensorLandscape,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity;
