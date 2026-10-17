// Viewer3D sample: a spherical surface, a height field, a ball-and-stick scene and a crystal structure.
// Usage: Viewer3D.Sample             opens the window
//        Viewer3D.Sample --export DIR writes the README images to DIR
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Layout;
using Avalonia.Themes.Fluent;
using Viewer3D;
using Viewer3D.Avalonia;
using Viewer3D.Crystal;

namespace Viewer3D.Sample;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder builder = AppBuilder.Configure<SampleApp>().UsePlatformDetect();
        int export = Array.IndexOf(args, "--export");
        if (export >= 0)
        {
            string dir = export + 1 < args.Length ? args[export + 1] : ".";
            builder.SetupWithoutStarting();
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "surface.png"), Scenes.Viewer(Scenes.Surface(90), Scenes.Surface(30)).RenderToPng(640, 480));
            File.WriteAllBytes(Path.Combine(dir, "atoms.png"), Scenes.Viewer(Scenes.Atoms(), null).RenderToPng(640, 480));
            File.WriteAllBytes(Path.Combine(dir, "crystal-ball-and-stick.png"), Scenes.Crystal(CrystalStyle.BallAndStick).RenderToPng(640, 480));
            File.WriteAllBytes(Path.Combine(dir, "crystal-space-filling.png"), Scenes.Crystal(CrystalStyle.SpaceFilling).RenderToPng(640, 480));
            Console.WriteLine($"Images written to {Path.GetFullPath(dir)}");
            return 0;
        }
        builder.StartWithClassicDesktopLifetime(args);
        return 0;
    }
}

internal sealed class SampleApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "Viewer3D sample",
                Width = 960,
                Height = 720,
                Content = new DockPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            [DockPanel.DockProperty] = Dock.Bottom,
                            Margin = new Thickness(8),
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Text = "Drag to rotate, right drag or Shift drag to move, wheel to zoom, double click to frame, arrows and +/- from the keyboard.",
                        },
                        new TabControl
                        {
                            ItemsSource = new[]
                            {
                                new TabItem { Header = "Spherical surface", Content = Scenes.Viewer(Scenes.Surface(90), Scenes.Surface(30)) },
                                new TabItem { Header = "Height field", Content = Scenes.Viewer(Scenes.HeightField(), null) },
                                new TabItem { Header = "Balls and sticks", Content = Scenes.Viewer(Scenes.Atoms(), null) },
                                new TabItem { Header = "Crystal", Content = Scenes.Crystal(CrystalStyle.BallAndStick) },
                                new TabItem { Header = "Space filling", Content = Scenes.Crystal(CrystalStyle.SpaceFilling) },
                            },
                        },
                    },
                },
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}

internal static class Scenes
{
    static readonly Rgba[] Palette =
    [
        new(68, 1, 84), new(72, 40, 120), new(62, 74, 137), new(49, 104, 142), new(38, 130, 142),
        new(31, 158, 137), new(53, 183, 121), new(109, 205, 89), new(180, 222, 44), new(253, 231, 37),
    ];

    public static Viewer3DControl Viewer(Scene scene, Scene? low)
    {
        var viewer = new Viewer3DControl
        {
            Scene = scene,
            LowDetailScene = low,
            AxisDirections = [new(Vector3.UnitX, "x"), new(Vector3.UnitY, "y"), new(Vector3.UnitZ, "z")],
        };
        viewer.Camera!.Orbit(-40, -60);
        viewer.Camera.Frame(scene);
        return viewer;
    }

    public static Scene Surface(int n)
    {
        var radii = new double[n + 1][];
        for (int i = 0; i <= n; i++)
        {
            double theta = i * Math.PI / n;
            int count = i == 0 || i == n ? 1 : 2 * n;
            radii[i] = new double[count];
            for (int j = 0; j < count; j++)
            {
                double phi = j * Math.PI / n, s = Math.Sin(theta);
                radii[i][j] = 1 + 0.35 * Math.Cos(2 * theta) + 0.2 * s * s * Math.Cos(4 * phi);
            }
        }
        double[] levels = Enumerable.Range(0, Palette.Length).Select(k => 0.6 + 0.08 * k).ToArray();
        return new Scene([SurfaceBuilder.Spherical(radii, n, map: new LevelColorMap(levels, Palette), lighting: MeshLighting.KeyLight)]);
    }

    public static Scene HeightField()
    {
        const int size = 80;
        var z = new double[size][];
        for (int i = 0; i < size; i++)
        {
            z[i] = new double[size];
            double y = -4 + 8.0 * i / (size - 1);
            for (int j = 0; j < size; j++)
            {
                double x = -4 + 8.0 * j / (size - 1);
                z[i][j] = 1.5 * Math.Sin(x) * Math.Cos(y) * Math.Exp(-(x * x + y * y) / 8);
            }
        }
        var map = new FuncColorMap(v => Rgba.Lerp(Palette[0], Palette[^1], (float)((v + 1) / 2)));
        return new Scene([SurfaceBuilder.HeightField(z, -4, 4, -4, 4, map: map, lighting: MeshLighting.KeyLight)]);
    }

    public static Scene Atoms()
    {
        var items = new List<SceneItem>();
        var blue = new Rgba(70, 110, 220);
        var orange = new Rgba(235, 140, 40);
        const float corner = 0.35f, center = 0.45f;
        var body = Vector3.Zero;
        items.Add(new SphereItem { Id = 0, Center = body, Radius = center, Color = orange });
        items.Add(new LabelItem { Position = body, Text = "Cs", Size = 14, Offset = new Vector2(30, -48) });
        int id = 1;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    var p = new Vector3(x, y, z);
                    items.Add(new SphereItem { Id = id, Center = p, Radius = corner, Color = blue });
                    items.Add(new RodItem { Id = 100 + id, Start = body, End = p, Radius = 0.08f, StartColor = orange, EndColor = blue, StartTrim = center, EndTrim = corner });
                    id++;
                }
            }
        }
        // Cell edges.
        Vector3[] c = [new(-1, -1, -1), new(1, -1, -1), new(1, 1, -1), new(-1, 1, -1), new(-1, -1, 1), new(1, -1, 1), new(1, 1, 1), new(-1, 1, 1)];
        int[] edges = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
        for (int e = 0; e < edges.Length; e += 2)
            items.Add(new LineItem { Start = c[edges[e]], End = c[edges[e + 1]], Color = new Rgba(120, 120, 120), Width = 1.5f });
        return new Scene(items);
    }

    /// <summary>
    /// Fluorite, CaF2 (Fm-3m, a = 5.463 angstrom), with every Ca-F bond (2.366 angstrom) and the Shannon ionic radii
    /// of Ca2+ (VIII, 1.12) and F- (IV, 1.31).
    /// </summary>
    public static Viewer3DControl Crystal(CrystalStyle style)
    {
        const double a = 5.463;
        var cell = new CrystalCell(a, a, a, 90, 90, 90);
        var atoms = new List<CrystalAtom>();
        foreach ((double x, double y, double z) in new[] { (0, 0, 0), (0, 0.5, 0.5), (0.5, 0, 0.5), (0.5, 0.5, 0.0) })
            atoms.Add(new CrystalAtom("Ca1", "Ca", x, y, z) { SiteIndex = 0, Radius = 1.12 });
        foreach (double x in new[] { 0.25, 0.75 })
            foreach (double y in new[] { 0.25, 0.75 })
                foreach (double z in new[] { 0.25, 0.75 })
                    atoms.Add(new CrystalAtom("F1", "F", x, y, z) { SiteIndex = 1, Radius = 1.31 });
        // The bonds of one Ca, as a program that looks around one atom per site gives them; the expansion adds the rest.
        var bonds = new List<CrystalBond>();
        for (int f = 4; f < atoms.Count; f++)
            for (int i = -1; i <= 0; i++)
                for (int j = -1; j <= 0; j++)
                    for (int k = -1; k <= 0; k++)
                    {
                        Vector3 d = cell.ToCartesian(atoms[f].X + i, atoms[f].Y + j, atoms[f].Z + k);
                        if (d.Length() < 2.5f)
                            bonds.Add(new CrystalBond(0, f, new LatticeShift(i, j, k)));
                    }
        var structure = new CrystalStructure(cell, atoms, bonds);
        structure = structure with { Bonds = CrystalBondExpansion.ExtendToEquivalentAtoms(structure) };
        CrystalScene scene = new CrystalSceneBuilder().Build(structure, new CrystalSceneOptions { Style = style, IncludeLabels = false });
        var viewer = new Viewer3DControl { Scene = scene.Scene, AxisDirections = scene.Axes };
        viewer.Camera!.LookAlong(-cell.AVector, cell.CVector);
        viewer.Camera.Orbit(-35, -25);
        viewer.Camera.Frame(scene.Scene);
        return viewer;
    }
}
