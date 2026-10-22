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
    /// Marks a void site: an empty interstice of the structure, not an atom. It is drawn as a small translucent marker
    /// (and, with <see cref="CrystalSceneOptions.ShowVoidSpheres"/>, as the sphere of radius <see cref="Radius"/>, the
    /// largest sphere that fits in the void), it can be the center of a polyhedron, and it stays out of the legends, of
    /// the space-filling scale, of the displacement shapes and of <see cref="CrystalPacking"/>.
    /// </summary>
    public bool IsVoid { get; init; }

    /// <summary>
    /// Index of the crystallographic site the atom belongs to: atoms of one site are equivalent by symmetry. Used to
    /// extend bonds given for one atom of a site to the others (<see cref="CrystalBondExpansion"/>). -1 when unknown.
    /// </summary>
    public int SiteIndex { get; init; } = -1;

    /// <summary>Index of the atom in the owner's own data, for reporting a selection back. -1 when unused.</summary>
    public int SourceIndex { get; init; } = -1;

    /// <summary>
    /// Displacement tensors of the atom at this position, for <see cref="CrystalStyle.ThermalEllipsoids"/>: one entry
    /// draws one shape, so a site shared by elements with different tensors gives one entry per element. Null draws the
    /// atom as having no data.
    /// </summary>
    public IReadOnlyList<AtomDisplacement>? Displacements { get; init; }

    /// <summary>
    /// Index of the atom's group in <see cref="CrystalStructure.Groups"/>: a class of atoms the owner defines (the atoms
    /// next to a defect, one grain, one layer), with its own color and appearance rules. -1 when the atom has no group.
    /// </summary>
    public int Group { get; init; } = -1;
}

/// <summary>A group of atoms, for <see cref="CrystalAtom.Group"/>: its name in the legend and its color.</summary>
/// <param name="Label">Name shown in the legend of <see cref="AtomColorScheme.Group"/>.</param>
/// <param name="Color">Color of the group's atoms in <see cref="AtomColorScheme.Group"/>.</param>
public sealed record CrystalAtomGroup(string Label, Rgba Color);

/// <summary>
/// One displacement tensor of an atom, in the Cartesian frame of <see cref="CrystalCell"/> (a along x, b in the xy
/// plane), in square angstrom.
/// </summary>
/// <param name="Element">Element the tensor belongs to, which picks the color; null takes the color of the atom.</param>
/// <param name="Cartesian6">U11, U22, U33, U12, U13 and U23; null when there is no data for this atom.</param>
public sealed record AtomDisplacement(string? Element, double[]? Cartesian6);

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

/// <summary>One vertex of a <see cref="CrystalPolyhedron"/>: an atom, translated as the second atom of a bond.</summary>
/// <param name="Atom">Index of the vertex atom, in <see cref="CrystalStructure.Atoms"/>.</param>
/// <param name="Shift">
/// Cell translation applied to the vertex atom, from the coordinates as given, with the center atom at its coordinates
/// as given (the rule of <see cref="CrystalBond.Shift"/>).
/// </param>
public readonly record struct PolyhedronVertex(int Atom, LatticeShift Shift);

/// <summary>
/// A coordination polyhedron around one atom, as its owner computed it: the layer does not compute hulls, it draws the
/// faces in the order given. Drawn by <see cref="CrystalStyle.Polyhedra"/> and
/// <see cref="CrystalStyle.PolyhedraWithEllipsoids"/> around every drawn image of the center.
/// </summary>
/// <param name="Center">Index of the center atom, in <see cref="CrystalStructure.Atoms"/>.</param>
/// <param name="Vertices">The vertices.</param>
/// <param name="Faces">
/// The faces, each a polygon of three or more indices into <paramref name="Vertices"/>, counter-clockwise seen from
/// outside. A polyhedron with an index out of range is not drawn.
/// </param>
public sealed record CrystalPolyhedron(int Center, IReadOnlyList<PolyhedronVertex> Vertices, IReadOnlyList<int[]> Faces)
{
    /// <summary>Color of the faces, without alpha (the opacity is an option of the scene); null takes the center's color.</summary>
    public Rgba? Color { get; init; }

    /// <summary>Index of the polyhedron in the owner's own data, for reporting a selection back. -1 when unused.</summary>
    public int SourceIndex { get; init; } = -1;
}

/// <summary>A crystal structure: the cell, the atoms in it and the bonds between them.</summary>
/// <param name="Cell">The unit cell.</param>
/// <param name="Atoms">The atoms, each at its own position (equivalent atoms listed one by one).</param>
/// <param name="Bonds">The bonds; may be empty.</param>
public sealed record CrystalStructure(CrystalCell Cell, IReadOnlyList<CrystalAtom> Atoms, IReadOnlyList<CrystalBond> Bonds)
{
    /// <summary>The coordination polyhedra; may be empty. Only the polyhedra styles draw them.</summary>
    public IReadOnlyList<CrystalPolyhedron> Polyhedra { get; init; } = [];

    /// <summary>The groups that <see cref="CrystalAtom.Group"/> points into; may be empty.</summary>
    public IReadOnlyList<CrystalAtomGroup> Groups { get; init; } = [];

    /// <summary>
    /// Items drawn over the structure, in the Cartesian frame of <see cref="CrystalCell"/> (a along x, b in the xy plane):
    /// lines, arrows, planes and labels that point at something in the structure. Their <see cref="SceneItem.Id"/> is
    /// the owner's and must be negative, so it never meets the ids of the atoms, rods and polyhedra; picking one reports
    /// <see cref="CrystalItemKind.Annotation"/>. Hidden by <see cref="CrystalAppearance.ShowAnnotations"/>.
    /// </summary>
    public IReadOnlyList<SceneItem> Annotations { get; init; } = [];
}
