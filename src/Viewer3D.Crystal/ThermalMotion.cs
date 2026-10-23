using System.Numerics;

namespace Viewer3D.Crystal;

/// <summary>How <see cref="ThermalMotion"/> moves the atoms.</summary>
public sealed record ThermalMotionOptions
{
    /// <summary>The options with every default.</summary>
    public static ThermalMotionOptions Default { get; } = new();

    /// <summary>
    /// Scale k of the displacements: over the loop each atom spreads with covariance k² U. 1 shows the displacement the
    /// tensor gives; up to 3 exaggerates it for the eye. Greater than 0 and at most 3.
    /// </summary>
    public double Amplitude { get; init; } = 1;

    /// <summary>Frames in one loop, from 8 to 120.</summary>
    public int FramesPerPeriod { get; init; } = 30;

    /// <summary>How long one loop lasts on screen. Positive.</summary>
    public TimeSpan Period { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Seed of the phases of each atom: the same seed gives the same loop.</summary>
    public int Seed { get; init; } = 1;
}

/// <summary>
/// An illustrative animation of the atomic displacement tensors: each atom runs a closed loop whose positions, taken over
/// the loop, have the covariance k² U of its tensor, the Gaussian displacement the ellipsoids draw (Johnson 1976, ORTEP-II,
/// ORNL-5138; Trueblood et al. 1996, Acta Cryst. A52, 770). It shows the size and the shape of the distribution, not a
/// real motion: the atoms move independently, with phases drawn once from <see cref="ThermalMotionOptions.Seed"/>, and
/// the correlations between atoms (the phonons) are not in a tensor.
/// </summary>
/// <remarks>
/// With λᵢ and eᵢ the eigenvalues and unit eigenvectors of U, the offset at angle θ is
/// d(θ) = k Σᵢ sqrt(2 λᵢ) eᵢ fᵢ(θ), the three functions {cos(θ + ψ), sin(θ + ψ), cos(2θ + ψ')} in an order drawn per
/// atom. Over N ≥ 5 equally spaced angles they have mean 0, mean square 1/2 and no cross terms, so the mean of d dᵀ over
/// the frames is k² U exactly. An atom without a tensor, with one that is not positive definite, or a void site stays
/// at rest; a site shared by several elements moves with its first tensor.
/// </remarks>
public static class ThermalMotion
{
    /// <summary>True when some atom of <paramref name="structure"/> has a tensor to move with.</summary>
    public static bool HasMotion(CrystalStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);
        return structure.Atoms.Any(a => Axes(a) is not null);
    }

    /// <summary>
    /// The Cartesian offset of every atom in every frame of the loop, in angstrom: element [frame][atom], indexed as
    /// <see cref="CrystalStructure.Atoms"/>, ready for <see cref="CrystalSceneOptions.AtomOffsets"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of its range.</exception>
    public static Vector3[][] Offsets(CrystalStructure structure, ThermalMotionOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        options ??= ThermalMotionOptions.Default;
        Check(options);
        int n = options.FramesPerPeriod;
        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
        var offsets = new Vector3[n][];
        for (int f = 0; f < n; f++)
            offsets[f] = new Vector3[atoms.Count];
        var random = new Random(options.Seed);
        for (int i = 0; i < atoms.Count; i++)
        {
            // Drawn for every atom, moving or not, so the phases of an atom do not depend on the atoms before it.
            double psi = 2 * Math.PI * random.NextDouble();
            double psi2 = 2 * Math.PI * random.NextDouble();
            int[] order = Orders[random.Next(Orders.Length)];
            if (Axes(atoms[i]) is not { } axes)
                continue;
            for (int f = 0; f < n; f++)
            {
                double theta = 2 * Math.PI * f / n;
                Span<double> g = [Math.Cos(theta + psi), Math.Sin(theta + psi), Math.Cos(2 * theta + psi2)];
                double x = 0, y = 0, z = 0;
                for (int k = 0; k < 3; k++)
                {
                    double w = options.Amplitude * g[order[k]];
                    x += w * axes[k].X;
                    y += w * axes[k].Y;
                    z += w * axes[k].Z;
                }
                offsets[f][i] = new Vector3((float)x, (float)y, (float)z);
            }
        }
        return offsets;
    }

    /// <summary>
    /// Builds the loop of <paramref name="structure"/> as a <see cref="SceneSequence"/> that repeats: one scene per frame,
    /// each from <paramref name="builder"/> with the offsets of the frame, so every frame has the items and ids of the
    /// scene at rest. The weight is checked from the atoms before building and again frame by frame.
    /// </summary>
    /// <param name="builder">The scene builder.</param>
    /// <param name="structure">The structure, with its displacement tensors.</param>
    /// <param name="sceneOptions">Options of every frame; their <see cref="CrystalSceneOptions.AtomOffsets"/> are replaced.</param>
    /// <param name="appearance">Appearance of every frame; null takes the default.</param>
    /// <param name="motion">How the atoms move; null takes the defaults.</param>
    /// <param name="lowDetail">
    /// Also build the lighter version of each frame, for the styles that have one (ellipsoids without their principal
    /// ellipses, polyhedra without back faces); other styles get none.
    /// </param>
    /// <param name="maxWeight">Ceiling of the weight of the sequence (<see cref="SceneSequence.Weight"/>).</param>
    /// <param name="cancellationToken">Checked before each frame.</param>
    /// <exception cref="ArgumentException">No atom of the structure has a tensor to move with (<see cref="HasMotion"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option of <paramref name="motion"/> is out of its range.</exception>
    /// <exception cref="SceneSequenceTooLargeException">The frames weigh more than <paramref name="maxWeight"/>.</exception>
    /// <exception cref="OperationCanceledException">The build was canceled.</exception>
    public static SceneSequence BuildSequence(CrystalSceneBuilder builder, CrystalStructure structure, CrystalSceneOptions sceneOptions,
        CrystalAppearance? appearance = null, ThermalMotionOptions? motion = null, bool lowDetail = false,
        long maxWeight = SceneSequence.DefaultMaxWeight, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(sceneOptions);
        motion ??= ThermalMotionOptions.Default;
        Check(motion);
        if (!HasMotion(structure))
            throw new ArgumentException("No atom of the structure has a displacement tensor to move with.", nameof(structure));
        bool light = lowDetail && sceneOptions.Style is CrystalStyle.ThermalEllipsoids or CrystalStyle.Polyhedra or CrystalStyle.PolyhedraWithEllipsoids;
        int n = motion.FramesPerPeriod;
        SceneSequence.CheckWeight(builder.Estimate(structure, sceneOptions, appearance).Atoms * n * (light ? 2L : 1L), maxWeight);

        Vector3[][] offsets = Offsets(structure, motion);
        var scenes = new Scene[n];
        Scene?[]? lighter = light ? new Scene?[n] : null;
        long weight = 0;
        for (int f = 0; f < n; f++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CrystalSceneOptions frame = sceneOptions with { AtomOffsets = offsets[f] };
            scenes[f] = builder.Build(structure, frame, appearance).Scene;
            weight += SceneSequence.WeightOf(scenes[f]);
            if (lighter is not null)
            {
                lighter[f] = builder.Build(structure, frame with { PrincipalEllipses = false, PolyhedronBackFaces = false }, appearance).Scene;
                weight += SceneSequence.WeightOf(lighter[f]);
            }
            SceneSequence.CheckWeight(weight, maxWeight);
        }
        return new SceneSequence(scenes, motion.Period / n, SequenceRepeat.Loop, lighter, maxWeight);
    }

    // The six orders of the three functions of the loop.
    static readonly int[][] Orders = [[0, 1, 2], [0, 2, 1], [1, 0, 2], [1, 2, 0], [2, 0, 1], [2, 1, 0]];

    static void Check(ThermalMotionOptions options)
    {
        if (!(options.Amplitude > 0 && options.Amplitude <= 3))
            throw new ArgumentOutOfRangeException(nameof(options), options.Amplitude, "The amplitude must be greater than 0 and at most 3.");
        if (options.FramesPerPeriod is < 8 or > 120)
            throw new ArgumentOutOfRangeException(nameof(options), options.FramesPerPeriod, "A loop has from 8 to 120 frames.");
        if (options.Period <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.Period, "A loop lasts a positive time.");
    }

    /// <summary>The three axes sqrt(2 λᵢ) eᵢ of the atom's first tensor, in double; null when the atom stays at rest.</summary>
    static (double X, double Y, double Z)[]? Axes(CrystalAtom atom)
    {
        if (atom.IsVoid || atom.Displacements is null)
            return null;
        foreach (AtomDisplacement d in atom.Displacements)
        {
            if (d.Cartesian6 is not { Length: 6 } u || u.Any(v => !double.IsFinite(v)))
                continue;
            double[,] m =
            {
                { u[0], u[3], u[4] },
                { u[3], u[1], u[5] },
                { u[4], u[5], u[2] },
            };
            (double[] values, double[,] vectors) = SymmetricEigen3.Solve(m);
            if (values[0] <= 0)
                return null;
            var axes = new (double X, double Y, double Z)[3];
            for (int k = 0; k < 3; k++)
            {
                double length = Math.Sqrt(2 * values[k]);
                axes[k] = (vectors[0, k] * length, vectors[1, k] * length, vectors[2, k] * length);
            }
            return axes;
        }
        return null;
    }
}
