namespace Viewer3D.Crystal;

/// <summary>A whole number of cell translations along a, b and c.</summary>
public readonly record struct LatticeShift(int A, int B, int C)
{
    /// <summary>No translation.</summary>
    public static LatticeShift Zero => default;

    /// <summary>Sum of two translations.</summary>
    public static LatticeShift operator +(LatticeShift x, LatticeShift y) => new(x.A + y.A, x.B + y.B, x.C + y.C);

    /// <summary>Difference of two translations.</summary>
    public static LatticeShift operator -(LatticeShift x, LatticeShift y) => new(x.A - y.A, x.B - y.B, x.C - y.C);
}

/// <summary>One element of a site shared by several elements, with its occupancy.</summary>
/// <param name="Element">Element symbol.</param>
/// <param name="Fraction">Occupancy of this element on the site, between 0 and 1.</param>
public sealed record ElementShare(string Element, double Fraction);

/// <summary>
/// One atom of the cell, at fractional coordinates. Atoms equivalent by symmetry are listed one by one: the layer does
/// not apply space group operations.
/// </summary>
/// <param name="Label">Label shown next to the atom.</param>
/// <param name="Element">Element symbol, which picks the color and the default radius.</param>
/// <param name="X">Fractional coordinate along a.</param>
/// <param name="Y">Fractional coordinate along b.</param>
/// <param name="Z">Fractional coordinate along c.</param>
public sealed record CrystalAtom(string Label, string Element, double X, double Y, double Z)
{
    /// <summary>Total occupancy of the position. Below 1, the sphere shows an empty sector.</summary>
    public double Occupancy { get; init; } = 1;

    /// <summary>
    /// The elements sharing the position, with their occupancies. With two or more, the sphere is painted in sectors.
    /// Null means the position holds <see cref="Element"/> alone, at <see cref="Occupancy"/>.
    /// </summary>
    public IReadOnlyList<ElementShare>? Shares { get; init; }

    /// <summary>Radius in angstrom (an ionic radius, for example); null takes the element radius of <see cref="ElementTable"/>.</summary>
    public double? Radius { get; init; }

    /// <summary>
    /// Index of the crystallographic site the atom belongs to: atoms of one site are equivalent by symmetry. Used to
    /// extend bonds given for one atom of a site to the others (<see cref="CrystalBondExpansion"/>). -1 when unknown.
    /// </summary>
    public int SiteIndex { get; init; } = -1;

    /// <summary>Index of the atom in the owner's own data, for reporting a selection back. -1 when unused.</summary>
    public int SourceIndex { get; init; } = -1;
}

/// <summary>
/// A bond from atom <see cref="First"/> to atom <see cref="Second"/> translated by <see cref="Shift"/>, both indices
/// into <see cref="CrystalStructure.Atoms"/>.
/// </summary>
/// <param name="First">Index of the first atom.</param>
/// <param name="Second">Index of the second atom.</param>
/// <param name="Shift">Cell translation applied to the second atom, from the coordinates as given.</param>
public sealed record CrystalBond(int First, int Second, LatticeShift Shift)
{
    /// <summary>Index of the bond in the owner's own data, for reporting a selection back. -1 when unused.</summary>
    public int SourceIndex { get; init; } = -1;
}

/// <summary>A crystal structure: the cell, the atoms in it and the bonds between them.</summary>
/// <param name="Cell">The unit cell.</param>
/// <param name="Atoms">The atoms, each at its own position (equivalent atoms listed one by one).</param>
/// <param name="Bonds">The bonds; may be empty.</param>
public sealed record CrystalStructure(CrystalCell Cell, IReadOnlyList<CrystalAtom> Atoms, IReadOnlyList<CrystalBond> Bonds);
