# Family Wheels

A shared vehicle calendar for an Android tablet or Windows dashboard. Family members do not need individual app logins: one Microsoft account connects the dashboard, and each person chooses their name when reserving.

Open **Settings > Connect / choose calendar**, sign in with a personal Microsoft account, and choose an editable calendar. Then add each family member and each individual car, bicycle, or other vehicle. The transport type and picture are configurable; vehicles and people can be hidden and restored.

The app lists calendars returned as editable by Microsoft Graph. Some Outlook family/group or shared calendars may not be exposed or writable this way. If the existing family calendar is missing or fails to sync, sign in as its owner; alternatively, create a calendar owned by the connected account in Outlook, share it with the family, and select it in the app. Actual access depends on Microsoft's account and sharing configuration.

## Use the dashboard

Choose **Week** or **Month**. Drag a vehicle picture or name onto a day, or tap a vehicle and then the day; choose who needs it. In month view, each colored vehicle icon is paired with the person's colored initials; `+N` indicates more events. Select a day for full names, and scroll its details if needed. Tap a reservation to cancel it. The selected day's vehicle list shows availability, and a saving indicator appears while changes sync. **Sync** refreshes immediately; the open dashboard also refreshes periodically. Booking is disabled when the calendar cannot be synced.

**Try sample** opens a separate, temporary family of four with three cars and two e-bikes. It never writes to Microsoft or changes saved settings; sample bookings disappear on exit.

Reservations are all-day Outlook events. Other events are displayed read-only. Keep the Family Wheels code in each reservation's event body intact: if a damaged reservation is detected, that day is blocked until the event is repaired or removed in Outlook.

The dashboard checks availability before and after each booking and serializes its own operations. Microsoft Graph does not provide an atomic one-vehicle-per-day lock across independent devices or Outlook edits: simultaneous external changes can still produce conflicts, which need manual resolution. Family names and vehicle definitions are stored only on this device, not synced; use consistent vehicle names on any other dashboard. Everyone with access to the dashboard can make or cancel reservations and change settings.

## Build and test

Install the .NET 11 SDK and MAUI Android and Windows workloads. From this folder in PowerShell:

```powershell
dotnet test .\VehicleCalendarPlanner.Tests\VehicleCalendarPlanner.Tests.csproj
dotnet build .\VehicleCalendarPlanner\VehicleCalendarPlanner.csproj -f net11.0-windows10.0.19041.0 -r win-arm64
dotnet run --project .\VehicleCalendarPlanner\VehicleCalendarPlanner.csproj -f net11.0-windows10.0.19041.0 -r win-arm64
dotnet build .\VehicleCalendarPlanner\VehicleCalendarPlanner.csproj -f net11.0-android -c Debug -p:AndroidEnableFastDeployment=false
```

Use `win-x64` instead of `win-arm64` on an x64 Windows machine. The Android command creates a development-signed, standalone APK at `VehicleCalendarPlanner\bin\Debug\net11.0-android\com.companyname.vehiclecalendarplanner-Signed.apk`. It is for device testing, not production distribution; a release requires your own signing key.

Windows sample interactions and the core booking/Graph tests have been exercised. Real Microsoft sign-in and sync, and Android device behavior, must still be verified with the account and a physical device.

## Projects

- `VehicleCalendarPlanner.Core` – booking rules, Microsoft Graph calendar access and settings storage (no UI).
- `VehicleCalendarPlanner.Common` – the shared MAUI app: pages, sign-in, styles, strings, images and fonts.
- `VehicleCalendarPlanner` – Android tablet/phone and Windows app.
- `VehicleCalendarPlanner.Automotive` – Android Automotive OS app (for example Volvo with Google built-in).

## Android Automotive

The automotive app has two launcher entries:

- **Family Wheels** – a read-only list of reservations for the coming week, built with the Car App Library so it can be used while driving.
- **Family Wheels Planner** – the full planner, locked to landscape. Android Automotive only allows it while parked. On portrait screens the system letterboxes it.

The car has no usable browser for Microsoft sign-in, so the automotive app uses a device code instead. Open **Family Wheels Planner > Settings > Connect / choose calendar** while parked, then open the shown address on your phone and enter the code. The app registration must allow public client flows (Azure portal: **App registrations > Authentication > Allow public client flows**).

```powershell
dotnet build .\VehicleCalendarPlanner.Automotive\VehicleCalendarPlanner.Automotive.csproj -c Debug -p:AndroidEnableFastDeployment=false
```

Test it with an Android Automotive emulator: in Android Studio's SDK Manager, install an *Automotive* system image (or Volvo's/Polestar's emulator add-on), create an automotive AVD, and install the APK with `adb install`. Production cars do not allow sideloading. Getting the app into a real car requires Google Play, and Play only accepts certain car app categories. The car agenda declares the IoT category because Play has no calendar category, so review is likely to reject it.
