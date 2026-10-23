using System.Globalization;
using System.Numerics;
using System.Text;
using Viewer3D;
using Viewer3D.Avalonia;

namespace Viewer3D.Sample;

/// <summary>
/// Times a Wavefront OBJ model as a function of its triangle count: a latitude-longitude sphere written as OBJ text, the
/// time of <see cref="ObjReader.Read(TextReader, Func{string, TextReader?}?, ObjReadOptions?, CancellationToken)"/>, of
/// <see cref="FramePreparer.Prepare(Scene, Camera, float, float, DrawList)"/> and of a whole
/// <see cref="Viewer3DControl.RenderToPng"/>. A host checks <see cref="ObjReadOptions.MaxTriangles"/> against where the
/// frame rate falls below what it accepts. Run in Release: <c>--bench-obj 25000,100000,250000,500000</c>.
/// </summary>
internal static class ObjBench
{
    const int Width = 960, Height = 720;

    public static void Run(IReadOnlyList<int> counts)
    {
        Console.WriteLine($"OBJ sphere, {Width} x {Height}, median of 15 runs (ms, and frames per second of render+png):");
        Console.WriteLine(" asked   triangles      read   prepare   render+png      fps");
        // The bench measures past the default ceiling on purpose.
        var options = new ObjReadOptions { MaxTriangles = int.MaxValue, MaxVertices = int.MaxValue, MaxFileBytes = long.MaxValue };
        foreach (int asked in counts)
        {
            // 2 slices (stacks - 1) triangles, with twice as many slices as stacks.
            int stacks = Math.Max(3, (int)MathF.Round(MathF.Sqrt(asked / 4f)));
            string text = ObjText.Sphere(stacks, 2 * stacks, bands: 1);
            Scene scene = ObjReader.Read(new StringReader(text), options: options).ToScene();
            int triangles = scene.Items.OfType<MeshItem>().Sum(m => m.TriangleCount);
            var camera = new Camera();
            camera.Orbit(40, 30);
            camera.Frame(scene);
            var preparer = new FramePreparer();
            var list = new DrawList();
            var control = new Viewer3DControl { Scene = scene };
            control.Camera!.CopyFrom(camera);
            double read = EllipsoidBench.Median(() => ObjReader.Read(new StringReader(text), options: options));
            double prepare = EllipsoidBench.Median(() => preparer.Prepare(scene, camera, Width, Height, list));
            double render = EllipsoidBench.Median(() => control.RenderToPng(Width, Height));
            Console.WriteLine($"{asked,6}   {triangles,9}   {read,7:F2}   {prepare,7:F2}   {render,10:F2}   {1000 / render,6:F1}");
        }
    }
}

/// <summary>OBJ text written by the sample, so no model file is kept in the repository.</summary>
internal static class ObjText
{
    /// <summary>
    /// A unit sphere of <paramref name="stacks"/> rings and <paramref name="slices"/> segments as OBJ text, with quads
    /// (split by the reader) and a triangle fan at each pole. With <paramref name="bands"/> above 1, the rings are
    /// painted in that many bands that alternate between two materials, defined in <see cref="SphereMaterials"/>.
    /// </summary>
    public static string Sphere(int stacks, int slices, int bands)
    {
        var text = new StringBuilder();
        if (bands > 1)
            text.AppendLine("mtllib sphere.mtl");
        text.AppendLine("v 0 1 0");
        for (int i = 1; i < stacks; i++)
        {
            double theta = Math.PI * i / stacks;
            for (int j = 0; j < slices; j++)
            {
                double phi = 2 * Math.PI * j / slices;
                text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                    $"v {Math.Sin(theta) * Math.Cos(phi):F6} {Math.Cos(theta):F6} {-Math.Sin(theta) * Math.Sin(phi):F6}"));
            }
        }
        text.AppendLine("v 0 -1 0");
        int bottom = 2 + (stacks - 1) * slices;
        int Ring(int i, int j) => 2 + (i - 1) * slices + j % slices;
        string? material = null;
        void Use(int ring)
        {
            if (bands <= 1)
                return;
            string next = ring * bands / stacks % 2 == 0 ? "warm" : "cool";
            if (next != material)
                text.AppendLine($"usemtl {next}");
            material = next;
        }
        Use(0);
        for (int j = 0; j < slices; j++)
            text.AppendLine($"f 1 {Ring(1, j)} {Ring(1, j + 1)}");
        for (int i = 1; i < stacks - 1; i++)
        {
            Use(i);
            for (int j = 0; j < slices; j++)
                text.AppendLine($"f {Ring(i, j)} {Ring(i + 1, j)} {Ring(i + 1, j + 1)} {Ring(i, j + 1)}");
        }
        Use(stacks - 1);
        for (int j = 0; j < slices; j++)
            text.AppendLine($"f {bottom} {Ring(stacks - 1, j + 1)} {Ring(stacks - 1, j)}");
        return text.ToString();
    }

    /// <summary>The two materials of a banded <see cref="Sphere"/>.</summary>
    public const string SphereMaterials = """
        newmtl warm
        Kd 0.92 0.55 0.16
        newmtl cool
        Kd 0.22 0.45 0.86
        """;

    /// <summary>A banded sphere, read back as a scene: what the README shows for OBJ meshes.</summary>
    public static Scene BandedSphere() =>
        ObjReader.Read(new StringReader(Sphere(24, 48, bands: 6)),
            name => name == "sphere.mtl" ? new StringReader(SphereMaterials) : null).ToScene();
}
