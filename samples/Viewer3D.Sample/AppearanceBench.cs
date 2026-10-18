using System.Collections.Immutable;
using Viewer3D;
using Viewer3D.Crystal;

namespace Viewer3D.Sample;

/// <summary>
/// Times <see cref="CrystalSceneBuilder.Build(CrystalStructure, CrystalSceneOptions, CrystalAppearance?)"/> on the
/// fluorite of the sample with its Ca cubes, repeated to the largest cell block that stays under N atoms, in the
/// Polyhedra style: with no appearance, with <see cref="CrystalAppearance.Default"/>, and with an appearance that has a
/// rule at every level. Every rebuild of the structure view of a host pays this once. Run in Release:
/// <c>--bench-appearance N</c>.
/// </summary>
internal static class AppearanceBench
{
    public static void Run(int atoms)
    {
        CrystalStructure structure = Scenes.Fluorite();
        int repeat = 1;
        while (structure.Atoms.Count * (repeat + 1) * (repeat + 1) * (repeat + 1) <= atoms)
            repeat++;
        var options = new CrystalSceneOptions { Style = CrystalStyle.Polyhedra, RepeatA = repeat, RepeatB = repeat, RepeatC = repeat };
        var builder = new CrystalSceneBuilder();
        CrystalAppearance full = Full(structure);
        CrystalScene sample = builder.Build(structure, options, full);
        Console.WriteLine($"fluorite {repeat} x {repeat} x {repeat}: {builder.Build(structure, options).Scene.Items.Count} items "
            + $"({sample.Scene.Items.Count} with the rules, {sample.HiddenAtomCount} atoms hidden), median of 15 runs (ms):");
        Console.WriteLine($"no appearance       {EllipsoidBench.Median(() => builder.Build(structure, options)),8:F2}");
        Console.WriteLine($"default appearance  {EllipsoidBench.Median(() => builder.Build(structure, options, CrystalAppearance.Default)),8:F2}");
        Console.WriteLine($"rules at every level{EllipsoidBench.Median(() => builder.Build(structure, options, full)),8:F2}");
    }

    /// <summary>A rule on each level of the three cascades, and a hidden image, so that every lookup has something to find.</summary>
    static CrystalAppearance Full(CrystalStructure structure) => CrystalAppearance.Default with
    {
        ColorScheme = AtomColorScheme.Site,
        AtomDefault = new AtomStyle { Opacity = 0.9 },
        ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add("F", new AtomStyle { RadiusScale = 0.8 }),
        BySite = ImmutableDictionary<int, AtomStyle>.Empty.Add(0, new AtomStyle { Color = new Rgba(40, 160, 60) }),
        ByAtom = ImmutableDictionary<int, AtomStyle>.Empty.Add(5, new AtomStyle { Opacity = 0.4 }),
        ByImage = ImmutableDictionary<AtomImage, AtomStyle>.Empty.Add(new AtomImage(4, new LatticeShift(0, 0, 0)), new AtomStyle { Visible = false }),
        BondDefault = new BondStyle { Radius = 0.08 },
        ByElementPair = ImmutableDictionary<ElementPair, BondStyle>.Empty.Add(ElementPair.Of("Ca", "F"), new BondStyle { Color = new Rgba(90, 90, 90) }),
        PolyhedronDefault = new PolyhedronStyle { FaceOpacity = 0.5 },
        PolyhedronByElement = ImmutableDictionary<string, PolyhedronStyle>.Empty.Add("Ca", new PolyhedronStyle { EdgeWidth = 2f }),
        PolyhedronBySite = ImmutableDictionary<int, PolyhedronStyle>.Empty.Add(0, new PolyhedronStyle { FaceColor = new Rgba(60, 120, 200) }),
        ByPolyhedron = ImmutableDictionary<int, PolyhedronStyle>.Empty.Add(structure.Polyhedra.Count - 1, new PolyhedronStyle { ShowEdges = false }),
    };
}
