using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

namespace Viewer3D.Avalonia.Tests;

/// <summary>Several selected items, the modifier keys of a click, and what is under the pointer.</summary>
public class Viewer3DControlSelectionTests
{
    static readonly Rgba Red = new(220, 30, 30), Blue = new(30, 30, 220);

    static Viewer3DControl Control(params SceneItem[] items)
    {
        var control = new Viewer3DControl { Scene = new Scene(items) };
        control.Camera!.Frame(Vector3.Zero, 1 / 1.05f);
        return control;
    }

    static bool IsGreenish(SKColor c) => c.Green > 100 && c.Green > c.Red + 60 && c.Green > c.Blue + 60;

    static int Greenish(SKBitmap png, int fromX, int toX)
    {
        int count = 0;
        for (int x = fromX; x < toX; x++)
            for (int y = 0; y < png.Height; y++)
                if (IsGreenish(png.GetPixel(x, y)))
                    count++;
        return count;
    }

    static Viewer3DControl TwoSpheres() => Control(
        new SphereItem { Id = 1, Center = new Vector3(-0.5f, 0, 0), Radius = 0.3f, Color = Red },
        new SphereItem { Id = 2, Center = new Vector3(0.5f, 0, 0), Radius = 0.3f, Color = Blue });

    [AvaloniaFact]
    public void SelectedItemIds_RingsEveryItemInTheSelectionColor()
    {
        Viewer3DControl control = TwoSpheres();
        control.Resources["Viewer3DSelectionColor"] = Color.FromRgb(0, 160, 0);
        TestWindows.Show(control);

        using SKBitmap none = TestWindows.Decode(control.RenderToPng(100, 100));
        control.SelectedItemIds = [1, 2];
        using SKBitmap both = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.Equal(0, Greenish(none, 0, 100));
        Assert.True(Greenish(both, 0, 50) > 0, "no ring on the left sphere");
        Assert.True(Greenish(both, 50, 100) > 0, "no ring on the right sphere");
    }

    [AvaloniaFact]
    public void SelectedItemId_JoinsSelectedItemIds()
    {
        Viewer3DControl control = TwoSpheres();
        control.Resources["Viewer3DSelectionColor"] = Color.FromRgb(0, 160, 0);
        TestWindows.Show(control);
        control.SelectedItemId = 1;
        control.SelectedItemIds = [2];

        using SKBitmap png = TestWindows.Decode(control.RenderToPng(100, 100));

        Assert.True(Greenish(png, 0, 50) > 0, "no ring on the left sphere");
        Assert.True(Greenish(png, 50, 100) > 0, "no ring on the right sphere");
    }

    [AvaloniaFact]
    public void ItemClicked_CarriesTheModifierKeys()
    {
        Viewer3DControl control = Control(new SphereItem { Id = 7, Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);
        var clicks = new List<ItemClickedEventArgs>();
        control.ItemClicked += (_, e) => clicks.Add(e);

        window.MouseDown(new Point(150, 100), MouseButton.Left, RawInputModifiers.Control);
        window.MouseUp(new Point(150, 100), MouseButton.Left, RawInputModifiers.Control);
        // Longe do primeiro: no mesmo ponto seria duplo clique, que reenquadra a cena e nao seleciona.
        window.MouseDown(new Point(170, 115), MouseButton.Left);
        window.MouseUp(new Point(170, 115), MouseButton.Left);

        Assert.Equal(2, clicks.Count);
        Assert.True(clicks[0].Modifiers.HasFlag(KeyModifiers.Control));
        Assert.Equal(KeyModifiers.None, clicks[1].Modifiers);
        Assert.Equal(7, clicks[0].Pick?.ItemId);
    }

    [AvaloniaFact]
    public void HoveredPick_FollowsThePointer()
    {
        Viewer3DControl control = Control(new SphereItem { Id = 7, Radius = 0.5f, Color = Red });
        Window window = TestWindows.Show(control);
        TestWindows.Capture(window);

        window.MouseMove(new Point(150, 100));
        int? over = control.HoveredPick?.ItemId;
        window.MouseMove(new Point(3, 3));

        Assert.Equal(7, over);
        Assert.Null(control.HoveredPick);
    }
}
