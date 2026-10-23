using System.Numerics;
using System.Runtime.CompilerServices;
using Viewer3D;
using Viewer3D.Avalonia;
using Viewer3D.Crystal;

namespace Viewer3D.Sample;

/// <summary>
/// Measures a <see cref="SceneSequence"/>. <c>--bench-sequence-memory N</c> counts the managed bytes a frame holds per
/// unit of <see cref="SceneSequence.WeightOf"/> (items plus mesh vertices), for the kinds of frame an animation is made
/// of: fluorite blocks of cost about N in ball and stick and in polyhedra, N ellipsoids, 500 translucent octahedra and
/// an OBJ sphere of 25,000 triangles. The worst kind sets <see cref="SceneSequence.DefaultMaxWeight"/>. It counts bytes,
/// not time, so it runs on a busy machine. <c>--bench-sequence N</c> times a frame of a sequence of fluorite blocks of
/// cost about N: the frame change with <see cref="FramePreparer.Prepare(Scene, Camera, float, float, DrawList)"/>, and a
/// whole <see cref="Viewer3DControl.RenderToPng"/>. Run both in Release.
/// </summary>
internal static class SequenceBench
{
    const int Width = 960, Height = 720, Frames = 30;

    // Weight retained per kind, so that a kind is measured over a few million units whatever its frame weighs.
    const long TargetWeight = 2_000_000;

    public static void RunMemory(int cost)
    {
        CrystalStructure fluorite = Scenes.Fluorite();
        var builder = new CrystalSceneBuilder();
        CrystalSceneOptions block = Unbounded(builder, fluorite, cost);
        CrystalSceneOptions polyhedra = block with { Style = CrystalStyle.Polyhedra };
        // 2 x 158 x 78 = 24,648 triangles.
        string sphere = ObjText.Sphere(79, 158, bands: 1);
        var kinds = new (string Name, Func<int, Scene> Frame)[]
        {
            ($"fluorite {block.RepeatA}x{block.RepeatB}x{block.RepeatC}, ball and stick", _ => builder.Build(fluorite, block).Scene),
            ($"fluorite {block.RepeatA}x{block.RepeatB}x{block.RepeatC}, polyhedra", _ => builder.Build(fluorite, polyhedra).Scene),
            ($"{cost} ellipsoids", k => Ellipsoids(cost, k)),
            ("500 octahedra, double-sided", _ => PolyhedronBench.DoubleSided(500)),
            ("OBJ sphere, 25,000 triangles", _ => ObjReader.Read(new StringReader(sphere)).ToScene()),
        };
        Console.WriteLine("managed bytes held per unit of weight (items plus mesh vertices), frames kept alive in a sequence:");
        Console.WriteLine("frame                                        weight   frames     MiB   bytes/unit");
        double worst = 0;
        foreach (var (name, frame) in kinds)
        {
            long one = SceneSequence.WeightOf(frame(0));
            int count = (int)Math.Clamp(TargetWeight / Math.Max(1, one), 4, 2000);
            (long weight, long bytes) = Held(frame, count);
            double perUnit = (double)bytes / weight;
            worst = Math.Max(worst, perUnit);
            Console.WriteLine($"{name,-42} {one,8:N0}   {count,6}   {bytes / 1048576.0,6:F1}   {perUnit,10:F1}");
        }
        Console.WriteLine($"worst {worst:F1} bytes per unit: 512 MiB holds {Math.Floor(512.0 * 1048576 / worst):N0} units.");
    }

    // In its own frame, so nothing of the kind measured before is still reachable from a stack slot.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static (long Weight, long Bytes) Held(Func<int, Scene> frame, int count)
    {
        long before = GC.GetTotalMemory(true);
        var sequence = new SceneSequence(Enumerable.Range(1, count).Select(frame).ToArray(), TimeSpan.FromMilliseconds(40),
            maxWeight: long.MaxValue);
        long after = GC.GetTotalMemory(true);
        GC.KeepAlive(sequence);
        return (sequence.Weight, after - before);
    }

    // The block CostBench.Block sizes for cost, with no ceiling: the faded neighbors push the scene built past the cost
    // the estimate counted, and the builder would fall back to one cell.
    static CrystalSceneOptions Unbounded(CrystalSceneBuilder builder, CrystalStructure structure, int cost)
        => CostBench.Block(builder, structure, cost) with { MaxCost = int.MaxValue };

    public static void RunTime(int cost)
    {
        CrystalStructure fluorite = Scenes.Fluorite();
        var builder = new CrystalSceneBuilder();
        CrystalSceneOptions block = Unbounded(builder, fluorite, cost);
        var sequence = new SceneSequence(Enumerable.Range(0, Frames).Select(_ => builder.Build(fluorite, block).Scene).ToArray(),
            TimeSpan.FromMilliseconds(40));
        var control = new Viewer3DControl { Sequence = sequence };
        control.FrameScene();
        control.Camera!.Orbit(40, 30);
        var camera = control.Camera.Clone();
        var preparer = new FramePreparer();
        var list = new DrawList();
        int next = 0;
        double prepare = EllipsoidBench.Median(() =>
        {
            control.FrameIndex = next++ % Frames;
            preparer.Prepare(sequence.Frames[control.FrameIndex], camera, Width, Height, list);
        });
        double render = EllipsoidBench.Median(() =>
        {
            control.FrameIndex = next++ % Frames;
            control.RenderToPng(Width, Height);
        });
        Console.WriteLine($"fluorite {block.RepeatA} x {block.RepeatB} x {block.RepeatC}, {sequence.Frames[0].Items.Count} items per frame, "
            + $"{Frames} frames, {Width} x {Height}, median of 15 runs (ms, and frames per second of render+png):");
        Console.WriteLine("frame change + prepare   render+png      fps");
        Console.WriteLine($"{prepare,22:F2}   {render,10:F2}   {1000 / render,6:F1}");
    }

    // Count ellipsoids on a grid, with orientations that change with the frame, so no two frames share an item.
    static Scene Ellipsoids(int count, int frame)
    {
        var random = new Random(150 + frame);
        var items = new List<SceneItem>(count);
        int side = (int)Math.Ceiling(Math.Cbrt(count));
        for (int k = 0; k < count; k++)
        {
            var center = new Vector3(k % side, k / side % side, k / (side * side)) * 1.2f;
            Quaternion q = Quaternion.CreateFromYawPitchRoll((float)random.NextDouble() * 6, (float)random.NextDouble() * 6, 0);
            Vector3[] axes = [Vector3.Transform(new Vector3(0.35f, 0, 0), q), Vector3.Transform(new Vector3(0, 0.25f, 0), q),
                Vector3.Transform(new Vector3(0, 0, 0.15f), q)];
            items.Add(new EllipsoidItem { Id = k, Center = center, Axes = axes, Color = new Rgba(160, 90, 140) });
        }
        return new Scene(items);
    }
}
