using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

public class Viewer3DControlSphereHaloTests
{
    static readonly Rgba Red = new(220, 30, 30);
    static readonly Rgba Blue = new(20, 60, 230);

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static bool IsBackground(SKColor c) => c.Red > 245 && c.Green > 245 && c.Blue > 245;

    [AvaloniaFact]
    public void RenderToPng_SphereWithHalo_HasTheHaloColorJustOutsideTheDisc()
    {
        // Radius 0.4 is 20 px; the ring runs from 20.5 to 23.5 px, so 22 px from the center is in it.
        using SKBitmap png = TestWindows.Decode(
            Control(new SphereItem { Radius = 0.4f, Color = Red, Halo = Blue }).RenderToPng(100, 100));

        foreach ((int x, int y) in new[] { (72, 50), (28, 50), (50, 72), (50, 28) })
        {
            SKColor c = png.GetPixel(x, y);
            Assert.True(c.Blue > 180 && c.Red < 80, $"({x}, {y}) is {c}");
        }
        Assert.True(IsBackground(png.GetPixel(78, 50)), $"beyond the ring {png.GetPixel(78, 50)}");
    }

    [AvaloniaFact]
    public void RenderToPng_SphereWithoutHalo_HasTheBackgroundJustOutsideTheDisc()
    {
        using SKBitmap png = TestWindows.Decode(
            Control(new SphereItem { Radius = 0.4f, Color = Red }).RenderToPng(100, 100));

        Assert.True(IsBackground(png.GetPixel(72, 50)), $"{png.GetPixel(72, 50)}");
    }
}
