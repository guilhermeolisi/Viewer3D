namespace Viewer3D.Crystal.Tests;

/// <summary>Structures written by hand, with their sources.</summary>
internal static class Fixtures
{
    /// <summary>Fluorita CaF2, Fm-3m, a = 5,463 A (ICSD/AMCSD): Ca em 4a, F em 8c. Ca-F = a sqrt(3)/4 = 2,3656 A.</summary>
    public const double FluoriteA = 5.463;

    public static double FluoriteCaF => FluoriteA * Math.Sqrt(3) / 4;

    public static readonly (double X, double Y, double Z)[] Ca4a = [(0, 0, 0), (0, 0.5, 0.5), (0.5, 0, 0.5), (0.5, 0.5, 0)];

    public static CrystalAtom[] FluoriteAtoms()
    {
        var atoms = new List<CrystalAtom>();
        for (int k = 0; k < Ca4a.Length; k++)
            atoms.Add(new CrystalAtom("Ca1", "Ca", Ca4a[k].X, Ca4a[k].Y, Ca4a[k].Z) { SiteIndex = 0, SourceIndex = 0 });
        foreach (double x in new[] { 0.25, 0.75 })
            foreach (double y in new[] { 0.25, 0.75 })
                foreach (double z in new[] { 0.25, 0.75 })
                    atoms.Add(new CrystalAtom("F1", "F", x, y, z) { SiteIndex = 1, SourceIndex = 1 });
        return [.. atoms];
    }

    /// <summary>The fluorite with every Ca-F bond given once, from Ca (32 bonds, 8 per Ca, 4 per F).</summary>
    public static CrystalStructure Fluorite() => WithBondsBelow(FluoriteCell, FluoriteAtoms(), 2.5, (a, b) => a.Element == "Ca" && b.Element == "F");

    public static CrystalCell FluoriteCell => new(FluoriteA, FluoriteA, FluoriteA, 90, 90, 90);

    /// <summary>Every pair (first, second image in -1..1) closer than <paramref name="limit"/> and accepted by the filter.</summary>
    public static CrystalStructure WithBondsBelow(CrystalCell cell, IReadOnlyList<CrystalAtom> atoms, double limit,
        Func<CrystalAtom, CrystalAtom, bool> accept)
    {
        var bonds = new List<CrystalBond>();
        for (int i = 0; i < atoms.Count; i++)
            for (int j = 0; j < atoms.Count; j++)
            {
                if (!accept(atoms[i], atoms[j]))
                    continue;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                        for (int c = -1; c <= 1; c++)
                        {
                            if (i == j && a == 0 && b == 0 && c == 0)
                                continue;
                            double d = Distance(cell, atoms[i], atoms[j], new LatticeShift(a, b, c));
                            if (d < limit)
                                bonds.Add(new CrystalBond(i, j, new LatticeShift(a, b, c)) { SourceIndex = bonds.Count });
                        }
            }
        return new CrystalStructure(cell, atoms, bonds);
    }

    public static double Distance(CrystalCell cell, CrystalAtom p, CrystalAtom q, LatticeShift s)
        => cell.Basis().Distance(q.X + s.A - p.X, q.Y + s.B - p.Y, q.Z + s.C - p.Z);

    public static CrystalStructure Metal(string element, CrystalCell cell, params (double X, double Y, double Z)[] positions)
        => new(cell, [.. positions.Select(p => new CrystalAtom(element + "1", element, p.X, p.Y, p.Z) { SiteIndex = 0 })], []);

    /// <summary>Polonio cubico simples, a = 3,359 A: vizinho a 3,359 A.</summary>
    public static CrystalStructure Polonium() => Metal("Po", new CrystalCell(3.359, 3.359, 3.359, 90, 90, 90), (0, 0, 0));

    /// <summary>Ferro alfa, CCC, a = 2,8665 A: vizinho a a sqrt(3)/2 = 2,4825 A.</summary>
    public static CrystalStructure Iron() => Metal("Fe", new CrystalCell(2.8665, 2.8665, 2.8665, 90, 90, 90), (0, 0, 0), (0.5, 0.5, 0.5));

    /// <summary>Cobre, CFC, a = 3,615 A: vizinho a a/sqrt(2) = 2,5562 A.</summary>
    public static CrystalStructure Copper() => Metal("Cu", new CrystalCell(3.615, 3.615, 3.615, 90, 90, 90), Ca4a);

    /// <summary>Magnesio HC, a = 3,2094 A, c = 5,2108 A, 2c: vizinho fora do plano a sqrt(a^2/3 + c^2/4) = 3,1966 A.</summary>
    public static CrystalStructure Magnesium()
        => Metal("Mg", new CrystalCell(3.2094, 3.2094, 5.2108, 90, 90, 120), (1.0 / 3, 2.0 / 3, 0.25), (2.0 / 3, 1.0 / 3, 0.75));
}
