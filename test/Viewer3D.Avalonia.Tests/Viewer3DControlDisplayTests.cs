using System.Numerics;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>The background and the outline the host sets on the control.</summary>
public class Viewer3DControlDisplayTests
{
    static readonly Rgba Red = new(220, 30, 30);

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static int Differing(SKBitmap a, SKBitmap b)
    {
        int count = 0;
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                    count++;
        return count;
    }

    static int Brightness(SKColor c) => c.Red + c.Green + c.Blue;

    static EllipsoidItem Flat() => new()
    {
        Axes = [new(0.8f, 0, 0), new(0, 0.2f, 0), new(0, 0, 0.5f)],
        Color = Red,
    };

    [AvaloniaFact]
    public void SceneBackground_WithAlphaZero_GivesATransparentPng()
    {
        Viewer3DControl control = Control(new SphereItem { Radius = 0.3f, Color = Red });
        control.SceneBackground = new Rgba(255, 255, 255, 0);

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.Equal(0, png.GetPixel(2, 2).Alpha);
        Assert.Equal(255, png.GetPixel(50, 50).Alpha);
    }

    [AvaloniaFact]
    public void SceneBackground_BeatsTheThemeResource()
    {
        Viewer3DControl control = Control();
        control.Resources["Viewer3DBackgroundColor"] = Color.FromRgb(0, 128, 0);
        control.SceneBackground = new Rgba(10, 20, 200);
        TestWindows.Show(control);

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(40, 40));

        Assert.Equal(new SKColor(10, 20, 200), png.GetPixel(20, 20));
    }

    [AvaloniaFact]
    public void OutlineWidth_Null_KeepsTheDefaultLook()
    {
        // Null e o padrao: as mesmas imagens que o controle dava antes da propriedade, esfera e elipsoide.
        Viewer3DControl plain = Control(new SphereItem { Radius = 0.5f, Color = Red }, Flat());
        Viewer3DControl unset = Control(new SphereItem { Radius = 0.5f, Color = Red }, Flat());
        unset.OutlineWidth = null;

        using SKBitmap a = TestWindows.Decode(plain.RenderToPng(100, 100));
        using SKBitmap b = TestWindows.Decode(unset.RenderToPng(100, 100));

        Assert.Equal(0, Differing(a, b));
    }

    [AvaloniaFact]
    public void OutlineWidth_Zero_TakesTheEllipseOutlineOff()
    {
        Viewer3DControl control = Control(Flat());

        using SKBitmap outlined = TestWindows.Decode(control.RenderToPng(100, 100));
        control.OutlineWidth = 0;
        using SKBitmap bare = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(Differing(outlined, bare) > 0, "the default 1 px outline did not go away");
    }

    [AvaloniaFact]
    public void OutlineWidth_Three_DarkensTheSphereEdge()
    {
        // Raio 0,5 = 25 px: o pixel a 24 px do centro fica dentro do contorno de 3 px.
        Viewer3DControl control = Control(new SphereItem { Radius = 0.5f, Color = Red });

        using SKBitmap bare = TestWindows.Decode(control.RenderToPng(100, 100));
        control.OutlineWidth = 3;
        using SKBitmap outlined = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(Brightness(outlined.GetPixel(74, 50)) < Brightness(bare.GetPixel(74, 50)),
            $"edge {bare.GetPixel(74, 50)} -> {outlined.GetPixel(74, 50)}");
        Assert.Equal(bare.GetPixel(50, 50), outlined.GetPixel(50, 50));
    }
}
