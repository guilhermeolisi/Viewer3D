namespace Viewer3D.Crystal.Tests;

public class CrystalBondFinderTests
{
    /// <summary>NaCl, Fm-3m, a = 5.64 A: Na on 4a, Cl on 4b. Na-Cl = a/2 = 2.82 A; Na-Na and Cl-Cl = a/sqrt(2) = 3.99 A.</summary>
    const double SaltA = 5.64;

    static CrystalStructure Salt(double shiftFirstNa = 0)
    {
        var atoms = new List<CrystalAtom>();
        foreach (var (x, y, z) in Fixtures.Ca4a)
            atoms.Add(new CrystalAtom("Na", "Na", x + (atoms.Count == 0 ? shiftFirstNa : 0), y, z));
        foreach (var (x, y, z) in Fixtures.Ca4a)
            atoms.Add(new CrystalAtom("Cl", "Cl", (x + 0.5) % 1, y, z));
        return new CrystalStructure(new CrystalCell(SaltA, SaltA, SaltA, 90, 90, 90), atoms, []);
    }

    static int[] Neighbors(CrystalStructure structure, IReadOnlyList<CrystalBond> bonds, int atom, string element)
        => [.. bonds.Where(b => (b.First == atom && structure.Atoms[b.Second].Element == element)
                || (b.Second == atom && structure.Atoms[b.First].Element == element)).Select(b => b.First == atom ? b.Second : b.First)];

    [Fact]
    public void Salt_EachNaHasSixCl_AndNoNaNaNorClCl()
    {
        CrystalStructure salt = Salt();

        IReadOnlyList<CrystalBond> bonds = CrystalBondFinder.Find(salt, periodic: true);

        // The metallic radius of Na would bond Na to Na at 3.99 A (1.86 + 1.86 + 0.45 = 4.17): the rule for metals keeps
        // it out, since the structure has Cl.
        Assert.Equal(24, bonds.Count);
        for (int na = 0; na < 4; na++)
        {
            Assert.Equal(6, Neighbors(salt, bonds, na, "Cl").Length);
            Assert.Empty(Neighbors(salt, bonds, na, "Na"));
        }
        foreach (CrystalBond bond in bonds)
            Assert.Equal(SaltA / 2, Fixtures.Distance(salt.Cell, salt.Atoms[bond.First], salt.Atoms[bond.Second], bond.Shift), 1e-9);
    }

    [Fact]
    public void Fluorite_EachCaHasEightF_AndNoCaCa()
    {
        var fluorite = new CrystalStructure(Fixtures.FluoriteCell, Fixtures.FluoriteAtoms(), []);

        IReadOnlyList<CrystalBond> bonds = CrystalBondFinder.Find(fluorite, periodic: true);

        Assert.Equal(32, bonds.Count);
        for (int ca = 0; ca < 4; ca++)
        {
            Assert.Equal(8, Neighbors(fluorite, bonds, ca, "F").Length);
            Assert.Empty(Neighbors(fluorite, bonds, ca, "Ca"));
        }
        foreach (CrystalBond bond in bonds)
            Assert.Equal(Fixtures.FluoriteCaF, Fixtures.Distance(fluorite.Cell, fluorite.Atoms[bond.First], fluorite.Atoms[bond.Second], bond.Shift), 1e-9);
    }

    [Fact]
    public void CoordinatesOutsideTheCell_GiveShiftsFromTheCoordinatesAsGiven()
    {
        // The first Na moved by a whole cell along a: the same crystal, so the same 24 bonds of 2.82 A, with the shifts
        // counted from where the atom was written.
        CrystalStructure salt = Salt(shiftFirstNa: 1);

        IReadOnlyList<CrystalBond> bonds = CrystalBondFinder.Find(salt, periodic: true);

        Assert.Equal(24, bonds.Count);
        Assert.Equal(6, Neighbors(salt, bonds, 0, "Cl").Length);
        foreach (CrystalBond bond in bonds)
            Assert.Equal(SaltA / 2, Fixtures.Distance(salt.Cell, salt.Atoms[bond.First], salt.Atoms[bond.Second], bond.Shift), 1e-9);
    }

    [Fact]
    public void AMetalAlone_BondsToItsOwnImages_EachOnce()
    {
        // Simple cubic polonium: six neighbors at 3.359 A, all images of the one atom (1.53 + 1.53 + 0.45 = 3.51).
        CrystalStructure polonium = Fixtures.Polonium();

        IReadOnlyList<CrystalBond> bonds = CrystalBondFinder.Find(polonium, periodic: true);

        Assert.Equal(3, bonds.Count);
        Assert.All(bonds, b => Assert.Equal((0, 0), (b.First, b.Second)));
        Assert.Equal(3, bonds.Select(b => b.Shift).Distinct().Count());
    }

    [Fact]
    public void Water_WithoutCell_HasTwoOH_AndNoHH()
    {
        // Water: O-H 0.9572 A, H-O-H 104.52 degrees, H-H 1.514 A (0.31 + 0.31 + 0.45 = 1.07, no bond).
        var box = new CrystalCell(10, 10, 10, 90, 90, 90);
        double half = 104.52 / 2 * Math.PI / 180;
        CrystalAtom[] atoms =
        [
            new("O1", "O", 0.5, 0.5, 0.5),
            new("H2", "H", 0.5 + 0.09572 * Math.Sin(half), 0.5 + 0.09572 * Math.Cos(half), 0.5),
            new("H3", "H", 0.5 - 0.09572 * Math.Sin(half), 0.5 + 0.09572 * Math.Cos(half), 0.5),
        ];

        IReadOnlyList<CrystalBond> bonds = CrystalBondFinder.Find(new CrystalStructure(box, atoms, []), periodic: false);

        Assert.Equal([(0, 1), (0, 2)], bonds.Select(b => (b.First, b.Second)));
        Assert.All(bonds, b => Assert.Equal(LatticeShift.Zero, b.Shift));
        Assert.All(bonds, b => Assert.True(b.IsAutomatic));
    }

    [Fact]
    public void WithoutCell_TheImagesAcrossTheEdgeAreNotBonded()
    {
        // Two atoms 0.7 A apart only through the edge of the box.
        var box = new CrystalCell(10, 10, 10, 90, 90, 90);
        CrystalAtom[] atoms = [new("O1", "O", 0.02, 0.5, 0.5), new("H2", "H", 0.95, 0.5, 0.5)];
        var structure = new CrystalStructure(box, atoms, []);

        Assert.Empty(CrystalBondFinder.Find(structure, periodic: false));
        Assert.Equal(new LatticeShift(-1, 0, 0), Assert.Single(CrystalBondFinder.Find(structure, periodic: true)).Shift);
    }

    [Fact]
    public void OverlappingPositions_VoidSites_AndUnknownElements_AreNotBonded()
    {
        var box = new CrystalCell(10, 10, 10, 90, 90, 90);
        CrystalAtom[] atoms =
        [
            new("O1", "O", 0.5, 0.5, 0.5),
            new("O2", "O", 0.52, 0.5, 0.5),
            new("V3", "O", 0.5, 0.6, 0.5) { IsVoid = true },
            new("X4", "Xx", 0.5, 0.4, 0.5),
        ];

        Assert.Empty(CrystalBondFinder.Find(new CrystalStructure(box, atoms, []), periodic: false));
    }
}
