namespace Viewer3D.Crystal;

/// <summary>
/// Extends bonds given for some atoms of a site to every atom of that site. A program that finds bonds only around one
/// representative atom per site (as the Sindarin engine does) gives the full picture this way: atoms of one site are
/// equivalent by symmetry, so each has neighbors at the same distances.
/// </summary>
public static class CrystalBondExpansion
{
    /// <summary>Highest cell translation searched, per axis, for a neighbor.</summary>
    public const int MaxShift = 5;

    /// <summary>
    /// The given bonds, plus, for every pair of sites (i, j) with a given bond, a bond from each atom of site i to each
    /// image of an atom of site j whose distance equals one of the distances given for that pair within
    /// <paramref name="tolerance"/> angstrom. Matching the given distances, rather than a range or a bonding rule,
    /// reproduces whatever rule chose the given bonds, since symmetry keeps the distances. A bond already present, in
    /// either direction, is not repeated; a new one takes the <see cref="CrystalBond.SourceIndex"/> and the
    /// <see cref="CrystalBond.IsAutomatic"/> of the given bond with the same distance. Atoms with <see cref="CrystalAtom.SiteIndex"/> -1, and their bonds, are left as they are.
    /// </summary>
    public static IReadOnlyList<CrystalBond> ExtendToEquivalentAtoms(CrystalStructure structure, double tolerance = 1e-4)
    {
        ArgumentNullException.ThrowIfNull(structure);
        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
        IReadOnlyList<CrystalBond> given = structure.Bonds;
        CellBasis basis = structure.Cell.Basis();
        var result = new List<CrystalBond>(given);
        if (!basis.Valid || given.Count == 0)
            return result;

        var present = new HashSet<(int, int, LatticeShift)>();
        foreach (CrystalBond bond in given)
            present.Add(Key(bond.First, bond.Second, bond.Shift));

        // Distancias dadas por par de sitios, cada uma com a ligacao de origem.
        var distances = new Dictionary<(int, int), List<(double Distance, int Source, bool Automatic)>>();
        foreach (CrystalBond bond in given)
        {
            if ((uint)bond.First >= (uint)atoms.Count || (uint)bond.Second >= (uint)atoms.Count)
                continue;
            int si = atoms[bond.First].SiteIndex, sj = atoms[bond.Second].SiteIndex;
            if (si < 0 || sj < 0)
                continue;
            double d = Distance(basis, atoms[bond.First], atoms[bond.Second], bond.Shift);
            if (!distances.TryGetValue((si, sj), out List<(double, int, bool)>? list))
                distances[(si, sj)] = list = [];
            if (!list.Exists(x => Math.Abs(x.Item1 - d) <= tolerance))
                list.Add((d, bond.SourceIndex, bond.IsAutomatic));
        }

        var bySite = new Dictionary<int, List<int>>();
        for (int k = 0; k < atoms.Count; k++)
        {
            int site = atoms[k].SiteIndex;
            if (site < 0)
                continue;
            if (!bySite.TryGetValue(site, out List<int>? members))
                bySite[site] = members = [];
            members.Add(k);
        }

        foreach (((int si, int sj), List<(double Distance, int Source, bool Automatic)> list) in distances)
        {
            if (!bySite.TryGetValue(si, out List<int>? firsts) || !bySite.TryGetValue(sj, out List<int>? seconds))
                continue;
            double dmax = list.Max(x => x.Distance) + tolerance;
            int m = Math.Min((int)Math.Ceiling(dmax / basis.MinEdge) + 1, MaxShift);
            foreach (int p in firsts)
                foreach (int q in seconds)
                {
                    CrystalAtom a = atoms[p], b = atoms[q];
                    // Imagem de q mais proxima de p, em torno da qual se procura.
                    var center = new LatticeShift((int)-Math.Round(b.X - a.X), (int)-Math.Round(b.Y - a.Y), (int)-Math.Round(b.Z - a.Z));
                    for (int x = -m; x <= m; x++)
                        for (int y = -m; y <= m; y++)
                            for (int z = -m; z <= m; z++)
                            {
                                LatticeShift shift = center + new LatticeShift(x, y, z);
                                if (p == q && shift == LatticeShift.Zero)
                                    continue;
                                double d = Distance(basis, a, b, shift);
                                int match = list.FindIndex(e => Math.Abs(e.Distance - d) <= tolerance);
                                if (match < 0 || !present.Add(Key(p, q, shift)))
                                    continue;
                                result.Add(new CrystalBond(p, q, shift) { SourceIndex = list[match].Source, IsAutomatic = list[match].Automatic });
                            }
                }
        }
        return result;
    }

    /// <summary>
    /// The sources that draw the same bonds as <paramref name="source"/> after <see cref="ExtendToEquivalentAtoms"/>:
    /// the <see cref="CrystalBond.SourceIndex"/> of every given bond between the same two sites, in either direction, at
    /// the same distance within <paramref name="tolerance"/> angstrom. The expansion gives every image of such a bond the
    /// source of whichever of them it met first, so removing one of them alone would remove some images and not others.
    /// A source of -1, one not in <see cref="CrystalStructure.Bonds"/>, or one whose atoms have no site, gives itself.
    /// </summary>
    public static IReadOnlySet<int> EquivalentSources(CrystalStructure structure, int source, double tolerance = 1e-4)
    {
        ArgumentNullException.ThrowIfNull(structure);
        var result = new HashSet<int> { source };
        if (source < 0)
            return result;
        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
        CellBasis basis = structure.Cell.Basis();
        CrystalBond? first = null;
        foreach (CrystalBond bond in structure.Bonds)
        {
            if (bond.SourceIndex == source && (uint)bond.First < (uint)atoms.Count && (uint)bond.Second < (uint)atoms.Count)
            {
                first = bond;
                break;
            }
        }
        if (first is null || !basis.Valid)
            return result;
        (int si, int sj) = (atoms[first.First].SiteIndex, atoms[first.Second].SiteIndex);
        if (si < 0 || sj < 0)
            return result;
        double d = Distance(basis, atoms[first.First], atoms[first.Second], first.Shift);
        foreach (CrystalBond bond in structure.Bonds)
        {
            if (bond.SourceIndex < 0 || (uint)bond.First >= (uint)atoms.Count || (uint)bond.Second >= (uint)atoms.Count)
                continue;
            int a = atoms[bond.First].SiteIndex, b = atoms[bond.Second].SiteIndex;
            if (((a == si && b == sj) || (a == sj && b == si))
                && Math.Abs(Distance(basis, atoms[bond.First], atoms[bond.Second], bond.Shift) - d) <= tolerance)
                result.Add(bond.SourceIndex);
        }
        return result;
    }

    /// <summary>The same key for a bond and for its reverse.</summary>
    static (int, int, LatticeShift) Key(int first, int second, LatticeShift shift)
        => first < second || (first == second && Positive(shift)) ? (first, second, shift) : (second, first, LatticeShift.Zero - shift);

    static bool Positive(LatticeShift s) => s.A > 0 || (s.A == 0 && (s.B > 0 || (s.B == 0 && s.C >= 0)));

    static double Distance(CellBasis basis, CrystalAtom p, CrystalAtom q, LatticeShift s)
        => basis.Distance(q.X + s.A - p.X, q.Y + s.B - p.Y, q.Z + s.C - p.Z);
}
