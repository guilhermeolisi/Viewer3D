using System.Numerics;
using Viewer3D;
using Viewer3D.Avalonia;

namespace Viewer3D.Sample;

/// <summary>
/// Times a frame of many coordination polyhedra: N translucent octahedra with their edges and seven spheres each (the
/// center and the six vertices), drawn double-sided as the still scene does and single-sided as the scene of a gesture
/// does, against the spheres alone. <see cref="FramePreparer.Prepare(Scene, Camera, float, float, DrawList)"/> alone and
/// a whole <see cref="Viewer3DControl.RenderToPng"/>. Run in Release: <c>--bench-polyhedra N</c>.
/// </summary>
internal static class PolyhedronBench
{
    const int Width = 960, Height = 720;

    /// <summary>The six vertices of an octahedron, and its eight faces counter-clockwise seen from outside.</summary>
    static readonly Vector3[] Octahedron = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1)];

    static readonly int[] Faces =
    [
        0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4,
        2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5,
    ];

    static readonly int[] Edges = [0, 2, 2, 1, 1, 3, 3, 0, 0, 4, 1, 4, 2, 4, 3, 4, 0, 5, 1, 5, 2, 5, 3, 5];

    public static void Run(int count)
    {
        var doubleSided = new List<SceneItem>(8 * count);
        var singleSided = new List<SceneItem>(8 * count);
        var spheres = new List<SceneItem>(7 * count);
        int side = (int)Math.Ceiling(Math.Cbrt(count));
        int id = 0;
        for (int k = 0; k < count; k++)
        {
            var center = new Vector3(k % side, k / side % side, k / (side * side)) * 3f;
            Vector3[] positions = [.. Octahedron.Select(v => center + v * 1.2f)];
            var color = new Rgba(70, 120, 200, 90);
            doubleSided.Add(Mesh(id, positions, color, true));
            singleSided.Add(Mesh(id, positions, color, false));
            id++;
            var atoms = new List<SceneItem> { new SphereItem { Id = id++, Center = center, Radius = 0.3f, Color = new Rgba(200, 120, 60) } };
            foreach (Vector3 p in positions)
                atoms.Add(new SphereItem { Id = id++, Center = p, Radius = 0.25f, Color = new Rgba(220, 40, 40) });
            doubleSided.AddRange(atoms);
            singleSided.AddRange(atoms);
            spheres.AddRange(atoms);
        }
        Console.WriteLine($"{count} octahedra, {Width} x {Height}, median of 15 runs (ms, and frames per second of render+png):");
        Console.WriteLine("scene                        prepare   render+png      fps");
        Report("polyhedra, double-sided", new Scene(doubleSided));
        Report("polyhedra, single-sided", new Scene(singleSided));
        Report("spheres only", new Scene(spheres));
    }

    /// <summary>The double-sided scene of <see cref="Run"/>: <paramref name="count"/> octahedra with their spheres.</summary>
    internal static Scene DoubleSided(int count)
    {
        var items = new List<SceneItem>(8 * count);
        int side = (int)Math.Ceiling(Math.Cbrt(count));
        int id = 0;
        for (int k = 0; k < count; k++)
        {
            var center = new Vector3(k % side, k / side % side, k / (side * side)) * 3f;
            Vector3[] positions = [.. Octahedron.Select(v => center + v * 1.2f)];
            items.Add(Mesh(id++, positions, new Rgba(70, 120, 200, 90), true));
            items.Add(new SphereItem { Id = id++, Center = center, Radius = 0.3f, Color = new Rgba(200, 120, 60) });
            foreach (Vector3 p in positions)
                items.Add(new SphereItem { Id = id++, Center = p, Radius = 0.25f, Color = new Rgba(220, 40, 40) });
        }
        return new Scene(items);
    }

    static MeshItem Mesh(int id, Vector3[] positions, Rgba color, bool doubleSided) => new()
    {
        Id = id,
        Positions = positions,
        Indices = Faces,
        Edges = Edges,
        Color = color,
        Lighting = MeshLighting.KeyLight,
        DoubleSided = doubleSided,
    };

    static void Report(string name, Scene scene)
    {
        var camera = new Camera();
        camera.Frame(scene);
        camera.Orbit(40, 30);
        var preparer = new FramePreparer();
        var list = new DrawList();
        var control = new Viewer3DControl { Scene = scene };
        control.Camera!.CopyFrom(camera);
        double prepare = EllipsoidBench.Median(() => preparer.Prepare(scene, camera, Width, Height, list));
        double render = EllipsoidBench.Median(() => control.RenderToPng(Width, Height));
        Console.WriteLine($"{name,-28} {prepare,7:F2}   {render,10:F2}   {1000 / render,6:F1}");
    }
}
