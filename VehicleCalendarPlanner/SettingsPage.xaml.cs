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
    private static string[] IconChoices =>
        [AppResources.IconCar, AppResources.IconBicycle, AppResources.IconVan, AppResources.IconScooter, AppResources.IconBus, AppResources.IconOther];
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
                await DisplayAlertAsync(AppResources.SampleMode, AppResources.ExitSampleFirst, AppResources.OK);
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
                    await DisplayAlertAsync(AppResources.NoEditableCalendars,
                        AppResources.NoEditableCalendarsMessage, AppResources.OK);
                    return;
                }
                var labels = writable.Select((calendar, i) =>
                    $"{i + 1}. {calendar.Name}{(string.IsNullOrWhiteSpace(calendar.Owner) ? "" : $" ({calendar.Owner})")}").ToArray();
                var selection = await DisplayActionSheetAsync(string.Format(AppResources.ChooseCalendarFor, account), AppResources.Cancel, null, labels);
                var index = Array.IndexOf(labels, selection);
                if (index < 0)
                    return;
                var chosen = writable[index];
                if (!string.IsNullOrWhiteSpace(_session.Settings.CalendarId) && _session.Settings.CalendarId != chosen.Id)
                {
                    var change = await DisplayAlertAsync(AppResources.ChangeCalendarTitle,
                        AppResources.ChangeCalendarMessage,
                        AppResources.ChangeCalendar, AppResources.KeepCurrent);
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
            var confirm = await DisplayAlertAsync(AppResources.DisconnectTitle,
                AppResources.DisconnectMessage,
                AppResources.Disconnect, AppResources.KeepConnected);
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
            ? AppResources.SampleModeSettings
            : string.IsNullOrWhiteSpace(_session.Settings.CalendarId)
                ? AppResources.NotConnected
                : string.Format(AppResources.SelectedCalendar, _session.Settings.CalendarName);
        DisconnectButton.IsVisible = !_session.IsDemo && !string.IsNullOrWhiteSpace(_session.Settings.CalendarId);
        ConnectButton.IsVisible = !_session.IsDemo;

        PeopleList.Clear();
        if (_session.Settings.People.Count == 0)
            PeopleList.Add(EmptyText(AppResources.NoFamilyMembers));
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
            PeopleList.Add(CreateSettingsRow(avatar, person.Name, person.IsActive ? AppResources.FamilyMember : AppResources.Hidden,
                person.IsActive, () => EditPersonAsync(person),
                () => TogglePersonAsync(person)));
        }

        VehiclesList.Clear();
        if (_session.Settings.Vehicles.Count == 0)
            VehiclesList.Add(EmptyText(AppResources.NoVehiclesAdded));
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
                vehicle.IsActive ? vehicle.Type : string.Format(AppResources.TypeHidden, vehicle.Type), vehicle.IsActive,
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
        var editButton = RowButton(AppResources.Edit, title);
        editButton.Clicked += async (_, _) => await RunUiAsync(edit);
        grid.Add(editButton, 2);
        var toggleButton = RowButton(active ? AppResources.Hide : AppResources.Restore, title);
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
        var name = await DisplayPromptAsync(person is null ? AppResources.AddFamilyMember : AppResources.EditFamilyMember,
            AppResources.NamePrompt, AppResources.Save, AppResources.Cancel, AppResources.NamePlaceholder, 40,
            initialValue: person?.Name);
        if (name is null)
            return;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(AppResources.EnterName);
        if (_session.Settings.People.Any(x => x.Id != person?.Id && SameName(x.Name, name)))
            throw new InvalidOperationException(AppResources.NameExists);

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
        var name = await DisplayPromptAsync(vehicle is null ? AppResources.AddVehicle : AppResources.EditVehicle,
            AppResources.VehicleNamePrompt, AppResources.Next, AppResources.Cancel, AppResources.VehicleNamePlaceholder, 40,
            initialValue: vehicle?.Name);
        if (name is null)
            return;
        name = name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(AppResources.EnterVehicleName);
        if (_session.Settings.Vehicles.Any(x => x.Id != vehicle?.Id && SameName(x.Name, name)))
            throw new InvalidOperationException(AppResources.VehicleNameExists);
        var type = await DisplayPromptAsync(AppResources.TransportTypeTitle, AppResources.TransportTypePrompt,
            AppResources.Next, AppResources.Cancel, AppResources.TransportTypePlaceholder, 40, initialValue: vehicle?.Type);
        if (type is null)
            return;
        type = type.Trim();
        if (string.IsNullOrWhiteSpace(type))
            throw new InvalidOperationException(AppResources.EnterTransportType);
        var iconChoice = await DisplayActionSheetAsync(AppResources.ChoosePicture, AppResources.Cancel, null, IconChoices);
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
        if (person.IsActive && !await DisplayAlertAsync(AppResources.HideFamilyMemberTitle,
            string.Format(AppResources.HideFamilyMemberMessage, person.Name),
            AppResources.Hide, AppResources.Keep))
            return;
        await SaveChangeAsync(() => person.IsActive = !person.IsActive, () => person.IsActive = !person.IsActive);
    }

    private async Task ToggleVehicleAsync(Vehicle vehicle)
    {
        if (vehicle.IsActive && !await DisplayAlertAsync(AppResources.HideVehicleTitle,
            string.Format(AppResources.HideVehicleMessage, vehicle.Name),
            AppResources.Hide, AppResources.Keep))
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
            await DisplayAlertAsync(AppResources.CouldNotSave, ex.Message, AppResources.OK);
        }
    }
}
