using System.Diagnostics;
using Microsoft.Identity.Client;
using Microsoft.Maui.Controls.Shapes;
using VehicleCalendarPlanner.Core;
using VehicleCalendarPlanner.Services;

namespace VehicleCalendarPlanner;

public partial class SettingsPage : ContentPage
{
    private static readonly string[] PersonColors = ["#467A76", "#AD735D", "#647BA4", "#A679A0", "#668B60", "#AF8A45"];
    private static readonly string[] VehicleColors = ["#D9ECE7", "#E8E8F7", "#F8E9DB", "#F7E5D8", "#E5EBD7", "#E4EDF5"];
    private static readonly string[] IconChoices = ["Car", "Bicycle", "Van", "Scooter", "Bus or truck", "Other"];
    private readonly PlannerSession _session;
    private readonly MicrosoftSignIn _signIn;

    public SettingsPage(PlannerSession session, MicrosoftSignIn signIn)
    {
        InitializeComponent();
        _session = session;
        _signIn = signIn;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RenderSettings();
    }

    private async void OnBackClicked(object? sender, EventArgs e) =>
        await RunUiAsync(() => Navigation.PopAsync());

    private async void OnConnectClicked(object? sender, EventArgs e) =>
        await RunUiAsync(async () =>
        {
            if (_session.IsDemo)
            {
                await DisplayAlertAsync("Sample mode", "Return to the calendar and exit the sample before connecting your own calendar.", "OK");
                return;
            }
            ConnectButton.IsEnabled = false;
            try
            {
                var account = await _signIn.SignInAsync();
                var calendars = await _session.Calendar.GetCalendarsAsync();
                var writable = calendars.Where(x => x.CanEdit).ToList();
                if (writable.Count == 0)
                {
                    await DisplayAlertAsync("No editable calendars",
                        "Microsoft did not return an editable calendar for this account. Try signing in as the calendar owner; some shared or group calendars may not be available through Microsoft Graph.", "OK");
                    return;
                }
                var labels = writable.Select((calendar, i) =>
                    $"{i + 1}. {calendar.Name}{(string.IsNullOrWhiteSpace(calendar.Owner) ? "" : $" ({calendar.Owner})")}").ToArray();
                var selection = await DisplayActionSheetAsync($"Choose a calendar for {account}", "Cancel", null, labels);
                var index = Array.IndexOf(labels, selection);
                if (index < 0)
                    return;
                var chosen = writable[index];
                if (!string.IsNullOrWhiteSpace(_session.Settings.CalendarId) && _session.Settings.CalendarId != chosen.Id)
                {
                    var change = await DisplayAlertAsync("Change calendar?",
                        "Existing reservations stay in the old calendar. This dashboard will show reservations from the newly selected calendar.",
                        "Change calendar", "Keep current");
                    if (!change)
                        return;
                }
                var previousId = _session.Settings.CalendarId;
                var previousName = _session.Settings.CalendarName;
                await SaveChangeAsync(
                    () => { _session.Settings.CalendarId = chosen.Id; _session.Settings.CalendarName = chosen.Name; },
                    () => { _session.Settings.CalendarId = previousId; _session.Settings.CalendarName = previousName; });
            }
            catch (MsalClientException ex) when (ex.ErrorCode == "authentication_canceled")
            {
                // The user closed the Microsoft sign-in window.
            }
            finally
            {
                ConnectButton.IsEnabled = true;
            }
        });

    private async void OnDisconnectClicked(object? sender, EventArgs e) =>
        await RunUiAsync(async () =>
        {
            if (_session.IsDemo)
                return;
            var confirm = await DisplayAlertAsync("Disconnect Microsoft?",
                "Reservations stay in the Microsoft calendar. This dashboard will need to be connected again.",
                "Disconnect", "Keep connected");
            if (!confirm)
                return;
            var previousId = _session.Settings.CalendarId;
            var previousName = _session.Settings.CalendarName;
            await SaveChangeAsync(
                () => { _session.Settings.CalendarId = null; _session.Settings.CalendarName = null; },
                () => { _session.Settings.CalendarId = previousId; _session.Settings.CalendarName = previousName; });
            await _signIn.SignOutAsync();
        });

    private async void OnAddPersonClicked(object? sender, EventArgs e) =>
        await RunUiAsync(() => EditPersonAsync(null));

    private async void OnAddVehicleClicked(object? sender, EventArgs e) =>
        await RunUiAsync(() => EditVehicleAsync(null));

    private void RenderSettings()
    {
        ConnectionLabel.Text = _session.IsDemo
            ? "Sample mode: these changes are temporary and never reach your Microsoft calendar."
            : string.IsNullOrWhiteSpace(_session.Settings.CalendarId)
                ? "Not connected. Sign in once, then choose your existing family calendar."
                : $"Selected: {_session.Settings.CalendarName}";
        DisconnectButton.IsVisible = !_session.IsDemo && !string.IsNullOrWhiteSpace(_session.Settings.CalendarId);
        ConnectButton.IsVisible = !_session.IsDemo;

        PeopleList.Clear();
        if (_session.Settings.People.Count == 0)
            PeopleList.Add(EmptyText("No family members added yet."));
        foreach (var person in _session.Settings.People.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name))
        {
            var initial = string.IsNullOrWhiteSpace(person.Name) ? "?" : person.Name[..1].ToUpperInvariant();
            var avatar = new Border
            {
                BackgroundColor = Color.FromArgb(person.Color),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 20 },
                WidthRequest = 40,
                HeightRequest = 40,
                Content = new Label
                {
                    Text = initial,
                    TextColor = Colors.White,
                    FontFamily = "OpenSansSemibold",
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center
                }
            };
            PeopleList.Add(CreateSettingsRow(avatar, person.Name, person.IsActive ? "Family member" : "Hidden",
                person.IsActive, () => EditPersonAsync(person),
                () => TogglePersonAsync(person)));
        }

        VehiclesList.Clear();
        if (_session.Settings.Vehicles.Count == 0)
            VehiclesList.Add(EmptyText("No vehicles added yet."));
        foreach (var vehicle in _session.Settings.Vehicles.OrderByDescending(x => x.IsActive).ThenBy(x => x.Name))
        {
            var image = new Border
            {
                BackgroundColor = Color.FromArgb(vehicle.Color),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 11 },
                WidthRequest = 44,
                HeightRequest = 44,
                Content = new Image { Source = $"vehicle_{SafeIcon(vehicle.Icon)}.png", WidthRequest = 30, HeightRequest = 30 }
            };
            VehiclesList.Add(CreateSettingsRow(image, vehicle.Name,
                vehicle.IsActive ? vehicle.Type : $"{vehicle.Type} - hidden", vehicle.IsActive,
                () => EditVehicleAsync(vehicle), () => ToggleVehicleAsync(vehicle)));
        }
    }

    private View CreateSettingsRow(View icon, string title, string subtitle, bool active, Func<Task> edit, Func<Task> toggle)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(46)), new(GridLength.Star), new(GridLength.Auto), new(GridLength.Auto)
            },
            ColumnSpacing = 8,
            Padding = new Thickness(8, 4)
        };
        grid.Add(icon);
        var text = new VerticalStackLayout { VerticalOptions = LayoutOptions.Center, Spacing = 0 };
        text.Add(new Label { Text = title, FontFamily = "OpenSansSemibold", TextColor = Color.FromArgb("#294E48"), FontSize = 13 });
        text.Add(new Label { Text = subtitle, TextColor = Color.FromArgb("#8A9D96"), FontSize = 11 });
        grid.Add(text, 1);
        var editButton = RowButton("Edit", title);
        editButton.Clicked += async (_, _) => await RunUiAsync(edit);
        grid.Add(editButton, 2);
        var toggleButton = RowButton(active ? "Hide" : "Restore", title);
        toggleButton.Clicked += async (_, _) => await RunUiAsync(toggle);
        grid.Add(toggleButton, 3);
        return new Border
        {
            BackgroundColor = Color.FromArgb("#F7FAF8"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Content = grid
        };
    }

    private static Button RowButton(string text, string itemName)
    {
        var button = new Button
        {
            Text = text,
            BackgroundColor = Colors.Transparent,
            TextColor = Color.FromArgb("#3A796C"),
            FontSize = 12,
            Padding = new Thickness(6),
            MinimumHeightRequest = 44,
            MinimumWidthRequest = 44
        };
        SemanticProperties.SetDescription(button, $"{text} {itemName}");
        return button;
    }

    private async Task EditPersonAsync(FamilyMember? person)
    {
        var name = await DisplayPromptAsync(person is null ? "Add a family member" : "Edit family member",
            "What name should appear on the calendar?", "Save", "Cancel", "Name", 40,
            initialValue: person?.Name);
        if (name is null)
            return;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Please enter a name.");
        if (_session.Settings.People.Any(x => x.Id != person?.Id && SameName(x.Name, name)))
            throw new InvalidOperationException("That name already exists. Restore the hidden person instead if needed.");

        if (person is null)
        {
            var added = new FamilyMember { Name = name, Color = PersonColors[_session.Settings.People.Count % PersonColors.Length] };
            await SaveChangeAsync(() => _session.Settings.People.Add(added), () => _session.Settings.People.Remove(added));
        }
        else
        {
            var oldName = person.Name;
            await SaveChangeAsync(() => person.Name = name, () => person.Name = oldName);
        }
    }

    private async Task EditVehicleAsync(Vehicle? vehicle)
    {
        var name = await DisplayPromptAsync(vehicle is null ? "Add a vehicle" : "Edit vehicle",
            "Give this individual vehicle a name, like Blue car or E-bike 1.", "Next", "Cancel", "Vehicle name", 40,
            initialValue: vehicle?.Name);
        if (name is null)
            return;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Please enter a vehicle name.");
        if (_session.Settings.Vehicles.Any(x => x.Id != vehicle?.Id && SameName(x.Name, name)))
            throw new InvalidOperationException("That vehicle name already exists. Restore the hidden vehicle instead if needed.");
        var type = await DisplayPromptAsync("Type of transport", "You can use any type, such as electric bike or cargo bike.",
            "Next", "Cancel", "Transport type", 40, initialValue: vehicle?.Type);
        if (type is null)
            return;
        type = type.Trim();
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException("Please enter a transport type.");
        var iconChoice = await DisplayActionSheetAsync("Choose a picture for this vehicle", "Cancel", null, IconChoices);
        var iconIndex = Array.IndexOf(IconChoices, iconChoice);
        if (iconIndex < 0)
            return;
        var icon = new[] { "car", "bike", "van", "scooter", "bus", "other" }[iconIndex];

        if (vehicle is null)
        {
            var added = new Vehicle
            {
                Name = name, Type = type, Icon = icon,
                Color = VehicleColors[_session.Settings.Vehicles.Count % VehicleColors.Length]
            };
            await SaveChangeAsync(() => _session.Settings.Vehicles.Add(added), () => _session.Settings.Vehicles.Remove(added));
        }
        else
        {
            var previous = (vehicle.Name, vehicle.Type, vehicle.Icon);
            await SaveChangeAsync(
                () => { vehicle.Name = name; vehicle.Type = type; vehicle.Icon = icon; },
                () => { vehicle.Name = previous.Name; vehicle.Type = previous.Type; vehicle.Icon = previous.Icon; });
        }
    }

    private async Task TogglePersonAsync(FamilyMember person)
    {
        if (person.IsActive && !await DisplayAlertAsync("Hide family member?",
            $"Hide {person.Name} from new reservations? Existing reservations stay on the calendar. You can restore them later.",
            "Hide", "Keep"))
            return;
        await SaveChangeAsync(() => person.IsActive = !person.IsActive, () => person.IsActive = !person.IsActive);
    }

    private async Task ToggleVehicleAsync(Vehicle vehicle)
    {
        if (vehicle.IsActive && !await DisplayAlertAsync("Hide vehicle?",
            $"Hide {vehicle.Name} from new reservations? Existing reservations stay on the calendar. You can restore it later.",
            "Hide", "Keep"))
            return;
        await SaveChangeAsync(() => vehicle.IsActive = !vehicle.IsActive, () => vehicle.IsActive = !vehicle.IsActive);
    }

    private async Task SaveChangeAsync(Action apply, Action undo)
    {
        apply();
        try
        {
            await _session.SaveAsync();
        }
        catch
        {
            undo();
            throw;
        }
        RenderSettings();
    }

    private static bool SameName(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string SafeIcon(string icon) =>
        icon is "car" or "bike" or "van" or "scooter" or "bus" or "other" ? icon : "other";

    private static Label EmptyText(string text) => new()
    {
        Text = text, TextColor = Color.FromArgb("#91A49C"), FontSize = 12, Margin = new Thickness(3, 5)
    };

    private async Task RunUiAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Family Wheels settings failed: {ex}");
            await DisplayAlertAsync("Could not save", ex.Message, "OK");
        }
    }
}
