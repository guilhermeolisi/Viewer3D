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
/// their neighbors in the 26 cells around), so it never depends on the repetition.
/// </para>
/// <para>
/// Spheres are shaded by the viewer with a light from the upper left. A mesh added to the same scene (coordination
/// polyhedra) should use <see cref="MeshLighting.KeyLight"/>, whose light comes from the same side.
/// </para>
/// </remarks>
public sealed class CrystalSceneBuilder
{
    /// <summary>A position this close to the far face of the block counts as on it.</summary>
    public const double BoundaryTolerance = 1e-4;

    static readonly Rgba UnknownColor = new(128, 128, 128);
    static readonly Rgba FadeTarget = new(200, 200, 200);
    const byte FadedAlpha = 110;

    /// <summary>Builds the scene of <paramref name="structure"/>.</summary>
    public CrystalScene Build(CrystalStructure structure, CrystalSceneOptions options)
    {
        ArgumentNullException.ThrowIfNull(structure);
        ArgumentNullException.ThrowIfNull(options);
        CellBasis basis = structure.Cell.Basis();
        if (!basis.Valid)
            return new CrystalScene(Scene.Empty, structure.Cell, [], 0, [], 0, "The cell is not valid.",
                new Dictionary<int, CrystalItemInfo>());

        var n = new LatticeShift(Math.Clamp(options.RepeatA, 1, 3), Math.Clamp(options.RepeatB, 1, 3),
            Math.Clamp(options.RepeatC, 1, 3));
        var context = new Context(structure, options, basis);
        CrystalScene scene = context.Build(n, null);
        if (scene.AtomCount > CrystalSceneOptions.MaxAtoms && n != new LatticeShift(1, 1, 1))
        {
            string warning = $"{n.A} x {n.B} x {n.C} cells would draw {scene.AtomCount} atoms, more than "
                + $"{CrystalSceneOptions.MaxAtoms}; showing one cell.";
            scene = context.Build(new LatticeShift(1, 1, 1), warning);
        }
        return scene;
    }

    /// <summary>An atom reduced to [0, 1): its position and the translation the reduction took off.</summary>
    readonly record struct Reduced(double X, double Y, double Z, LatticeShift Floor);

    sealed class Context
    {
        readonly CrystalStructure structure;
        readonly CrystalSceneOptions options;
        readonly CellBasis basis;
        readonly Reduced[] reduced;
        readonly double[] radius;
        readonly Rgba[] color;
        readonly IReadOnlyList<SphereSector>?[] sectors;
        readonly double scale;

        public Context(CrystalStructure structure, CrystalSceneOptions options, CellBasis basis)
        {
            this.structure = structure;
            this.options = options;
            this.basis = basis;
            IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
            reduced = new Reduced[atoms.Count];
            radius = new double[atoms.Count];
            color = new Rgba[atoms.Count];
            sectors = new IReadOnlyList<SphereSector>?[atoms.Count];
            for (int i = 0; i < atoms.Count; i++)
            {
                CrystalAtom atom = atoms[i];
                reduced[i] = Reduce(atom.X, atom.Y, atom.Z);
                string main = MainElement(atom);
                radius[i] = atom.Radius is double r && r > 0 ? r : ElementTable.TryGet(main, out ElementInfo e) ? e.Radius : 1.0;
                color[i] = ColorOf(main);
                sectors[i] = SectorsOf(atom);
            }
            scale = options.Style == CrystalStyle.SpaceFilling ? TouchingScale() : options.BallScale;
        }

        public CrystalScene Build(LatticeShift n, string? warning)
        {
            IReadOnlyList<CrystalAtom> atoms = structure.Atoms;
            var items = new List<SceneItem>();
            var lookup = new Dictionary<int, CrystalItemInfo>();
            var drawn = new Dictionary<(int Atom, LatticeShift Image), Vector3>();
            var own = new List<(int Atom, LatticeShift Image)>();
            var boundary = new List<(int Atom, LatticeShift Image)>();
            int nextId = 1;

            for (int i = 0; i < atoms.Count; i++)
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

            void AddSphere(int atom, LatticeShift t, bool faded)
            {
                Vector3 p = Position(atom, t);
                drawn[(atom, t)] = p;
                int id = nextId++;
                Rgba baseColor = faded ? Fade(color[atom]) : color[atom];
                IReadOnlyList<SphereSector>? s = sectors[atom];
                if (faded && s is not null)
                    s = [.. s.Select(x => x with { Color = Fade(x.Color) })];
                items.Add(new SphereItem { Id = id, Center = p, Radius = (float)(scale * radius[atom]), Color = baseColor, Sectors = s });
                lookup[id] = new CrystalItemInfo(CrystalItemKind.Atom, atom, -1, t - reduced[atom].Floor, faded);
            }

            foreach ((int atom, LatticeShift t) in own)
                AddSphere(atom, t, false);
            foreach ((int atom, LatticeShift t) in boundary)
                AddSphere(atom, t, false);

            // Ligacoes: de cada imagem desenhada, nos dois sentidos de cada ligacao dada. Vizinho fora do bloco so a
            // partir de imagem propria, e entao com esfera esmaecida ou sem a ligacao.
            var ownSet = new HashSet<(int, LatticeShift)>(own);
            var pairs = new HashSet<((int, LatticeShift), (int, LatticeShift))>();
            var rods = new List<(int Bond, int First, LatticeShift FirstImage, int Second, LatticeShift SecondImage)>();
            var completions = new HashSet<(int, LatticeShift)>();
            var origins = new List<(int Atom, LatticeShift Image)>(own.Count + boundary.Count);
            origins.AddRange(own);
            origins.AddRange(boundary);
            var bondsByAtom = BondsByAtom();
            foreach ((int atom, LatticeShift t) in origins)
            {
                if (!bondsByAtom.TryGetValue(atom, out List<(int Bond, int Other, LatticeShift Shift)>? list))
                    continue;
                foreach ((int bond, int other, LatticeShift shift) in list)
                {
                    LatticeShift u = t + shift;
                    bool partnerDrawn = drawn.ContainsKey((other, u));
                    if (!partnerDrawn)
                    {
                        if (!options.CompleteCoordination || !ownSet.Contains((atom, t)))
                            continue;
                        completions.Add((other, u));
                    }
                    var key = Order((atom, t), (other, u));
                    if (pairs.Add(key))
                        rods.Add((bond, atom, t, other, u));
                }
            }
            foreach ((int atom, LatticeShift t) in completions)
                AddSphere(atom, t, true);

            if (options.Style == CrystalStyle.BallAndStick)
            {
                foreach ((int bond, int first, LatticeShift ft, int second, LatticeShift st) in rods)
                {
                    bool faded = completions.Contains((first, ft)) || completions.Contains((second, st));
                    int id = nextId++;
                    items.Add(new RodItem
                    {
                        Id = id,
                        Start = drawn[(first, ft)],
                        End = drawn[(second, st)],
                        Radius = (float)options.BondRadius,
                        StartColor = completions.Contains((first, ft)) ? Fade(color[first]) : color[first],
                        EndColor = completions.Contains((second, st)) ? Fade(color[second]) : color[second],
                        StartTrim = (float)(scale * radius[first]),
                        EndTrim = (float)(scale * radius[second]),
                    });
                    lookup[id] = new CrystalItemInfo(CrystalItemKind.Bond, first, bond, ft - reduced[first].Floor, faded);
                }
            }

            AddCellEdges(items, n);

            if (options.IncludeLabels)
            {
                foreach ((int atom, LatticeShift t) in own)
                {
                    items.Add(new LabelItem
                    {
                        Position = drawn[(atom, t)],
                        Text = atoms[atom].Label,
                        Offset = new Vector2(4, -6),
                    });
                }
            }

            var axes = new List<AxisDirection>
            {
                new(structure.Cell.AVector, "a"),
                new(structure.Cell.BVector, "b"),
                new(structure.Cell.CVector, "c"),
            };
            int atomCount = own.Count + boundary.Count + completions.Count;
            return new CrystalScene(new Scene(items), structure.Cell, axes, scale, Legend(), atomCount, warning, lookup);
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

        double TouchingScale()
        {
            double best = double.PositiveInfinity;
            IReadOnlyList<CrystalBond> bonds = structure.Bonds;
            int count = structure.Atoms.Count;
            foreach (CrystalBond bond in bonds)
            {
                if ((uint)bond.First >= (uint)count || (uint)bond.Second >= (uint)count)
                    continue;
                double d = Distance(bond.First, bond.Second, ReducedShift(bond));
                if (d > 0)
                    best = Math.Min(best, d / (radius[bond.First] + radius[bond.Second]));
            }
            if (double.IsFinite(best))
                return best;
            for (int i = 0; i < count; i++)
                for (int j = i; j < count; j++)
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
            return double.IsFinite(best) ? best : 1.0;
        }

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

        List<ElementLegendEntry> Legend()
        {
            var symbols = new HashSet<string>(StringComparer.Ordinal);
            foreach (CrystalAtom atom in structure.Atoms)
            {
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

        Rgba ColorOf(string element)
        {
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

        static string MainElement(CrystalAtom atom)
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

        IReadOnlyList<SphereSector>? SectorsOf(CrystalAtom atom)
        {
            if (atom.Shares is { Count: > 1 } shares)
                return [.. shares.Select(s => new SphereSector((float)s.Fraction, ColorOf(s.Element)))];
            if (atom.Shares is { Count: 1 } single)
                return single[0].Fraction < 1 - 1e-6 ? [new SphereSector((float)single[0].Fraction, ColorOf(single[0].Element))] : null;
            return atom.Occupancy < 1 - 1e-6 ? [new SphereSector((float)Math.Max(atom.Occupancy, 0), ColorOf(atom.Element))] : null;
        }

        static Rgba Fade(Rgba c) => Rgba.Lerp(c, FadeTarget, 0.5f) with { A = FadedAlpha };

        static Reduced Reduce(double x, double y, double z)
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
    }
}
