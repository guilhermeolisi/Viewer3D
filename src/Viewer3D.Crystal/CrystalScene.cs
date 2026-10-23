namespace Viewer3D.Crystal;

/// <summary>How the atoms of a crystal structure are drawn. New styles add values at the end.</summary>
public enum CrystalStyle
{
    /// <summary>Small spheres, <see cref="CrystalSceneOptions.BallScale"/> times the atom radius, joined by rods.</summary>
    BallAndStick,

    /// <summary>
    /// Large spheres without rods, every radius times the one scale at which the closest bonded pair just touches, so
    /// no bonded pair overlaps.
    /// </summary>
    SpaceFilling,

    /// <summary>
    /// Each atom as the ellipsoid of its displacement tensor (<see cref="CrystalAtom.Displacements"/>) at
    /// <see cref="CrystalSceneOptions.ProbabilityLevel"/>, joined by thin rods: a sphere when the tensor is isotropic, a
    /// small cube when it is not positive definite, and a small dashed sphere when there is no data.
    /// </summary>
    ThermalEllipsoids,

    /// <summary>
    /// <see cref="BallAndStick"/> with the coordination polyhedra of <see cref="CrystalStructure.Polyhedra"/> around each
    /// drawn image of their centers: translucent faces (<see cref="CrystalSceneOptions.PolyhedronOpacity"/>) with their
    /// edges, lit like the spheres.
    /// </summary>
    Polyhedra,

    /// <summary><see cref="Polyhedra"/> with the atoms drawn as in <see cref="ThermalEllipsoids"/>.</summary>
    PolyhedraWithEllipsoids,
}

/// <summary>Which part of the infinite crystal a scene shows. New values go at the end.</summary>
public enum CrystalClip
{
    /// <summary>A block of whole cells, <see cref="CrystalSceneOptions.RepeatA"/> x B x C, with the cell edges.</summary>
    Cell,

    /// <summary>
    /// Every atom image within <see cref="CrystalSceneOptions.SphereRadius"/> of
    /// <see cref="CrystalSceneOptions.SphereCenter"/>, whatever cell it falls in, without cell edges.
    /// </summary>
    Sphere,
}

/// <summary>A point in fractional coordinates of the cell.</summary>
public readonly record struct FractionalPoint(double X, double Y, double Z);

/// <summary>
/// The cost of a scene counted before building it (<see cref="CrystalSceneBuilder.Estimate"/>): the images of the block
/// and of its far faces, or of the sphere, and the polyhedra around them. It is a lower bound of the cost of the scene
/// built: an atom the appearance hides or may hide in some image is left out, the polyhedra are left out when a length
/// limit may drop some, and the faded neighbors are never counted, so the scene built costs the same or more.
/// </summary>
/// <param name="Atoms">Images counted.</param>
/// <param name="Polyhedra">Polyhedra counted, around the images of their centers.</param>
/// <param name="Cost"><paramref name="Atoms"/> plus <see cref="CrystalSceneOptions.PolyhedronCost"/> per polyhedron.</param>
/// <param name="Ceiling">The ceiling it was held against, <see cref="CrystalSceneOptions.MaxCost"/>.</param>
/// <param name="Fits">
/// In a block, the repetition that fits: the one asked when it fits, else the one asked with its largest axis taken down
/// one cell at a time until it fits, or 1 x 1 x 1. In a sphere, 1 x 1 x 1.
/// </param>
/// <param name="FitsRadius">
/// In a sphere, the radius that fits: the one asked when it fits, else the largest multiple of 0.5 angstrom below it that
/// fits, or 0 when none does. In a block, NaN.
/// </param>
public sealed record CrystalSceneEstimate(int Atoms, int Polyhedra, int Cost, int Ceiling, LatticeShift Fits, double FitsRadius)
{
    /// <summary>Whether the cost is within the ceiling.</summary>
    public bool Fit => Cost <= Ceiling;
}

/// <summary>Options of <see cref="CrystalSceneBuilder.Build(CrystalStructure, CrystalSceneOptions, CrystalAppearance?)"/>.</summary>
public sealed record CrystalSceneOptions
{
    /// <summary>
    /// Default of <see cref="MaxCost"/>, the ceiling on the cost of a scene. A repetition or a sphere that goes past it
    /// falls back to one cell and the scene carries a <see cref="CrystalScene.Warning"/>.
    /// </summary>
    public const int MaxAtoms = 3000;

    /// <summary>
    /// What one polyhedron counts for against the ceiling, in spheres: its faces and edges cost about as much to prepare
    /// and paint as this many spheres. Provisional, to be set from the polyhedra benchmark.
    /// </summary>
    public const int PolyhedronCost = 8;

    /// <summary>Drawing style.</summary>
    public CrystalStyle Style { get; init; } = CrystalStyle.BallAndStick;

    /// <summary>Cells along a, at least 1; the cost ceiling (<see cref="MaxCost"/>) bounds the block.</summary>
    public int RepeatA { get; init; } = 1;

    /// <summary>Cells along b, at least 1; the cost ceiling (<see cref="MaxCost"/>) bounds the block.</summary>
    public int RepeatB { get; init; } = 1;

    /// <summary>Cells along c, at least 1; the cost ceiling (<see cref="MaxCost"/>) bounds the block.</summary>
    public int RepeatC { get; init; } = 1;

    /// <summary>
    /// Ceiling on the cost of a scene: its spheres plus <see cref="PolyhedronCost"/> per polyhedron. A value below 1
    /// takes <see cref="MaxAtoms"/>. <see cref="CrystalSceneBuilder.Estimate"/> gives the cost before building.
    /// </summary>
    public int MaxCost { get; init; } = MaxAtoms;

    /// <summary>
    /// Block of cells or sphere around a point. In <see cref="CrystalClip.Sphere"/> the repetition and
    /// <see cref="ShowBoundaryAtoms"/> do not apply, and a bond is drawn only when both of its atoms are in the sphere.
    /// </summary>
    public CrystalClip Clip { get; init; } = CrystalClip.Cell;

    /// <summary>Center of the sphere of <see cref="CrystalClip.Sphere"/>, in fractional coordinates.</summary>
    public FractionalPoint SphereCenter { get; init; } = new(0.5, 0.5, 0.5);

    /// <summary>
    /// Radius of the sphere of <see cref="CrystalClip.Sphere"/>, in angstrom. An image at this distance from the center
    /// is inside. A value that is not positive and finite takes 6.
    /// </summary>
    public double SphereRadius { get; init; } = 6;

    /// <summary>
    /// Longest bond drawn, in angstrom; null (Automatic) draws the bonds marked <see cref="CrystalBond.IsAutomatic"/>
    /// and no other. A bond is drawn when each of its two atoms lets it through: the limit of an atom is its
    /// <see cref="AtomStyle.MaxBondLength"/> when a rule sets one, else this one; with no limit the atom lets through
    /// the automatic bonds, and with a limit every bond no longer than it (0 lets none through). The vertices of a
    /// polyhedron count as automatic bonds of its center, and a polyhedron is drawn only when every center-vertex pair
    /// passes, so a limit never adds vertices to a polyhedron.
    /// </summary>
    public double? MaxBondLength { get; init; }

    /// <summary>Also draws the atoms on the far faces, edges and corners of the block of cells.</summary>
    public bool ShowBoundaryAtoms { get; init; } = true;

    /// <summary>
    /// Draws the bonded neighbors that fall outside the block of cells as faded spheres, so every atom of the block
    /// shows its whole coordination. Without it, bonds to outside atoms are left out.
    /// </summary>
    public bool CompleteCoordination { get; init; } = true;

    /// <summary>Labels the atoms of the block (not the boundary or faded ones).</summary>
    public bool IncludeLabels { get; init; } = true;

    /// <summary>Sphere radius as a fraction of the atom radius, in <see cref="CrystalStyle.BallAndStick"/>.</summary>
    public double BallScale { get; init; } = 0.4;

    /// <summary>Rod radius, in angstrom. <see cref="CrystalStyle.ThermalEllipsoids"/> uses 0.4 of it.</summary>
    public double BondRadius { get; init; } = 0.12;

    /// <summary>
    /// Probability of the ellipsoids, in percent, in <see cref="CrystalStyle.ThermalEllipsoids"/>: 30, 50, 70, 90
    /// or 99 (<see cref="ProbabilityScale"/>). Any other value takes 50.
    /// </summary>
    public int ProbabilityLevel { get; init; } = 50;

    /// <summary>
    /// Display magnification of the displacement shapes, on top of the factor of <see cref="ProbabilityLevel"/>: 1 draws
    /// the ellipsoids at their probability, as ORTEP does; a larger value draws them that many times larger, which the
    /// caption should then say. A value that is not finite and positive takes 1.
    /// </summary>
    public double DisplacementMagnification { get; init; } = 1;

    /// <summary>
    /// Radius of the dashed sphere of an atom with no data, and half edge of the cube of a tensor that is not positive
    /// definite, in angstrom.
    /// </summary>
    public double MarkerSize { get; init; } = 0.3;

    /// <summary>
    /// Draws the visible halves of the principal ellipses on each ellipsoid. A scene for gestures can turn them off.
    /// </summary>
    public bool PrincipalEllipses { get; init; } = true;

    /// <summary>Opacity of the polyhedron faces, from 0 (invisible) to 1 (opaque), in the polyhedra styles.</summary>
    public double PolyhedronOpacity { get; init; } = 0.35;

    /// <summary>
    /// Draws the vertex atoms of the polyhedra. Off, an image that is a vertex of a drawn polyhedron and not the center
    /// of one is left out, with its label and rods; the centers always stay.
    /// </summary>
    public bool ShowPolyhedronVertices { get; init; } = true;

    /// <summary>Draws the rods from the center of a drawn polyhedron to its vertices.</summary>
    public bool ShowBondsInsidePolyhedra { get; init; } = true;

    /// <summary>
    /// Draws the back faces of the polyhedra, seen through the front ones. A scene for gestures can turn them off: half
    /// the faces, and the front ones still show the shape.
    /// </summary>
    public bool PolyhedronBackFaces { get; init; } = true;

    /// <summary>The polyhedra drawn, as indices into <see cref="CrystalStructure.Polyhedra"/>; null draws them all.</summary>
    public IReadOnlySet<int>? EnabledPolyhedra { get; init; }

    /// <summary>Colors by element symbol, over those of <see cref="ElementTable"/>.</summary>
    public IReadOnlyDictionary<string, Rgba>? ElementColors { get; init; }

    /// <summary>Color of the cell edges.</summary>
    public Rgba CellColor { get; init; } = new(128, 128, 128);

    /// <summary>
    /// Draws each void site (<see cref="CrystalAtom.IsVoid"/>) as the sphere of its <see cref="CrystalAtom.Radius"/>, the
    /// largest sphere that fits in the void, instead of the small marker. A void without a radius keeps the marker.
    /// </summary>
    public bool ShowVoidSpheres { get; init; }

    /// <summary>
    /// Radius of the marker of a void site, in angstrom, in every style: the style scale does not apply to it, so a void
    /// never looks like an atom.
    /// </summary>
    public double VoidMarkerRadius { get; init; } = 0.1;

    /// <summary>Color of the void sites, unless an appearance rule gives one.</summary>
    public Rgba VoidColor { get; init; } = new(150, 150, 150);

    /// <summary>Opacity of the void markers and spheres, from 0 (invisible) to 1 (opaque).</summary>
    public double VoidOpacity { get; init; } = 0.5;
}

/// <summary>One element of the legend.</summary>
/// <param name="Element">Element symbol.</param>
/// <param name="Color">Color the scene uses for it.</param>
public sealed record ElementLegendEntry(string Element, Rgba Color);

/// <summary>One entry of <see cref="CrystalScene.ColorLegend"/>: a text and its color.</summary>
public sealed record ColorLegendEntry(string Text, Rgba Color);

/// <summary>What a pickable item of a <see cref="CrystalScene"/> stands for.</summary>
public enum CrystalItemKind
{
    /// <summary>A sphere, or a shape of <see cref="CrystalStyle.ThermalEllipsoids"/>.</summary>
    Atom,

    /// <summary>A rod.</summary>
    Bond,

    /// <summary>A coordination polyhedron, its faces or its edges.</summary>
    Polyhedron,

    /// <summary>An item of <see cref="CrystalStructure.Annotations"/>.</summary>
    Annotation,
}

/// <summary>What a sphere, rod or polyhedron of a <see cref="CrystalScene"/> stands for.</summary>
/// <param name="Kind">Sphere, rod or polyhedron.</param>
/// <param name="AtomIndex">
/// The atom, in <see cref="CrystalStructure.Atoms"/>; for a rod, its first atom; for a polyhedron, its center.
/// </param>
/// <param name="BondIndex">The bond, in <see cref="CrystalStructure.Bonds"/>; -1 for a sphere or a polyhedron.</param>
/// <param name="Offset">
/// Cell translation from the coordinates of the atom as given to where it is drawn (for a rod, of its first atom; for
/// a polyhedron, of its center).
/// </param>
/// <param name="IsCompletion">True for a faded sphere outside the block, or a rod to one.</param>
public sealed record CrystalItemInfo(CrystalItemKind Kind, int AtomIndex, int BondIndex, LatticeShift Offset, bool IsCompletion)
{
    /// <summary>The polyhedron, in <see cref="CrystalStructure.Polyhedra"/>; -1 for a sphere or a rod.</summary>
    public int PolyhedronIndex { get; init; } = -1;

    /// <summary>
    /// The annotation, in <see cref="CrystalStructure.Annotations"/> (the first one with the picked id); -1 for anything
    /// else.
    /// </summary>
    public int AnnotationIndex { get; init; } = -1;
}

/// <summary>The scene of a crystal structure, with what the viewer around it needs.</summary>
public sealed class CrystalScene
{
    readonly IReadOnlyDictionary<int, CrystalItemInfo> lookup;

    internal CrystalScene(Scene scene, CrystalCell cell, IReadOnlyList<AxisDirection> axes, double sphereScale,
        IReadOnlyList<ElementLegendEntry> legend, int atomCount, string? warning, IReadOnlyDictionary<int, CrystalItemInfo> lookup,
        int polyhedronCount = 0)
    {
        Scene = scene;
        Cell = cell;
        Axes = axes;
        SphereScale = sphereScale;
        Legend = legend;
        AtomCount = atomCount;
        PolyhedronCount = polyhedronCount;
        Warning = warning;
        this.lookup = lookup;
    }

    /// <summary>The items: spheres, rods, polyhedra, cell edges and labels, in angstrom.</summary>
    public Scene Scene { get; }

    /// <summary>The cell the scene was built from.</summary>
    public CrystalCell Cell { get; }

    /// <summary>
    /// The a, b and c directions for the axis indicator, without color, so the viewer gives them the colors of its
    /// palette. The cell axes are not drawn in the scene.
    /// </summary>
    public IReadOnlyList<AxisDirection> Axes { get; }

    /// <summary>
    /// Sphere radius over atom radius: <see cref="CrystalSceneOptions.BallScale"/> in ball and stick, the touching scale
    /// in space filling. In thermal ellipsoids, the factor of the probability level (<see cref="ProbabilityScale"/>).
    /// </summary>
    public double SphereScale { get; }

    /// <summary>The elements drawn, each once, by atomic number.</summary>
    public IReadOnlyList<ElementLegendEntry> Legend { get; }

    /// <summary>Spheres in the scene, faded ones included.</summary>
    public int AtomCount { get; }

    /// <summary>Polyhedra in the scene: one per drawn image of a center.</summary>
    public int PolyhedronCount { get; }

    /// <summary>Why the scene is not what was asked (repetition past the ceiling, invalid cell); null when it is.</summary>
    public string? Warning { get; }

    /// <summary>Images of the block and of its far faces that the appearance hid.</summary>
    public int HiddenAtomCount { get; init; }

    /// <summary>
    /// What the atom colors mean in the color scheme of the appearance: the elements, the sites, or stops of the
    /// occupancy scale.
    /// </summary>
    public IReadOnlyList<ColorLegendEntry> ColorLegend { get; init; } = [];

    /// <summary>What the item with this <see cref="SceneItem.Id"/> stands for; null for an edge, a label or an unknown id.</summary>
    public CrystalItemInfo? Lookup(int itemId) => lookup.TryGetValue(itemId, out CrystalItemInfo? info) ? info : null;
}
