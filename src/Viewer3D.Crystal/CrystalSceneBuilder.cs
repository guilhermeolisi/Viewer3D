using System.Collections.Immutable;
using System.Numerics;

namespace Viewer3D.Crystal;

/// <summary>
/// Turns a <see cref="CrystalStructure"/> into a <see cref="CrystalScene"/>. Pure and stateless: the same input gives
/// the same scene, on any thread.
/// </summary>
/// <remarks>
/// <para>
/// Positions are reduced to [0, 1) first, and a bond keeps its length: the cell translation that the reduction takes
/// off each end is put back into the bond. The block is <c>RepeatA x RepeatB x RepeatC</c> cells. Each atom has one
/// image per cell of the block, and those images carry the labels. With <see cref="CrystalSceneOptions.ShowBoundaryAtoms"/>
/// the images on the far faces of the block (coordinate within 1e-4 of the block size) are drawn too.
/// </para>
/// <para>
/// A bond is drawn between two drawn images, once per pair. From an image of the block to a neighbor outside it, the
/// neighbor is drawn faded (<see cref="CrystalSceneOptions.CompleteCoordination"/>) or the bond is left out. The
/// cell edges are lines, each shared edge drawn once; the axes are not drawn in the scene, only returned for the axis
/// indicator.
/// </para>
/// <para>
/// In space filling the spheres are scaled by s = min over the bonds of d / (r1 + r2), the scale at which the closest
/// bonded pair just touches. Without bonds, s comes from the shortest distance between two atoms of one cell (with
/// their neighbors in the 26 cells around), so it never depends on the repetition. <see cref="CrystalPacking"/> gives the
/// same scale and the fraction of the cell the spheres fill.
/// </para>
/// <para>
/// In thermal ellipsoids each atom is drawn from its displacement tensors, the same for every image, as each image is
/// a translation of the atom: one shape per tensor, with semi-axes f·sqrt(λᵢ) along the eigenvectors, f from
/// <see cref="ProbabilityScale"/>. The rods are 0.4 of <see cref="CrystalSceneOptions.BondRadius"/> and are cut where
/// they leave the shapes at their ends.
/// </para>
/// <para>
/// In the polyhedra styles each polyhedron of <see cref="CrystalStructure.Polyhedra"/> is drawn around every drawn image
/// of its center (block and far faces), with its vertices at the images the bond rule gives, so a polyhedron around a
/// center on the far face of the block is whole: its vertices outside the block are faded spheres
/// (<see cref="CrystalSceneOptions.CompleteCoordination"/>). Each copy is one translucent mesh, its faces fanned into
/// triangles in the given turn and its edges the sides of the faces. Rods are drawn as in ball and stick, the ones from
/// a center to its vertices only with <see cref="CrystalSceneOptions.ShowBondsInsidePolyhedra"/>.
/// </para>
/// <para>
/// A void site (<see cref="CrystalAtom.IsVoid"/>) is drawn in every style as one translucent sphere of
/// <see cref="CrystalSceneOptions.VoidColor"/>: the marker, <see cref="CrystalSceneOptions.VoidMarkerRadius"/> without the
/// style scale, or its own radius with <see cref="CrystalSceneOptions.ShowVoidSpheres"/>. It has no displacement shapes,
/// no sectors, no place in the legends and no weight in the space-filling scale, and a polyhedron may be centered on it.
/// </para>
/// <para>
/// Spheres are shaded by the viewer with a light from the upper left. The polyhedra use
/// <see cref="MeshLighting.KeyLight"/>, whose light comes from the same side.
/// </para>
/// </remarks>
public sealed class CrystalSceneBuilder
{
    /// <summary>A position this close to the far face of the block counts as on it.</summary>
    public const double BoundaryTolerance = 1e-4;

    static readonly Rgba UnknownColor = new(128, 128, 128);
    static readonly Rgba FadeTarget = new(200, 200, 200);
    static readonly Rgba OccupancyLow = new(40, 80, 220);
    static readonly Rgba OccupancyHigh = new(220, 40, 40);
    const byte FadedAlpha = 110;

    /// <summary>Builds the scene of <paramref name="structure"/>.</summary>
    public CrystalScene Build(CrystalStructure structure, CrystalSceneOptions options) => Build(structure, options, null);

    /// <summary>
    /// Builds the scene of <paramref name="structure"/> with <paramref name="appearance"/>; null is
    /// <see cref="CrystalAppearance.Default"/>, the scene of the style alone.
    /// </summary>
    /// <remarks>
    /// The polyhedra are found before any atom is hidden, so hiding a center or a vertex leaves the polyhedron whole. A
    /// hidden atom takes its label and rods with it, and is counted in <see cref="CrystalScene.HiddenAtomCount"/> when it
    /// is an image of the block or of its far faces. In the thermal styles the radius scale is ignored.
    /// </remarks>
    public CrystalScene Build(CrystalStructure structure, CrystalSceneOptions options, CrystalAppearance? appearance)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);
        CellBasis basis = structure.Cell.Basis();
        if (!basis.Valid)
            return InvalidCell(structure);

        var context = new Context(structure, options, basis, appearance ?? CrystalAppearance.Default);
        int ceiling = CeilingOf(options);
        if (options.Clip == CrystalClip.Sphere)
            return BuildSphere(options, context, ceiling);
        LatticeShift n = RepeatOf(options);
        if (n == One)
            return context.Build(n, null);
        // Counted before building, so a block far past the ceiling is never built. The count leaves out every atom the
        // appearance may hide, so it never passes the cost of the scene built: a scene that fits is built as before.
        (long atoms, long polyhedra) = context.CountBlock(n);
        if (CostOf(atoms, polyhedra) > ceiling)
            return context.Build(One, BlockWarning(n, atoms, polyhedra, ceiling));
        CrystalScene scene = context.Build(n, null);
        if (CostOf(scene) > ceiling)
            scene = context.Build(One, BlockWarning(n, scene.AtomCount, scene.PolyhedronCount, ceiling));
        return scene;
    }

    /// <summary>
    /// The cost of the scene of <paramref name="structure"/>, counted without building it, against
    /// <see cref="CrystalSceneOptions.MaxCost"/>, and the repetition or radius that fits (<see cref="CrystalSceneEstimate"/>).
    /// A sphere whose expected count (atoms per cell times the sphere volume over the cell volume) is far past the ceiling
    /// is counted by that expectation alone, with no polyhedra.
    /// </summary>
    public CrystalSceneEstimate Estimate(CrystalStructure structure, CrystalSceneOptions options, CrystalAppearance? appearance = null)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);
        CellBasis basis = structure.Cell.Basis();
        int ceiling = CeilingOf(options);
        bool sphere = options.Clip == CrystalClip.Sphere;
        if (!basis.Valid)
            return new CrystalSceneEstimate(0, 0, 0, ceiling, sphere ? One : RepeatOf(options), sphere ? SphereOf(options).Radius : double.NaN);
        return EstimateWith(new Context(structure, options, basis, appearance ?? CrystalAppearance.Default), options, ceiling);
    }

    /// <summary>
    /// Builds the scene of <paramref name="structure"/> only when it fits <see cref="CrystalSceneOptions.MaxCost"/>, with
    /// no fallback to one cell: false, with <paramref name="scene"/> null, when the <paramref name="estimate"/> is past the
    /// ceiling, or when the scene built is (its faded neighbors count), and then the estimate carries the cost of the
    /// scene built and a repetition or radius one step below the one asked.
    /// </summary>
    public bool TryBuild(CrystalStructure structure, CrystalSceneOptions options, CrystalAppearance? appearance,
        out CrystalScene? scene, out CrystalSceneEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);
        scene = null;
        CellBasis basis = structure.Cell.Basis();
        int ceiling = CeilingOf(options);
        bool sphere = options.Clip == CrystalClip.Sphere;
        if (!basis.Valid)
        {
            estimate = new CrystalSceneEstimate(0, 0, 0, ceiling, sphere ? One : RepeatOf(options), sphere ? SphereOf(options).Radius : double.NaN);
            scene = InvalidCell(structure);
            return true;
        }
        var context = new Context(structure, options, basis, appearance ?? CrystalAppearance.Default);
        estimate = EstimateWith(context, options, ceiling);
        if (!estimate.Fit)
            return false;
        CrystalScene built = sphere ? context.Build(One, null, SphereOf(options)) : context.Build(RepeatOf(options), null);
        int cost = CostOf(built);
        if (cost > ceiling)
        {
            double radius = SphereOf(options).Radius;
            estimate = estimate with
            {
                Atoms = built.AtomCount,
                Polyhedra = built.PolyhedronCount,
                Cost = cost,
                Fits = sphere ? One : Shrink(RepeatOf(options)),
                FitsRadius = sphere ? Math.Max(0, Math.Ceiling(2 * radius) / 2 - 0.5) : double.NaN,
            };
            return false;
        }
        scene = built;
        return true;
    }

    static readonly LatticeShift One = new(1, 1, 1);

    static CrystalScene InvalidCell(CrystalStructure structure)
        => new(Scene.Empty, structure.Cell, [], 0, [], 0, "The cell is not valid.", new Dictionary<int, CrystalItemInfo>());

    static int CeilingOf(CrystalSceneOptions options) => options.MaxCost >= 1 ? options.MaxCost : CrystalSceneOptions.MaxAtoms;

    static LatticeShift RepeatOf(CrystalSceneOptions options)
        => new(Math.Max(1, options.RepeatA), Math.Max(1, options.RepeatB), Math.Max(1, options.RepeatC));

    /// <summary>The sphere of the options, with a center that is not finite at 0.5 and a radius that is not positive at 6.</summary>
    static SphereClip SphereOf(CrystalSceneOptions options)
    {
        FractionalPoint c = options.SphereCenter;
        double radius = double.IsFinite(options.SphereRadius) && options.SphereRadius > 0 ? options.SphereRadius : 6;
        return new SphereClip(new FractionalPoint(Finite(c.X), Finite(c.Y), Finite(c.Z)), radius);

        static double Finite(double v) => double.IsFinite(v) ? v : 0.5;
    }

    static int CostOf(long atoms, long polyhedra)
        => (int)Math.Min(int.MaxValue, atoms + (long)CrystalSceneOptions.PolyhedronCost * polyhedra);

    static int CostOf(CrystalScene scene) => CostOf(scene.AtomCount, scene.PolyhedronCount);

    /// <summary>The repetition with its largest axis one cell shorter (a before b before c on a tie); 1 x 1 x 1 stays.</summary>
    static LatticeShift Shrink(LatticeShift n)
    {
        int max = Math.Max(n.A, Math.Max(n.B, n.C));
        if (max <= 1)
            return n;
        return n.A == max ? n with { A = n.A - 1 } : n.B == max ? n with { B = n.B - 1 } : n with { C = n.C - 1 };
    }

    static CrystalSceneEstimate EstimateWith(Context context, CrystalSceneOptions options, int ceiling)
    {
        if (options.Clip == CrystalClip.Sphere)
        {
            SphereClip clip = SphereOf(options);
            (long atoms, long polyhedra) = context.CountSphere(clip.Center, clip.Radius, ceiling);
            int cost = CostOf(atoms, polyhedra);
            double fits = clip.Radius;
            if (cost > ceiling)
            {
                fits = 0;
                for (double r = Math.Ceiling(2 * clip.Radius) / 2 - 0.5; r >= 0.5; r -= 0.5)
                {
                    (long a, long p) = context.CountSphere(clip.Center, r, ceiling);
                    if (CostOf(a, p) <= ceiling)
                    {
                        fits = r;
                        break;
                    }
                }
            }
            return new CrystalSceneEstimate((int)Math.Min(int.MaxValue, atoms), (int)Math.Min(int.MaxValue, polyhedra), cost,
                ceiling, One, fits);
        }
        LatticeShift n = RepeatOf(options);
        (long blockAtoms, long blockPolyhedra) = context.CountBlock(n);
        LatticeShift fit = n;
        (long fa, long fp) = (blockAtoms, blockPolyhedra);
        while (CostOf(fa, fp) > ceiling && fit != One)
        {
            fit = Shrink(fit);
            (fa, fp) = context.CountBlock(fit);
        }
        return new CrystalSceneEstimate((int)Math.Min(int.MaxValue, blockAtoms), (int)Math.Min(int.MaxValue, blockPolyhedra),
            CostOf(blockAtoms, blockPolyhedra), ceiling, fit, double.NaN);
    }

    static string BlockWarning(LatticeShift n, long atoms, long polyhedra, int ceiling)
        => polyhedra == 0
            ? $"{n.A} x {n.B} x {n.C} cells would draw {atoms} atoms, more than {ceiling}; showing one cell."
            : $"{n.A} x {n.B} x {n.C} cells would draw {atoms} atoms and {polyhedra} polyhedra, more than the {ceiling} "
                + $"atoms allowed (a polyhedron counts as {CrystalSceneOptions.PolyhedronCost}); showing one cell.";

    static string SphereWarning(double radius, long atoms, long polyhedra, int ceiling)
        => polyhedra == 0
            ? $"A sphere of radius {radius:0.##} Å would draw {atoms} atoms, more than {ceiling}; showing one cell."
            : $"A sphere of radius {radius:0.##} Å would draw {atoms} atoms and {polyhedra} polyhedra, more than the {ceiling} "
                + $"atoms allowed (a polyhedron counts as {CrystalSceneOptions.PolyhedronCost}); showing one cell.";

    /// <summary>
    /// The scene of <see cref="CrystalClip.Sphere"/>; past the ceiling, one cell with a warning. A sphere whose expected
    /// count (atoms per cell times the sphere volume over the cell volume) is far past the ceiling is not counted nor built.
    /// </summary>
    static CrystalScene BuildSphere(CrystalSceneOptions options, Context context, int ceiling)
    {
        SphereClip clip = SphereOf(options);
        double expected = context.ExpectedInSphere(clip.Radius);
        if (expected > 4.0 * ceiling)
        {
            return context.Build(One, $"A sphere of radius {clip.Radius:0.##} Å would draw about {expected:0} atoms, more than "
                + $"{ceiling}; showing one cell.");
        }
        (long atoms, long polyhedra) = context.CountSphere(clip.Center, clip.Radius, ceiling);
        if (CostOf(atoms, polyhedra) > ceiling)
            return context.Build(One, SphereWarning(clip.Radius, atoms, polyhedra, ceiling));
        CrystalScene scene = context.Build(One, null, clip);
        if (CostOf(scene) <= ceiling)
            return scene;
        return context.Build(One, SphereWarning(clip.Radius, scene.AtomCount, scene.PolyhedronCount, ceiling));
    }

    /// <summary>
    /// The cell translations along one axis that can bring an atom of [0, 1) within <paramref name="radius"/> of
    /// <paramref name="center"/>: the sphere reaches radius times the reciprocal length in fractional units along the
    /// axis (radius over the plane spacing). An image coordinate r + t, with r in [0, 1), lies in
    /// [center - reach, center + reach] only for t from ceil(center - reach) - 1 to floor(center + reach).
    /// </summary>
    internal static (int Min, int Max) SphereSpan(double center, double radius, double reciprocalLength)
    {
        double reach = radius * reciprocalLength;
        return ((int)Math.Ceiling(center - reach) - 1, (int)Math.Floor(center + reach));
    }

    /// <summary>The sphere of <see cref="CrystalClip.Sphere"/>, already checked.</summary>
    internal readonly record struct SphereClip(FractionalPoint Center, double Radius);

    /// <summary>A bond this much longer than a length limit, in angstrom, still passes it.</summary>
    const double LengthTolerance = 1e-4;

    /// <summary>An image this far outside the sphere, in angstrom, still counts as inside.</summary>
    const double SphereTolerance = 1e-4;

    /// <summary>An atom reduced to [0, 1): its position and the translation the reduction took off.</summary>
    internal readonly record struct Reduced(double X, double Y, double Z, LatticeShift Floor);

    /// <summary>The element with the largest share of the site; the atom's own element without shares.</summary>
    internal static string MainElement(CrystalAtom atom)
    {
        if (atom.Shares is not { Count: > 0 } shares)
            return atom.Element;
        ElementShare main = shares[0];
        foreach (ElementShare share in shares)
        {
            if (share.Fraction > main.Fraction)
                main = share;
        }
        return main.Element;
    }

    internal static Reduced Reduce(double x, double y, double z)
    {
        (double rx, int fx) = Wrap(x);
        (double ry, int fy) = Wrap(y);
        (double rz, int fz) = Wrap(z);
        return new Reduced(rx, ry, rz, new LatticeShift(fx, fy, fz));
    }

    /// <summary>The coordinate in [0, 1) and the whole cells taken off; a value within 1e-6 below 1 wraps to 0.</summary>
    static (double, int) Wrap(double v)
    {
        if (!double.IsFinite(v))
            return (0, 0);
        double f = Math.Floor(v);
        double r = v - f;
        if (r > 1 - 1e-6)
        {
            r = 0;
            f += 1;
        }
        return (r, (int)f);
    }

    sealed class Context
    {
        readonly CrystalStructure structure;
        readonly CrystalSceneOptions options;
        readonly CellBasis basis;
        readonly Reduced[] reduced;
        readonly double[] radius;
        readonly Rgba[] color;
        readonly IReadOnlyList<SphereSector>?[] sectors;
        readonly DisplacementShape[]?[] shapes;
        readonly bool thermal;
        readonly bool polyhedra;
        readonly double scale;
        readonly CrystalAppearance app;
        readonly AtomStyle[] atomStyle;
        readonly Rgba?[] elementRuleColor;
        readonly PolyhedronStyle[] polyhedronStyle;
        readonly bool limits;
        double[]? bondLength;

        public Context(CrystalStructure structure, CrystalSceneOptions options, CellBasis basis, CrystalAppearance app)
        {
            this.structure = structure;
            this.options = options;
            this.basis = basis;
            this.app = app;
            IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
            reduced = new Reduced[atoms.Count];
            radius = new double[atoms.Count];
            color = new Rgba[atoms.Count];
            sectors = new IReadOnlyList<SphereSector>?[atoms.Count];
            atomStyle = new AtomStyle[atoms.Count];
            elementRuleColor = new Rgba?[atoms.Count];
            Dictionary<int, int> siteOrder = SiteOrder(atoms);
            for (int i = 0; i < atoms.Count; i++)
            {
                CrystalAtom atom = atoms[i];
                reduced[i] = Reduce(atom.X, atom.Y, atom.Z);
                string main = MainElement(atom);
                radius[i] = CrystalPacking.RadiusOf(atom);
                atomStyle[i] = app.ResolveAtom(structure, i);
                if (atom.IsVoid)
                {
                    // A void: one color in every scheme, no sectors, no element rule.
                    color[i] = options.VoidColor;
                    continue;
                }
                elementRuleColor[i] = app.ByElement.TryGetValue(ElementTable.Normalize(main), out AtomStyle? e) ? e.Color : null;
                switch (app.ColorScheme)
                {
                    case AtomColorScheme.Site:
                        color[i] = SitePalette.Color(siteOrder[i]);
                        break;
                    case AtomColorScheme.Occupancy:
                        color[i] = OccupancyColor(atom.Occupancy);
                        break;
                    case AtomColorScheme.Group when (uint)atom.Group < (uint)structure.Groups.Count:
                        color[i] = structure.Groups[atom.Group].Color;
                        break;
                    default:
                        color[i] = ColorOf(main);
                        sectors[i] = SectorsOf(atom);
                        break;
                }
            }
            polyhedronStyle = new PolyhedronStyle[structure.Polyhedra.Count];
            for (int k = 0; k < polyhedronStyle.Length; k++)
                polyhedronStyle[k] = app.ResolvePolyhedron(structure, k);
            limits = HasLengthLimits(structure, options, app);
            thermal = options.Style is CrystalStyle.ThermalEllipsoids or CrystalStyle.PolyhedraWithEllipsoids;
            polyhedra = options.Style is CrystalStyle.Polyhedra or CrystalStyle.PolyhedraWithEllipsoids;
            shapes = new DisplacementShape[]?[atoms.Count];
            if (thermal)
            {
                int level = ProbabilityScale.Levels.Contains(options.ProbabilityLevel) ? options.ProbabilityLevel : 50;
                scale = ProbabilityScale.For(level);
                for (int i = 0; i < atoms.Count; i++)
                    shapes[i] = atoms[i].IsVoid ? null : ShapesOf(atoms[i], i);
            }
            else
            {
                scale = options.Style == CrystalStyle.SpaceFilling
                    ? CrystalPacking.TouchingScale(structure, basis, reduced, radius)
                    : options.BallScale;
            }
        }

        /// <summary>
        /// One shape per displacement of the atom, each in the color of its element; no displacement at all gives one
        /// marker of no data.
        /// </summary>
        DisplacementShape[] ShapesOf(CrystalAtom atom, int index)
        {
            if (atom.Displacements is not { Count: > 0 } list)
                return [DisplacementShape.From(null, scale, options.MarkerSize, color[index], null)];
            var result = new DisplacementShape[list.Count];
            for (int k = 0; k < list.Count; k++)
            {
                AtomDisplacement d = list[k];
                Rgba c = list.Count > 1 && d.Element is string element && app.ColorScheme == AtomColorScheme.Element
                    ? ColorOf(element)
                    : color[index];
                result[k] = DisplacementShape.From(d.Cartesian6, scale, options.MarkerSize, c, d.Element);
            }
            return result;
        }

        /// <summary>The style of image <paramref name="t"/> of the atom (a block image, not an offset).</summary>
        AtomStyle Style(int atom, LatticeShift t)
            => app.ByImage.Count == 0 ? atomStyle[atom] : app.ResolveAtom(structure, atom, t - reduced[atom].Floor);

        /// <summary>
        /// The color a rule gives the atom over its scheme color; null when none does, or when the color is the one of
        /// the element rule in the element scheme, which already took it with the sectors.
        /// </summary>
        Rgba? OverrideColor(int atom, AtomStyle style)
        {
            if (style.Color is not { } c)
                return null;
            if (app.ColorScheme == AtomColorScheme.Element && elementRuleColor[atom] == c)
                return null;
            return c;
        }

        static Rgba WithOpacity(Rgba c, double? opacity)
            => opacity is { } o && double.IsFinite(o) ? c with { A = (byte)Math.Round(c.A * Math.Clamp(o, 0, 1)) } : c;

        /// <summary>The color of the image, without fading: the rule color or the scheme color, with the rule opacity.</summary>
        Rgba AtomColor(int atom, AtomStyle style) => WithOpacity(OverrideColor(atom, style) ?? color[atom], style.Opacity);

        double RadiusScale(AtomStyle style)
            => !thermal && style.RadiusScale is { } f && double.IsFinite(f) && f > 0 ? f : 1;

        /// <summary>Where a bond from the atom along <paramref name="direction"/> leaves its shapes.</summary>
        float Reach(int atom, LatticeShift t, Vector3 direction)
        {
            if (structure.Atoms[atom].IsVoid)
                return (float)VoidRadius(atom);
            if (!thermal)
                return (float)(scale * radius[atom] * RadiusScale(Style(atom, t)));
            float reach = 0;
            foreach (DisplacementShape shape in shapes[atom]!)
                reach = MathF.Max(reach, shape.Reach(direction));
            return reach;
        }

        /// <summary>
        /// The drawn radius of a void site, in angstrom, the same in every style: its own radius with
        /// <see cref="CrystalSceneOptions.ShowVoidSpheres"/>, else the marker.
        /// </summary>
        double VoidRadius(int atom)
        {
            if (options.ShowVoidSpheres && structure.Atoms[atom].Radius is double r && double.IsFinite(r) && r > 0)
                return r;
            double marker = options.VoidMarkerRadius;
            return double.IsFinite(marker) && marker > 0 ? marker : 0.1;
        }

        /// <summary>The color of a void image: the rule color or the void color, with the void opacity, then faded.</summary>
        Rgba VoidLook(int atom, AtomStyle style, bool faded)
        {
            double opacity = double.IsFinite(options.VoidOpacity) ? Math.Clamp(options.VoidOpacity, 0, 1) : 0.5;
            Rgba c = WithOpacity(style.Color ?? color[atom], opacity);
            return Look(c, faded, style.Opacity);
        }

        /// <summary>
        /// The scene of the block of <paramref name="n"/> cells or, with <paramref name="sphere"/>, of the images inside
        /// the sphere: all of them count as the block's own, with no far faces, no completion of a bond (a bond with an
        /// end outside is left out) and no cell edges. A vertex outside the sphere of a polyhedron drawn around a center
        /// inside is still a faded sphere, so the polyhedron is whole.
        /// </summary>
        public CrystalScene Build(LatticeShift n, string? warning, SphereClip? sphere = null)
        {
            IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
            var items = new List<SceneItem>();
            var lookup = new Dictionary<int, CrystalItemInfo>();
            var drawn = new Dictionary<(int Atom, LatticeShift Image), Vector3>();
            var own = new List<(int Atom, LatticeShift Image)>();
            var boundary = new List<(int Atom, LatticeShift Image)>();
            int nextId = 1;

            if (sphere is { } clip)
                SphereImages(clip, own);
            else
                BlockImages(n, own, boundary);

            // Polyhedra: one copy per drawn image of the center. Without the vertices, an image that is a vertex and not a
            // center is left out with its label and rods; without the inner bonds, each copy's center-vertex rod is.
            List<PolyhedronInstance> instances = polyhedra ? Instances(own, boundary) : [];
            var hidden = new HashSet<(int, LatticeShift)>();
            var inside = new HashSet<((int, LatticeShift), (int, LatticeShift))>();
            var centers = new HashSet<(int, LatticeShift)>(instances.Select(p => (p.Center, p.Image)));

            // Aparencia: a imagem escondida sai com rotulo e bastoes, e o poliedro em volta dela ou que passa por ela
            // fica, porque os poliedros ja foram montados acima. Vale nos quatro lugares onde uma imagem entra.
            bool IsHidden(int atom, LatticeShift t)
                => !app.ShowAtoms || Style(atom, t).Visible == false
                    || (!app.ShowPolyhedronCenters && centers.Contains((atom, t)));
            var shownAtoms = new HashSet<int>(centers.Select(c => c.Item1));
            int hiddenCount = 0;
            foreach ((int atom, LatticeShift t) in own.Concat(boundary))
            {
                if (IsHidden(atom, t))
                    hiddenCount++;
                else
                    shownAtoms.Add(atom);
            }
            if (hiddenCount > 0)
            {
                own.RemoveAll(x => IsHidden(x.Atom, x.Image));
                boundary.RemoveAll(x => IsHidden(x.Atom, x.Image));
            }

            if (instances.Count > 0)
            {
                foreach (PolyhedronInstance p in instances)
                {
                    foreach ((int, LatticeShift) vertex in p.Vertices)
                    {
                        if (!options.ShowPolyhedronVertices && !centers.Contains(vertex))
                            hidden.Add(vertex);
                        if (!options.ShowBondsInsidePolyhedra)
                            inside.Add(Order((p.Center, p.Image), vertex));
                    }
                }
                own.RemoveAll(hidden.Contains);
                boundary.RemoveAll(hidden.Contains);
            }

            void AddSphere(int atom, LatticeShift t, bool faded)
            {
                Vector3 p = Position(atom, t);
                drawn[(atom, t)] = p;
                AtomStyle style = Style(atom, t);
                if (structure.Atoms[atom].IsVoid)
                {
                    int voidId = nextId++;
                    items.Add(new SphereItem
                    {
                        Id = voidId,
                        Center = p,
                        Radius = (float)VoidRadius(atom),
                        Color = VoidLook(atom, style, faded),
                    });
                    lookup[voidId] = new CrystalItemInfo(CrystalItemKind.Atom, atom, -1, t - reduced[atom].Floor, faded);
                    return;
                }
                Rgba? rule = OverrideColor(atom, style);
                IReadOnlyList<SphereSector>? s = rule is null ? sectors[atom] : null;
                if (s is not null && (faded || style.Opacity is not null))
                    s = [.. s.Select(x => x with { Color = Look(x.Color, faded, style.Opacity) })];
                if (thermal)
                {
                    AddShapes(atom, t, p, faded, s, rule, style.Opacity);
                    return;
                }
                int id = nextId++;
                Rgba baseColor = Look(rule ?? color[atom], faded, style.Opacity);
                items.Add(new SphereItem
                {
                    Id = id,
                    Center = p,
                    Radius = (float)(scale * radius[atom] * RadiusScale(style)),
                    Color = baseColor,
                    Sectors = s,
                    Halo = style.Halo,
                });
                lookup[id] = new CrystalItemInfo(CrystalItemKind.Atom, atom, -1, t - reduced[atom].Floor, faded);
            }

            // Thermal style: one item per displacement of the atom, all pointing to the same atom in the selection. The
            // occupancy sectors apply only with a single displacement; with several, each one already has its element color.
            // A rule color and opacity apply to every shape.
            void AddShapes(int atom, LatticeShift t, Vector3 p, bool faded, IReadOnlyList<SphereSector>? s, Rgba? rule, double? opacity)
            {
                DisplacementShape[] list = shapes[atom]!;
                IReadOnlyList<SphereSector>? shapeSectors = list.Length == 1 ? s : null;
                foreach (DisplacementShape shape in list)
                {
                    int id = nextId++;
                    Rgba c = Look(rule ?? shape.Color, faded, opacity);
                    SceneItem item = shape.Kind switch
                    {
                        DisplacementShapeKind.Ellipsoid => new EllipsoidItem
                        {
                            Id = id,
                            Center = p,
                            Axes = shape.Axes!,
                            Color = c,
                            Sectors = shapeSectors,
                            PrincipalEllipses = options.PrincipalEllipses,
                        },
                        DisplacementShapeKind.Cube => MeshShapes.Cube(p, shape.Radius, c, id),
                        DisplacementShapeKind.Missing => new SphereItem { Id = id, Center = p, Radius = shape.Radius, Color = c, Dashed = true },
                        _ => new SphereItem { Id = id, Center = p, Radius = shape.Radius, Color = c, Sectors = shapeSectors },
                    };
                    items.Add(item);
                    lookup[id] = new CrystalItemInfo(CrystalItemKind.Atom, atom, -1, t - reduced[atom].Floor, faded);
                }
            }

            foreach ((int atom, LatticeShift t) in own)
                AddSphere(atom, t, false);
            foreach ((int atom, LatticeShift t) in boundary)
                AddSphere(atom, t, false);

            // A vertex outside the block, of any copy (around a block center or a far-face one): a faded sphere.
            var completions = new HashSet<(int, LatticeShift)>();
            if (options.CompleteCoordination && options.ShowPolyhedronVertices)
            {
                foreach (PolyhedronInstance p in instances)
                {
                    foreach ((int atom, LatticeShift u) in p.Vertices)
                    {
                        if (!drawn.ContainsKey((atom, u)) && !IsHidden(atom, u) && completions.Add((atom, u)))
                            AddSphere(atom, u, true);
                    }
                }
            }

            // Ligacoes: de cada imagem desenhada, nos dois sentidos de cada ligacao dada. Vizinho fora do bloco so a
            // partir de imagem propria, e entao com esfera esmaecida ou sem a ligacao.
            var ownSet = new HashSet<(int, LatticeShift)>(own);
            var pairs = new HashSet<((int, LatticeShift), (int, LatticeShift))>();
            var rods = new List<(int Bond, int First, LatticeShift FirstImage, int Second, LatticeShift SecondImage)>();
            var origins = new List<(int Atom, LatticeShift Image)>(own.Count + boundary.Count);
            origins.AddRange(own);
            origins.AddRange(boundary);
            var bondsByAtom = BondsByAtom();
            bool removed = app.RemovedBonds.Count > 0;
            foreach ((int atom, LatticeShift t) in origins)
            {
                if (!bondsByAtom.TryGetValue(atom, out List<(int Bond, int Other, LatticeShift Shift)>? list))
                    continue;
                foreach ((int bond, int other, LatticeShift shift) in list)
                {
                    LatticeShift u = t + shift;
                    if (hidden.Contains((other, u)) || IsHidden(other, u))
                        continue;
                    // A removed bond: skipped before the pair and the faded sphere, so neither is kept for it.
                    if (removed && structure.Bonds[bond].SourceIndex is int source && source >= 0 && app.RemovedBonds.Contains(source))
                        continue;
                    if (limits && !BondPasses(bond, atom, t, other, u))
                        continue;
                    bool partnerDrawn = drawn.ContainsKey((other, u));
                    if (!partnerDrawn)
                    {
                        if (sphere is not null || !options.CompleteCoordination || !ownSet.Contains((atom, t)))
                            continue;
                        completions.Add((other, u));
                    }
                    var key = Order((atom, t), (other, u));
                    if (pairs.Add(key))
                        rods.Add((bond, atom, t, other, u));
                }
            }
            foreach ((int atom, LatticeShift t) in completions)
            {
                if (!drawn.ContainsKey((atom, t)))
                    AddSphere(atom, t, true);
            }

            if (options.Style is not CrystalStyle.SpaceFilling && app.ShowBonds)
            {
                float rodRadius = (float)(thermal ? 0.4 * options.BondRadius : options.BondRadius);
                foreach ((int bond, int first, LatticeShift ft, int second, LatticeShift st) in rods)
                {
                    if (inside.Count > 0 && inside.Contains(Order((first, ft), (second, st))))
                        continue;
                    BondStyle bondStyle = app.BondDefault == BondStyle.Empty && app.ByElementPair.Count == 0 && app.ByBond.Count == 0
                        ? BondStyle.Empty
                        : app.ResolveBond(structure, bond);
                    if (bondStyle.Visible == false)
                        continue;
                    bool firstFaded = completions.Contains((first, ft)), secondFaded = completions.Contains((second, st));
                    bool faded = firstFaded || secondFaded;
                    int id = nextId++;
                    Vector3 start = drawn[(first, ft)], end = drawn[(second, st)];
                    Rgba startColor = bondStyle.Color ?? (OverrideColor(first, Style(first, ft)) ?? color[first]);
                    Rgba endColor = bondStyle.Color ?? (OverrideColor(second, Style(second, st)) ?? color[second]);
                    items.Add(new RodItem
                    {
                        Id = id,
                        Start = start,
                        End = end,
                        Radius = bondStyle.Radius is { } r && double.IsFinite(r) && r > 0 ? (float)r : rodRadius,
                        StartColor = Look(startColor, firstFaded, bondStyle.Opacity),
                        EndColor = Look(endColor, secondFaded, bondStyle.Opacity),
                        StartTrim = Reach(first, ft, end - start),
                        EndTrim = Reach(second, st, start - end),
                    });
                    lookup[id] = new CrystalItemInfo(CrystalItemKind.Bond, first, bond, ft - reduced[first].Floor, faded);
                }
            }

            foreach (PolyhedronInstance p in instances)
            {
                int id = nextId++;
                items.Add(Mesh(p, id));
                lookup[id] = new CrystalItemInfo(CrystalItemKind.Polyhedron, p.Center, -1, p.Image - reduced[p.Center].Floor, false)
                {
                    PolyhedronIndex = p.Index,
                };
            }

            if (app.ShowCell && sphere is null)
                AddCellEdges(items, n);

            if (app.ShowAnnotations)
            {
                IReadOnlyList<SceneItem> annotations = structure.Annotations;
                for (int k = 0; k < annotations.Count; k++)
                {
                    SceneItem item = annotations[k];
                    items.Add(item);
                    // The owner gives the Id, negative; several items with one Id count as the first annotation with it.
                    lookup.TryAdd(item.Id, new CrystalItemInfo(CrystalItemKind.Annotation, -1, -1, LatticeShift.Zero, false)
                    {
                        AnnotationIndex = k,
                    });
                }
            }

            if (options.IncludeLabels)
            {
                foreach ((int atom, LatticeShift t) in own)
                {
                    AtomStyle style = Style(atom, t);
                    if (style.ShowLabel == false)
                        continue;
                    items.Add(new LabelItem
                    {
                        Position = drawn[(atom, t)],
                        Text = LabelOf(atom, style.LabelText ?? AtomLabelText.Label),
                        Offset = new Vector2(4, -6),
                        // 12 e o tamanho padrao do LabelItem.
                        Size = app.LabelSize is { } size && double.IsFinite(size) && size > 0 ? (float)size : 12f,
                    });
                }
            }

            var axes = new List<AxisDirection>
            {
                new(structure.Cell.AVector, "a"),
                new(structure.Cell.BVector, "b"),
                new(structure.Cell.CVector, "c"),
            };
            List<ElementLegendEntry> legend = Legend(hiddenCount == 0 ? null : shownAtoms);
            return new CrystalScene(new Scene(items), structure.Cell, axes, scale, legend, drawn.Count, warning, lookup,
                instances.Count)
            {
                HiddenAtomCount = hiddenCount,
                ColorLegend = ColorLegend(legend, hiddenCount == 0 ? null : shownAtoms),
            };
        }

        /// <summary>The images of the block of <paramref name="n"/> cells and, with the boundary atoms, of its far faces.</summary>
        void BlockImages(LatticeShift n, List<(int Atom, LatticeShift Image)> own, List<(int Atom, LatticeShift Image)> boundary)
        {
            for (int i = 0; i < reduced.Length; i++)
            {
                Reduced r = reduced[i];
                for (int a = 0; a <= n.A; a++)
                    for (int b = 0; b <= n.B; b++)
                        for (int c = 0; c <= n.C; c++)
                        {
                            var t = new LatticeShift(a, b, c);
                            bool isOwn = a < n.A && b < n.B && c < n.C;
                            if (isOwn)
                            {
                                own.Add((i, t));
                            }
                            else if (options.ShowBoundaryAtoms && OnBlock(r.X, a, n.A) && OnBlock(r.Y, b, n.B) && OnBlock(r.Z, c, n.C))
                            {
                                boundary.Add((i, t));
                            }
                        }
            }
        }

        /// <summary>
        /// The images <see cref="BlockImages"/> gives, counted without listing them, and the polyhedra around them: per
        /// axis, n images, and one more on the far face for an atom with coordinate 0 when the boundary atoms are drawn.
        /// </summary>
        public (long Atoms, long Polyhedra) CountBlock(LatticeShift n)
        {
            var images = new long[reduced.Length];
            bool[] counted = CountedAtoms();
            long atoms = 0;
            for (int i = 0; i < reduced.Length; i++)
            {
                Reduced r = reduced[i];
                images[i] = Along(r.X, n.A) * Along(r.Y, n.B) * Along(r.Z, n.C);
                if (counted[i])
                    atoms += images[i];
            }
            return (atoms, CountPolyhedra(images));

            long Along(double coordinate, int size)
                => size + (options.ShowBoundaryAtoms && OnBlock(coordinate, size, size) ? 1L : 0L);
        }

        /// <summary>Atoms per cell times the volume of a sphere of <paramref name="radius"/> over the cell volume.</summary>
        public double ExpectedInSphere(double radius)
            => structure.Atoms.Count * 4 * Math.PI * radius * radius * radius / (3 * basis.Volume);

        /// <summary>
        /// The images <see cref="SphereImages"/> gives, counted, and the polyhedra around them; a sphere expected past four
        /// times <paramref name="ceiling"/> is not walked, and counts as its expectation with no polyhedra.
        /// </summary>
        public (long Atoms, long Polyhedra) CountSphere(FractionalPoint center, double radius, int ceiling)
        {
            double expected = ExpectedInSphere(radius);
            if (expected > 4.0 * ceiling)
                return ((long)Math.Min(long.MaxValue / 2, Math.Ceiling(expected)), 0);
            var images = new long[reduced.Length];
            ForEachInSphere(center, radius, (i, _) => images[i]++);
            bool[] counted = CountedAtoms();
            long atoms = 0;
            for (int i = 0; i < images.Length; i++)
            {
                if (counted[i])
                    atoms += images[i];
            }
            return (atoms, CountPolyhedra(images));
        }

        /// <summary>
        /// The atoms whose images are all drawn whatever the image: false for an atom the appearance hides or may hide in
        /// some image (atoms off, a hidden style, a rule on one of its images, a polyhedron center or vertex left out). The
        /// counts skip those atoms and so never pass the cost of the scene built, which also draws the faded neighbors.
        /// </summary>
        bool[] CountedAtoms()
        {
            var counted = new bool[reduced.Length];
            if (!app.ShowAtoms)
                return counted;
            Array.Fill(counted, true);
            for (int i = 0; i < counted.Length; i++)
            {
                if (atomStyle[i].Visible == false)
                    counted[i] = false;
            }
            foreach (AtomImage image in app.ByImage.Keys)
            {
                if ((uint)image.Atom < (uint)counted.Length)
                    counted[image.Atom] = false;
            }
            if (!polyhedra || (app.ShowPolyhedronCenters && options.ShowPolyhedronVertices))
                return counted;
            IReadOnlyList<CrystalPolyhedron> list = structure.Polyhedra;
            for (int k = 0; k < list.Count; k++)
            {
                if (!app.ShowPolyhedronCenters && (uint)list[k].Center < (uint)counted.Length)
                    counted[list[k].Center] = false;
                if (options.ShowPolyhedronVertices)
                    continue;
                foreach (PolyhedronVertex v in list[k].Vertices)
                {
                    if ((uint)v.Atom < (uint)counted.Length)
                        counted[v.Atom] = false;
                }
            }
            return counted;
        }

        /// <summary>
        /// The polyhedra drawn around <paramref name="images"/> images of each atom: each enabled, visible and valid
        /// polyhedron once per image of its center; none when a length limit may drop some of them.
        /// </summary>
        long CountPolyhedra(long[] images)
        {
            if (!polyhedra || limits)
                return 0;
            long count = 0;
            IReadOnlyList<CrystalPolyhedron> list = structure.Polyhedra;
            for (int k = 0; k < list.Count; k++)
            {
                if (options.EnabledPolyhedra is { } enabled && !enabled.Contains(k))
                    continue;
                if (polyhedronStyle[k].Visible == false || !IsValid(list[k]))
                    continue;
                count += images[list[k].Center];
            }
            return count;
        }

        /// <summary>Every image of every atom within the sphere, in atom order and then by translation.</summary>
        void SphereImages(SphereClip sphere, List<(int Atom, LatticeShift Image)> own)
            => ForEachInSphere(sphere.Center, sphere.Radius, (i, t) => own.Add((i, t)));

        void ForEachInSphere(FractionalPoint c, double radius, Action<int, LatticeShift> add)
        {
            (double ra, double rb, double rc) = basis.ReciprocalLengths();
            double limit = radius + SphereTolerance;
            (int a0, int a1) = SphereSpan(c.X, limit, ra);
            (int b0, int b1) = SphereSpan(c.Y, limit, rb);
            (int c0, int c1) = SphereSpan(c.Z, limit, rc);
            for (int i = 0; i < reduced.Length; i++)
            {
                Reduced r = reduced[i];
                for (int a = a0; a <= a1; a++)
                    for (int b = b0; b <= b1; b++)
                        for (int k = c0; k <= c1; k++)
                        {
                            if (basis.Distance(r.X + a - c.X, r.Y + b - c.Y, r.Z + k - c.Z) <= limit)
                                add(i, new LatticeShift(a, b, k));
                        }
            }
        }

        /// <summary>
        /// True when some length limit can change which bonds are drawn: a limit in the options or in an atom rule, or a
        /// candidate bond. Without one, every bond and polyhedron is drawn as before the limits existed.
        /// </summary>
        static bool HasLengthLimits(CrystalStructure structure, CrystalSceneOptions options, CrystalAppearance app)
        {
            return options.MaxBondLength is not null || app.AtomDefault.MaxBondLength is not null || Any(app.ByElement)
                || Any(app.BySite) || Any(app.ByGroup) || Any(app.ByAtom) || Any(app.ByImage)
                || structure.Bonds.Any(b => !b.IsAutomatic);

            static bool Any<TKey>(ImmutableDictionary<TKey, AtomStyle> rules) where TKey : notnull
                => rules.Values.Any(s => s.MaxBondLength is not null);
        }

        /// <summary>The limit of an image: the one of its rules, else the one of the options; null is no limit.</summary>
        double? LimitOf(int atom, LatticeShift t)
            => Style(atom, t).MaxBondLength is { } own && !double.IsNaN(own) ? own
                : options.MaxBondLength is { } all && !double.IsNaN(all) ? all : null;

        /// <summary>Whether one end lets a bond of this length through (<see cref="CrystalSceneOptions.MaxBondLength"/>).</summary>
        static bool Lets(double? limit, bool automatic, double length)
            => limit is { } l ? length <= l + LengthTolerance : automatic;

        bool BondPasses(int bond, int atom, LatticeShift t, int other, LatticeShift u)
        {
            bool automatic = structure.Bonds[bond].IsAutomatic;
            bondLength ??= BondLengths();
            double length = bondLength[bond];
            return Lets(LimitOf(atom, t), automatic, length) && Lets(LimitOf(other, u), automatic, length);
        }

        /// <summary>Every center-vertex pair of one copy passes, the vertices counting as automatic bonds.</summary>
        bool VerticesPass(int center, LatticeShift t, (int Atom, LatticeShift Image)[] vertices)
        {
            double? centerLimit = LimitOf(center, t);
            Reduced c = reduced[center];
            foreach ((int atom, LatticeShift u) in vertices)
            {
                Reduced v = reduced[atom];
                double length = basis.Distance(v.X + u.A - c.X - t.A, v.Y + u.B - c.Y - t.B, v.Z + u.C - c.Z - t.C);
                if (!Lets(centerLimit, true, length) || !Lets(LimitOf(atom, u), true, length))
                    return false;
            }
            return true;
        }

        double[] BondLengths()
        {
            IReadOnlyList<CrystalBond> bonds = structure.Bonds;
            var lengths = new double[bonds.Count];
            int count = structure.Atoms.Count;
            for (int k = 0; k < bonds.Count; k++)
            {
                CrystalBond b = bonds[k];
                if ((uint)b.First < (uint)count && (uint)b.Second < (uint)count)
                    lengths[k] = Distance(b.First, b.Second, ReducedShift(b));
            }
            return lengths;
        }

        string LabelOf(int atom, AtomLabelText text)
        {
            IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
            CrystalAtom a = atoms[atom];
            switch (text)
            {
                case AtomLabelText.Element:
                    return Symbol(MainElement(a));
                case AtomLabelText.Site when a.SiteIndex >= 0:
                    foreach (CrystalAtom other in atoms)
                    {
                        if (other.SiteIndex == a.SiteIndex)
                            return other.Label;
                    }
                    return a.Label;
                default:
                    return a.Label;
            }
        }

        /// <summary>
        /// Order of each atom's site among the sites, by first appearance; an atom without site is a site of its label.
        /// The void sites take no place, so they do not shift the colors of the atoms.
        /// </summary>
        static Dictionary<int, int> SiteOrder(IReadOnlyList<CrystalAtom> atoms)
        {
            var keys = new Dictionary<string, int>(StringComparer.Ordinal);
            var result = new Dictionary<int, int>();
            for (int i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].IsVoid)
                    continue;
                string key = SiteKey(atoms[i]);
                if (!keys.TryGetValue(key, out int order))
                    keys[key] = order = keys.Count;
                result[i] = order;
            }
            return result;
        }

        static string SiteKey(CrystalAtom atom)
            => atom.SiteIndex >= 0 ? "#" + atom.SiteIndex.ToString(System.Globalization.CultureInfo.InvariantCulture) : "@" + atom.Label;

        static Rgba OccupancyColor(double occupancy)
            => Rgba.Lerp(OccupancyLow, OccupancyHigh, (float)(double.IsFinite(occupancy) ? Math.Clamp(occupancy, 0, 1) : 1));

        /// <summary>
        /// What the colors of the atoms mean: the element legend in the element scheme, one entry per drawn site in the
        /// site scheme (the label of its first atom), and three stops in the occupancy scheme.
        /// </summary>
        List<ColorLegendEntry> ColorLegend(List<ElementLegendEntry> legend, HashSet<int>? shown)
        {
            switch (app.ColorScheme)
            {
                case AtomColorScheme.Site:
                    {
                        var result = new List<ColorLegendEntry>();
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            if ((shown is not null && !shown.Contains(i)) || atoms[i].IsVoid)
                                continue;
                            if (seen.Add(SiteKey(atoms[i])))
                                result.Add(new ColorLegendEntry(atoms[i].Label, color[i]));
                        }
                        return result;
                    }
                case AtomColorScheme.Occupancy:
                    return [new("0", OccupancyColor(0)), new("0.5", OccupancyColor(0.5)), new("1", OccupancyColor(1))];
                case AtomColorScheme.Group:
                    {
                        // The groups with a drawn atom, in group order; then the elements of the atoms without a group.
                        IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
                        IReadOnlyList<CrystalAtomGroup> groups = structure.Groups;
                        var present = new bool[groups.Count];
                        var loose = new HashSet<string>(StringComparer.Ordinal);
                        for (int i = 0; i < atoms.Count; i++)
                        {
                            if ((shown is not null && !shown.Contains(i)) || atoms[i].IsVoid)
                                continue;
                            if ((uint)atoms[i].Group < (uint)groups.Count)
                                present[atoms[i].Group] = true;
                            else
                                loose.Add(Symbol(MainElement(atoms[i])));
                        }
                        var result = new List<ColorLegendEntry>();
                        for (int g = 0; g < groups.Count; g++)
                        {
                            if (present[g])
                                result.Add(new ColorLegendEntry(groups[g].Label, groups[g].Color));
                        }
                        result.AddRange(legend.Where(e => loose.Contains(e.Element)).Select(e => new ColorLegendEntry(e.Element, e.Color)));
                        return result;
                    }
                default:
                    return [.. legend.Select(e => new ColorLegendEntry(e.Element, e.Color))];
            }
        }

        /// <summary>One drawn copy of a polyhedron: around the image <see cref="Image"/> of its center.</summary>
        readonly record struct PolyhedronInstance(int Index, int Center, LatticeShift Image, (int Atom, LatticeShift Image)[] Vertices);

        /// <summary>
        /// Every enabled, valid polyhedron around every drawn image of its center. Vertex i of the copy around image t
        /// is at image <c>t + Floor(vertex) + Shift - Floor(center)</c> of its atom, the rule of the bonds.
        /// </summary>
        List<PolyhedronInstance> Instances(List<(int Atom, LatticeShift Image)> own, List<(int Atom, LatticeShift Image)> boundary)
        {
            var result = new List<PolyhedronInstance>();
            IReadOnlyList<CrystalPolyhedron> list = structure.Polyhedra;
            if (list.Count == 0)
                return result;
            var images = new Dictionary<int, List<LatticeShift>>();
            foreach ((int atom, LatticeShift t) in own.Concat(boundary))
            {
                if (!images.TryGetValue(atom, out List<LatticeShift>? atomImages))
                    images[atom] = atomImages = [];
                atomImages.Add(t);
            }
            for (int k = 0; k < list.Count; k++)
            {
                CrystalPolyhedron p = list[k];
                if (options.EnabledPolyhedra is { } enabled && !enabled.Contains(k))
                    continue;
                if (polyhedronStyle[k].Visible == false)
                    continue;
                if (!IsValid(p) || !images.TryGetValue(p.Center, out List<LatticeShift>? centerImages))
                    continue;
                foreach (LatticeShift t in centerImages)
                {
                    var vertices = new (int, LatticeShift)[p.Vertices.Count];
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        PolyhedronVertex v = p.Vertices[i];
                        vertices[i] = (v.Atom, t + reduced[v.Atom].Floor + v.Shift - reduced[p.Center].Floor);
                    }
                    if (limits && !VerticesPass(p.Center, t, vertices))
                        continue;
                    result.Add(new PolyhedronInstance(k, p.Center, t, vertices));
                }
            }
            return result;
        }

        /// <summary>Center and vertex atoms in range, and every face a polygon of vertices in range.</summary>
        bool IsValid(CrystalPolyhedron p)
        {
            int count = structure.Atoms.Count;
            if ((uint)p.Center >= (uint)count || p.Vertices is not { Count: >= 3 } vertices || p.Faces is not { Count: >= 1 } faces)
                return false;
            foreach (PolyhedronVertex v in vertices)
            {
                if ((uint)v.Atom >= (uint)count)
                    return false;
            }
            foreach (int[] face in faces)
            {
                if (face is not { Length: >= 3 })
                    return false;
                foreach (int i in face)
                {
                    if ((uint)i >= (uint)vertices.Count)
                        return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The mesh of one copy: each face as a fan of triangles from its first vertex (keeping its turn, so the front
        /// side is outside), and each side of a face as an edge, once. The face diagonals are not edges.
        /// </summary>
        MeshItem Mesh(PolyhedronInstance instance, int id)
        {
            CrystalPolyhedron polyhedron = structure.Polyhedra[instance.Index];
            var positions = new Vector3[instance.Vertices.Length];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = Position(instance.Vertices[i].Atom, instance.Vertices[i].Image);
            var indices = new List<int>();
            var edges = new List<int>();
            var sides = new HashSet<(int, int)>();
            foreach (int[] face in polyhedron.Faces)
            {
                for (int k = 1; k + 1 < face.Length; k++)
                {
                    indices.Add(face[0]);
                    indices.Add(face[k]);
                    indices.Add(face[k + 1]);
                }
                for (int k = 0; k < face.Length; k++)
                {
                    int a = face[k], b = face[(k + 1) % face.Length];
                    if (sides.Add((Math.Min(a, b), Math.Max(a, b))))
                    {
                        edges.Add(a);
                        edges.Add(b);
                    }
                }
            }
            PolyhedronStyle style = polyhedronStyle[instance.Index];
            double asked = style.FaceOpacity ?? options.PolyhedronOpacity;
            double opacity = double.IsFinite(asked) ? Math.Clamp(asked, 0, 1) : 0.35;
            Rgba faceColor = (style.FaceColor ?? polyhedron.Color ?? color[instance.Center]) with { A = (byte)Math.Round(255 * opacity) };
            return new MeshItem
            {
                Id = id,
                Positions = positions,
                Indices = style.ShowFaces == false ? [] : [.. indices],
                Edges = style.ShowEdges == false ? null : [.. edges],
                Color = faceColor,
                EdgeColor = style.EdgeColor,
                // 1,5 e a largura padrao do MeshItem.
                EdgeWidth = style.EdgeWidth is { } w && float.IsFinite(w) && w > 0 ? w : 1.5f,
                Lighting = MeshLighting.KeyLight,
                DoubleSided = options.PolyhedronBackFaces,
            };
        }

        Vector3 Position(int atom, LatticeShift t)
        {
            Reduced r = reduced[atom];
            return basis.ToCartesian(r.X + t.A, r.Y + t.B, r.Z + t.C);
        }

        static bool OnBlock(double coordinate, int image, int size)
            => image < size || coordinate + image <= size + BoundaryTolerance;

        static ((int, LatticeShift), (int, LatticeShift)) Order((int Atom, LatticeShift Image) x, (int Atom, LatticeShift Image) y)
            => Compare(x, y) <= 0 ? (x, y) : (y, x);

        static int Compare((int Atom, LatticeShift Image) x, (int Atom, LatticeShift Image) y)
        {
            int c = x.Atom.CompareTo(y.Atom);
            if (c == 0)
                c = x.Image.A.CompareTo(y.Image.A);
            if (c == 0)
                c = x.Image.B.CompareTo(y.Image.B);
            if (c == 0)
                c = x.Image.C.CompareTo(y.Image.C);
            return c;
        }

        /// <summary>
        /// Each bond in both directions, with the shift between the reduced images: the second atom of a bond sits at
        /// its given coordinates plus <c>Shift</c>, that is at its reduced image <c>Floor2 + Shift - Floor1</c> cells
        /// from the reduced image of the first.
        /// </summary>
        Dictionary<int, List<(int Bond, int Other, LatticeShift Shift)>> BondsByAtom()
        {
            var map = new Dictionary<int, List<(int, int, LatticeShift)>>();
            IReadOnlyList<CrystalBond> bonds = structure.Bonds;
            int count = structure.Atoms.Count;
            for (int k = 0; k < bonds.Count; k++)
            {
                CrystalBond bond = bonds[k];
                if ((uint)bond.First >= (uint)count || (uint)bond.Second >= (uint)count)
                    continue;
                LatticeShift shift = ReducedShift(bond);
                if (bond.First == bond.Second && shift == LatticeShift.Zero)
                    continue;
                Add(map, bond.First, (k, bond.Second, shift));
                Add(map, bond.Second, (k, bond.First, LatticeShift.Zero - shift));
            }
            return map;

            static void Add(Dictionary<int, List<(int, int, LatticeShift)>> map, int key, (int, int, LatticeShift) value)
            {
                if (!map.TryGetValue(key, out List<(int, int, LatticeShift)>? list))
                    map[key] = list = [];
                list.Add(value);
            }
        }

        LatticeShift ReducedShift(CrystalBond bond)
            => reduced[bond.Second].Floor + bond.Shift - reduced[bond.First].Floor;

        double Distance(int first, int second, LatticeShift shift)
        {
            Reduced p = reduced[first], q = reduced[second];
            return basis.Distance(q.X + shift.A - p.X, q.Y + shift.B - p.Y, q.Z + shift.C - p.Z);
        }

        void AddCellEdges(List<SceneItem> items, LatticeShift n)
        {
            void Edge(int a, int b, int c, int da, int db, int dc)
                => items.Add(new LineItem
                {
                    Start = basis.ToCartesian(a, b, c),
                    End = basis.ToCartesian(a + da, b + db, c + dc),
                    Color = options.CellColor,
                    Width = 1f,
                });
            for (int a = 0; a <= n.A; a++)
                for (int b = 0; b <= n.B; b++)
                    for (int c = 0; c <= n.C; c++)
                    {
                        // Uma aresta por ponto da rede e direcao, so as que ficam no bloco: as internas nao se repetem.
                        if (a < n.A)
                            Edge(a, b, c, 1, 0, 0);
                        if (b < n.B)
                            Edge(a, b, c, 0, 1, 0);
                        if (c < n.C)
                            Edge(a, b, c, 0, 0, 1);
                    }
        }

        /// <summary>The elements of the atoms in <paramref name="shown"/>; all atoms when null.</summary>
        List<ElementLegendEntry> Legend(HashSet<int>? shown)
        {
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (shown is not null && !shown.Contains(i))
                    continue;
                CrystalAtom atom = atoms[i];
                if (atom.IsVoid)
                    continue;
                if (atom.Shares is { Count: > 0 } shares)
                {
                    foreach (ElementShare share in shares)
                        symbols.Add(Symbol(share.Element));
                }
                else
                {
                    symbols.Add(Symbol(atom.Element));
                }
            }
            return [.. symbols
                .OrderBy(s => ElementTable.TryGet(s, out ElementInfo e) ? e.Z : int.MaxValue)
                .ThenBy(s => s, StringComparer.Ordinal)
                .Select(s => new ElementLegendEntry(s, ColorOf(s)))];
        }

        static string Symbol(string element)
        {
            string normalized = ElementTable.Normalize(element);
            return ElementTable.TryGet(normalized, out _) ? normalized : element;
        }

        /// <summary>
        /// The color of an element: the color of its appearance rule, then <see cref="CrystalSceneOptions.ElementColors"/>,
        /// then the element table. Legend, sectors, rods and faces all take it from here.
        /// </summary>
        Rgba ColorOf(string element)
        {
            if (app.ByElement.Count > 0 && app.ByElement.TryGetValue(ElementTable.Normalize(element), out AtomStyle? rule)
                && rule.Color is { } ruled)
            {
                return ruled;
            }
            IReadOnlyDictionary<string, Rgba>? custom = options.ElementColors;
            if (custom is not null)
            {
                if (custom.TryGetValue(element, out Rgba c))
                    return c;
                if (custom.TryGetValue(ElementTable.Normalize(element), out c))
                    return c;
            }
            return ElementTable.TryGet(element, out ElementInfo e) ? e.Color : UnknownColor;
        }

        IReadOnlyList<SphereSector>? SectorsOf(CrystalAtom atom)
        {
            if (atom.Shares is { Count: > 1 } shares)
                return [.. shares.Select(s => new SphereSector((float)s.Fraction, ColorOf(s.Element)))];
            if (atom.Shares is { Count: 1 } single)
                return single[0].Fraction < 1 - 1e-6 ? [new SphereSector((float)single[0].Fraction, ColorOf(single[0].Element))] : null;
            return atom.Occupancy < 1 - 1e-6 ? [new SphereSector((float)Math.Max(atom.Occupancy, 0), ColorOf(atom.Element))] : null;
        }

        static Rgba Fade(Rgba c) => Rgba.Lerp(c, FadeTarget, 0.5f) with { A = FadedAlpha };

        /// <summary>The color faded when the image completes a coordination, then with the rule opacity.</summary>
        static Rgba Look(Rgba c, bool faded, double? opacity) => WithOpacity(faded ? Fade(c) : c, opacity);
    }
}
