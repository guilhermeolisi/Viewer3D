using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>Dragging an item marked by <see cref="Viewer3DControl.CanDragItem"/> instead of rotating the view.</summary>
public class Viewer3DControlItemDragTests
{
    // Window of 300 x 200: the sphere at the origin is drawn around (150, 100).
    static (Viewer3DControl control, Window window, List<string> events) Show(Func<int, bool>? canDrag)
    {
        var control = new Viewer3DControl
        {
            Scene = new Scene([new SphereItem { Id = 7, Radius = 0.5f, Color = new Rgba(220, 30, 30) }]),
            CanDragItem = canDrag,
        };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        var events = new List<string>();
        control.ItemDragStarted += (_, e) => events.Add($"start {e.ItemId}");
        control.ItemDragged += (_, e) => events.Add($"move {e.ItemId}");
        control.ItemDragCompleted += (_, e) => events.Add(e.Canceled ? $"cancel {e.ItemId}" : $"end {e.ItemId}");
        control.ItemClicked += (_, e) => events.Add($"click {e.Pick?.ItemId}");
        return (control, window, events);
    }

    [AvaloniaFact]
    public void DraggingAMarkedItem_ShouldRaiseStartMoveAndEnd_AndLeaveTheCameraStill()
    {
        var (control, window, events) = Show(id => id == 7);
        Quaternion before = control.Camera!.Orientation;

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(152, 101), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(170, 110), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(190, 120), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(190, 120), MouseButton.Left);

        Assert.Equal(["start 7", "move 7", "move 7", "end 7"], events);
        Assert.Equal(before, control.Camera.Orientation);
    }

    [AvaloniaFact]
    public void TheRayOfADrag_ShouldPassUnderThePointer()
    {
        var (control, window, _) = Show(id => id == 7);
        Ray? ray = null;
        control.ItemDragged += (_, e) => ray = e.Ray;

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(180, 90), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(180, 90), MouseButton.Left);

        Assert.NotNull(ray);
        Projector projector = control.Camera!.CreateProjector(300, 200);
        Assert.True(ray.Value.TryIntersectPlane(Vector3.Zero, Vector3.UnitZ, out Vector3 hit));
        Assert.True(projector.Project(hit, out float x, out float y, out _));
        Assert.Equal(180f, x, 2);
        Assert.Equal(90f, y, 2);
    }

    [AvaloniaFact]
    public void DraggingTheBackground_ShouldRotateAsBefore()
    {
        var (control, window, events) = Show(id => id == 7);
        Quaternion before = control.Camera!.Orientation;

        window.MouseDown(new Point(10, 10), MouseButton.Left);
        window.MouseMove(new Point(60, 40), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(60, 40), MouseButton.Left);

        Assert.Empty(events);
        Assert.NotEqual(before, control.Camera.Orientation);
    }

    [AvaloniaFact]
    public void DraggingAnItemThatIsNotMarked_ShouldRotateAsBefore()
    {
        var (control, window, events) = Show(_ => false);
        Quaternion before = control.Camera!.Orientation;

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(190, 120), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(190, 120), MouseButton.Left);

        Assert.Empty(events);
        Assert.NotEqual(before, control.Camera.Orientation);
    }

    [AvaloniaFact]
    public void AMoveBelowTheTolerance_OnAMarkedItem_ShouldStayAClick_WithoutRotating()
    {
        var (control, window, events) = Show(id => id == 7);
        Quaternion before = control.Camera!.Orientation;

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(152, 102), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(152, 102), MouseButton.Left);

        Assert.Equal(["click 7"], events);
        Assert.Equal(before, control.Camera.Orientation);
    }

    [AvaloniaFact]
    public void LosingTheCapture_ShouldEndTheDragAsCanceled()
    {
        var (control, window, events) = Show(id => id == 7);
        IPointer? pointer = null;
        control.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, true);

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(180, 110), RawInputModifiers.LeftMouseButton);
        Assert.NotNull(pointer);
        pointer.Capture(null);
        window.MouseUp(new Point(180, 110), MouseButton.Left);

        Assert.Equal(["start 7", "move 7", "cancel 7"], events);
        Assert.False(control.IsGestureActive);
    }

    [AvaloniaFact]
    public void TheDrag_ShouldCountAsAGesture()
    {
        var (control, window, _) = Show(id => id == 7);
        bool activeDuringDrag = false;
        control.ItemDragged += (_, _) => activeDuringDrag = control.IsGestureActive;

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(180, 110), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(180, 110), MouseButton.Left);

        Assert.True(activeDuringDrag);
        Assert.False(control.IsGestureActive);
    }
}
