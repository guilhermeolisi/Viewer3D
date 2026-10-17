using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

public class Viewer3DControlTests
{
    static readonly Rgba Red = new(220, 30, 30), Blue = new(30, 30, 220);
    const uint White = 0xFFFFFFFF;

    static readonly AxisDirection[] Axes = [new(Vector3.UnitX, "a"), new(Vector3.UnitY, "b"), new(Vector3.UnitZ, "c")];

    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static bool IsReddish(SKColor c) => c.Red > 120 && c.Red > c.Blue + 60 && c.Red > c.Green + 60;

    static bool IsBluish(SKColor c) => c.Blue > 120 && c.Blue > c.Red + 60 && c.Blue > c.Green + 60;

    [AvaloniaFact]
    public void Screen_PaintsTheSceneThroughSkia()
    {
        Window window = TestWindows.Show(Control(new SphereItem { Radius = 0.5f, Color = Red }));

        uint[,] pixels = TestWindows.Capture(window);

        Assert.True(IsReddish(new SKColor(pixels[150, 100])), $"center 0x{pixels[150, 100]:X8}");
        Assert.Equal(White, pixels[2, 2]);
        Assert.Equal(White, pixels[297, 197]);
    }

    [AvaloniaFact]
    public void RenderToPng_SphereIsLitAtTheCenterAndCornersAreBackground()
    {
        Viewer3DControl control = Control(new SphereItem { Radius = 0.5f, Color = Red });

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(120, 80));

        Assert.Equal(120, png.Width);
        Assert.Equal(80, png.Height);
        Assert.True(IsReddish(png.GetPixel(60, 40)), $"center {png.GetPixel(60, 40)}");
        Assert.Equal(SKColors.White, png.GetPixel(1, 1));
        Assert.Equal(SKColors.White, png.GetPixel(118, 78));
    }

    [AvaloniaFact]
    public void RenderToPng_ScaleMultipliesThePixels()
    {
        Viewer3DControl control = Control(new SphereItem { Radius = 0.5f, Color = Red });

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 50, 2));

        Assert.Equal(200, png.Width);
        Assert.Equal(100, png.Height);
        Assert.True(IsReddish(png.GetPixel(100, 50)));
    }

    [AvaloniaFact]
    public void RenderToPng_FrontSphereHidesTheBackOne()
    {
        // The back sphere is given last; depth, not list order, must decide.
        Viewer3DControl control = Control(
            new SphereItem { Center = new Vector3(0, 0, 0.5f), Radius = 0.4f, Color = Red },
            new SphereItem { Center = new Vector3(0, 0, -0.5f), Radius = 0.4f, Color = Blue });

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(IsReddish(png.GetPixel(50, 50)), $"center {png.GetPixel(50, 50)}");
    }

    [AvaloniaFact]
    public void RenderToPng_BackSphereShowsWhenTheCameraTurnsAround()
    {
        Viewer3DControl control = Control(
            new SphereItem { Center = new Vector3(0, 0, 0.5f), Radius = 0.4f, Color = Red },
            new SphereItem { Center = new Vector3(0, 0, -0.5f), Radius = 0.4f, Color = Blue });

        control.Camera!.Orbit(MathF.PI / Camera.OrbitRadiansPerPixel, 0);
        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(IsBluish(png.GetPixel(50, 50)), $"center {png.GetPixel(50, 50)}");
    }

    [AvaloniaFact]
    public void Drag_StartingOverTheAxisIndicator_RotatesTheCamera()
    {
        Viewer3DControl control = Control(new SphereItem { Radius = 0.5f, Color = Red });
        control.AxisDirections = Axes;
        Window window = TestWindows.Show(control);
        Quaternion before = control.Camera!.Orientation;
        int started = 0, ended = 0;
        control.GestureStarted += (_, _) => started++;
        control.GestureEnded += (_, _) => ended++;

        window.MouseDown(new Point(20, 180), MouseButton.Left);
        window.MouseMove(new Point(60, 170), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(60, 170), MouseButton.Left);

        Assert.NotEqual(before, control.Camera.Orientation);
        Assert.Equal(1, started);
        Assert.Equal(1, ended);
    }

    [AvaloniaFact]
    public void RightDrag_MovesTheTarget()
    {
        Viewer3DControl control = Control(new SphereItem { Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        Quaternion orientation = control.Camera!.Orientation;

        window.MouseDown(new Point(150, 100), MouseButton.Right);
        window.MouseMove(new Point(170, 100), RawInputModifiers.RightMouseButton);
        window.MouseUp(new Point(170, 100), MouseButton.Right);

        Assert.Equal(orientation, control.Camera.Orientation);
        Assert.True(control.Camera.Target.X < 0);
    }

    [AvaloniaFact]
    public void Gesture_PaintsTheLowDetailSceneUntilItEnds()
    {
        var full = new Scene([new SphereItem { Radius = 0.5f, Color = Red }]);
        var low = new Scene([new SphereItem { Radius = 0.5f, Color = Blue }]);
        var control = new Viewer3DControl { Scene = full, LowDetailScene = low };
        Window window = TestWindows.Show(control);

        TestWindows.Capture(window);
        Assert.Same(full, control.LastPaintedScene);

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(160, 105), RawInputModifiers.LeftMouseButton);
        uint[,] during = TestWindows.Capture(window);
        Assert.Same(low, control.LastPaintedScene);
        Assert.True(IsBluish(new SKColor(during[150, 100])));

        window.MouseUp(new Point(160, 105), MouseButton.Left);
        TestWindows.Capture(window);
        Assert.Same(full, control.LastPaintedScene);
    }

    [AvaloniaFact]
    public void CameraChange_RepaintsOnlyWhenSomethingChanged()
    {
        Viewer3DControl control = Control(new SphereItem { Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        int painted = control.PaintCount;

        TestWindows.Capture(window);
        int idle = control.PaintCount;
        control.Camera!.Orbit(10, 0);
        TestWindows.Capture(window);

        Assert.Equal(painted, idle);
        Assert.Equal(idle + 1, control.PaintCount);
    }

    [AvaloniaFact]
    public void SharedCamera_RepaintsBothViewers()
    {
        Viewer3DControl first = Control(new SphereItem { Radius = 0.5f, Color = Red });
        var second = new Viewer3DControl { Scene = first.Scene, Camera = first.Camera };
        var panel = new StackPanel { Children = { new Border { Height = 100, Child = first }, new Border { Height = 100, Child = second } } };
        Window window = TestWindows.Show(panel);
        TestWindows.Capture(window);
        int before = second.PaintCount;

        first.Camera!.Orbit(10, 0);
        TestWindows.Capture(window);

        Assert.Equal(before + 1, second.PaintCount);
    }

    [AvaloniaFact]
    public void Theme_LightAndDarkGiveDifferentBackgrounds()
    {
        Viewer3DControl control = Control();
        Window window = TestWindows.Show(control);

        window.RequestedThemeVariant = ThemeVariant.Light;
        using SKBitmap light = TestWindows.Decode(control.RenderToPng(40, 40));
        window.RequestedThemeVariant = ThemeVariant.Dark;
        using SKBitmap dark = TestWindows.Decode(control.RenderToPng(40, 40));

        Assert.Equal(SKColors.White, light.GetPixel(20, 20));
        Assert.NotEqual(light.GetPixel(20, 20), dark.GetPixel(20, 20));
        Assert.True(dark.GetPixel(20, 20).Red < 80);
    }

    [AvaloniaFact]
    public void Theme_ResourceOverridesTheBackground()
    {
        Viewer3DControl control = Control();
        control.Resources["Viewer3DBackgroundColor"] = Color.FromRgb(0, 128, 0);
        TestWindows.Show(control);

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(40, 40));

        Assert.Equal(new SKColor(0, 128, 0), png.GetPixel(20, 20));
    }

    [AvaloniaTheory]
    [InlineData(200, 150)]
    [InlineData(500, 400)]
    public void AxisIndicator_IsPaintedInTheLowerLeftCorner(int width, int height)
    {
        Viewer3DControl control = Control();
        control.AxisDirections = Axes;

        using SKBitmap shown = TestWindows.Decode(control.RenderToPng(width, height));
        control.ShowAxisIndicator = false;
        using SKBitmap hidden = TestWindows.Decode(control.RenderToPng(width, height));

        // At the identity orientation a points right, b up and c at the viewer (a dot at the origin).
        Vector2 origin = SkiaPainter.AxisOrigin(height);
        int ox = (int)origin.X, oy = (int)origin.Y;
        Assert.True(IsReddish(shown.GetPixel(ox + 20, oy)), $"a arrow {shown.GetPixel(ox + 20, oy)}");
        Assert.NotEqual(SKColors.White, shown.GetPixel(ox, oy - 20));
        Assert.NotEqual(SKColors.White, shown.GetPixel(ox, oy));
        Assert.Equal(SKColors.White, hidden.GetPixel(ox + 20, oy));
        // Nothing of the indicator outside its corner.
        Assert.Equal(SKColors.White, shown.GetPixel(width - 5, 5));
    }

    [AvaloniaFact]
    public void AxisIndicator_DoesNotChangeWithZoomOrPan()
    {
        Viewer3DControl control = Control();
        control.AxisDirections = Axes;
        using SKBitmap before = TestWindows.Decode(control.RenderToPng(200, 150));

        control.Camera!.ZoomAt(3, 10, 10, 200, 150);
        control.Camera.Pan(30, 30, 200, 150);
        using SKBitmap after = TestWindows.Decode(control.RenderToPng(200, 150));

        Assert.Equal(before.Bytes, after.Bytes);
    }

    [AvaloniaFact]
    public void Hover_ReportsTheItemUnderThePointer()
    {
        Viewer3DControl control = Control(new SphereItem { Id = 42, Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        var picks = new List<PickResult?>();
        control.ItemHovered += (_, e) => picks.Add(e.Pick);

        window.MouseMove(new Point(150, 100));
        window.MouseMove(new Point(5, 5));

        Assert.Equal(2, picks.Count);
        Assert.Equal(42, picks[0]?.ItemId);
        Assert.Null(picks[1]);
    }

    [AvaloniaFact]
    public void Projection_IsAppliedToTheCamera()
    {
        var control = new Viewer3DControl { Projection = ProjectionKind.Perspective };
        var camera = new Camera();

        control.Camera = camera;

        Assert.Equal(ProjectionKind.Perspective, camera.Projection);
    }

    [AvaloniaFact]
    public void EachControlStartsWithItsOwnCamera()
    {
        Assert.NotSame(new Viewer3DControl().Camera, new Viewer3DControl().Camera);
    }
}
