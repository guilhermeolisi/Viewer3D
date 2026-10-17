namespace Viewer3D.Crystal.Tests;

public class SpaceFillingTests
{
    // Raio desenhado em float, em angstrom: 1e-4 A cobre o arredondamento.
    const double Tolerance = 1e-4;

    static readonly CrystalSceneBuilder Builder = new();

    static readonly CrystalSceneOptions SpaceFilling = new() { Style = CrystalStyle.SpaceFilling, CompleteCoordination = false };

    public static TheoryData<string, double> Metals => new()
    {
        { "Po", 3.359 },
        { "Fe", 2.8665 * Math.Sqrt(3) / 2 },
        { "Cu", 3.615 / Math.Sqrt(2) },
        { "Mg", Math.Sqrt(3.2094 * 3.2094 / 3 + 5.2108 * 5.2108 / 4) },
    };

    static CrystalStructure MetalNamed(string element) => element switch
    {
        "Po" => Fixtures.Polonium(),
        "Fe" => Fixtures.Iron(),
        "Cu" => Fixtures.Copper(),
        _ => Fixtures.Magnesium(),
    };

    [Theory]
    [MemberData(nameof(Metals))]
    public void SingleElementMetals_TouchAtHalfTheNearestNeighborDistance(string element, double nearest)
    {
        CrystalScene scene = Builder.Build(MetalNamed(element), SpaceFilling);

        Assert.All(scene.Scene.Items.OfType<SphereItem>(), s => Assert.Equal(nearest / 2, s.Radius, Tolerance));
    }

    [Fact]
    public void Fluorite_BondedCaAndFTouch_AndNoBondedPairOverlaps()
    {
        CrystalStructure s = Fixtures.Fluorite();

        CrystalScene scene = Builder.Build(s, SpaceFilling);

        Assert.True(ElementTable.TryGet("Ca", out ElementInfo ca));
        Assert.True(ElementTable.TryGet("F", out ElementInfo f));
        double sum = scene.SphereScale * (ca.Radius + f.Radius);
        Assert.Equal(Fixtures.FluoriteCaF, sum, Tolerance);
        foreach (CrystalBond bond in s.Bonds)
        {
            double d = Fixtures.Distance(s.Cell, s.Atoms[bond.First], s.Atoms[bond.Second], bond.Shift);
            Assert.True(d >= sum - Tolerance, $"{d} < {sum}");
        }
    }

    [Fact]
    public void WithoutBonds_ScaleComesFromTheShortestDistance()
    {
        // Sem ligacoes, o par mais proximo da fluorita e Ca-F (2,3656 A), pela soma dos raios da tabela.
        CrystalStructure bonded = Fixtures.Fluorite();
        var s = bonded with { Bonds = [] };

        CrystalScene scene = Builder.Build(s, SpaceFilling);

        Assert.True(ElementTable.TryGet("Ca", out ElementInfo ca));
        Assert.True(ElementTable.TryGet("F", out ElementInfo f));
        double candidates = Math.Min(Fixtures.FluoriteCaF / (ca.Radius + f.Radius),
            Math.Min(Fixtures.FluoriteA / 2 / (2 * f.Radius), Fixtures.FluoriteA / Math.Sqrt(2) / (2 * ca.Radius)));
        Assert.Equal(candidates, scene.SphereScale, 1e-9);
    }

    [Fact]
    public void SpaceFilling_DrawsNoRods()
    {
        CrystalScene scene = Builder.Build(Fixtures.Fluorite(), SpaceFilling with { CompleteCoordination = true });

        Assert.Empty(scene.Scene.Items.OfType<RodItem>());
        Assert.NotEmpty(scene.Scene.Items.OfType<SphereItem>());
    }

    [Fact]
    public void Scale_DoesNotDependOnRepetition()
    {
        CrystalStructure bonded = Fixtures.Fluorite();
        CrystalStructure unbonded = bonded with { Bonds = [] };

        foreach (CrystalStructure s in new[] { bonded, unbonded })
        {
            double one = Builder.Build(s, SpaceFilling).SphereScale;
            double three = Builder.Build(s, SpaceFilling with { RepeatA = 3, RepeatB = 2, RepeatC = 3 }).SphereScale;
            Assert.Equal(one, three, 1e-12);
        }
    }

    [Fact]
    public void BallAndStick_ScaleIsTheBallScale()
    {
        CrystalScene scene = Builder.Build(Fixtures.Fluorite(), new CrystalSceneOptions { BallScale = 0.3 });

        Assert.Equal(0.3, scene.SphereScale, 1e-12);
        Assert.True(ElementTable.TryGet("F", out ElementInfo f));
        Assert.Contains(scene.Scene.Items.OfType<SphereItem>(), x => Math.Abs(x.Radius - 0.3 * f.Radius) < Tolerance);
    }
}
