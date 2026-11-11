using Android.App;
using Android.Content;
using Android.Content.PM;
using Microsoft.Identity.Client;

namespace VehicleCalendarPlanner;

[Activity(Exported = true, LaunchMode = LaunchMode.SingleTask, NoHistory = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
[IntentFilter(new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "msalcb8a627b-0974-40cd-8ced-aa5c8e6659c5",
    DataHost = "vehiclecalendar")]
public sealed class MsalActivity : BrowserTabActivity;
