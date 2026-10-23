namespace Viewer3D.Crystal;

/// <summary>
/// Bonds found from the geometry alone, for structures that come without them (the frames of a trajectory): two atoms
/// are bonded when their distance d satisfies 0.4 &lt;= d &lt;= r1 + r2 + 0.45 angstrom, with r the radius of
/// <see cref="ElementTable"/> (the criterion of Jmol). The table holds metallic radii for metals, which would bond every
/// pair of neighboring cations of an ionic solid (Na to Na at 3.99 angstrom in NaCl), so two metals are bonded only
/// when the structure has no other element. Void sites, atoms of an unknown element and atoms closer than 0.4 angstrom
/// are never bonded. Atoms are binned in a grid of the largest bond length, so the search is linear in the atoms.
/// </summary>
public static class CrystalBondFinder
{
    /// <summary>Tolerance added to the sum of the two radii, in angstrom.</summary>
    public const double Tolerance = 0.45;

    /// <summary>Shortest distance taken as a bond, in angstrom: closer atoms are overlapping positions, not bonded.</summary>
    public const double MinDistance = 0.4;

    /// <summary>
    /// The bonds of <paramref name="structure"/>, each once. With <paramref name="periodic"/>, an atom also bonds to the
    /// images of the others in the 26 neighboring cells (and to its own); without it, only within the coordinates as
    /// given. The shifts follow the rule of <see cref="CrystalBond.Shift"/>, from the coordinates as given.
    /// </summary>
    public static IReadOnlyList<CrystalBond> Find(CrystalStructure structure, bool periodic)
    {
        ArgumentNullException.ThrowIfNull(structure);
        CellBasis basis = structure.Cell.Basis();
        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
        if (!basis.Valid || atoms.Count < (periodic ? 1 : 2))
            return [];

        var radius = new double[atoms.Count];
        var metal = new bool[atoms.Count];
        var floor = new LatticeShift[atoms.Count];
        var position = new (double X, double Y, double Z)[atoms.Count];
        bool anyNonMetal = false;
        double largest = 0;
        for (int i = 0; i < atoms.Count; i++)
        {
            CrystalAtom atom = atoms[i];
            radius[i] = double.NaN;
            if (!atom.IsVoid && ElementTable.TryGet(atom.Element, out ElementInfo info))
            {
                radius[i] = info.Radius;
                metal[i] = info.IsMetal;
                anyNonMetal |= !info.IsMetal;
                largest = Math.Max(largest, info.Radius);
            }
            // Com cela, a busca corre sobre as posicoes reduzidas a [0, 1), e as imagens -1..1 cobrem os vizinhos.
            CrystalSceneBuilder.Reduced r = periodic
                ? CrystalSceneBuilder.Reduce(atom.X, atom.Y, atom.Z)
                : new CrystalSceneBuilder.Reduced(atom.X, atom.Y, atom.Z, LatticeShift.Zero);
            floor[i] = r.Floor;
            basis.ToCartesian(r.X, r.Y, r.Z, out double x, out double y, out double z);
            position[i] = (x, y, z);
        }
        double reach = 2 * largest + Tolerance;
        if (!(reach > 0))
            return [];

        var grid = new Dictionary<(int, int, int), List<int>>();
        for (int i = 0; i < atoms.Count; i++)
        {
            if (double.IsNaN(radius[i]))
                continue;
            (int, int, int) key = Bin(position[i], reach);
            if (!grid.TryGetValue(key, out List<int>? bin))
                grid[key] = bin = [];
            bin.Add(i);
        }

        var bonds = new List<CrystalBond>();
        int span = periodic ? 1 : 0;
        for (int sa = -span; sa <= span; sa++)
            for (int sb = -span; sb <= span; sb++)
                for (int sc = -span; sc <= span; sc++)
                {
                    var shift = new LatticeShift(sa, sb, sc);
                    basis.ToCartesian(sa, sb, sc, out double tx, out double ty, out double tz);
                    for (int i = 0; i < atoms.Count; i++)
                    {
                        if (double.IsNaN(radius[i]))
                            continue;
                        // Atomo j transladado por shift perto de i: |p_j + T - p_i| = |p_j - (p_i - T)|.
                        (double qx, double qy, double qz) = (position[i].X - tx, position[i].Y - ty, position[i].Z - tz);
                        (int bx, int by, int bz) = Bin((qx, qy, qz), reach);
                        for (int gx = bx - 1; gx <= bx + 1; gx++)
                            for (int gy = by - 1; gy <= by + 1; gy++)
                                for (int gz = bz - 1; gz <= bz + 1; gz++)
                                {
                                    if (!grid.TryGetValue((gx, gy, gz), out List<int>? bin))
                                        continue;
                                    foreach (int j in bin)
                                    {
                                        // Cada ligacao uma vez: (i, j, s) e (j, i, -s) sao a mesma.
                                        if (j < i || (j == i && !Positive(shift)))
                                            continue;
                                        if (metal[i] && metal[j] && anyNonMetal)
                                            continue;
                                        double dx = position[j].X - qx, dy = position[j].Y - qy, dz = position[j].Z - qz;
                                        double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                                        if (d < MinDistance || d > radius[i] + radius[j] + Tolerance)
                                            continue;
                                        // Das posicoes reduzidas para as coordenadas como vieram: S = s - F_j + F_i.
                                        bonds.Add(new CrystalBond(i, j, shift - floor[j] + floor[i]));
                                    }
                                }
                    }
                }
        bonds.Sort((x, y) => x.First != y.First ? x.First.CompareTo(y.First)
            : x.Second != y.Second ? x.Second.CompareTo(y.Second)
            : Compare(x.Shift, y.Shift));
        return bonds;
    }

    static (int, int, int) Bin((double X, double Y, double Z) p, double size)
        => ((int)Math.Floor(p.X / size), (int)Math.Floor(p.Y / size), (int)Math.Floor(p.Z / size));

    // The first nonzero component is positive.
    static bool Positive(LatticeShift s) => s.A != 0 ? s.A > 0 : s.B != 0 ? s.B > 0 : s.C > 0;

    static int Compare(LatticeShift x, LatticeShift y)
        => x.A != y.A ? x.A.CompareTo(y.A) : x.B != y.B ? x.B.CompareTo(y.B) : x.C.CompareTo(y.C);
}
