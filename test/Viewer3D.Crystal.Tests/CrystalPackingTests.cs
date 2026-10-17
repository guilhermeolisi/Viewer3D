namespace Viewer3D.Crystal.Tests;

public class CrystalPackingTests
{
    // As fracoes de livro sao exatas (pi/6, pi*sqrt(3)/8, pi/(3*sqrt(2))): a conta em double erra muito abaixo de 1e-9.
    const double Exact = 1e-9;

    static readonly CrystalSceneBuilder Builder = new();

    [Fact]
    public void Measure_ShouldGiveSimpleCubicFraction()
    {
        PackingMeasure m = CrystalPacking.Measure(Fixtures.Polonium());

        Assert.Equal(Math.PI / 6, m.Fraction, Exact);
        Assert.Equal(0.5236, m.Fraction, 1e-4);
    }

    [Fact]
    public void Measure_ShouldGiveBodyCenteredFraction()
    {
        PackingMeasure m = CrystalPacking.Measure(Fixtures.Iron());

        Assert.Equal(Math.PI * Math.Sqrt(3) / 8, m.Fraction, Exact);
        Assert.Equal(0.6802, m.Fraction, 1e-4);
    }

    [Fact]
    public void Measure_ShouldGiveFaceCenteredFraction()
    {
        PackingMeasure m = CrystalPacking.Measure(Fixtures.Copper());

        Assert.Equal(Math.PI / (3 * Math.Sqrt(2)), m.Fraction, Exact);
        Assert.Equal(0.7405, m.Fraction, 1e-4);
    }

    [Fact]
    public void Measure_ShouldGiveIdealHexagonalFraction()
    {
        // HC ideal: c/a = sqrt(8/3), os 12 vizinhos a mesma distancia a; a fracao e a mesma do CFC.
        double a = 3.2094;
        CrystalStructure ideal = Fixtures.Metal("Mg", new CrystalCell(a, a, a * Math.Sqrt(8.0 / 3), 90, 90, 120),
            (1.0 / 3, 2.0 / 3, 0.25), (2.0 / 3, 1.0 / 3, 0.75));

        PackingMeasure m = CrystalPacking.Measure(ideal);

        Assert.Equal(Math.PI / (3 * Math.Sqrt(2)), m.Fraction, Exact);
    }

    [Fact]
    public void Measure_RealMagnesium_ShouldUseTheShorterOutOfPlaneNeighbour()
    {
        // Mg real (c/a = 1,6236 < 1,633): o vizinho fora do plano (3,1966 A) e mais curto que a (3,2094 A), e as esferas
        // tocam por ele. Fracao = 2 (4/3) pi (d/2)^3 / (a^2 c sin 120), cerca de 0,736, abaixo do HC ideal.
        double a = 3.2094, c = 5.2108;
        double d = Math.Sqrt(a * a / 3 + c * c / 4);
        double expected = 2 * 4.0 / 3 * Math.PI * Math.Pow(d / 2, 3) / (a * a * c * Math.Sin(Math.PI * 2 / 3));

        PackingMeasure m = CrystalPacking.Measure(Fixtures.Magnesium());

        Assert.Equal(expected, m.Fraction, Exact);
        Assert.True(m.Fraction < Math.PI / (3 * Math.Sqrt(2)));
    }

    [Fact]
    public void Measure_ShouldGiveCellVolume()
    {
        PackingMeasure m = CrystalPacking.Measure(Fixtures.Fluorite());

        Assert.Equal(Math.Pow(Fixtures.FluoriteA, 3), m.CellVolume, 1e-9);
    }

    [Fact]
    public void Measure_ShouldWeightSpheresByOccupancy()
    {
        CrystalStructure full = Fixtures.Copper();
        var half = full with { Atoms = [.. full.Atoms.Select(x => x with { Occupancy = 0.5 })] };

        PackingMeasure f = CrystalPacking.Measure(full);
        PackingMeasure h = CrystalPacking.Measure(half);

        Assert.Equal(f.Fraction / 2, h.Fraction, Exact);
    }

    [Theory]
    [InlineData("Fluorite")]
    [InlineData("FluoriteWithoutBonds")]
    [InlineData("Magnesium")]
    public void Measure_ShouldMatchSceneSphereScale(string name)
    {
        CrystalStructure s = name switch
        {
            "Fluorite" => Fixtures.Fluorite(),
            "FluoriteWithoutBonds" => Fixtures.Fluorite() with { Bonds = [] },
            _ => Fixtures.Magnesium(),
        };

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions { Style = CrystalStyle.SpaceFilling });

        Assert.Equal(scene.SphereScale, CrystalPacking.Measure(s).TouchingScale);
    }

    [Fact]
    public void Measure_InvalidCell_ShouldGiveZero()
    {
        var s = new CrystalStructure(new CrystalCell(0, 1, 1, 90, 90, 90), [new CrystalAtom("X", "Cu", 0, 0, 0)], []);

        Assert.Equal(new PackingMeasure(0, 0, 0, 0), CrystalPacking.Measure(s));
    }
}
