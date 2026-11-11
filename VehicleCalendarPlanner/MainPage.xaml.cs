using System.Diagnostics;
using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using VehicleCalendarPlanner.Core;
using VehicleCalendarPlanner.Services;

namespace VehicleCalendarPlanner;

public partial class MainPage : ContentPage
{
    private const string DragPrefix = "family-wheels:";
    private readonly PlannerSession _session;
    private readonly PlannerService _planner;
    private readonly SettingsPage _settingsPage;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Dictionary<DateOnly, DayVisual> _dayViews = [];
    private readonly Dictionary<Guid, Border> _vehicleCards = [];
    private readonly List<MonthPreview> _monthPreviews = [];
    private IReadOnlyList<CalendarEntry> _entries = [];
    private DateOnly _selectedDate = DateOnly.FromDateTime(DateTime.Today);
    private DateOnly _lastToday = DateOnly.FromDateTime(DateTime.Today);
    private Guid? _selectedVehicle;
    private bool _monthView;
    private bool _initialized;
    private bool _ready;
    private bool _narrow;
    private bool _operationInProgress;
    private bool _statusWarning;
    private int _monthWeekCount;
    private int _monthPreviewSlots = 1;
    private int _timerGeneration;

    private sealed record DayVisual(Button Button, Border Border, bool IsMonth, bool HasWarning = false, Label? Count = null);
    private sealed record MonthPreview(IReadOnlyList<CalendarEntry> Entries, FlexLayout Icons, Label Count);

    public MainPage(PlannerSession session, PlannerService planner, SettingsPage settingsPage)
    {
        InitializeComponent();
        _session = session;
        _planner = planner;
        _settingsPage = settingsPage;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await RunUiAsync(async () =>
        {
            if (!_initialized)
            {
                await _session.LoadAsync();
                _initialized = true;
            }
            await RefreshAsync();
            StartRefreshTimer();
        });
    }

    protected override void OnDisappearing()
    {
        _timerGeneration++;
        base.OnDisappearing();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0)
            return;
        var narrow = width < 560;
        if (_narrow == narrow)
            return;
        _narrow = narrow;
        AdaptiveLayout.ColumnDefinitions.Clear();
        AdaptiveLayout.RowDefinitions.Clear();
        PeriodControls.ColumnDefinitions.Clear();
        PeriodControls.RowDefinitions.Clear();
        if (narrow)
        {
            PeriodControls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            PeriodControls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            PeriodControls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            PeriodControls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(ViewSwitch, 1);
            Grid.SetColumn(PeriodLabel, 0);
            Grid.SetRow(PeriodLabel, 1);
            Grid.SetColumnSpan(PeriodLabel, 2);
            AdaptiveLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AdaptiveLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            AdaptiveLayout.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(VehicleRail, 0);
            Grid.SetRow(VehicleRail, 1);
            VehicleRail.HeightRequest = 190;
            ManageVehiclesButton.IsVisible = false;
            VehicleScroll.Orientation = ScrollOrientation.Horizontal;
            VehicleItems.Direction = FlexDirection.Row;
        }
        else
        {
            PeriodControls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            PeriodControls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            PeriodControls.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            PeriodControls.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Grid.SetColumn(ViewSwitch, 2);
            Grid.SetColumn(PeriodLabel, 1);
            Grid.SetRow(PeriodLabel, 0);
            Grid.SetColumnSpan(PeriodLabel, 1);
            AdaptiveLayout.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            AdaptiveLayout.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(180)));
            AdaptiveLayout.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            Grid.SetColumn(VehicleRail, 1);
            Grid.SetRow(VehicleRail, 0);
            VehicleRail.HeightRequest = -1;
            ManageVehiclesButton.IsVisible = true;
            VehicleScroll.Orientation = ScrollOrientation.Vertical;
            VehicleItems.Direction = FlexDirection.Column;
        }
        if (_initialized)
            RenderVehicles();
    }

    private async void OnSettingsClicked(object? sender, EventArgs e) =>
        await RunUiAsync(() => Navigation.PushAsync(_settingsPage));

    private async void OnRefreshClicked(object? sender, EventArgs e) =>
        await RunUiAsync(() => RefreshAsync(showError: true));

    private async void OnSampleClicked(object? sender, EventArgs e) =>
        await RunUiAsync(async () =>
        {
            if (_session.IsDemo)
                await _session.LeaveDemoAsync();
            else
                _session.StartDemo();
            _entries = [];
            _selectedVehicle = null;
            _selectedDate = DateOnly.FromDateTime(DateTime.Today);
            await RefreshAsync();
        });

    private async void OnPreviousClicked(object? sender, EventArgs e) =>
        await ChangeDateAsync(_monthView ? _selectedDate.AddMonths(-1) : _selectedDate.AddDays(-7));

    private async void OnNextClicked(object? sender, EventArgs e) =>
        await ChangeDateAsync(_monthView ? _selectedDate.AddMonths(1) : _selectedDate.AddDays(7));

    private async void OnTodayClicked(object? sender, EventArgs e) =>
        await ChangeDateAsync(DateOnly.FromDateTime(DateTime.Today));

    private async void OnWeekClicked(object? sender, EventArgs e)
    {
        _monthView = false;
        await RunUiAsync(() => RefreshAsync());
    }

    private async void OnMonthClicked(object? sender, EventArgs e)
    {
        _monthView = true;
        await RunUiAsync(() => RefreshAsync());
    }

    private async Task ChangeDateAsync(DateOnly date)
    {
        _selectedDate = date;
        await RunUiAsync(() => RefreshAsync());
    }

    private async Task RefreshAsync(bool showError = false)
    {
        await _refreshGate.WaitAsync();
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (today != _lastToday && _selectedDate == _lastToday)
                _selectedDate = today;
            _lastToday = today;

            var calendar = _session.Calendar;
            var calendarId = _session.Settings.CalendarId;
            SampleButton.Text = _session.IsDemo ? "Exit sample" : "Try sample";
            if (string.IsNullOrWhiteSpace(calendarId))
            {
                _entries = [];
                _ready = false;
                SetStatus("Connect your Microsoft calendar and add your family in Settings.", false);
                Render();
                return;
            }

            var (start, end) = VisibleRange();
            try
            {
                var entries = await calendar.GetEventsAsync(calendarId, start, end);
                if (!ReferenceEquals(calendar, _session.Calendar) || calendarId != _session.Settings.CalendarId)
                    return;
                _entries = entries;
                _ready = true;
                var conflicts = CountConflicts(entries);
                var suffix = conflicts > 0 ? $" {conflicts} conflicting or damaged reservation(s) need attention." : "";
                SetStatus(_session.IsDemo
                    ? $"SAMPLE MODE - changes are not saved or sent to Microsoft.{suffix}"
                    : $"{_session.Settings.CalendarName} - updated {DateTime.Now:t}.{suffix}", conflicts > 0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Calendar refresh failed: {ex}");
                _ready = false;
                SetStatus($"Calendar not up to date: {ex.Message} Reservations are unavailable until sync succeeds.", true);
                if (showError)
                    await DisplayAlertAsync("Could not sync", ex.Message, "OK");
            }
            Render();
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void StartRefreshTimer()
    {
        var generation = ++_timerGeneration;
        Dispatcher.StartTimer(TimeSpan.FromMinutes(1), () =>
        {
            if (generation != _timerGeneration)
                return false;
            if (!_operationInProgress)
                _ = RefreshFromTimerAsync();
            return true;
        });
    }

    private async Task RefreshFromTimerAsync()
    {
        try
        {
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Automatic refresh failed: {ex}");
            _ready = false;
            SetStatus($"Automatic sync failed: {ex.Message}", true);
        }
    }

    private (DateOnly Start, DateOnly End) VisibleRange()
    {
        if (_monthView)
            return CalendarMath.MonthGrid(_selectedDate);
        var start = CalendarMath.StartOfWeek(_selectedDate);
        return (start, start.AddDays(7));
    }

    private void Render()
    {
        var (start, end) = VisibleRange();
        PeriodLabel.Text = _monthView
            ? _selectedDate.ToString("MMMM yyyy", CultureInfo.CurrentCulture)
            : $"{start:d MMM} - {end.AddDays(-1):d MMM yyyy}";
        CalendarHeading.Text = _monthView ? "THE MONTH AT A GLANCE" : "THE WEEK AHEAD";
        UpdateInstruction();
        SetViewButton(WeekButton, !_monthView);
        SetViewButton(MonthButton, _monthView);
        _dayViews.Clear();
        CalendarContent.Clear();
        WeekScroll.IsVisible = !_monthView;
        MonthArea.IsVisible = _monthView;
        if (_monthView)
            RenderMonth(start, end);
        else
        {
            MonthGrid.Clear();
            _monthPreviews.Clear();
            for (var day = start; day < end; day = day.AddDays(1))
                CalendarContent.Add(CreateDayCard(day));
        }
        RenderVehicles();
    }

    private void UpdateInstruction()
    {
        InstructionLabel.Text = _selectedVehicle is { } id && _session.Settings.Vehicles.FirstOrDefault(x => x.Id == id) is { } vehicle
            ? $"Now tap a day for {vehicle.Name}, or drag the vehicle."
            : "Drag a vehicle onto a day, or tap a vehicle and then a day.";
    }

    private void RenderVehicles()
    {
        VehicleItems.Clear();
        _vehicleCards.Clear();
        AvailabilityLabel.Text = $"Availability for {_selectedDate:ddd d MMM}";
        var vehicles = _session.Settings.Vehicles.Where(x => x.IsActive).ToList();
        var damagedReservation = _entries.Any(x => x.OccursOn(_selectedDate) && x.IsDamagedReservation);
        if (vehicles.Count == 0)
        {
            VehicleItems.Add(new Label
            {
                Text = "No vehicles yet. Add your family's rides in Settings.",
                TextColor = Color.FromArgb("#68827A"),
                FontSize = 12,
                Margin = new Thickness(2, 8)
            });
            return;
        }

        foreach (var vehicle in vehicles)
        {
            var booked = _entries.FirstOrDefault(x => x.OccursOn(_selectedDate) && PlannerService.MatchesVehicle(x.Reservation, vehicle));
            var status = !_ready ? "Sync needed" : booked?.Reservation is { } reservation
                ? $"With {DisplayPerson(reservation)}"
                : damagedReservation ? "Needs review" : "Available";
            var icon = new Image
            {
                Source = $"vehicle_{SafeIcon(vehicle.Icon)}.png",
                WidthRequest = 34,
                HeightRequest = 34,
                Aspect = Aspect.AspectFit,
                InputTransparent = true
            };
            AutomationProperties.SetIsInAccessibleTree(icon, false);

            void ToggleSelection()
            {
                _selectedVehicle = _selectedVehicle == vehicle.Id ? null : vehicle.Id;
                UpdateInstruction();
                UpdateVehicleSelection();
            }

            var iconTap = new TapGestureRecognizer();
            iconTap.Tapped += (_, _) => ToggleSelection();
            var iconBackground = new Border
            {
                BackgroundColor = Color.FromArgb(vehicle.Color),
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 12 },
                WidthRequest = 52,
                HeightRequest = 52,
                Content = icon,
                VerticalOptions = LayoutOptions.Center,
                AutomationId = $"vehicle-icon-{vehicle.Name}"
            };
            SemanticProperties.SetDescription(iconBackground, $"Select or drag {vehicle.Name}");
            AddDragSource(iconBackground, vehicle);
            iconBackground.GestureRecognizers.Add(iconTap);
            var name = new Label
            {
                Text = vehicle.Name,
                AutomationId = $"vehicle-name-{vehicle.Name}",
                TextColor = Color.FromArgb("#244D48"),
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation,
                MinimumHeightRequest = 24
            };
            SemanticProperties.SetHint(name, "Select this vehicle, then choose a day, or drag it onto a day");
            AddDragSource(name, vehicle);
            var nameTap = new TapGestureRecognizer();
            nameTap.Tapped += (_, _) => ToggleSelection();
            name.GestureRecognizers.Add(nameTap);
            var select = new Button
            {
                Text = status,
                AutomationId = $"vehicle-select-{vehicle.Name}",
                TextColor = Color.FromArgb(_ready && booked is null && !damagedReservation ? "#3E715B" : "#AD7257"),
                BackgroundColor = Color.FromArgb("#F4F8F5"),
                BorderColor = Color.FromArgb("#DFEAE5"),
                BorderWidth = 1,
                FontSize = 11,
                CornerRadius = 8,
                Padding = 0,
                MinimumHeightRequest = 44
            };
            SemanticProperties.SetDescription(select, $"Select {vehicle.Name}. {status}");
            SemanticProperties.SetHint(select, "Choose a calendar day after selecting this vehicle");
            select.Clicked += (_, _) => ToggleSelection();
            var nameAndStatus = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
            nameAndStatus.Add(name);
            nameAndStatus.Add(select);
            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(new GridLength(52)),
                    new(GridLength.Star)
                },
                ColumnSpacing = 6
            };
            grid.Add(iconBackground);
            grid.Add(nameAndStatus, 1);
            var card = new Border
            {
                BackgroundColor = Colors.White,
                Stroke = new SolidColorBrush(Color.FromArgb(_selectedVehicle == vehicle.Id ? "#4E9A83" : "#DFEAE5")),
                StrokeThickness = _selectedVehicle == vehicle.Id ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 14 },
                Padding = new Thickness(7),
                Margin = _narrow ? new Thickness(0, 0, 8, 0) : new Thickness(0, 0, 0, 8),
                WidthRequest = _narrow ? 172 : 154,
                Content = grid
            };
            _vehicleCards[vehicle.Id] = card;
            VehicleItems.Add(card);
        }
    }

    private void AddDragSource(View source, Vehicle vehicle)
    {
        var drag = new DragGestureRecognizer { CanDrag = true };
        drag.DragStarting += (_, args) =>
        {
            InstructionLabel.Text = $"Drag {vehicle.Name} onto a calendar day.";
            args.Data.Properties["FamilyWheelsVehicleId"] = vehicle.Id.ToString();
            args.Data.Text = $"{DragPrefix}{vehicle.Id}";
        };
        source.GestureRecognizers.Add(drag);
    }

    private void UpdateVehicleSelection()
    {
        foreach (var (id, card) in _vehicleCards)
        {
            var selected = id == _selectedVehicle;
            card.Stroke = new SolidColorBrush(Color.FromArgb(selected ? "#4E9A83" : "#DFEAE5"));
            card.StrokeThickness = selected ? 2 : 1;
        }
    }

    private Border CreateDayCard(DateOnly date)
    {
        var dayEvents = EventsOn(date);
        var dateButton = new Button
        {
            Text = $"{date:ddd}\n{date.Day}",
            AutomationId = $"day-{date:yyyy-MM-dd}",
            BackgroundColor = date == _selectedDate ? Color.FromArgb("#3A796C") : Color.FromArgb("#EFF6F2"),
            TextColor = date == _selectedDate ? Colors.White : Color.FromArgb("#315E55"),
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            CornerRadius = 12,
            Padding = 2,
            MinimumWidthRequest = 58,
            MinimumHeightRequest = 58,
            VerticalOptions = LayoutOptions.Start
        };
        SemanticProperties.SetDescription(dateButton, date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture));
        SemanticProperties.SetHint(dateButton, "Select this day or reserve the selected vehicle");
        dateButton.Clicked += async (_, _) => await RunUiAsync(() => SelectDayAsync(date));
        var details = new VerticalStackLayout { Spacing = 4, VerticalOptions = LayoutOptions.Center };
        if (dayEvents.Count == 0)
            details.Add(new Label { Text = "Open day - drop a vehicle here", TextColor = Color.FromArgb("#A1B3AC"), FontSize = 12 });
        else
            foreach (var entry in dayEvents)
                details.Add(CreateEventView(entry, date));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(new GridLength(60)), new(GridLength.Star) },
            ColumnSpacing = 12
        };
        grid.Add(dateButton);
        grid.Add(details, 1);
        var border = new Border
        {
            BackgroundColor = date == _selectedDate ? Color.FromArgb("#F7FBF8") : Colors.White,
            Stroke = new SolidColorBrush(Color.FromArgb(date == _selectedDate ? "#A7C9B9" : "#E8F0EC")),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(8),
            MinimumHeightRequest = 78,
            Content = grid
        };
        AddDropTarget(border, date);
        _dayViews[date] = new DayVisual(dateButton, border, false);
        return border;
    }

    private void RenderMonth(DateOnly start, DateOnly end)
    {
        MonthGrid.Clear();
        MonthGrid.ColumnDefinitions.Clear();
        MonthGrid.RowDefinitions.Clear();
        _monthPreviews.Clear();
        for (var column = 0; column < 7; column++)
            MonthGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        MonthGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var column = 0; column < 7; column++)
        {
            var day = start.AddDays(column);
            MonthGrid.Add(new Label
            {
                Text = day.ToString("ddd", CultureInfo.CurrentCulture),
                FontSize = 14,
                FontFamily = "OpenSansSemibold",
                TextColor = Color.FromArgb("#607F73"),
                HorizontalTextAlignment = TextAlignment.Center
            }, column, 0);
        }
        var dayCount = end.DayNumber - start.DayNumber;
        _monthWeekCount = dayCount / 7;
        for (var row = 0; row < _monthWeekCount; row++)
            MonthGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));

        for (var index = 0; index < dayCount; index++)
        {
            var date = start.AddDays(index);
            var events = EventsOn(date);
            var button = new Button
            {
                Text = date.Day.ToString(CultureInfo.CurrentCulture),
                AutomationId = $"day-{date:yyyy-MM-dd}",
                BackgroundColor = date == _selectedDate ? Color.FromArgb("#3A796C") : Colors.Transparent,
                TextColor = date == _selectedDate ? Colors.White :
                    Color.FromArgb(date.Month == _selectedDate.Month ? "#244D48" : "#ACBDB5"),
                FontFamily = "OpenSansSemibold",
                FontSize = 18,
                CornerRadius = 10,
                Padding = 0,
                HeightRequest = 44,
                MinimumWidthRequest = 44
            };
            var summaries = events.Select(entry => DescribeEntry(entry, events)).ToArray();
            SemanticProperties.SetDescription(button,
                $"{date.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture)}. " +
                (summaries.Length == 0 ? "Nothing planned." : string.Join("; ", summaries)));
            SemanticProperties.SetHint(button, "Select this day or reserve the selected vehicle");
            button.Clicked += async (_, _) => await RunUiAsync(() => SelectDayAsync(date));
            var icons = new FlexLayout
            {
                Direction = FlexDirection.Row,
                Wrap = FlexWrap.Wrap,
                JustifyContent = FlexJustify.Center,
                AlignContent = FlexAlignContent.Start,
                AlignItems = FlexAlignItems.Start,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 1, 0, 0),
                InputTransparent = true
            };
            var count = new Label
            {
                FontSize = 11,
                FontFamily = "OpenSansSemibold",
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Start,
                Margin = new Thickness(0, 2, 3, 0),
                InputTransparent = true
            };
            AutomationProperties.SetIsInAccessibleTree(icons, false);
            AutomationProperties.SetIsInAccessibleTree(count, false);
            var content = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new(new GridLength(44)),
                    new(GridLength.Star)
                }
            };
            content.Add(button);
            content.Add(icons, 0, 1);
            content.Add(count);
            var cell = new Border
            {
                BackgroundColor = Color.FromArgb(date.Month == _selectedDate.Month ? "#F8FBF9" : "#F4F7F5"),
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                MinimumHeightRequest = 72,
                Content = content
            };
            var warning = events.Any(x => x.IsDamagedReservation ||
                x.Reservation is { } reservation && HasReservationConflict(events, reservation));
            var visual = new DayVisual(button, cell, true, warning, count);
            _dayViews[date] = visual;
            ApplyDayAppearance(date, visual);
            var preview = new MonthPreview(events, icons, count);
            _monthPreviews.Add(preview);
            RenderMonthPreview(preview);
            AddDropTarget(cell, date);
            if (index == 0)
                cell.SizeChanged += OnMonthCellSizeChanged;
            MonthGrid.Add(cell, index % 7, index / 7 + 1);
        }
        RenderMonthDetails();
        UpdateMonthViewport();
    }

    private void OnMonthViewportSizeChanged(object? sender, EventArgs e) => UpdateMonthViewport();

    private void UpdateMonthViewport()
    {
        if (!_monthView || _monthWeekCount == 0)
            return;
        if (MonthScroll.Width <= 0 || MonthScroll.Height <= 0)
            return;
        var height = Math.Max(MonthScroll.Height, 28 + _monthWeekCount * 76);
        if (Math.Abs(MonthGrid.HeightRequest - height) > 0.5)
            MonthGrid.HeightRequest = height;
    }

    private void OnMonthCellSizeChanged(object? sender, EventArgs e)
    {
        if (!_monthView || sender is not Border cell || cell.Width <= 0 || cell.Height <= 0)
            return;
        var columns = Math.Max(1, (int)((cell.Width - 4) / 51));
        var rows = Math.Max(1, (int)((cell.Height - 50) / 24));
        var slots = columns * rows;
        if (slots == _monthPreviewSlots)
            return;
        _monthPreviewSlots = slots;
        foreach (var preview in _monthPreviews)
            RenderMonthPreview(preview);
    }

    private void RenderMonthPreview(MonthPreview preview)
    {
        preview.Icons.Clear();
        var reservations = preview.Entries.Where(x => x.Reservation is not null).ToList();
        var shown = Math.Min(reservations.Count, _monthPreviewSlots);
        for (var index = 0; index < shown; index++)
        {
            var reservation = reservations[index].Reservation!;
            preview.Icons.Add(CreateReservationBadge(reservation, HasReservationConflict(preview.Entries, reservation)));
        }
        if (shown == 0 && preview.Entries.Count > 0)
        {
            var damaged = preview.Entries.Any(x => x.IsDamagedReservation);
            preview.Icons.Add(new Label
            {
                Text = damaged ? "!" : "Event",
                FontSize = 12,
                FontFamily = "OpenSansSemibold",
                TextColor = Color.FromArgb(damaged ? "#9B5948" : "#54766A"),
                HorizontalTextAlignment = TextAlignment.Center
            });
            shown = 1;
        }
        var remaining = preview.Entries.Count - shown;
        preview.Count.Text = remaining > 0 ? $"+{remaining}" : "";
    }

    private View CreateReservationBadge(ReservationDetails reservation, bool conflict)
    {
        var vehicle = _session.Settings.Vehicles.FirstOrDefault(x => PlannerService.MatchesVehicle(reservation, x));
        var person = _session.Settings.People.FirstOrDefault(x => x.Id == reservation.PersonId)
            ?? _session.Settings.People.FirstOrDefault(x =>
                string.Equals(x.Name, reservation.PersonName, StringComparison.OrdinalIgnoreCase));
        var personColor = Color.FromArgb(person?.Color ?? "#718A80");
        var icon = new Image
        {
            Source = $"vehicle_{SafeIcon(vehicle?.Icon ?? "other")}.png",
            WidthRequest = 20,
            HeightRequest = 20,
            Aspect = Aspect.AspectFit
        };
        var vehicleBadge = new Border
        {
            BackgroundColor = Color.FromArgb(vehicle?.Color ?? "#E5ECE8"),
            Stroke = new SolidColorBrush(Color.FromArgb(conflict ? "#B45D44" : "#E5ECE8")),
            StrokeThickness = conflict ? 2 : 0,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            WidthRequest = 24,
            HeightRequest = 24,
            Content = icon
        };
        var initials = new Label
        {
            Text = Initials(DisplayPerson(reservation)),
            TextColor = AvatarTextColor(personColor),
            FontFamily = "OpenSansSemibold",
            FontSize = 10,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var personBadge = new Border
        {
            BackgroundColor = personColor,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            WidthRequest = 24,
            HeightRequest = 24,
            Content = initials
        };
        AutomationProperties.SetIsInAccessibleTree(icon, false);
        AutomationProperties.SetIsInAccessibleTree(vehicleBadge, false);
        AutomationProperties.SetIsInAccessibleTree(initials, false);
        AutomationProperties.SetIsInAccessibleTree(personBadge, false);
        var pair = new HorizontalStackLayout { Spacing = 1, Margin = new Thickness(1, 0), InputTransparent = true };
        pair.Add(vehicleBadge);
        pair.Add(personBadge);
        return pair;
    }

    private static string Initials(string name)
    {
        var trimmed = name.Trim();
        var characters = StringInfo.ParseCombiningCharacters(trimmed);
        if (characters.Length == 0)
            return "?";
        var end = characters.Length > 2 ? characters[2] : trimmed.Length;
        return trimmed[..end].ToUpper(CultureInfo.CurrentCulture);
    }

    private static Color AvatarTextColor(Color background)
    {
        static double Linear(double value) =>
            value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        var luminance = 0.2126 * Linear(background.Red) + 0.7152 * Linear(background.Green) +
            0.0722 * Linear(background.Blue);
        return luminance > 0.179 ? Colors.Black : Colors.White;
    }

    private void RenderMonthDetails()
    {
        MonthDetailsHeading.Text = _selectedDate.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        MonthDetails.Clear();
        var selectedEvents = EventsOn(_selectedDate);
        if (selectedEvents.Count == 0)
            MonthDetails.Add(new Label
            {
                Text = "Nothing planned yet.",
                TextColor = Color.FromArgb("#718A80"),
                FontSize = 13,
                VerticalOptions = LayoutOptions.Center
            });
        else
            foreach (var entry in selectedEvents)
            {
                var view = CreateEventView(entry, _selectedDate);
                view.HorizontalOptions = LayoutOptions.Start;
                view.VerticalOptions = LayoutOptions.Center;
                MonthDetails.Add(view);
            }
    }

    private void UpdateSelectedDay(DateOnly previousDate)
    {
        if (_dayViews.TryGetValue(previousDate, out var previous))
            ApplyDayAppearance(previousDate, previous);
        if (_selectedDate != previousDate && _dayViews.TryGetValue(_selectedDate, out var current))
            ApplyDayAppearance(_selectedDate, current);
        if (_monthView)
            RenderMonthDetails();
        RenderVehicles();
    }

    private void ApplyDayAppearance(DateOnly date, DayVisual visual)
    {
        var selected = date == _selectedDate;
        visual.Button.BackgroundColor = selected
            ? Color.FromArgb("#3A796C")
            : visual.IsMonth ? Colors.Transparent : Color.FromArgb("#EFF6F2");
        visual.Button.TextColor = selected ? Colors.White :
            Color.FromArgb(visual.IsMonth && date.Month != _selectedDate.Month ? "#ACBDB5" : "#315E55");
        if (visual.IsMonth)
        {
            visual.Border.Stroke = new SolidColorBrush(Color.FromArgb(
                visual.HasWarning ? "#C07156" : selected ? "#A7C9B9" : "#E5EFEA"));
            visual.Border.StrokeThickness = visual.HasWarning || selected ? 2 : 1;
            if (visual.Count is not null)
                visual.Count.TextColor = selected ? Colors.White : Color.FromArgb("#54766A");
        }
        else
        {
            visual.Border.BackgroundColor = selected ? Color.FromArgb("#F7FBF8") : Colors.White;
            visual.Border.Stroke = new SolidColorBrush(Color.FromArgb(selected ? "#A7C9B9" : "#E8F0EC"));
        }
    }

    private string DescribeEntry(CalendarEntry entry, IReadOnlyList<CalendarEntry> dayEntries)
    {
        if (entry.Reservation is { } reservation)
            return $"{DisplayVehicle(reservation)} reserved for {DisplayPerson(reservation)}" +
                (HasReservationConflict(dayEntries, reservation) ? ", conflict" : "");
        return entry.IsDamagedReservation
            ? $"Needs attention: {entry.Subject}"
            : $"{(entry.IsAllDay ? "All day" : entry.Start.ToString("t", CultureInfo.CurrentCulture))}, {entry.Subject}";
    }

    private static bool HasReservationConflict(IEnumerable<CalendarEntry> entries, ReservationDetails reservation) =>
        entries.Count(x => x.Reservation is { } other &&
            (other.VehicleId == reservation.VehicleId ||
             string.Equals(other.VehicleName, reservation.VehicleName, StringComparison.OrdinalIgnoreCase))) > 1;

    private View CreateEventView(CalendarEntry entry, DateOnly date)
    {
        if (entry.Reservation is { } reservation)
        {
            var conflict = HasReservationConflict(_entries.Where(x => x.OccursOn(date)), reservation);
            var button = new Button
            {
                Text = $"{DisplayVehicle(reservation)}  -  {DisplayPerson(reservation)}{(conflict ? "  CONFLICT" : "")}",
                AutomationId = $"booking-{reservation.VehicleName}-{reservation.PersonName}",
                BackgroundColor = Color.FromArgb(conflict ? "#FAE3DC" : "#E8F3ED"),
                TextColor = Color.FromArgb(conflict ? "#984D38" : "#315F53"),
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                CornerRadius = 10,
                Padding = new Thickness(8, 5),
                MinimumHeightRequest = 44,
                HorizontalOptions = LayoutOptions.Fill
            };
            SemanticProperties.SetHint(button, "Open to cancel this reservation");
            button.Clicked += async (_, _) => await RunUiAsync(() => CancelReservationAsync(entry, date));
            return button;
        }
        return new Label
        {
            Text = entry.IsDamagedReservation
                ? $"NEEDS ATTENTION: {entry.Subject}"
                : $"{(entry.IsAllDay ? "All day" : entry.Start.ToString("t", CultureInfo.CurrentCulture))}  {entry.Subject}",
            TextColor = Color.FromArgb(entry.IsDamagedReservation ? "#9B5948" : "#718A80"),
            FontSize = 12,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(2, 3)
        };
    }

    private void AddDropTarget(View target, DateOnly date)
    {
        var drop = new DropGestureRecognizer { AllowDrop = true };
        drop.DragOver += (_, args) =>
        {
            args.AcceptedOperation = DataPackageOperation.Copy;
            InstructionLabel.Text = $"Release to reserve on {date:ddd d MMM}.";
        };
        drop.Drop += async (_, args) => await RunUiAsync(async () =>
        {
            InstructionLabel.Text = "Opening the reservation...";
            string? value = null;
            if (args.Data.Properties.TryGetValue("FamilyWheelsVehicleId", out var property))
                value = property?.ToString();
            if (value is null)
            {
                var text = await args.Data.GetTextAsync();
                if (text?.StartsWith(DragPrefix, StringComparison.Ordinal) == true)
                    value = text[DragPrefix.Length..];
            }
            if (Guid.TryParse(value, out var vehicleId))
                await BookAsync(vehicleId, date);
            else
            {
                UpdateInstruction();
                await DisplayAlertAsync("Can't reserve", "Drag a vehicle from the vehicle list onto a day.", "OK");
            }
        });
        target.GestureRecognizers.Add(drop);
    }

    private async Task SelectDayAsync(DateOnly date)
    {
        if (_selectedVehicle is { } vehicleId)
        {
            await BookAsync(vehicleId, date);
            return;
        }
        await SelectDateAsync(date);
    }

    private async Task SelectDateAsync(DateOnly date)
    {
        var previousDate = _selectedDate;
        var previousRange = VisibleRange();
        _selectedDate = date;
        if (VisibleRange() != previousRange)
            await RefreshAsync();
        else
            UpdateSelectedDay(previousDate);
    }

    private async Task BookAsync(Guid vehicleId, DateOnly date)
    {
        if (_operationInProgress)
            return;
        _operationInProgress = true;
        try
        {
            if (!_ready)
            {
                await DisplayAlertAsync("Sync needed", "Connect and sync the family calendar before making a reservation.", "OK");
                return;
            }
            var vehicle = _session.Settings.Vehicles.FirstOrDefault(x => x.Id == vehicleId && x.IsActive);
            if (vehicle is null)
                throw new InvalidOperationException("That vehicle is no longer active.");
            var people = _session.Settings.People.Where(x => x.IsActive).ToList();
            if (people.Count == 0)
            {
                await DisplayAlertAsync("Add your family", "Add at least one family member in Settings first.", "OK");
                return;
            }
            var choice = await DisplayActionSheetAsync($"Who needs {vehicle.Name} on {date:ddd d MMM}?",
                "Cancel", null, people.Select(x => x.Name).ToArray());
            var person = people.FirstOrDefault(x => x.Name == choice);
            if (person is null)
            {
                await SelectDateAsync(date);
                return;
            }
            await RunWithSavingAsync($"Saving {vehicle.Name} for {person.Name} on {date:ddd d MMM}...", async () =>
            {
                try
                {
                    var result = await _planner.BookAsync(_session.Calendar, _session.Settings, vehicle.Id, person.Id, date);
                    _selectedDate = date;
                    _selectedVehicle = null;
                    await RefreshAsync();
                    if (result.HasConflict)
                        await DisplayAlertAsync("Reservation needs attention",
                            "The reservation was saved, but another booking or damaged reservation exists on this day. Please resolve it in the calendar.", "OK");
                    else if (result.VerificationFailed)
                        await DisplayAlertAsync("Reservation saved",
                            "The reservation was saved, but the follow-up check failed. Sync again to check for conflicts.", "OK");
                    else
                        SemanticScreenReader.Announce($"{vehicle.Name} reserved for {person.Name} on {date:dddd d MMMM}.");
                }
                catch (BookingConflictException ex)
                {
                    _selectedDate = date;
                    await RefreshAsync();
                    await DisplayAlertAsync("Already taken", ex.Message, "OK");
                }
                catch (ReservationWriteUncertainException ex)
                {
                    _selectedDate = date;
                    await RefreshAsync();
                    await DisplayAlertAsync("Check the calendar", ex.Message, "OK");
                }
            });
        }
        finally
        {
            _operationInProgress = false;
            UpdateInstruction();
        }
    }

    private async Task CancelReservationAsync(CalendarEntry entry, DateOnly date)
    {
        if (!_ready || entry.Reservation is not { } reservation)
        {
            await DisplayAlertAsync("Sync needed", "Sync the calendar before changing a reservation.", "OK");
            return;
        }
        var confirm = await DisplayAlertAsync("Cancel this reservation?",
            $"{DisplayVehicle(reservation)} is reserved for {DisplayPerson(reservation)} on {date:dddd d MMMM}.",
            "Cancel reservation", "Keep it");
        if (!confirm)
            return;
        _operationInProgress = true;
        try
        {
            await RunWithSavingAsync("Removing the reservation...", async () =>
            {
                await _planner.CancelAsync(_session.Calendar, _session.Settings, entry);
                await RefreshAsync();
                SemanticScreenReader.Announce("Reservation canceled.");
            });
        }
        finally
        {
            _operationInProgress = false;
        }
    }

    private List<CalendarEntry> EventsOn(DateOnly date) => _entries.Where(x => x.OccursOn(date))
        .OrderBy(x => x.Reservation is null).ThenBy(x => x.Start).ThenBy(x => x.Subject).ToList();

    private string DisplayVehicle(ReservationDetails reservation) =>
        _session.Settings.Vehicles.FirstOrDefault(x => x.Id == reservation.VehicleId)?.Name ?? reservation.VehicleName;

    private string DisplayPerson(ReservationDetails reservation) =>
        _session.Settings.People.FirstOrDefault(x => x.Id == reservation.PersonId)?.Name ?? reservation.PersonName;

    private static string SafeIcon(string icon) =>
        icon is "car" or "bike" or "van" or "scooter" or "bus" or "other" ? icon : "other";

    private static int CountConflicts(IReadOnlyList<CalendarEntry> entries)
    {
        var conflicts = entries.Count(x => x.IsDamagedReservation);
        var reservations = entries.Where(x => x.Reservation is not null).ToList();
        for (var i = 0; i < reservations.Count; i++)
        for (var j = i + 1; j < reservations.Count; j++)
        {
            var left = reservations[i];
            var right = reservations[j];
            if (DateOnly.FromDateTime(left.Start) <= DateOnly.FromDateTime(right.End.AddTicks(-1)) &&
                DateOnly.FromDateTime(right.Start) <= DateOnly.FromDateTime(left.End.AddTicks(-1)) &&
                (left.Reservation!.VehicleId == right.Reservation!.VehicleId ||
                 string.Equals(left.Reservation.VehicleName, right.Reservation.VehicleName, StringComparison.OrdinalIgnoreCase)))
                conflicts++;
        }
        return conflicts;
    }

    private void SetStatus(string text, bool warning)
    {
        _statusWarning = warning;
        StatusLabel.Text = text;
        StatusCard.BackgroundColor = Color.FromArgb(warning ? "#FBECE4" : "#E4F1EC");
        StatusLabel.TextColor = Color.FromArgb(warning ? "#8F513D" : "#315E55");
    }

    private async Task RunWithSavingAsync(string message, Func<Task> action)
    {
        var previousStatus = StatusLabel.Text;
        var previousWarning = _statusWarning;
        SetStatus(message, false);
        SavingIndicator.IsVisible = true;
        SavingIndicator.IsRunning = true;
        CalendarCard.IsEnabled = false;
        VehicleRail.IsEnabled = false;
        PeriodControls.IsEnabled = false;
        SyncButton.IsEnabled = false;
        SettingsButton.IsEnabled = false;
        SampleButton.IsEnabled = false;
        try
        {
            await Task.Yield();
            await action();
        }
        finally
        {
            SavingIndicator.IsRunning = false;
            SavingIndicator.IsVisible = false;
            CalendarCard.IsEnabled = true;
            VehicleRail.IsEnabled = true;
            PeriodControls.IsEnabled = true;
            SyncButton.IsEnabled = true;
            SettingsButton.IsEnabled = true;
            SampleButton.IsEnabled = true;
            if (StatusLabel.Text == message)
                SetStatus(previousStatus, previousWarning);
        }
    }

    private static void SetViewButton(Button button, bool selected)
    {
        button.BackgroundColor = Color.FromArgb(selected ? "#3A796C" : "#FFFFFF");
        button.TextColor = selected ? Colors.White : Color.FromArgb("#285B55");
    }

    private async Task RunUiAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Family Wheels action failed: {ex}");
            await DisplayAlertAsync("Something went wrong", ex.Message, "OK");
        }
    }
}
