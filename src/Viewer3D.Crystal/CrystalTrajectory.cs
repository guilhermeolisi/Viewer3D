using System.Globalization;

namespace Viewer3D.Crystal;

/// <summary>One frame of a <see cref="CrystalTrajectory"/>.</summary>
/// <param name="Structure">The atoms of the frame, with the bonds found from its geometry.</param>
/// <param name="IsPeriodic">
/// True when the frame has a cell of its own (a crystal, a periodic box). False for a molecule: its cell is a box around
/// the whole trajectory, drawn without edges and never repeated.
/// </param>
public sealed record CrystalTrajectoryFrame(CrystalStructure Structure, bool IsPeriodic);

/// <summary>
/// A series of structures read from a trajectory file, one per frame, ready to become a <see cref="SceneSequence"/>
/// through <see cref="ToSequence"/>. A frame without a cell (a molecule) gets a box: one orthorhombic box for the whole
/// trajectory, the union of the extents of every such frame plus 1 angstrom on each side, so the molecule moves inside a
/// fixed frame. Immutable.
/// </summary>
public sealed class CrystalTrajectory
{
    /// <summary>Space left around a molecule inside its box, in angstrom.</summary>
    public const double BoxMargin = 1;

    /// <summary>Creates a trajectory from frames already made.</summary>
    /// <param name="frames">The frames, in playing order; at least one.</param>
    /// <param name="warnings">What the reader accepted but the user should know.</param>
    /// <param name="frameDuration">How long each frame stays on screen. Positive.</param>
    public CrystalTrajectory(IReadOnlyList<CrystalTrajectoryFrame> frames, IReadOnlyList<string> warnings, TimeSpan frameDuration)
    {
        ArgumentNullException.ThrowIfNull(frames);
        ArgumentNullException.ThrowIfNull(warnings);
        if (frames.Count == 0)
            throw new ArgumentException("A trajectory needs at least one frame.", nameof(frames));
        if (frameDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(frameDuration), "A frame lasts a positive time.");
        Frames = frames;
        Warnings = warnings;
        FrameDuration = frameDuration;
    }

    /// <summary>The frames, in playing order.</summary>
    public IReadOnlyList<CrystalTrajectoryFrame> Frames { get; }

    /// <summary>What the reader accepted but the user should know (an unknown element, a left-handed cell).</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>How long each frame stays on screen.</summary>
    public TimeSpan FrameDuration { get; }

    /// <summary>
    /// Builds every frame with <paramref name="builder"/> into a <see cref="SceneSequence"/>. A molecule frame is drawn
    /// as one block without the box edges. The scene cost ceiling is the one of <paramref name="options"/>
    /// (<see cref="CrystalSceneOptions.MaxCost"/>): raise it for large frames. The weight of the sequence is checked
    /// before building, from the atoms each frame will have at least, and again frame by frame as they are built, so a
    /// trajectory past <paramref name="maxWeight"/> stops at the frame that passes it.
    /// </summary>
    /// <param name="builder">The scene builder.</param>
    /// <param name="options">Options of every frame; a molecule frame takes one block of its box.</param>
    /// <param name="appearance">Appearance of every frame; null takes the default.</param>
    /// <param name="lowDetail">
    /// Also build the lighter version of each frame, for the styles that have one (ellipsoids without their principal
    /// ellipses, polyhedra without back faces); other styles get none.
    /// </param>
    /// <param name="maxWeight">Ceiling of the weight of the sequence (<see cref="SceneSequence.Weight"/>).</param>
    /// <param name="cancellationToken">Checked before each frame.</param>
    /// <exception cref="SceneSequenceTooLargeException">The frames weigh more than <paramref name="maxWeight"/>.</exception>
    /// <exception cref="OperationCanceledException">The build was canceled.</exception>
    public SceneSequence ToSequence(CrystalSceneBuilder builder, CrystalSceneOptions options, CrystalAppearance? appearance = null,
        bool lowDetail = false, long maxWeight = SceneSequence.DefaultMaxWeight, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        appearance ??= CrystalAppearance.Default;
        CrystalAppearance boxed = appearance with { ShowCell = false };
        CrystalSceneOptions molecule = options with { RepeatA = 1, RepeatB = 1, RepeatC = 1, Clip = CrystalClip.Cell };
        bool light = lowDetail && options.Style is CrystalStyle.ThermalEllipsoids or CrystalStyle.Polyhedra or CrystalStyle.PolyhedraWithEllipsoids;

        // Antes de montar: os atomos que cada quadro tera pelo menos, uma ou duas vezes (com a versao leve).
        long least = 0;
        foreach (CrystalTrajectoryFrame frame in Frames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            (CrystalSceneOptions o, CrystalAppearance a) = frame.IsPeriodic ? (options, appearance) : (molecule, boxed);
            least += builder.Estimate(frame.Structure, o, a).Atoms * (light ? 2L : 1L);
            SceneSequence.CheckWeight(least, maxWeight);
        }

        var scenes = new Scene[Frames.Count];
        Scene?[]? lighter = light ? new Scene?[Frames.Count] : null;
        long weight = 0;
        for (int k = 0; k < Frames.Count; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CrystalTrajectoryFrame frame = Frames[k];
            (CrystalSceneOptions o, CrystalAppearance a) = frame.IsPeriodic ? (options, appearance) : (molecule, boxed);
            scenes[k] = builder.Build(frame.Structure, o, a).Scene;
            weight += SceneSequence.WeightOf(scenes[k]);
            if (lighter is not null)
            {
                lighter[k] = builder.Build(frame.Structure, o with { PrincipalEllipses = false, PolyhedronBackFaces = false }, a).Scene;
                weight += SceneSequence.WeightOf(lighter[k]);
            }
            SceneSequence.CheckWeight(weight, maxWeight);
        }
        return new SceneSequence(scenes, FrameDuration, lowDetailFrames: lighter, maxWeight: maxWeight);
    }

    /// <summary>
    /// The trajectory of frames read as Cartesian positions: each periodic frame in the cell of its lattice vectors, each
    /// molecule frame in the box of the whole trajectory, with the bonds of <see cref="CrystalBondFinder"/>.
    /// </summary>
    internal static CrystalTrajectory FromCartesian(IReadOnlyList<CartesianFrame> frames, List<string> warnings, TimeSpan frameDuration)
    {
        (double X, double Y, double Z) low = (double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity);
        (double X, double Y, double Z) high = (double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity);
        foreach (CartesianFrame frame in frames)
        {
            if (frame.Lattice is not null)
                continue;
            for (int i = 0; i < frame.Symbols.Count; i++)
            {
                double x = frame.Positions[3 * i], y = frame.Positions[3 * i + 1], z = frame.Positions[3 * i + 2];
                low = (Math.Min(low.X, x), Math.Min(low.Y, y), Math.Min(low.Z, z));
                high = (Math.Max(high.X, x), Math.Max(high.Y, y), Math.Max(high.Z, z));
            }
        }
        (double X, double Y, double Z) origin = (low.X - BoxMargin, low.Y - BoxMargin, low.Z - BoxMargin);
        var box = new CrystalCell(high.X - low.X + 2 * BoxMargin, high.Y - low.Y + 2 * BoxMargin, high.Z - low.Z + 2 * BoxMargin, 90, 90, 90);

        bool reversedOnce = false;
        var result = new CrystalTrajectoryFrame[frames.Count];
        for (int k = 0; k < frames.Count; k++)
        {
            CartesianFrame frame = frames[k];
            var atoms = new CrystalAtom[frame.Symbols.Count];
            CrystalCell cell;
            if (frame.Lattice is { } lattice)
            {
                LatticeFrame basis = LatticeFrame.Create(lattice);
                cell = basis.Cell;
                if (basis.Reversed && !reversedOnce)
                {
                    reversedOnce = true;
                    warnings.Add(string.Format(CultureInfo.InvariantCulture,
                        "The cell vectors of frame {0} are in left-handed order; c is reversed so the cell is right-handed, and the atoms stay where they are.",
                        k + 1));
                }
                for (int i = 0; i < atoms.Length; i++)
                {
                    (double u, double v, double w) = basis.ToFractional(frame.Positions[3 * i], frame.Positions[3 * i + 1], frame.Positions[3 * i + 2]);
                    atoms[i] = Atom(frame.Symbols[i], i, u, v, w);
                }
            }
            else
            {
                cell = box;
                for (int i = 0; i < atoms.Length; i++)
                {
                    atoms[i] = Atom(frame.Symbols[i], i, (frame.Positions[3 * i] - origin.X) / box.A,
                        (frame.Positions[3 * i + 1] - origin.Y) / box.B, (frame.Positions[3 * i + 2] - origin.Z) / box.C);
                }
            }
            bool periodic = frame.Lattice is not null;
            var structure = new CrystalStructure(cell, atoms, []);
            result[k] = new CrystalTrajectoryFrame(structure with { Bonds = CrystalBondFinder.Find(structure, periodic) }, periodic);
        }
        return new CrystalTrajectory(result, warnings, frameDuration);
    }

    // The label is the element and the position of the atom in its frame, from 1: O1, H2, H3.
    static CrystalAtom Atom(string symbol, int index, double x, double y, double z)
        => new(symbol + (index + 1).ToString(CultureInfo.InvariantCulture), symbol, x, y, z) { SourceIndex = index };
}

/// <summary>
/// One frame as a reader gets it: element symbols, Cartesian positions in angstrom (three per atom) and, for a periodic
/// frame, the nine components of the lattice vectors a, b and c in the same frame.
/// </summary>
internal sealed record CartesianFrame(IReadOnlyList<string> Symbols, double[] Positions, double[]? Lattice);
