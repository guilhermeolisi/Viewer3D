namespace Viewer3D.Crystal;

/// <summary>How the atoms of a crystal structure are drawn. Later styles (polyhedra, displacement ellipsoids) add values.</summary>
public enum CrystalStyle
{
    /// <summary>Small spheres, <see cref="CrystalSceneOptions.BallScale"/> times the atom radius, joined by rods.</summary>
    BallAndStick,

    /// <summary>
    /// Large spheres without rods, every radius times the one scale at which the closest bonded pair just touches, so
    /// no bonded pair overlaps.
    /// </summary>
    SpaceFilling,
}

/// <summary>Options of <see cref="CrystalSceneBuilder.Build"/>.</summary>
public sealed record CrystalSceneOptions
{
    /// <summary>
    /// Ceiling on the spheres of a scene. A repetition that goes past it falls back to one cell and the scene carries a
    /// <see cref="CrystalScene.Warning"/>.
    /// </summary>
    public const int MaxAtoms = 3000;

    /// <summary>Drawing style.</summary>
    public CrystalStyle Style { get; init; } = CrystalStyle.BallAndStick;

    /// <summary>Cells along a, 1 to 3.</summary>
    public int RepeatA { get; init; } = 1;

    /// <summary>Cells along b, 1 to 3.</summary>
    public int RepeatB { get; init; } = 1;

    /// <summary>Cells along c, 1 to 3.</summary>
    public int RepeatC { get; init; } = 1;

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

    /// <summary>Rod radius, in angstrom.</summary>
    public double BondRadius { get; init; } = 0.12;

    /// <summary>Colors by element symbol, over those of <see cref="ElementTable"/>.</summary>
    public IReadOnlyDictionary<string, Rgba>? ElementColors { get; init; }

    /// <summary>Color of the cell edges.</summary>
    public Rgba CellColor { get; init; } = new(128, 128, 128);
}

/// <summary>One element of the legend.</summary>
/// <param name="Element">Element symbol.</param>
/// <param name="Color">Color the scene uses for it.</param>
public sealed record ElementLegendEntry(string Element, Rgba Color);

/// <summary>What a pickable item of a <see cref="CrystalScene"/> stands for.</summary>
public enum CrystalItemKind
{
    /// <summary>A sphere.</summary>
    Atom,

    /// <summary>A rod.</summary>
    Bond,
}

/// <summary>What a sphere or rod of a <see cref="CrystalScene"/> stands for.</summary>
/// <param name="Kind">Sphere or rod.</param>
/// <param name="AtomIndex">The atom, in <see cref="CrystalStructure.Atoms"/>; for a rod, its first atom.</param>
/// <param name="BondIndex">The bond, in <see cref="CrystalStructure.Bonds"/>; -1 for a sphere.</param>
/// <param name="Offset">
/// Cell translation from the coordinates of the atom as given to where it is drawn (for a rod, of its first atom).
/// </param>
/// <param name="IsCompletion">True for a faded sphere outside the block, or a rod to one.</param>
public sealed record CrystalItemInfo(CrystalItemKind Kind, int AtomIndex, int BondIndex, LatticeShift Offset, bool IsCompletion);

/// <summary>The scene of a crystal structure, with what the viewer around it needs.</summary>
public sealed class CrystalScene
{
    readonly IReadOnlyDictionary<int, CrystalItemInfo> lookup;

    internal CrystalScene(Scene scene, CrystalCell cell, IReadOnlyList<AxisDirection> axes, double sphereScale,
        IReadOnlyList<ElementLegendEntry> legend, int atomCount, string? warning, IReadOnlyDictionary<int, CrystalItemInfo> lookup)
    {
        Scene = scene;
        Cell = cell;
        Axes = axes;
        SphereScale = sphereScale;
        Legend = legend;
        AtomCount = atomCount;
        Warning = warning;
        this.lookup = lookup;
    }

    /// <summary>The items: spheres, rods, cell edges and labels, in angstrom.</summary>
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
    /// in space filling.
    /// </summary>
    public double SphereScale { get; }

    /// <summary>The elements drawn, each once, by atomic number.</summary>
    public IReadOnlyList<ElementLegendEntry> Legend { get; }

    /// <summary>Spheres in the scene, faded ones included.</summary>
    public int AtomCount { get; }

    /// <summary>Why the scene is not what was asked (repetition past the ceiling, invalid cell); null when it is.</summary>
    public string? Warning { get; }

    /// <summary>What the item with this <see cref="SceneItem.Id"/> stands for; null for an edge, a label or an unknown id.</summary>
    public CrystalItemInfo? Lookup(int itemId) => lookup.TryGetValue(itemId, out CrystalItemInfo? info) ? info : null;
}
