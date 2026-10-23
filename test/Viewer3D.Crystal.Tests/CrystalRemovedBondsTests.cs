using System.Collections.Immutable;

namespace Viewer3D.Crystal.Tests;

public class CrystalRemovedBondsTests
{
    static readonly CrystalSceneBuilder Builder = new();

    static CrystalSceneOptions Plain => new() { ShowBoundaryAtoms = false, CompleteCoordination = false };

    /// <summary>Si at the center of a 10 A cube, with O at 1.6 A and K at 3.0 A; bonds Si-O (source 0) and K-Si (1).</summary>
    static CrystalStructure Trio(int firstSource = 0, int secondSource = 1)
    {
        var cell = new CrystalCell(10, 10, 10, 90, 90, 90);
        CrystalAtom[] atoms =
        [
            new("Si1", "Si", 0.5, 0.5, 0.5) { SiteIndex = 0 },
            new("O1", "O", 0.66, 0.5, 0.5) { SiteIndex = 1 },
            new("K1", "K", 0.5, 0.8, 0.5) { SiteIndex = 2 },
        ];
        CrystalBond[] bonds =
        [
            new(0, 1, LatticeShift.Zero) { SourceIndex = firstSource },
            new(2, 0, LatticeShift.Zero) { SourceIndex = secondSource },
        ];
        return new CrystalStructure(cell, atoms, bonds);
    }

    /// <summary>
    /// Fluorite with the bonds a program gives for one atom per site: the Ca at the origin with its 8 F (sources 0 to 7),
    /// the F at (1/4, 1/4, 1/4) with its 4 Ca (8 to 11), and one Ca-F at the second distance, sqrt(11)/4 a (12).
    /// </summary>
    static CrystalStructure Representative()
    {
        CrystalStructure all = Fixtures.Fluorite();
        var bonds = new List<CrystalBond>();
        foreach (CrystalBond b in all.Bonds.Where(b => b.First == 0))
            bonds.Add(b with { SourceIndex = bonds.Count });
        foreach (CrystalBond b in all.Bonds.Where(b => b.Second == 4))
            bonds.Add(new CrystalBond(4, b.First, LatticeShift.Zero - b.Shift) { SourceIndex = bonds.Count });
        bonds.Add(new CrystalBond(0, 4, new LatticeShift(-1, 0, 0)) { SourceIndex = bonds.Count });
        return new CrystalStructure(all.Cell, [.. all.Atoms], bonds);
    }

    static List<int> RodBonds(CrystalScene scene)
        => [.. scene.Scene.Items.OfType<RodItem>().Select(r => scene.Lookup(r.Id)!.BondIndex).Order()];

    static CrystalAppearance Removing(params int[] sources)
        => CrystalAppearance.Default with { RemovedBonds = [.. sources] };

    [Fact]
    public void RemovedSource_DropsEveryImage_KeepsOthers()
    {
        // Fluorite with every Ca-F bond its own source: removing one source takes its rod in every cell drawn.
        CrystalStructure s = Fixtures.Fluorite();
        var options = Plain with { RepeatA = 2, RepeatB = 2, RepeatC = 2 };
        CrystalScene all = Builder.Build(s, options);
        CrystalScene cut = Builder.Build(s, options, Removing(0));

        List<int> before = RodBonds(all), after = RodBonds(cut);
        int images = before.Count(b => b == 0);
        Assert.True(images > 1);
        Assert.DoesNotContain(0, after);
        Assert.Equal(before.Count - images, after.Count);
        Assert.Equal(before.Where(b => b != 0), after);
    }

    [Fact]
    public void Trio_RemovingOneBond_KeepsTheOther()
    {
        Assert.Equal([1], RodBonds(Builder.Build(Trio(), Plain, Removing(0))));
        Assert.Equal([0], RodBonds(Builder.Build(Trio(), Plain, Removing(1))));
        Assert.Empty(RodBonds(Builder.Build(Trio(), Plain, Removing(0, 1))));
    }

    [Fact]
    public void RemovedBond_LeavesNoFadedSphere()
    {
        // With complete coordination, a removed bond leaves no faded neighbor: with all of them gone, only the 12 atoms.
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = true };

        CrystalScene free = Builder.Build(s, options);
        CrystalScene cut = Builder.Build(s, options, Removing([.. Enumerable.Range(0, s.Bonds.Count)]));

        Assert.True(free.AtomCount > 12);
        Assert.Equal(12, cut.AtomCount);
        Assert.Empty(cut.Scene.Items.OfType<RodItem>());
    }

    [Fact]
    public void Pick_ReturnsTheRightBond()
    {
        // Sources that are not the bond indices: the rod left still picks as bond 1, the K-Si, drawn from either end.
        CrystalScene scene = Builder.Build(Trio(firstSource: 7, secondSource: 3), Plain, Removing(7));

        RodItem rod = Assert.Single(scene.Scene.Items.OfType<RodItem>());
        CrystalItemInfo info = scene.Lookup(rod.Id)!;
        Assert.Equal(CrystalItemKind.Bond, info.Kind);
        Assert.Equal(1, info.BondIndex);
        Assert.Contains(info.AtomIndex, new[] { 0, 2 });
    }

    [Fact]
    public void SourceMinusOne_NeverRemoved()
    {
        CrystalScene scene = Builder.Build(Trio(firstSource: -1, secondSource: -1), Plain, Removing(-1));

        Assert.Equal([0, 1], RodBonds(scene));
    }

    [Fact]
    public void ShowAll_KeepsRemovedBonds()
    {
        CrystalAppearance app = Removing(0) with
        {
            ByBond = ImmutableDictionary<int, BondStyle>.Empty.Add(1, new BondStyle { Visible = false }),
        };

        CrystalAppearance shown = app.ShowAll();

        // The hidden bond 1 comes back; the removed bond 0 does not.
        Assert.Equal([0], shown.RemovedBonds);
        Assert.Equal([1], RodBonds(Builder.Build(Trio(), Plain, shown)));
    }

    [Fact]
    public void Sphere_KeepsRemovedBondsRemoved()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions
        {
            Clip = CrystalClip.Sphere,
            SphereCenter = new FractionalPoint(0.5, 0.5, 0.5),
            SphereRadius = 6,
        };

        CrystalScene all = Builder.Build(s, options);
        CrystalScene cut = Builder.Build(s, options, Removing([.. Enumerable.Range(0, s.Bonds.Count)]));

        Assert.NotEmpty(all.Scene.Items.OfType<RodItem>());
        Assert.Empty(cut.Scene.Items.OfType<RodItem>());
    }

    [Fact]
    public void EquivalentSources_TakesBothDirections_AndOnlyThatDistance()
    {
        CrystalStructure s = Representative();

        // Any Ca-F at the first distance, from Ca or from F, gives the 12 of them; the second distance only itself.
        int[] first = [.. Enumerable.Range(0, 12)];
        Assert.Equal(first, CrystalBondExpansion.EquivalentSources(s, 3).Order());
        Assert.Equal(first, CrystalBondExpansion.EquivalentSources(s, 10).Order());
        Assert.Equal([12], CrystalBondExpansion.EquivalentSources(s, 12));
        Assert.Equal([-1], CrystalBondExpansion.EquivalentSources(s, -1));
        Assert.Equal([99], CrystalBondExpansion.EquivalentSources(s, 99));
    }

    [Fact]
    public void EquivalentSources_RemoveEveryExpandedImage()
    {
        // After the expansion every new bond carries source 0 or 12: removing one given source alone leaves the
        // others drawn, removing its equivalents takes every bond at that distance.
        CrystalStructure given = Representative();
        CrystalStructure s = given with { Bonds = CrystalBondExpansion.ExtendToEquivalentAtoms(given) };
        CrystalScene all = Builder.Build(s, Plain);
        // A given F -> Ca inside the cell that does not repeat the origin Ca -> F: removing its source alone takes only it.
        int pick = Enumerable.Range(8, 4).First(k => given.Bonds[k].Second != 0);

        CrystalScene one = Builder.Build(s, Plain, Removing(pick));
        CrystalScene equivalent = Builder.Build(s, Plain, Removing([.. CrystalBondExpansion.EquivalentSources(given, pick)]));

        Assert.Equal(RodBonds(all).Count - 1, RodBonds(one).Count);
        Assert.All(RodBonds(equivalent), b => Assert.Equal(12, s.Bonds[b].SourceIndex));
        Assert.NotEmpty(RodBonds(equivalent));
    }

    [Fact]
    public void AtomsWithoutSite_GiveOnlyThemselves()
    {
        CrystalAtom[] atoms = [.. Fixtures.FluoriteAtoms().Select(a => a with { SiteIndex = -1 })];
        var s = new CrystalStructure(Fixtures.FluoriteCell, atoms,
            [new CrystalBond(0, 4, LatticeShift.Zero) { SourceIndex = 0 }, new CrystalBond(1, 5, LatticeShift.Zero) { SourceIndex = 1 }]);

        Assert.Equal([0], CrystalBondExpansion.EquivalentSources(s, 0));
    }
}
