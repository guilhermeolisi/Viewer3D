using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>A point cloud, a wire sphere and a dimension line, painted by the control.</summary>
public class PointCloudRenderTests
{
    static readonly IColorMap Map = new FuncColorMap(v => v > 0.5 ? new Rgba(220, 30, 30) : new Rgba(30, 60, 230));

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(IEnumerable<SceneItem> items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    [AvaloniaFact]
    public void RenderToPng_TheCloudPaintsEachPointInTheColorOfItsValue()
    {
        // A large red point on the left (x = 25 px) and a small blue one on the right (x = 75 px).
        SphereItem[] cloud = PointCloudShapes.Build([new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0)], [1, 0],
            v => v > 0.5 ? 0.3f : 0.1f, Map);

        using SKBitmap bitmap = TestWindows.Decode(Control(cloud).RenderToPng(100, 100));
        SKColor left = bitmap.GetPixel(25, 50), right = bitmap.GetPixel(75, 50), farRight = bitmap.GetPixel(75, 62);

        Assert.True(left.Red > left.Blue + 60, $"left {left}");
        Assert.True(right.Blue > right.Red + 60, $"right {right}");
        // 12 px below the small point is outside it (radius 5 px), and inside the large one would still be red.
        Assert.False(farRight.Blue > farRight.Red + 60, $"below the small point {farRight}");
    }

    [AvaloniaFact]
    public void RenderToPng_TheWireSphereDrawsItsOutlineAndLeavesTheCenterEmpty()
    {
        var red = new Rgba(220, 30, 30);
        // Meridians through z, seen from +z: a circle of radius 0.8 (40 px) and the diameters through the center.
        LineItem[] wire = LineShapes.WireSphere(Vector3.Zero, 0.8f, red, meridians: 1, parallels: 1, segments: 64, width: 3);

        using SKBitmap bitmap = TestWindows.Decode(Control(wire).RenderToPng(100, 100));

        // The equator, seen from above, is the outline at 40 px from the center.
        SKColor outline = bitmap.GetPixel(90, 50);
        Assert.True(outline.Red > 150 && outline.Green < 110, $"outline {outline}");
        SKColor inside = bitmap.GetPixel(70, 30);
        Assert.False(inside.Red > 150 && inside.Green < 110, $"inside {inside}");
    }
}
