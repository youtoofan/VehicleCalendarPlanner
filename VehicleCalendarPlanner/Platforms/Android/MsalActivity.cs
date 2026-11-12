using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Identity.Client;
using VehicleCalendarPlanner.Services;

namespace VehicleCalendarPlanner;

[Activity(Exported = true, LaunchMode = LaunchMode.SingleTask, NoHistory = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = MicrosoftSignIn.AndroidRedirectScheme,
    DataHost = MicrosoftSignIn.AndroidRedirectHost)]
public sealed class MsalActivity : BrowserTabActivity;
