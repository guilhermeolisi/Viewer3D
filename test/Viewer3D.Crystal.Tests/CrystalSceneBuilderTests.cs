using System.Diagnostics;
using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class CrystalSceneBuilderTests
{
    // Posicoes em float (Vector3), em angstrom: 1e-4 A cobre o arredondamento em celas de ate ~20 A.
    const float Tolerance = 1e-4f;

    static readonly CrystalSceneBuilder Builder = new();

    static CrystalSceneOptions Options(bool boundary = true, bool complete = true) => new()
    {
        ShowBoundaryAtoms = boundary,
        CompleteCoordination = complete,
    };

    static IEnumerable<(SphereItem Sphere, CrystalItemInfo Info)> Spheres(CrystalScene scene)
        => scene.Scene.Items.OfType<SphereItem>().Select(s => (s, scene.Lookup(s.Id)!));

    static IEnumerable<(RodItem Rod, CrystalItemInfo Info)> Rods(CrystalScene scene)
        => scene.Scene.Items.OfType<RodItem>().Select(r => (r, scene.Lookup(r.Id)!));

    static string ElementOf(CrystalStructure s, CrystalItemInfo info) => s.Atoms[info.AtomIndex].Element;

    [Fact]
    public void Fluorite_DrawsFourCaAndEightF_WithoutBoundaryAtoms()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options(boundary: false, complete: false));

        var spheres = Spheres(scene).ToList();
        Assert.Equal(4, spheres.Count(x => ElementOf(s, x.Info) == "Ca"));
        Assert.Equal(8, spheres.Count(x => ElementOf(s, x.Info) == "F"));
        Assert.Equal(12, scene.AtomCount);
    }

    [Fact]
    public void Fluorite_DrawsFourteenCa_WithBoundaryAtoms()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options(boundary: true, complete: false));

        var spheres = Spheres(scene).ToList();
        Assert.Equal(14, spheres.Count(x => ElementOf(s, x.Info) == "Ca"));
        Assert.Equal(8, spheres.Count(x => ElementOf(s, x.Info) == "F"));
        Assert.All(spheres, x => Assert.False(x.Info.IsCompletion));
    }

    [Fact]
    public void Fluorite_EachCellCa_HasEightCaFBonds_WithCompleteCoordination()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options(boundary: false, complete: true));

        var rods = Rods(scene).Select(x => x.Rod).ToList();
        var ownCa = Spheres(scene).Where(x => !x.Info.IsCompletion && ElementOf(s, x.Info) == "Ca").ToList();
        Assert.Equal(4, ownCa.Count);
        foreach ((SphereItem ca, _) in ownCa)
        {
            var ends = rods.Where(r => Near(r.Start, ca.Center) || Near(r.End, ca.Center)).ToList();
            Assert.Equal(8, ends.Count);
            Assert.All(ends, r => Assert.Equal(Fixtures.FluoriteCaF, (r.End - r.Start).Length(), Tolerance));
        }
        // Ca na origem: so o F em (1/4, 1/4, 1/4) esta na cela; os outros sete entram esmaecidos, e marcados.
        var completions = Spheres(scene).Where(x => x.Info.IsCompletion).ToList();
        Assert.NotEmpty(completions);
        Assert.All(completions, x => Assert.True(x.Sphere.Color.A < 255));
        Assert.Contains(completions, x => ElementOf(s, x.Info) == "F" && Near(x.Sphere.Center, Fixtures.FluoriteCell.ToCartesian(-0.25, -0.25, -0.25)));
        Assert.Equal(completions.Count, completions.Select(x => x.Sphere.Center).Distinct().Count());
        Assert.All(Rods(scene), x => Assert.Equal(x.Info.IsCompletion,
            completions.Any(c => Near(c.Sphere.Center, x.Rod.Start) || Near(c.Sphere.Center, x.Rod.End))));
    }

    [Fact]
    public void BondsToOutside_Disappear_WithoutCompleteCoordination()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options(boundary: false, complete: false));

        // Ca(0,0,0) liga 1 F da cela; Ca(0,1/2,1/2), Ca(1/2,0,1/2) e Ca(1/2,1/2,0), 4 cada.
        Assert.Equal(13, Rods(scene).Count());
        Assert.DoesNotContain(Spheres(scene), x => x.Info.IsCompletion);
    }

    [Fact]
    public void Repeat2x2x2_MultipliesTheCellAtoms()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options(boundary: false, complete: false) with { RepeatA = 2, RepeatB = 2, RepeatC = 2 });

        Assert.Equal(96, scene.AtomCount);
        Assert.Equal(96, scene.Scene.Items.OfType<SphereItem>().Count());
        Assert.Null(scene.Warning);
    }

    [Fact]
    public void RepeatAboveTheCeiling_FallsBackToOneCell_AndWarns()
    {
        // 120 atomos por cela: 3 x 3 x 3 daria 3240, acima do teto de 3000.
        var cell = new CrystalCell(20, 20, 20, 90, 90, 90);
        var atoms = Enumerable.Range(0, 120)
            .Select(k => new CrystalAtom($"O{k}", "O", 0.1 + 0.8 * (k % 5) / 5, 0.1 + 0.8 * (k / 5 % 5) / 5, 0.1 + 0.8 * (k / 25) / 5))
            .ToArray();
        var s = new CrystalStructure(cell, atoms, []);

        CrystalScene scene = Builder.Build(s, Options() with { RepeatA = 3, RepeatB = 3, RepeatC = 3 });

        Assert.Equal(120, scene.AtomCount);
        Assert.NotNull(scene.Warning);
        Assert.Equal(12, scene.Scene.Items.OfType<LineItem>().Count());
    }

    [Fact]
    public void CellEdges_AreTwelveLines_AndNoAxisIsDrawn()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene one = Builder.Build(s, Options());
        CrystalScene two = Builder.Build(s, Options() with { RepeatA = 2 });

        var lines = one.Scene.Items.OfType<LineItem>().ToList();
        Assert.Equal(12, lines.Count);
        Assert.All(lines, l => Assert.Equal(Fixtures.FluoriteA, (l.End - l.Start).Length(), Tolerance));
        Assert.All(lines, l => Assert.Equal(new CrystalSceneOptions().CellColor, l.Color));
        Assert.DoesNotContain(one.Scene.Items.OfType<LabelItem>(), l => l.Text is "a" or "b" or "c");
        // Duas celas ao longo de a dividem uma face: 24 - 4 arestas.
        Assert.Equal(20, two.Scene.Items.OfType<LineItem>().Count());
    }

    [Fact]
    public void Axes_AreTheCellVectors_WithoutColor()
    {
        var cell = new CrystalCell(4, 5, 6, 80, 95, 110);
        var s = new CrystalStructure(cell, [new CrystalAtom("O1", "O", 0.1, 0.2, 0.3)], []);

        CrystalScene scene = Builder.Build(s, Options());

        Assert.Equal(["a", "b", "c"], scene.Axes.Select(a => a.Label));
        Assert.All(scene.Axes, a => Assert.Null(a.Color));
        Assert.Equal(cell.AVector, scene.Axes[0].Direction);
        Assert.Equal(cell.BVector, scene.Axes[1].Direction);
        Assert.Equal(cell.CVector, scene.Axes[2].Direction);
    }

    [Fact]
    public void MixedSite_IsPaintedInSectorsByFraction()
    {
        var atom = new CrystalAtom("M1", "Fe", 0.5, 0.5, 0.5) { Shares = [new("Mg", 0.3), new("Fe", 0.7)] };
        var s = new CrystalStructure(new CrystalCell(5, 5, 5, 90, 90, 90), [atom], []);

        CrystalScene scene = Builder.Build(s, Options());

        SphereItem sphere = scene.Scene.Items.OfType<SphereItem>().Single();
        Assert.True(ElementTable.TryGet("Fe", out ElementInfo fe));
        Assert.True(ElementTable.TryGet("Mg", out ElementInfo mg));
        Assert.Equal(fe.Color, sphere.Color);
        Assert.NotNull(sphere.Sectors);
        Assert.Equal([new SphereSector(0.3f, mg.Color), new SphereSector(0.7f, fe.Color)], sphere.Sectors);
        Assert.Equal(["Mg", "Fe"], scene.Legend.Select(l => l.Element));
    }

    [Fact]
    public void PartialOccupancy_LeavesAnEmptySector()
    {
        var s = new CrystalStructure(new CrystalCell(5, 5, 5, 90, 90, 90),
            [new CrystalAtom("O1", "O", 0.5, 0.5, 0.5) { Occupancy = 0.6 }, new CrystalAtom("O2", "O", 0.1, 0.1, 0.1)], []);

        CrystalScene scene = Builder.Build(s, Options());

        var spheres = Spheres(scene).ToList();
        SphereItem partial = spheres.Single(x => x.Info.AtomIndex == 0).Sphere;
        Assert.True(ElementTable.TryGet("O", out ElementInfo o));
        Assert.Equal([new SphereSector(0.6f, o.Color)], partial.Sectors);
        Assert.Null(spheres.Single(x => x.Info.AtomIndex == 1).Sphere.Sectors);
    }

    [Fact]
    public void Labels_FollowIncludeLabels()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene with = Builder.Build(s, Options());
        CrystalScene without = Builder.Build(s, Options() with { IncludeLabels = false });

        // So os 12 atomos da cela levam rotulo, nem os da borda nem os esmaecidos.
        var labels = with.Scene.Items.OfType<LabelItem>().ToList();
        Assert.Equal(12, labels.Count);
        Assert.Equal(4, labels.Count(l => l.Text == "Ca1"));
        Assert.Empty(without.Scene.Items.OfType<LabelItem>());
    }

    [Fact]
    public void Lookup_ReturnsTheSourceAtomAndBond()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options());

        var spheres = Spheres(scene).ToList();
        var ids = scene.Scene.Items.Where(i => i is SphereItem or RodItem).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id > 0));
        Assert.All(spheres, x => Assert.Equal(CrystalItemKind.Atom, x.Info.Kind));
        (_, CrystalItemInfo cornerInfo) = spheres.Single(x =>
            Near(x.Sphere.Center, new Vector3((float)Fixtures.FluoriteA)) && !x.Info.IsCompletion);
        Assert.Equal(0, cornerInfo.AtomIndex);
        Assert.Equal(new LatticeShift(1, 1, 1), cornerInfo.Offset);
        foreach ((RodItem rod, CrystalItemInfo info) in Rods(scene))
        {
            Assert.Equal(CrystalItemKind.Bond, info.Kind);
            CrystalBond bond = s.Bonds[info.BondIndex];
            Assert.True(info.AtomIndex == bond.First || info.AtomIndex == bond.Second);
        }
        Assert.Null(scene.Lookup(scene.Scene.Items.OfType<LineItem>().First().Id));
        Assert.Null(scene.Lookup(-5));
    }

    [Fact]
    public void Legend_ListsEachElementOnce()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, Options());

        Assert.True(ElementTable.TryGet("F", out ElementInfo f));
        Assert.True(ElementTable.TryGet("Ca", out ElementInfo ca));
        Assert.Equal([new ElementLegendEntry("F", f.Color), new ElementLegendEntry("Ca", ca.Color)], scene.Legend);
    }

    [Fact]
    public void OwnerColor_OverridesTheTable()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var green = new Rgba(0, 200, 0);

        CrystalScene scene = Builder.Build(s, Options() with { ElementColors = new Dictionary<string, Rgba> { ["Ca"] = green } });

        Assert.All(Spheres(scene).Where(x => !x.Info.IsCompletion && ElementOf(s, x.Info) == "Ca"), x => Assert.Equal(green, x.Sphere.Color));
        Assert.Contains(new ElementLegendEntry("Ca", green), scene.Legend);
    }

    [Fact]
    public void Bond_ToAnUnreducedAtom_KeepsItsDistance()
    {
        // O F e dado em (1,25; 0,25; 0,25), fora da cela; a ligacao do Ca da origem chega a ele pelo deslocamento -1
        // em a, a 2,3656 A. Reduzido a (0,25; 0,25; 0,25), a ligacao tem de continuar com o mesmo comprimento.
        var cell = Fixtures.FluoriteCell;
        CrystalAtom[] atoms = [new("Ca1", "Ca", 0, 0, 0), new("F1", "F", 1.25, 0.25, 0.25)];
        var s = new CrystalStructure(cell, atoms, [new CrystalBond(0, 1, new LatticeShift(-1, 0, 0))]);

        CrystalScene scene = Builder.Build(s, Options(boundary: false, complete: true));

        RodItem rod = Rods(scene).Single().Rod;
        Assert.Equal(Fixtures.FluoriteCaF, (rod.End - rod.Start).Length(), Tolerance);
        SphereItem f = Spheres(scene).Single(x => x.Info.AtomIndex == 1).Sphere;
        Assert.True(Near(f.Center, cell.ToCartesian(0.25, 0.25, 0.25)));
        Assert.Equal(new LatticeShift(-1, 0, 0), Spheres(scene).Single(x => x.Info.AtomIndex == 1).Info.Offset);
    }

    [Fact]
    public void CeilingScene_BuildsAndPreparesAFrame_UnderAGenerousLimit()
    {
        // 3000 atomos (fluorita 5 x 5 x 10 celas como uma cela so, 250 celas de 12 atomos) com as ligacoes.
        // Medido em 10/10/2026: 37,9 ms para 2000 atomos com pintura (E147); 2 s da folga de dezenas de vezes para
        // Debug e maquina ocupada, e so pega regressao grosseira.
        var atoms = new List<CrystalAtom>();
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                for (int k = 0; k < 10; k++)
                    atoms.AddRange(Fixtures.FluoriteAtoms().Select(a => a with { X = (a.X + i) / 5, Y = (a.Y + j) / 5, Z = (a.Z + k) / 10 }));
        var cell = new CrystalCell(5 * Fixtures.FluoriteA, 5 * Fixtures.FluoriteA, 10 * Fixtures.FluoriteA, 90, 90, 90);
        CrystalStructure s = new(cell, atoms, []);
        var preparer = new FramePreparer();
        var camera = new Camera();

        var watch = Stopwatch.StartNew();
        CrystalScene scene = Builder.Build(s, Options(boundary: false));
        camera.Frame(scene.Scene);
        DrawList list = preparer.Prepare(scene.Scene, camera, 800, 600);
        watch.Stop();

        Assert.Equal(3000, scene.AtomCount);
        Assert.Null(scene.Warning);
        Assert.True(list.Circles.Length > 0);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"{watch.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public void InvalidCell_GivesAnEmptySceneWithAWarning()
    {
        var s = new CrystalStructure(new CrystalCell(4, 4, 4, 120, 120, 120), [new CrystalAtom("O1", "O", 0, 0, 0)], []);

        CrystalScene scene = Builder.Build(s, Options());

        Assert.Empty(scene.Scene.Items);
        Assert.NotNull(scene.Warning);
    }

    [Fact]
    public void AtomOffsets_OfAnotherLength_AreRefused()
    {
        CrystalStructure fluorite = Fixtures.Fluorite();

        var refused = Assert.Throws<ArgumentException>(() => Builder.Build(fluorite, Options() with { AtomOffsets = [Vector3.Zero] }));

        Assert.Contains("12 atoms", refused.Message);
    }

    static bool Near(Vector3 p, Vector3 q) => (p - q).Length() < 1e-3f;
}
