using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>A sphere painted in sectors, and the click that reports what is under the pointer.</summary>
public class Viewer3DControlSectorClickTests
{
    static readonly Rgba Red = new(220, 30, 30), Blue = new(30, 30, 220);

    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static bool IsReddish(SKColor c) => c.Red > 120 && c.Red > c.Blue + 60 && c.Red > c.Green + 60;

    static bool IsBluish(SKColor c) => c.Blue > 120 && c.Blue > c.Red + 60 && c.Blue > c.Green + 60;

    [AvaloniaFact]
    public void RenderToPng_PaintsEachSectorWithItsColor()
    {
        // Half and half from twelve o'clock, clockwise: red on the right, blue on the left.
        Viewer3DControl control = Control(new SphereItem
        {
            Radius = 0.5f,
            Color = Red,
            Sectors = [new SphereSector(0.5f, Red), new SphereSector(0.5f, Blue)],
        });

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(IsReddish(png.GetPixel(62, 45)), $"right {png.GetPixel(62, 45)}");
        Assert.True(IsBluish(png.GetPixel(30, 50)), $"left {png.GetPixel(30, 50)}");
    }

    [AvaloniaFact]
    public void RenderToPng_LeavesTheEmptySectorUnfilled()
    {
        // 0.6 of the turn from twelve o'clock clockwise reaches past six o'clock; the upper left quarter stays empty.
        Viewer3DControl control = Control(new SphereItem
        {
            Radius = 0.5f,
            Color = Red,
            Sectors = [new SphereSector(0.6f, Red)],
        });

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(IsReddish(png.GetPixel(62, 45)), $"right {png.GetPixel(62, 45)}");
        Assert.Equal(SKColors.White, png.GetPixel(35, 35));
    }

    [AvaloniaFact]
    public void ItemClicked_ReportsThePickedSphere_OnClickWithoutDrag()
    {
        Viewer3DControl control = Control(new SphereItem { Id = 7, Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        var clicks = new List<ItemClickedEventArgs>();
        control.ItemClicked += (_, e) => clicks.Add(e);

        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(152, 101), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(152, 101), MouseButton.Left);
        window.MouseDown(new Point(3, 3), MouseButton.Left);
        window.MouseUp(new Point(3, 3), MouseButton.Left);

        Assert.Equal(2, clicks.Count);
        Assert.Equal(7, clicks[0].Pick?.ItemId);
        Assert.Null(clicks[1].Pick);
    }

    [AvaloniaFact]
    public void ItemClicked_IsNotRaised_AfterADrag()
    {
        Viewer3DControl control = Control(new SphereItem { Id = 7, Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        int clicks = 0;
        control.ItemClicked += (_, _) => clicks++;

        // Out and back: the release is where the press was, but the pointer went further than the tolerance.
        window.MouseDown(new Point(150, 100), MouseButton.Left);
        window.MouseMove(new Point(180, 100), RawInputModifiers.LeftMouseButton);
        window.MouseMove(new Point(150, 100), RawInputModifiers.LeftMouseButton);
        window.MouseUp(new Point(150, 100), MouseButton.Left);

        Assert.Equal(0, clicks);
    }

    [AvaloniaFact]
    public void ItemClicked_IsNotRaised_OnRightButton()
    {
        Viewer3DControl control = Control(new SphereItem { Id = 7, Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        int clicks = 0;
        control.ItemClicked += (_, _) => clicks++;

        window.MouseDown(new Point(150, 100), MouseButton.Right);
        window.MouseUp(new Point(150, 100), MouseButton.Right);

        Assert.Equal(0, clicks);
    }
}
