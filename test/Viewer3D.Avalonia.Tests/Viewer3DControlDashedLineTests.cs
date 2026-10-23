using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>Dashed lines, as painted.</summary>
public class Viewer3DControlDashedLineTests
{
    static readonly Rgba Red = new(220, 30, 30);

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static int RedPixelsOnRow(SKBitmap bitmap, int y)
    {
        int count = 0;
        for (int x = 0; x < bitmap.Width; x++)
        {
            SKColor c = bitmap.GetPixel(x, y);
            if (c.Red > 150 && c.Green < 110 && c.Blue < 110)
                count++;
        }
        return count;
    }

    [AvaloniaFact]
    public void RenderToPng_DashedLine_LeavesGapsTheSolidLineFills()
    {
        // From x = 10 to x = 90 px on the middle row.
        var solid = new LineItem { Start = new Vector3(-0.8f, 0, 0), End = new Vector3(0.8f, 0, 0), Color = Red, Width = 3 };
        var dashed = new LineItem { Start = solid.Start, End = solid.End, Color = Red, Width = 3, Dash = 6 };

        using SKBitmap a = TestWindows.Decode(Control(solid).RenderToPng(100, 100));
        using SKBitmap b = TestWindows.Decode(Control(dashed).RenderToPng(100, 100));

        int full = RedPixelsOnRow(a, 50), half = RedPixelsOnRow(b, 50);
        Assert.InRange(full, 76, 82);
        // Dashes and gaps of the same length: about half of the line, whatever piece the line was split into.
        Assert.InRange(half, full * 0.4, full * 0.6);
    }
}
