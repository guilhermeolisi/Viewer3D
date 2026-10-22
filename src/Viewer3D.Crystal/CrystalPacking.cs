namespace Viewer3D.Crystal;

/// <summary>
/// The packing of the space filling style: the scale at which the spheres touch, and the fraction of the cell they fill.
/// </summary>
/// <param name="TouchingScale">
/// s, the factor on the radii at which the closest pair just touches (the same <see cref="CrystalScene.SphereScale"/> the
/// space filling scene draws).
/// </param>
/// <param name="SphereVolume">Volume of the spheres of one cell, in cubic angstrom, each weighted by its occupancy.</param>
/// <param name="CellVolume">Volume of the cell, in cubic angstrom.</param>
/// <param name="Fraction">SphereVolume / CellVolume; zero for an invalid cell.</param>
public sealed record PackingMeasure(double TouchingScale, double SphereVolume, double CellVolume, double Fraction);

/// <summary>
/// The packing fraction of a structure, with the radii and the scale of the space filling style, so the number matches
/// the picture.
/// </summary>
/// <remarks>
/// <para>
/// The radius of an atom is its own <see cref="CrystalAtom.Radius"/> when given, else the radius of its main element in
/// <see cref="ElementTable"/>, else 1 angstrom. The scale s is the minimum over the bonds of d / (r1 + r2); without
/// bonds, the minimum over every pair of one cell and its 26 neighbors.
/// </para>
/// <para>
/// The fraction is Σ occ·(4/3)π(s·r)³ / V over the atoms of the cell, occ the occupancy (the sum of the shares on a
/// mixed site). Void sites (<see cref="CrystalAtom.IsVoid"/>) are not atoms: they enter neither the scale nor the sum.
/// For one element in the ideal packings it gives the textbook values: simple cubic π/6 = 0.5236,
/// body-centred cubic π√3/8 = 0.6802, face-centred cubic and ideal hexagonal close packing π/(3√2) = 0.7405.
/// </para>
/// </remarks>
public static class CrystalPacking
{
    /// <summary>Measures the packing of <paramref name="structure"/>.</summary>
    public static PackingMeasure Measure(CrystalStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        CellBasis basis = structure.Cell.Basis();
        if (!basis.Valid)
            return new PackingMeasure(0, 0, 0, 0);
        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
        var reduced = new CrystalSceneBuilder.Reduced[atoms.Count];
        var radius = new double[atoms.Count];
        for (int i = 0; i < atoms.Count; i++)
        {
            reduced[i] = CrystalSceneBuilder.Reduce(atoms[i].X, atoms[i].Y, atoms[i].Z);
            radius[i] = RadiusOf(atoms[i]);
        }
        double scale = TouchingScale(structure, basis, reduced, radius);
        double spheres = 0;
        for (int i = 0; i < atoms.Count; i++)
        {
            if (atoms[i].IsVoid)
                continue;
            double r = scale * radius[i];
            spheres += OccupancyOf(atoms[i]) * 4.0 / 3.0 * Math.PI * r * r * r;
        }
        double volume = basis.Volume;
        return new PackingMeasure(scale, spheres, volume, volume > 0 ? spheres / volume : 0);
    }

    /// <summary>The radius of the space filling style, before the scale.</summary>
    internal static double RadiusOf(CrystalAtom atom)
        => atom.Radius is double r && r > 0 ? r
            : ElementTable.TryGet(CrystalSceneBuilder.MainElement(atom), out ElementInfo e) ? e.Radius : 1.0;

    static double OccupancyOf(CrystalAtom atom)
    {
        if (atom.Shares is not { Count: > 0 } shares)
            return Math.Max(atom.Occupancy, 0);
        double sum = 0;
        foreach (ElementShare share in shares)
            sum += Math.Max(share.Fraction, 0);
        return sum;
    }

    /// <summary>The scale at which the closest bonded pair touches; without bonds, the closest pair of all.</summary>
    internal static double TouchingScale(CrystalStructure structure, CellBasis basis, CrystalSceneBuilder.Reduced[] reduced,
        double[] radius)
    {
        double Distance(int first, int second, LatticeShift shift)
        {
            CrystalSceneBuilder.Reduced p = reduced[first], q = reduced[second];
            return basis.Distance(q.X + shift.A - p.X, q.Y + shift.B - p.Y, q.Z + shift.C - p.Z);
        }

        double best = double.PositiveInfinity;
        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
        int count = atoms.Count;
        foreach (CrystalBond bond in structure.Bonds)
        {
            if ((uint)bond.First >= (uint)count || (uint)bond.Second >= (uint)count)
                continue;
            if (atoms[bond.First].IsVoid || atoms[bond.Second].IsVoid)
                continue;
            LatticeShift shift = reduced[bond.Second].Floor + bond.Shift - reduced[bond.First].Floor;
            double d = Distance(bond.First, bond.Second, shift);
            if (d > 0)
                best = Math.Min(best, d / (radius[bond.First] + radius[bond.Second]));
        }
        if (double.IsFinite(best))
            return best;
        for (int i = 0; i < count; i++)
        {
            if (atoms[i].IsVoid)
                continue;
            for (int j = i; j < count; j++)
            {
                if (atoms[j].IsVoid)
                    continue;
                for (int a = -1; a <= 1; a++)
                    for (int b = -1; b <= 1; b++)
                        for (int c = -1; c <= 1; c++)
                        {
                            if (i == j && a == 0 && b == 0 && c == 0)
                                continue;
                            double d = Distance(i, j, new LatticeShift(a, b, c));
                            if (d > 1e-6)
                                best = Math.Min(best, d / (radius[i] + radius[j]));
                        }
            }
        }
        return double.IsFinite(best) ? best : 1.0;
    }
}
