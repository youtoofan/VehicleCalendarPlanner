using Android.Content;
using Android.Views;
using VehicleCalendarPlanner.Core;
using AView = Android.Views.View;
using View = Microsoft.Maui.Controls.View;

namespace VehicleCalendarPlanner;

// MAUI's Drag/DropGestureRecognizers are unreliable on Android (drop events reach only one
// target, and tap recognizers interfere), so Android uses the native drag-and-drop framework.
public partial class MainPage
{
    private const string AndroidDragLabel = "family-wheels-vehicle";

    private static void WhenPlatformViewReady(View view, Action<AView> attach)
    {
        if (view.Handler?.PlatformView is AView ready)
        {
            attach(ready);
            return;
        }

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (view.Handler?.PlatformView is not AView platform)
                return;
            view.HandlerChanged -= OnHandlerChanged;
            attach(platform);
        }

        view.HandlerChanged += OnHandlerChanged;
    }

    private void AddAndroidVehicleGestures(View source, Vehicle vehicle, Action toggleSelection)
    {
        WhenPlatformViewReady(source, platform =>
        {
            platform.Click += (_, _) => toggleSelection();
            platform.LongClick += (_, e) =>
            {
                var data = ClipData.NewPlainText(AndroidDragLabel, $"{DragPrefix}{vehicle.Id}");
                e.Handled = platform.StartDragAndDrop(data, new AView.DragShadowBuilder(platform), null, 0);
                if (e.Handled)
                {
                    platform.PerformHapticFeedback(FeedbackConstants.LongPress);
                    InstructionLabel.Text = string.Format(AppResources.DragVehicleOntoDay, vehicle.Name);
                }
            };
        });
    }

    private void AddAndroidDropTarget(View target, DateOnly date)
    {
        WhenPlatformViewReady(target, platform => platform.Drag += (_, e) =>
        {
            var drag = e.Event;
            if (drag is null)
                return;
            switch (drag.Action)
            {
                case DragAction.Started:
                    e.Handled = drag.ClipDescription?.Label == AndroidDragLabel;
                    return;
                case DragAction.Entered:
                    SetDropHighlight(date, true);
                    InstructionLabel.Text = string.Format(AppResources.ReleaseToReserve, date);
                    break;
                case DragAction.Exited:
                    SetDropHighlight(date, false);
                    break;
                case DragAction.Drop:
                    SetDropHighlight(date, false);
                    var text = drag.ClipData is { ItemCount: > 0 } clip ? clip.GetItemAt(0)?.Text : null;
                    _ = RunUiAsync(() => HandleDroppedVehicleAsync(ParseDragText(text), date));
                    break;
                case DragAction.Ended:
                    SetDropHighlight(date, false);
                    if (!drag.Result)
                        UpdateInstruction();
                    break;
            }
            e.Handled = true;
        });
    }

    private void SetDropHighlight(DateOnly date, bool active)
    {
        if (!_dayViews.TryGetValue(date, out var visual))
            return;
        if (!active)
        {
            ApplyDayAppearance(date, visual);
            return;
        }
        visual.Border.Stroke = new SolidColorBrush(Color.FromArgb("#2F8F72"));
        visual.Border.StrokeThickness = 3;
    }
}
