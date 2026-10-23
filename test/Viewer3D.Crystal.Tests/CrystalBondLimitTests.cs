using System.Collections.Immutable;

namespace Viewer3D.Crystal.Tests;

public class CrystalBondLimitTests
{
    static readonly CrystalSceneBuilder Builder = new();

    /// <summary>
    /// Celula cubica de 10 A com Si em (0,5; 0,5; 0,5), O a 1,6 A (x) e K a 3,0 A (y), e as ligacoes Si-O e K-Si dadas:
    /// distancias escolhidas para os limites ficarem entre elas.
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
        // Si-O = 1,6 A: o limite e fechado.
        CrystalScene scene = Builder.Build(Trio(), Plain with { MaxBondLength = 1.6 });

        Assert.Equal([0], RodBonds(scene));
    }

    [Fact]
    public void ElementLimitZero_MeansNoBondsFromThatElement()
    {
        // K "No bonds": some o K-Si, o Si-O fica.
        CrystalScene scene = Builder.Build(Trio(), Plain, ElementLimit("K", 0));

        Assert.Equal([0], RodBonds(scene));
    }

    [Fact]
    public void ElementLimit_OverridesTheGlobalOne()
    {
        // O geral corta tudo acima de 1 A; a regra do Si (3,5) nao basta, porque cada ponta decide: o O e o K ficam
        // com o geral. Com regras no Si e no O, o Si-O volta.
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
        // Elemento O em 0 (sem ligacoes); o sitio do O (indice 1) devolve 2 A: o Si-O volta.
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
        // K-Si vira candidata (3,0 A). Sem limite nao entra; com limite so no K tambem nao (o Si e Automatic); com o
        // geral, entra.
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
        // Fluorita com coordenacao completa: com limite 0, nenhuma ligacao e nenhuma esfera esmaecida.
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
        // Ca-F = 2,366 A: com 2,3 o cubo some, com 2,4 fica, e sem limite fica como sempre.
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
        // Uma candidata na estrutura liga a conferencia dos limites; os vertices contam como automaticos e o cubo fica.
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
        // Fluorita com as ligacoes do primeiro Ca dadas como candidatas: as dos outros tres Ca saem candidatas tambem.
        CrystalStructure s = Fixtures.Fluorite();
        var given = s.Bonds.Where(b => b.First == 0).Select(b => b with { IsAutomatic = false }).ToList();
        s = s with { Bonds = given };

        IReadOnlyList<CrystalBond> expanded = CrystalBondExpansion.ExtendToEquivalentAtoms(s);

        Assert.Equal(32, expanded.Count);
        Assert.All(expanded, b => Assert.False(b.IsAutomatic));
    }
}
