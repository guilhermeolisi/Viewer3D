using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>Translucent mesh faces, mesh edges, and the click on them.</summary>
public class Viewer3DControlTranslucentMeshTests
{
    static readonly Rgba Red = new(220, 30, 30);

    /// <summary>Camera on a view radius of 1: in a 100 px square, 50 px per scene unit, center at (50, 50).</summary>
    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    /// <summary>A square of side 1.6 facing the viewer at depth <paramref name="z"/>, split along the diagonal 0-2.</summary>
    static MeshItem Square(float z, Rgba color, int[]? edges = null, Rgba? edgeColor = null, int id = 0) => new()
    {
        Id = id,
        Positions = [new(-0.8f, -0.8f, z), new(0.8f, -0.8f, z), new(0.8f, 0.8f, z), new(-0.8f, 0.8f, z)],
        Indices = [0, 1, 2, 0, 2, 3],
        Color = color,
        Edges = edges,
        EdgeColor = edgeColor,
        EdgeWidth = 2,
    };

    static bool IsDark(SKColor c) => c.Red < 90 && c.Green < 90 && c.Blue < 90;

    [AvaloniaFact]
    public void RenderToPng_TranslucentFace_ShowsTheSphereBehindIt()
    {
        var sphere = new SphereItem { Center = new Vector3(0, 0, -0.5f), Radius = 0.5f, Color = Red };
        Viewer3DControl translucent = Control(Square(0.5f, new Rgba(30, 30, 220, 110)), sphere);
        Viewer3DControl opaque = Control(Square(0.5f, new Rgba(30, 30, 220)), sphere);

        using SKBitmap a = TestWindows.Decode(translucent.RenderToPng(100, 100));
        using SKBitmap b = TestWindows.Decode(opaque.RenderToPng(100, 100));

        // Alfa 110 de 255: ~43% de azul sobre ~57% do vermelho da esfera; opaca, so azul.
        SKColor mixed = a.GetPixel(50, 50), blue = b.GetPixel(50, 50);
        Assert.True(mixed.Red > 80 && mixed.Blue > 60, $"translucent {mixed}");
        Assert.True(blue.Red < 60 && blue.Blue > 150, $"opaque {blue}");
    }

    [AvaloniaFact]
    public void RenderToPng_TranslucentCube_ShowsItsBackFaces()
    {
        MeshItem single = MeshShapes.Cube(Vector3.Zero, 0.5f, new Rgba(220, 30, 30, 110));
        var both = new MeshItem
        {
            Positions = single.Positions,
            Indices = single.Indices,
            Color = single.Color,
            Lighting = single.Lighting,
            DoubleSided = true,
        };
        Viewer3DControl a = Control(single), b = Control(both);
        a.Camera!.Orbit(0.5f / Camera.OrbitRadiansPerPixel, 0.4f / Camera.OrbitRadiansPerPixel);
        b.Camera!.CopyFrom(a.Camera);

        using SKBitmap pa = TestWindows.Decode(a.RenderToPng(100, 100));
        using SKBitmap pb = TestWindows.Decode(b.RenderToPng(100, 100));

        // As faces do fundo somam mais uma camada de vermelho em todo o interior do cubo.
        int deeper = 0;
        for (int y = 40; y <= 60; y++)
            for (int x = 40; x <= 60; x++)
                if (pb.GetPixel(x, y).Green + 10 < pa.GetPixel(x, y).Green)
                    deeper++;
        Assert.True(deeper > 300, $"only {deeper} of 441 pixels are deeper");
    }

    [AvaloniaFact]
    public void RenderToPng_MeshEdges_AreOnlyTheGivenOnes()
    {
        Viewer3DControl control = Control(Square(0, new Rgba(230, 230, 230), [0, 1, 1, 2, 2, 3, 3, 0], Rgba.Black));

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        // Lados em x = 10 e x = 90, y = 10 e y = 90; a diagonal de (10, 90) a (90, 10) nao e aresta.
        Assert.True(IsDark(png.GetPixel(10, 50)), $"left side {png.GetPixel(10, 50)}");
        Assert.True(IsDark(png.GetPixel(50, 90)), $"bottom side {png.GetPixel(50, 90)}");
        Assert.False(IsDark(png.GetPixel(50, 50)), $"diagonal {png.GetPixel(50, 50)}");
        Assert.False(IsDark(png.GetPixel(30, 70)), $"diagonal {png.GetPixel(30, 70)}");
    }

    /// <summary>
    /// The window of <see cref="TestWindows.Show"/> is 300 x 200: 100 px per scene unit, the origin at (150, 100), so the
    /// square spans x 70 to 230.
    /// </summary>
    static (Viewer3DControl Control, Window Window, List<ItemClickedEventArgs> Clicks) Shown(params SceneItem[] items)
    {
        Viewer3DControl control = Control(items);
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        var clicks = new List<ItemClickedEventArgs>();
        control.ItemClicked += (_, e) => clicks.Add(e);
        return (control, window, clicks);
    }

    static void Click(Window window, Point at)
    {
        window.MouseDown(at, MouseButton.Left);
        window.MouseUp(at, MouseButton.Left);
    }

    [AvaloniaFact]
    public void Click_OnTranslucentFace_SelectsTheMesh()
    {
        var atom = new SphereItem { Id = 1, Radius = 0.3f, Color = Red };
        var (_, window, clicks) = Shown(Square(0.5f, new Rgba(30, 30, 220, 110), id: 5), atom);

        Click(window, new Point(210, 100));
        Click(window, new Point(150, 100));

        Assert.Equal(2, clicks.Count);
        Assert.Equal(5, clicks[0].Pick?.ItemId);
        // No meio, o atomo opaco visto atraves da face e o escolhido.
        Assert.Equal(1, clicks[1].Pick?.ItemId);
    }

    [AvaloniaFact]
    public void Click_OnMeshEdge_SelectsTheMesh()
    {
        var (_, window, clicks) = Shown(Square(0, new Rgba(30, 30, 220, 110), [0, 1, 1, 2, 2, 3, 3, 0], id: 5));

        // Logo fora do lado direito (x = 230): so a aresta, com a sua tolerancia, esta sob o ponteiro.
        Click(window, new Point(232, 100));
        Click(window, new Point(240, 100));

        Assert.Equal(2, clicks.Count);
        Assert.Equal(5, clicks[0].Pick?.ItemId);
        Assert.Equal(-1, clicks[0].Pick?.TriangleIndex);
        Assert.Null(clicks[1].Pick);
    }
}
