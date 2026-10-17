using System.Diagnostics;
using System.Numerics;
using Viewer3D;
using Viewer3D.Avalonia;

namespace Viewer3D.Sample;

/// <summary>
/// Times a frame of many ellipsoids: <see cref="FramePreparer.Prepare(Scene, Camera, float, float, DrawList)"/> alone,
/// and a whole <see cref="Viewer3DControl.RenderToPng"/> (prepare, paint and PNG encoding), with and without the
/// principal ellipses, against the same number of spheres. Run in Release: <c>--bench-ellipsoids N</c>.
/// </summary>
internal static class EllipsoidBench
{
    const int Width = 960, Height = 720, Warmup = 3, Runs = 15;

    public static void Run(int count)
    {
        var random = new Random(150);
        var ellipsoids = new List<SceneItem>(count);
        var ellipsoidsLight = new List<SceneItem>(count);
        var spheres = new List<SceneItem>(count);
        int side = (int)Math.Ceiling(Math.Cbrt(count));
        for (int k = 0; k < count; k++)
        {
            var center = new Vector3(k % side, k / side % side, k / (side * side)) * 1.2f;
            Quaternion q = Quaternion.CreateFromYawPitchRoll((float)random.NextDouble() * 6, (float)random.NextDouble() * 6, 0);
            Vector3[] axes = [Vector3.Transform(new Vector3(0.35f, 0, 0), q), Vector3.Transform(new Vector3(0, 0.25f, 0), q),
                Vector3.Transform(new Vector3(0, 0, 0.15f), q)];
            var color = new Rgba((byte)random.Next(60, 220), 90, 140);
            ellipsoids.Add(new EllipsoidItem { Id = k, Center = center, Axes = axes, Color = color });
            ellipsoidsLight.Add(new EllipsoidItem { Id = k, Center = center, Axes = axes, Color = color, PrincipalEllipses = false });
            spheres.Add(new SphereItem { Id = k, Center = center, Radius = 0.25f, Color = color });
        }
        Console.WriteLine($"{count} items, {Width} x {Height}, median of {Runs} runs (ms):");
        Console.WriteLine("scene                        prepare   render+png");
        Report("ellipsoids with arcs", new Scene(ellipsoids));
        Report("ellipsoids without arcs", new Scene(ellipsoidsLight));
        Report("spheres", new Scene(spheres));
    }

    static void Report(string name, Scene scene)
    {
        var camera = new Camera();
        camera.Frame(scene);
        camera.Orbit(40, 30);
        var preparer = new FramePreparer();
        var list = new DrawList();
        var control = new Viewer3DControl { Scene = scene };
        control.Camera!.CopyFrom(camera);
        double prepare = Median(() => preparer.Prepare(scene, camera, Width, Height, list));
        double render = Median(() => control.RenderToPng(Width, Height));
        Console.WriteLine($"{name,-28} {prepare,7:F2}   {render,10:F2}");
    }

    static double Median(Action action)
    {
        for (int k = 0; k < Warmup; k++)
            action();
        var times = new double[Runs];
        for (int k = 0; k < Runs; k++)
        {
            long start = Stopwatch.GetTimestamp();
            action();
            times[k] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }
        Array.Sort(times);
        return times[Runs / 2];
    }
}
