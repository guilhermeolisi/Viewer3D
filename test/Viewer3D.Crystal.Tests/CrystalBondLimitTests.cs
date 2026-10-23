using System.Collections.Immutable;

namespace Viewer3D.Crystal.Tests;

public class CrystalBondLimitTests
{
    static readonly CrystalSceneBuilder Builder = new();

    /// <summary>
    /// Cubic cell of 10 A with Si at (0.5, 0.5, 0.5), O at 1.6 A (x) and K at 3.0 A (y), and the Si-O and K-Si bonds
    /// given: distances chosen so the limits fall between them.
    /// </summary>
    static CrystalStructure Trio(bool kAutomatic = true)
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
            new(0, 1, LatticeShift.Zero) { SourceIndex = 0 },
            new(2, 0, LatticeShift.Zero) { SourceIndex = 1, IsAutomatic = kAutomatic },
        ];
        return new CrystalStructure(cell, atoms, bonds);
    }

    static CrystalSceneOptions Plain => new() { ShowBoundaryAtoms = false, CompleteCoordination = false };

    static List<int> RodBonds(CrystalScene scene)
        => [.. scene.Scene.Items.OfType<RodItem>().Select(r => scene.Lookup(r.Id)!.BondIndex).Order()];

    static CrystalAppearance ElementLimit(string element, double limit)
        => CrystalAppearance.Default with
        {
            ByElement = ImmutableDictionary<string, AtomStyle>.Empty.Add(element, new AtomStyle { MaxBondLength = limit }),
        };

    [Fact]
    public void NoLimit_DrawsTheAutomaticBonds()
    {
        CrystalScene scene = Builder.Build(Trio(), Plain);

        Assert.Equal([0, 1], RodBonds(scene));
    }

    [Fact]
    public void GlobalLimit_DropsTheLongerBond()
    {
        CrystalScene scene = Builder.Build(Trio(), Plain with { MaxBondLength = 2.0 });

        Assert.Equal([0], RodBonds(scene));
    }

    [Fact]
    public void GlobalLimit_AtTheBondLength_KeepsIt()
    {
        // Si-O = 1.6 A: the limit is inclusive.
        CrystalScene scene = Builder.Build(Trio(), Plain with { MaxBondLength = 1.6 });

        Assert.Equal([0], RodBonds(scene));
    }

    [Fact]
    public void ElementLimitZero_MeansNoBondsFromThatElement()
    {
        // K "No bonds": the K-Si goes, the Si-O stays.
        CrystalScene scene = Builder.Build(Trio(), Plain, ElementLimit("K", 0));

        Assert.Equal([0], RodBonds(scene));
    }

    [Fact]
    public void ElementLimit_OverridesTheGlobalOne()
    {
        // The global limit cuts everything above 1 A; the Si rule (3.5) is not enough, because each end decides: O and K
        // keep the global one. With rules on Si and O, the Si-O comes back.
        CrystalStructure s = Trio();
        CrystalAppearance both = ElementLimit("Si", 3.5) with
        {
            ByElement = ImmutableDictionary<string, AtomStyle>.Empty
                .Add("Si", new AtomStyle { MaxBondLength = 3.5 })
                .Add("O", new AtomStyle { MaxBondLength = 3.5 }),
        };

        Assert.Empty(RodBonds(Builder.Build(s, Plain with { MaxBondLength = 1.0 }, ElementLimit("Si", 3.5))));
        Assert.Equal([0], RodBonds(Builder.Build(s, Plain with { MaxBondLength = 1.0 }, both)));
    }

    [Fact]
    public void SiteLimit_OverridesTheElementOne()
    {
        // Element O at 0 (no bonds); the O site (index 1) gives 2 A back: the Si-O returns.
        CrystalAppearance appearance = ElementLimit("O", 0) with
        {
            BySite = ImmutableDictionary<int, AtomStyle>.Empty.Add(1, new AtomStyle { MaxBondLength = 2.0 }),
        };

        CrystalScene scene = Builder.Build(Trio(), Plain, appearance);

        Assert.Equal([0, 1], RodBonds(scene));
    }

    [Fact]
    public void Candidate_IsDrawnOnlyWhenBothEndsHaveALimit()
    {
        // K-Si becomes a candidate (3.0 A). With no limit it is left out; with a limit on K only, too (Si is
        // automatic); with the global one, it is drawn.
        CrystalStructure s = Trio(kAutomatic: false);

        Assert.Equal([0], RodBonds(Builder.Build(s, Plain)));
        Assert.Equal([0], RodBonds(Builder.Build(s, Plain, ElementLimit("K", 3.5))));
        Assert.Equal([0, 1], RodBonds(Builder.Build(s, Plain with { MaxBondLength = 3.5 })));
    }

    [Fact]
    public void Candidate_LongerThanTheLimit_IsNotDrawn()
    {
        CrystalScene scene = Builder.Build(Trio(kAutomatic: false), Plain with { MaxBondLength = 2.9 });

        Assert.Equal([0], RodBonds(scene));
    }

    [Fact]
    public void BondCutByTheLimit_MakesNoFadedNeighbor()
    {
        // Fluorite with complete coordination: with limit 0, no bond and no faded sphere.
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = false, CompleteCoordination = true };

        CrystalScene free = Builder.Build(s, options);
        CrystalScene cut = Builder.Build(s, options with { MaxBondLength = 0 });

        Assert.True(free.AtomCount > 12);
        Assert.Equal(12, cut.AtomCount);
        Assert.Empty(cut.Scene.Items.OfType<RodItem>());
    }

    [Fact]
    public void Polyhedron_IsHiddenWhenAVertexFailsTheLimit()
    {
        // Ca-F = 2.366 A: with 2.3 the cube goes, with 2.4 it stays, and with no limit it stays as always.
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        var options = new CrystalSceneOptions { Style = CrystalStyle.Polyhedra, ShowBoundaryAtoms = false };

        CrystalScene free = Builder.Build(s, options);
        CrystalScene below = Builder.Build(s, options with { MaxBondLength = 2.3 });
        CrystalScene above = Builder.Build(s, options with { MaxBondLength = 2.4 });

        Assert.Equal(4, free.PolyhedronCount);
        Assert.Equal(0, below.PolyhedronCount);
        Assert.Equal(4, above.PolyhedronCount);
    }

    [Fact]
    public void Polyhedron_IsHiddenWhenAVertexElementHasNoBonds()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        var options = new CrystalSceneOptions { Style = CrystalStyle.Polyhedra, ShowBoundaryAtoms = false };

        CrystalScene scene = Builder.Build(s, options, ElementLimit("F", 0));

        Assert.Equal(0, scene.PolyhedronCount);
    }

    [Fact]
    public void WithoutLimits_ACandidateDoesNotChangeThePolyhedra()
    {
        // A candidate in the structure turns the limit check on; the vertices count as automatic and the cube stays.
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        s = s with { Bonds = [.. s.Bonds, new CrystalBond(4, 5, LatticeShift.Zero) { IsAutomatic = false }] };
        var options = new CrystalSceneOptions { Style = CrystalStyle.Polyhedra, ShowBoundaryAtoms = false };

        CrystalScene scene = Builder.Build(s, options);

        Assert.Equal(4, scene.PolyhedronCount);
        Assert.DoesNotContain(s.Bonds.Count - 1, RodBonds(scene));
    }

    [Fact]
    public void Expansion_CopiesIsAutomatic()
    {
        // Fluorite with the bonds of the first Ca given as candidates: those of the other three Ca are candidates too.
        CrystalStructure s = Fixtures.Fluorite();
        var given = s.Bonds.Where(b => b.First == 0).Select(b => b with { IsAutomatic = false }).ToList();
        s = s with { Bonds = given };

        IReadOnlyList<CrystalBond> expanded = CrystalBondExpansion.ExtendToEquivalentAtoms(s);

        Assert.Equal(32, expanded.Count);
        Assert.All(expanded, b => Assert.False(b.IsAutomatic));
    }
}
