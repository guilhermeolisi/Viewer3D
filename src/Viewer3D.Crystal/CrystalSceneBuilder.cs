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
        int cost = scene.AtomCount + CrystalSceneOptions.PolyhedronCost * scene.PolyhedronCount;
        if (cost > CrystalSceneOptions.MaxAtoms && n != new LatticeShift(1, 1, 1))
        {
            string warning = scene.PolyhedronCount == 0
                ? $"{n.A} x {n.B} x {n.C} cells would draw {scene.AtomCount} atoms, more than "
                    + $"{CrystalSceneOptions.MaxAtoms}; showing one cell."
                : $"{n.A} x {n.B} x {n.C} cells would draw {scene.AtomCount} atoms and {scene.PolyhedronCount} polyhedra, "
                    + $"more than the {CrystalSceneOptions.MaxAtoms} atoms allowed (a polyhedron counts as "
                    + $"{CrystalSceneOptions.PolyhedronCost}); showing one cell.";
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
        readonly DisplacementShape[]?[] shapes;
        readonly bool thermal;
        readonly bool polyhedra;
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
            thermal = options.Style is CrystalStyle.ThermalEllipsoids or CrystalStyle.PolyhedraWithEllipsoids;
            polyhedra = options.Style is CrystalStyle.Polyhedra or CrystalStyle.PolyhedraWithEllipsoids;
            shapes = new DisplacementShape[]?[atoms.Count];
            if (thermal)
            {
                int level = ProbabilityScale.Levels.Contains(options.ProbabilityLevel) ? options.ProbabilityLevel : 50;
                scale = ProbabilityScale.For(level);
                for (int i = 0; i < atoms.Count; i++)
                    shapes[i] = ShapesOf(atoms[i], i);
            }
            else
            {
                scale = options.Style == CrystalStyle.SpaceFilling ? TouchingScale() : options.BallScale;
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
                Rgba c = list.Count > 1 && d.Element is string element ? ColorOf(element) : color[index];
                result[k] = DisplacementShape.From(d.Cartesian6, scale, options.MarkerSize, c, d.Element);
            }
            return result;
        }

        /// <summary>Where a bond from the atom along <paramref name="direction"/> leaves its shapes.</summary>
        float Reach(int atom, Vector3 direction)
        {
            if (!thermal)
                return (float)(scale * radius[atom]);
            float reach = 0;
            foreach (DisplacementShape shape in shapes[atom]!)
                reach = MathF.Max(reach, shape.Reach(direction));
            return reach;
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

            // Poliedros: uma instancia por imagem desenhada do centro. Sem os vertices, a imagem que e vertice e nao e
            // centro sai com rotulo e bastoes; sem os bastoes internos, sai o bastao centro-vertice de cada instancia.
            List<PolyhedronInstance> instances = polyhedra ? Instances(own, boundary) : [];
            var hidden = new HashSet<(int, LatticeShift)>();
            var inside = new HashSet<((int, LatticeShift), (int, LatticeShift))>();
            if (instances.Count > 0)
            {
                var centers = new HashSet<(int, LatticeShift)>(instances.Select(p => (p.Center, p.Image)));
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
                IReadOnlyList<SphereSector>? s = sectors[atom];
                if (faded && s is not null)
                    s = [.. s.Select(x => x with { Color = Fade(x.Color) })];
                if (thermal)
                {
                    AddShapes(atom, t, p, faded, s);
                    return;
                }
                int id = nextId++;
                Rgba baseColor = faded ? Fade(color[atom]) : color[atom];
                items.Add(new SphereItem { Id = id, Center = p, Radius = (float)(scale * radius[atom]), Color = baseColor, Sectors = s });
                lookup[id] = new CrystalItemInfo(CrystalItemKind.Atom, atom, -1, t - reduced[atom].Floor, faded);
            }

            // Thermal style: one item per displacement of the atom, all pointing to the same atom in the selection. The
            // occupancy sectors apply only with a single displacement; with several, each one already has its element color.
            void AddShapes(int atom, LatticeShift t, Vector3 p, bool faded, IReadOnlyList<SphereSector>? s)
            {
                DisplacementShape[] list = shapes[atom]!;
                IReadOnlyList<SphereSector>? shapeSectors = list.Length == 1 ? s : null;
                foreach (DisplacementShape shape in list)
                {
                    int id = nextId++;
                    Rgba c = faded ? Fade(shape.Color) : shape.Color;
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

            // Vertice fora do bloco, de qualquer instancia (de centro proprio ou da fronteira): esfera esmaecida.
            var completions = new HashSet<(int, LatticeShift)>();
            if (options.CompleteCoordination && options.ShowPolyhedronVertices)
            {
                foreach (PolyhedronInstance p in instances)
                {
                    foreach ((int atom, LatticeShift u) in p.Vertices)
                    {
                        if (!drawn.ContainsKey((atom, u)) && completions.Add((atom, u)))
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
            foreach ((int atom, LatticeShift t) in origins)
            {
                if (!bondsByAtom.TryGetValue(atom, out List<(int Bond, int Other, LatticeShift Shift)>? list))
                    continue;
                foreach ((int bond, int other, LatticeShift shift) in list)
                {
                    LatticeShift u = t + shift;
                    if (hidden.Contains((other, u)))
                        continue;
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
            {
                if (!drawn.ContainsKey((atom, t)))
                    AddSphere(atom, t, true);
            }

            if (options.Style is not CrystalStyle.SpaceFilling)
            {
                float rodRadius = (float)(thermal ? 0.4 * options.BondRadius : options.BondRadius);
                foreach ((int bond, int first, LatticeShift ft, int second, LatticeShift st) in rods)
                {
                    if (inside.Count > 0 && inside.Contains(Order((first, ft), (second, st))))
                        continue;
                    bool faded = completions.Contains((first, ft)) || completions.Contains((second, st));
                    int id = nextId++;
                    Vector3 start = drawn[(first, ft)], end = drawn[(second, st)];
                    items.Add(new RodItem
                    {
                        Id = id,
                        Start = start,
                        End = end,
                        Radius = rodRadius,
                        StartColor = completions.Contains((first, ft)) ? Fade(color[first]) : color[first],
                        EndColor = completions.Contains((second, st)) ? Fade(color[second]) : color[second],
                        StartTrim = Reach(first, end - start),
                        EndTrim = Reach(second, start - end),
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
            return new CrystalScene(new Scene(items), structure.Cell, axes, scale, Legend(), drawn.Count, warning, lookup,
                instances.Count);
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
            double opacity = double.IsFinite(options.PolyhedronOpacity) ? Math.Clamp(options.PolyhedronOpacity, 0, 1) : 0.35;
            Rgba faceColor = (polyhedron.Color ?? color[instance.Center]) with { A = (byte)Math.Round(255 * opacity) };
            return new MeshItem
            {
                Id = id,
                Positions = positions,
                Indices = [.. indices],
                Edges = [.. edges],
                Color = faceColor,
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
