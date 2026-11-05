using Viewer3D;
using Viewer3D.Avalonia;
using Viewer3D.Crystal;

namespace Viewer3D.Sample;

/// <summary>
/// Times the scene of the fluorite of the sample, in ball and stick with the bonded neighbors faded, as a function of its
/// cost (<see cref="CrystalSceneEstimate.Cost"/>): for each cost asked, the largest block of cells under it, the time of
/// <see cref="CrystalSceneBuilder.Build(CrystalStructure, CrystalSceneOptions, CrystalAppearance?)"/>, of
/// <see cref="FramePreparer.Prepare(Scene, Camera, float, float, DrawList)"/> and of a whole
/// <see cref="Viewer3DControl.RenderToPng"/>. A host picks its <see cref="CrystalSceneOptions.MaxCost"/> from where the frame
/// rate falls below what it accepts. Run in Release: <c>--bench-cost 3000,6000,12000</c>.
/// </summary>
internal static class CostBench
{
    const int Width = 960, Height = 720;

    public static void Run(IReadOnlyList<int> costs)
    {
        CrystalStructure structure = Scenes.Fluorite();
        var builder = new CrystalSceneBuilder();
        Console.WriteLine($"fluorite, ball and stick, {Width} x {Height}, median of 15 runs (ms, and frames per second of render+png):");
        Console.WriteLine("asked   block      estimate    built   items     build   prepare   render+png      fps");
        foreach (int asked in costs)
        {
            // Sized by the estimate, built without the ceiling: the faded neighbors count only in the scene built, which
            // passes the cost asked and would fall back to one cell, measuring the same scene at every cost. A host compares
            // MaxCost with the cost of the scene built, so that is the column to pick the ceiling from.
            CrystalSceneOptions options = Block(builder, structure, asked) with { MaxCost = int.MaxValue };
            CrystalScene scene = builder.Build(structure, options);
            int estimate = builder.Estimate(structure, options).Cost;
            int built = scene.AtomCount + CrystalSceneOptions.PolyhedronCost * scene.PolyhedronCount;
            var camera = new Camera();
            camera.Frame(scene.Scene);
            camera.Orbit(40, 30);
            var preparer = new FramePreparer();
            var list = new DrawList();
            var control = new Viewer3DControl { Scene = scene.Scene };
            control.Camera!.CopyFrom(camera);
            double build = EllipsoidBench.Median(() => builder.Build(structure, options));
            double prepare = EllipsoidBench.Median(() => preparer.Prepare(scene.Scene, camera, Width, Height, list));
            double render = EllipsoidBench.Median(() => control.RenderToPng(Width, Height));
            string block = $"{options.RepeatA} x {options.RepeatB} x {options.RepeatC}";
            Console.WriteLine($"{asked,5}   {block,-10} {estimate,8}   {built,6}   {scene.Scene.Items.Count,5}   {build,7:F2}   {prepare,7:F2}   {render,10:F2}   {1000 / render,6:F1}");
        }
    }

    /// <summary>The largest block, grown one axis at a time, whose cost stays under <paramref name="cost"/>, with that ceiling.</summary>
    internal static CrystalSceneOptions Block(CrystalSceneBuilder builder, CrystalStructure structure, int cost)
    {
        var options = new CrystalSceneOptions { MaxCost = cost };
        while (true)
        {
            CrystalSceneOptions next = ((options.RepeatA + options.RepeatB + options.RepeatC) % 3) switch
            {
                0 => options with { RepeatA = options.RepeatA + 1 },
                1 => options with { RepeatB = options.RepeatB + 1 },
                _ => options with { RepeatC = options.RepeatC + 1 },
            };
            if (!builder.Estimate(structure, next).Fit)
                return options;
            options = next;
        }
    }
}
