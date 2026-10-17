using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

public class Viewer3DControlEllipsoidTests
{
    static readonly Rgba Red = new(220, 30, 30);

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static bool IsBackground(SKColor c) => c.Red > 245 && c.Green > 245 && c.Blue > 245;

    static int PaintedRun(SKBitmap png, bool alongRow, int fixedAt)
    {
        int count = 0;
        for (int k = 0; k < (alongRow ? png.Width : png.Height); k++)
        {
            SKColor c = alongRow ? png.GetPixel(k, fixedAt) : png.GetPixel(fixedAt, k);
            if (!IsBackground(c))
                count++;
        }
        return count;
    }

    static EllipsoidItem Flat(bool arcs = true) => new()
    {
        Axes = [new(0.8f, 0, 0), new(0, 0.2f, 0), new(0, 0, 0.5f)],
        Color = Red,
        PrincipalEllipses = arcs,
    };

    [AvaloniaFact]
    public void RenderToPng_FlatEllipsoid_HasItsSemiAxesInPixels()
    {
        // De frente (vista ao longo de z): 2 x 0,8 x 50 = 80 px de largura e 2 x 0,2 x 50 = 20 px de altura.
        using SKBitmap png = TestWindows.Decode(Control(Flat()).RenderToPng(100, 100));

        // Tolerancia de 3 px: antialias e o contorno de 1 px nas duas bordas.
        Assert.InRange(PaintedRun(png, true, 50), 77, 83);
        Assert.InRange(PaintedRun(png, false, 50), 17, 23);
    }

    [AvaloniaFact]
    public void RenderToPng_FlatEllipsoidSeenFromTheSide_ShowsItsThirdSemiAxis()
    {
        Viewer3DControl control = Control(Flat());
        // Meia volta em torno de y (vista ao longo de x): a largura passa a ser o semi-eixo z, 2 x 0,5 x 50 = 50 px.
        control.Camera!.Orbit(MathF.PI / 2 / Camera.OrbitRadiansPerPixel, 0);

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.InRange(PaintedRun(png, true, 50), 47, 53);
        Assert.InRange(PaintedRun(png, false, 50), 17, 23);
    }

    [AvaloniaFact]
    public void RenderToPng_PrincipalEllipses_AreDrawnOnlyWhenAsked()
    {
        Viewer3DControl with_ = Control(Flat(true));
        Viewer3DControl without = Control(Flat(false));
        with_.Camera!.Orbit(0.4f / Camera.OrbitRadiansPerPixel, 0.3f / Camera.OrbitRadiansPerPixel);
        without.Camera!.CopyFrom(with_.Camera);

        using SKBitmap a = TestWindows.Decode(with_.RenderToPng(100, 100));
        using SKBitmap b = TestWindows.Decode(without.RenderToPng(100, 100));

        int differ = 0;
        for (int y = 0; y < 100; y++)
            for (int x = 0; x < 100; x++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    differ++;
        // Os arcos sao linhas de ~1 px atravessando o disco: dezenas de pixels.
        Assert.True(differ > 20, $"only {differ} pixels differ");
    }

    [AvaloniaFact]
    public void RenderToPng_Cube_IsPainted()
    {
        using SKBitmap png = TestWindows.Decode(Control(MeshShapes.Cube(Vector3.Zero, 0.3f, Red)).RenderToPng(100, 100));

        SKColor center = png.GetPixel(50, 50);
        Assert.True(center.Red > 100 && center.Red > center.Blue + 40, $"center {center}");
        Assert.True(IsBackground(png.GetPixel(5, 5)));
    }

    [AvaloniaFact]
    public void RenderToPng_DashedSphere_IsAnOutlineWithGaps()
    {
        using SKBitmap png = TestWindows.Decode(
            Control(new SphereItem { Radius = 0.5f, Color = Red, Dashed = true }).RenderToPng(100, 100));

        // Sem preenchimento no centro; no circulo de 25 px, tracos e falhas.
        Assert.True(IsBackground(png.GetPixel(50, 50)), $"center {png.GetPixel(50, 50)}");
        int painted = 0, empty = 0;
        for (int k = 0; k < 360; k++)
        {
            double t = k * Math.PI / 180;
            SKColor c = png.GetPixel((int)Math.Round(50 + 25 * Math.Cos(t)), (int)Math.Round(50 + 25 * Math.Sin(t)));
            if (IsBackground(c)) empty++; else painted++;
        }
        Assert.True(painted > 60, $"painted {painted}");
        Assert.True(empty > 30, $"empty {empty}");
    }
}
