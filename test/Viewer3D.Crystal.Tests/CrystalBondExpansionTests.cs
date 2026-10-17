namespace Viewer3D.Crystal.Tests;

public class CrystalBondExpansionTests
{
    /// <summary>
    /// As ligacoes do motor: so do primeiro atomo de cada sitio (Ca da origem com seus 8 F; F em (1/4, 1/4, 1/4) com
    /// seus 4 Ca), com SourceIndex 0 a 11.
    /// </summary>
    static CrystalStructure Representative()
    {
        CrystalStructure all = Fixtures.Fluorite();
        CrystalAtom[] atoms = [.. all.Atoms];
        var bonds = new List<CrystalBond>();
        foreach (CrystalBond b in all.Bonds.Where(b => b.First == 0))
            bonds.Add(b with { SourceIndex = bonds.Count });
        foreach (CrystalBond b in all.Bonds.Where(b => b.Second == 4))
            bonds.Add(new CrystalBond(4, b.First, LatticeShift.Zero - b.Shift) { SourceIndex = bonds.Count });
        return new CrystalStructure(all.Cell, atoms, bonds);
    }

    static int Degree(IReadOnlyList<CrystalBond> bonds, int atom)
        => bonds.Where(b => b.First == atom || b.Second == atom)
            .Select(b => b.First == atom ? (b.Second, b.Shift) : (b.First, LatticeShift.Zero - b.Shift))
            .Distinct()
            .Count();

    [Fact]
    public void Fluorite_FromRepresentativeBonds_GivesEightBondsPerCaAndFourPerF()
    {
        CrystalStructure s = Representative();
        Assert.Equal(12, s.Bonds.Count);

        IReadOnlyList<CrystalBond> bonds = CrystalBondExpansion.ExtendToEquivalentAtoms(s);

        for (int k = 0; k < 4; k++)
            Assert.Equal(8, Degree(bonds, k));
        for (int k = 4; k < 12; k++)
            Assert.Equal(4, Degree(bonds, k));
        // 32 ligacoes Ca-F; das 4 dadas em F -> Ca, uma (F da origem com o Ca da origem) repete uma dada em Ca -> F
        // no sentido inverso, e fica, como dada.
        Assert.Equal(32 + 1, bonds.Count);
        Assert.Equal(s.Bonds, bonds.Take(12));
        Assert.All(bonds, b => Assert.Equal(Fixtures.FluoriteCaF, Fixtures.Distance(s.Cell, s.Atoms[b.First], s.Atoms[b.Second], b.Shift), 1e-9));
    }

    [Fact]
    public void ExpandedBonds_KeepTheSourceIndex()
    {
        CrystalStructure s = Representative();

        IReadOnlyList<CrystalBond> bonds = CrystalBondExpansion.ExtendToEquivalentAtoms(s);

        // Todas as Ca-F tem a mesma distancia: as novas tomam a primeira dada com ela (SourceIndex 0).
        Assert.All(bonds.Skip(12), b => Assert.Equal(0, b.SourceIndex));
    }

    [Fact]
    public void OnlyTheGivenDistances_AreExtended()
    {
        // Duas distancias Ca-F dadas para o Ca da origem: 2,3656 A (vizinho) e 4,5297 A (segunda esfera). A segunda
        // tambem e estendida, e nada no meio (Ca-F nao tem outra distancia entre elas) nem alem.
        CrystalStructure baseStructure = Fixtures.Fluorite();
        CrystalAtom[] atoms = [.. baseStructure.Atoms];
        CrystalCell cell = baseStructure.Cell;
        var bonds = new List<CrystalBond>
        {
            new(0, 4, LatticeShift.Zero) { SourceIndex = 0 },
            new(0, 4, new LatticeShift(-1, 0, 0)) { SourceIndex = 1 },
        };
        double second = Fixtures.Distance(cell, atoms[0], atoms[4], new LatticeShift(-1, 0, 0));
        Assert.Equal(Fixtures.FluoriteA * Math.Sqrt(11) / 4, second, 1e-9);

        IReadOnlyList<CrystalBond> result = CrystalBondExpansion.ExtendToEquivalentAtoms(new CrystalStructure(cell, atoms, bonds));

        var distances = result.Select(b => Fixtures.Distance(cell, atoms[b.First], atoms[b.Second], b.Shift)).ToList();
        Assert.All(distances, d => Assert.True(Math.Abs(d - Fixtures.FluoriteCaF) < 1e-9 || Math.Abs(d - second) < 1e-9));
        // Cada Ca tem 8 F a 2,3656 A e 24 a a sqrt(11)/4.
        Assert.Equal(4 * (8 + 24), result.Count);
        Assert.All(result.Where(b => Math.Abs(Fixtures.Distance(cell, atoms[b.First], atoms[b.Second], b.Shift) - second) < 1e-9),
            b => Assert.Equal(1, b.SourceIndex));
    }

    [Fact]
    public void AtomsWithoutSite_AreLeftAsGiven()
    {
        CrystalAtom[] atoms = [.. Fixtures.FluoriteAtoms().Select(a => a with { SiteIndex = -1 })];
        var s = new CrystalStructure(Fixtures.FluoriteCell, atoms, [new CrystalBond(0, 4, LatticeShift.Zero)]);

        IReadOnlyList<CrystalBond> result = CrystalBondExpansion.ExtendToEquivalentAtoms(s);

        Assert.Single(result);
    }
}
