using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

public class Viewer3DControlAnimationTests
{
    static readonly Rgba Red = new(220, 30, 30);
    static readonly TimeSpan Move = TimeSpan.FromMilliseconds(200);

    /// <summary>Hands out animation frames at the times the test chooses, in place of the screen.</summary>
    sealed class FrameClock
    {
        readonly List<Action<TimeSpan>> pending = [];

        public int Pending => pending.Count;

        public void Request(Action<TimeSpan> callback) => pending.Add(callback);

        public void Tick(double milliseconds)
        {
            Action<TimeSpan>[] now = [.. pending];
            pending.Clear();
            foreach (Action<TimeSpan> callback in now)
                callback(TimeSpan.FromMilliseconds(milliseconds));
        }
    }

    static (Window Window, Viewer3DControl Control, FrameClock Clock, Scene Full, Scene Low) Shown()
    {
        var full = new Scene([new SphereItem { Radius = 0.5f, Color = Red }]);
        var low = new Scene([new SphereItem { Radius = 0.4f, Color = Red }]);
        var clock = new FrameClock();
        var control = new Viewer3DControl { Scene = full, LowDetailScene = low, RequestFrame = clock.Request };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        Window window = TestWindows.Show(control);
        return (window, control, clock, full, low);
    }

    static CameraState Turned(Camera camera, float degrees, float radius = 2)
    {
        Camera copy = camera.Clone();
        copy.Rotate(Vector3.UnitY, degrees * MathF.PI / 180);
        return copy.State with { ViewRadius = radius, Target = new Vector3(0.1f, 0, 0) };
    }

    [AvaloniaFact]
    public void AnimateCamera_EndsExactlyAtTheTarget_AndThenAsksForNoFrame()
    {
        var (_, control, clock, _, _) = Shown();
        CameraState start = control.Camera!.State;
        CameraState target = Turned(control.Camera, 90);

        control.AnimateCamera(target, Move);

        Assert.True(control.IsCameraAnimating);
        clock.Tick(1000);
        Assert.Equal(start, control.Camera.State);
        clock.Tick(1100);
        CameraState middle = control.Camera.State;
        Assert.NotEqual(start, middle);
        Assert.NotEqual(target, middle);
        clock.Tick(1250);
        Assert.Equal(target, control.Camera.State);
        Assert.False(control.IsCameraAnimating);
        Assert.Equal(0, clock.Pending);
    }

    [AvaloniaFact]
    public void AnimateCamera_PaintsLowDetailWhileMoving_AndTheFullSceneAtTheEnd()
    {
        var (window, control, clock, full, low) = Shown();

        control.AnimateCamera(Turned(control.Camera!, 60), Move);
        clock.Tick(0);
        clock.Tick(100);
        TestWindows.Capture(window);
        Assert.Same(low, control.LastPaintedScene);

        clock.Tick(300);
        TestWindows.Capture(window);
        Assert.Same(full, control.LastPaintedScene);
    }

    [AvaloniaFact]
    public void AnimateCamera_AppliesAtMost30FramesPerSecond()
    {
        var (_, control, clock, _, _) = Shown();
        CameraState start = control.Camera!.State;

        control.AnimateCamera(Turned(control.Camera, 90), Move);
        clock.Tick(0);
        clock.Tick(10);

        Assert.Equal(start, control.Camera.State);
        Assert.Equal(1, clock.Pending);
        clock.Tick(40);
        Assert.NotEqual(start, control.Camera.State);
    }

    [AvaloniaFact]
    public void AnimateCamera_ZeroDuration_JumpsWithoutAskingForAFrame()
    {
        var (_, control, clock, _, _) = Shown();
        CameraState target = Turned(control.Camera!, 45);

        control.AnimateCamera(target, TimeSpan.Zero);

        Assert.Equal(target, control.Camera!.State);
        Assert.False(control.IsCameraAnimating);
        Assert.Equal(0, clock.Pending);
    }

    [AvaloniaFact]
    public void AnimateCamera_Again_StartsFromWhereTheCameraIs()
    {
        var (_, control, clock, _, _) = Shown();
        CameraState first = Turned(control.Camera!, 90);
        CameraState second = Turned(control.Camera!, -90, 0.5f);

        control.AnimateCamera(first, Move);
        clock.Tick(0);
        clock.Tick(100);
        CameraState middle = control.Camera!.State;
        control.AnimateCamera(second, Move);
        clock.Tick(200);

        Assert.Equal(middle, control.Camera.State);
        clock.Tick(500);
        Assert.Equal(second, control.Camera.State);
    }

    [AvaloniaFact]
    public void Press_StopsTheMove()
    {
        var (window, control, clock, _, _) = Shown();
        control.AnimateCamera(Turned(control.Camera!, 90), Move);
        clock.Tick(0);

        window.MouseDown(new Point(150, 100), MouseButton.Left);

        Assert.False(control.IsCameraAnimating);
        window.MouseUp(new Point(150, 100), MouseButton.Left);
    }

    [AvaloniaFact]
    public void Wheel_StopsTheMove()
    {
        var (window, control, clock, _, _) = Shown();
        control.AnimateCamera(Turned(control.Camera!, 90), Move);
        clock.Tick(0);

        window.MouseWheel(new Point(150, 100), new global::Avalonia.Vector(0, 1));

        Assert.False(control.IsCameraAnimating);
    }

    [AvaloniaFact]
    public void Key_StopsTheMove()
    {
        var (window, control, clock, _, _) = Shown();
        control.Focusable = true;
        control.Focus();
        control.AnimateCamera(Turned(control.Camera!, 90), Move);
        clock.Tick(0);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);

        Assert.False(control.IsCameraAnimating);
    }

    [AvaloniaFact]
    public void GestureInAnotherViewerOfTheSameCamera_StopsTheMove()
    {
        var clock = new FrameClock();
        var camera = new Camera();
        var scene = new Scene([new SphereItem { Radius = 0.5f, Color = Red }]);
        var moving = new Viewer3DControl { Scene = scene, Camera = camera, RequestFrame = clock.Request };
        var other = new Viewer3DControl { Scene = scene, Camera = camera };
        TestWindows.Show(moving);
        Window otherWindow = TestWindows.Show(other);
        moving.AnimateCamera(Turned(camera, 90), Move);
        clock.Tick(0);
        clock.Tick(50);
        Assert.True(moving.IsCameraAnimating, "its own frames do not stop it");

        otherWindow.MouseDown(new Point(150, 100), MouseButton.Left);
        otherWindow.MouseMove(new Point(180, 100));
        otherWindow.MouseUp(new Point(180, 100), MouseButton.Left);

        Assert.False(moving.IsCameraAnimating);
    }

    [AvaloniaFact]
    public void Turntable_TurnsUntilStopped_AndStopsWhenDetached()
    {
        var (window, control, clock, _, _) = Shown();
        CameraState start = control.Camera!.State;

        control.StartTurntable(Vector3.UnitY, TimeSpan.FromSeconds(4));
        clock.Tick(0);
        clock.Tick(1000);

        Assert.True(control.IsCameraAnimating);
        Assert.Equal(start.Target, control.Camera.State.Target);
        // A quarter turn around the view's up: the front point goes to the side.
        Vector3 front = control.Camera.ToView(new Vector3(0, 0, 1));
        Assert.True(MathF.Abs(front.Z) < 1e-4f, $"front point at {front}");

        window.Content = null;

        Assert.False(control.IsCameraAnimating);
        clock.Tick(2000);
        Assert.Equal(0, clock.Pending);
    }

    [AvaloniaFact]
    public void StopTurntable_LeavesTheCameraWhereItIs()
    {
        var (_, control, clock, _, _) = Shown();
        control.StartTurntable(Vector3.UnitY, TimeSpan.FromSeconds(4));
        clock.Tick(0);
        clock.Tick(500);
        CameraState there = control.Camera!.State;

        control.StopTurntable();
        clock.Tick(1000);

        Assert.False(control.IsCameraAnimating);
        Assert.Equal(there, control.Camera.State);
        Assert.Equal(0, clock.Pending);
    }

    [AvaloniaFact]
    public void AnimateCamera_OutsideTheVisualTree_AppliesAtOnce()
    {
        var clock = new FrameClock();
        var control = new Viewer3DControl { RequestFrame = clock.Request };
        CameraState target = Turned(control.Camera!, 30);

        control.AnimateCamera(target, Move);

        Assert.Equal(target, control.Camera!.State);
        Assert.False(control.IsCameraAnimating);
        Assert.Equal(0, clock.Pending);
    }
}
