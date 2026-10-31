using System.Numerics;
using Avalonia.Headless.XUnit;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>The axes frame the host sets on the control: drawn on screen, in PNG exports and in frame renderers.</summary>
public class Viewer3DControlAxesFrameTests
{
    static readonly Rgba Red = new(220, 30, 30);

    static readonly AxesFrameOptions Frame = new(["x (nm)", "y (nm)", "z (nm)"], new AxesFrameLimits(-1, 1, 0.5));

    static Viewer3DControl Control(ProjectionKind projection)
    {
        var control = new Viewer3DControl
        {
            Scene = new Scene([new SphereItem { Radius = 0.3f, Color = Red }]),
            Projection = projection,
        };
        control.Camera!.Frame(Vector3.Zero, AxesFrame.FrameRadius(Frame.Limits!.Value));
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

    [AvaloniaTheory]
    [InlineData(ProjectionKind.Orthographic)]
    [InlineData(ProjectionKind.Perspective)]
    public void AxesFrame_ChangesThePng(ProjectionKind projection)
    {
        Viewer3DControl control = Control(projection);
        using SKBitmap plain = TestWindows.Decode(control.RenderToPng(200, 200));

        control.AxesFrame = Frame;
        using SKBitmap framed = TestWindows.Decode(control.RenderToPng(200, 200));

        Assert.True(Differing(plain, framed) > 200);
    }

    [AvaloniaTheory]
    [InlineData(ProjectionKind.Orthographic)]
    [InlineData(ProjectionKind.Perspective)]
    public void TickNumbers_AreDrawnWhereTheLayoutPutsThem(ProjectionKind projection)
    {
        Viewer3DControl control = Control(projection);
        control.Camera!.Orbit(0.6f, 0.4f);
        using SKBitmap plain = TestWindows.Decode(control.RenderToPng(200, 200));
        control.AxesFrame = Frame;
        using SKBitmap framed = TestWindows.Decode(control.RenderToPng(200, 200));

        // As mesmas contas do controle, pelo preparador: o centro de cada numero na tela.
        var preparer = new FramePreparer { AxesFrameOptions = Frame };
        DrawList list = preparer.Prepare(control.Scene!, control.Camera.Clone(), 200, 200);
        AxesFrameText[] numbers = list.AxesFrame!.Texts.Where(t => !t.IsTitle).ToArray();
        Assert.NotEmpty(numbers);

        int withInk = 0;
        foreach (AxesFrameText number in numbers)
        {
            int changed = 0;
            for (int dx = -6; dx <= 6; dx++)
                for (int dy = -5; dy <= 5; dy++)
                {
                    int x = (int)MathF.Round(number.Center.X) + dx, y = (int)MathF.Round(number.Center.Y) + dy;
                    if (x >= 0 && y >= 0 && x < 200 && y < 200 && plain.GetPixel(x, y) != framed.GetPixel(x, y))
                        changed++;
                }
            if (changed > 0)
                withInk++;
        }
        Assert.Equal(numbers.Length, withInk);
    }

    [AvaloniaTheory]
    [InlineData(ProjectionKind.Orthographic)]
    [InlineData(ProjectionKind.Perspective)]
    public void Title_IsDrawnAtTheTopCenter_AndNowhereElse(ProjectionKind projection)
    {
        Viewer3DControl control = Control(projection);
        control.AxesFrame = Frame;
        using SKBitmap untitled = TestWindows.Decode(control.RenderToPng(200, 200));

        control.AxesFrame = Frame with { Title = "Crystallite radius (nm)" };
        using SKBitmap titled = TestWindows.Decode(control.RenderToPng(200, 200));

        // The title is drawn centred, on the baseline 8 + FrameTitleSize = 21 px: its ink fills the top band around the
        // centre, and the rest of the image is the same pixel for pixel.
        int top = 0, centre = 0, below = 0;
        for (int x = 0; x < 200; x++)
            for (int y = 0; y < 200; y++)
            {
                if (untitled.GetPixel(x, y) == titled.GetPixel(x, y))
                    continue;
                if (y < 30)
                {
                    top++;
                    if (x is > 60 and < 140)
                        centre++;
                }
                else
                    below++;
            }
        Assert.True(top > 50, $"the title changed {top} pixels of the top band");
        Assert.True(centre > top / 2, "the ink of the title is centred");
        Assert.Equal(0, below);
    }

    [AvaloniaFact]
    public void AxesFrame_DoesNotChangeTheProjection()
    {
        Viewer3DControl control = Control(ProjectionKind.Perspective);
        control.AxesFrame = Frame;
        Assert.Equal(ProjectionKind.Perspective, control.Projection);
        Assert.Equal(ProjectionKind.Perspective, control.Camera!.Projection);

        control.AxesFrame = null;
        Assert.Equal(ProjectionKind.Perspective, control.Projection);
    }

    [AvaloniaFact]
    public void FrameRenderer_KeepsTheAxesFrameOfTheControl()
    {
        Viewer3DControl control = Control(ProjectionKind.Orthographic);
        using SKBitmap plain = TestWindows.Decode(control.CreateFrameRenderer(120, 120).Render(0));

        control.AxesFrame = Frame;
        SceneFrameRenderer renderer = control.CreateFrameRenderer(120, 120);
        control.AxesFrame = null;
        using SKBitmap framed = TestWindows.Decode(renderer.Render(0));

        Assert.True(Differing(plain, framed) > 100);
        using SKBitmap direct = TestWindows.Decode(control.RenderToPng(120, 120));
        Assert.Equal(0, Differing(plain, direct));
    }
}
