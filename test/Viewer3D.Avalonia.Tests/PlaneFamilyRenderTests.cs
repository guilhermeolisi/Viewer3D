using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>A family of planes and a slice, painted by the control.</summary>
public class PlaneFamilyRenderTests
{
    static readonly Rgba Blue = new(30, 60, 230, 120);
    static readonly Rgba Orange = new(245, 140, 20);

    /// <summary>A box of side 1.6 around the center, one unit deep, seen from +z.</summary>
    static readonly CellBox Box = new(new Vector3(-0.8f, -0.8f, -0.5f), new Vector3(1.6f, 0, 0), new Vector3(0, 1.6f, 0),
        new Vector3(0, 0, 1));

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(IEnumerable<SceneItem> items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    [AvaloniaFact]
    public void RenderToPng_PlanesFacingTheViewer_PaintTheirColor()
    {
        // Three planes z = -0.5, 0 and 0.5, translucent blue stacked over the background.
        Viewer3DControl control = Control(PlaneFamilyShapes.Build(Vector3.UnitZ, 0.5f, 0, 2, Box, Blue, null, Orange));

        using SKBitmap bitmap = TestWindows.Decode(control.RenderToPng(100, 100));
        SKColor center = bitmap.GetPixel(50, 50), outside = bitmap.GetPixel(2, 2);

        Assert.True(center.Blue > center.Red + 60, $"center {center}");
        Assert.NotEqual(outside, center);
    }

    [AvaloniaFact]
    public void RenderToPng_TheHighlightedFrontPlane_CoversTheOthers()
    {
        // Plane k = 2 is the front one (z = 0.5), opaque orange: nothing blue shows through it.
        Viewer3DControl control = Control(PlaneFamilyShapes.Build(Vector3.UnitZ, 0.5f, 0, 2, Box, Blue, 2, Orange));

        using SKBitmap bitmap = TestWindows.Decode(control.RenderToPng(100, 100));
        SKColor center = bitmap.GetPixel(50, 50);

        Assert.True(center.Red > 180 && center.Blue < 90, $"center {center}");
    }

    [AvaloniaFact]
    public void RenderToPng_ASliceHidesTheSphereOffThePlane()
    {
        // Red sphere on the plane z = 0, green one in front of it at z = 0.6: sliced at z = 0, only red remains.
        var red = new SphereItem { Id = 1, Center = Vector3.Zero, Radius = 0.3f, Color = new Rgba(220, 30, 30) };
        var green = new SphereItem { Id = 2, Center = new Vector3(0, 0, 0.6f), Radius = 0.3f, Color = new Rgba(30, 200, 30) };
        Scene sliced = SceneSlice.Apply(new Scene([red, green]), Vector3.Zero, Vector3.UnitZ, 0.1f, SliceMode.Hide);
        Viewer3DControl full = Control([red, green]), slice = Control(sliced.Items);

        using SKBitmap a = TestWindows.Decode(full.RenderToPng(100, 100));
        using SKBitmap b = TestWindows.Decode(slice.RenderToPng(100, 100));

        Assert.True(a.GetPixel(50, 50).Green > a.GetPixel(50, 50).Red, $"full {a.GetPixel(50, 50)}");
        Assert.True(b.GetPixel(50, 50).Red > b.GetPixel(50, 50).Green, $"slice {b.GetPixel(50, 50)}");
    }
}
