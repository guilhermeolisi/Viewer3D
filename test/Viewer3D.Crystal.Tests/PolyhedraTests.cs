using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class PolyhedraTests
{
    static readonly CrystalSceneBuilder Builder = new();

    static CrystalSceneOptions Options(CrystalStyle style = CrystalStyle.Polyhedra) => new()
    {
        Style = style,
        ShowBoundaryAtoms = false,
        CompleteCoordination = false,
        IncludeLabels = false,
    };

    /// <summary>The polyhedra of a scene: the meshes with edges (a cube marker of the thermal style has none).</summary>
    static List<MeshItem> Polyhedra(CrystalScene scene) => [.. scene.Scene.Items.OfType<MeshItem>().Where(m => m.Edges is not null)];

    static int EdgeCount(MeshItem mesh) => mesh.Edges!.Length / 2;

    /// <summary>Every triangle turns counter-clockwise seen from outside: its normal points away from the center.</summary>
    static void AssertOutward(MeshItem mesh, Vector3 center)
    {
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            Vector3 a = mesh.Positions[mesh.Indices[3 * t]], b = mesh.Positions[mesh.Indices[3 * t + 1]], c = mesh.Positions[mesh.Indices[3 * t + 2]];
            Vector3 normal = Vector3.Cross(b - a, c - a);
            Assert.True(Vector3.Dot(normal, (a + b + c) / 3 - center) > 0, $"triangle {t} faces inward");
        }
    }

    static Vector3 Mean(Vector3[] points)
    {
        var sum = Vector3.Zero;
        foreach (Vector3 p in points)
            sum += p;
        return sum / points.Length;
    }

    /// <summary>Rock salt NaCl, a = 5.64 A: Na at the origin and the three neighbor Cl, each both ways (Na-Cl = 2.82 A).</summary>
    static CrystalStructure RockSaltOctahedron(double centerX = 0)
    {
        CrystalAtom[] atoms =
        [
            new("Na1", "Na", centerX, 0, 0),
            new("Cl1", "Cl", 0.5, 0, 0),
            new("Cl1", "Cl", 0, 0.5, 0),
            new("Cl1", "Cl", 0, 0, 0.5),
        ];
        int c = (int)Math.Floor(centerX);
        PolyhedronVertex[] vertices =
        [
            new(1, new LatticeShift(c, 0, 0)), new(1, new LatticeShift(c - 1, 0, 0)),
            new(2, new LatticeShift(c, 0, 0)), new(2, new LatticeShift(c, -1, 0)),
            new(3, new LatticeShift(c, 0, 0)), new(3, new LatticeShift(c, 0, -1)),
        ];
        var cell = new CrystalCell(5.64, 5.64, 5.64, 90, 90, 90);
        Vector3[] relative = [.. vertices.Select(v => cell.ToCartesian(
            atoms[v.Atom].X + v.Shift.A - centerX, atoms[v.Atom].Y + v.Shift.B, atoms[v.Atom].Z + v.Shift.C))];
        return new CrystalStructure(cell, atoms, [])
        {
            Polyhedra = [new CrystalPolyhedron(0, vertices, Fixtures.HullFaces(relative)) { SourceIndex = 7 }],
        };
    }

    [Fact]
    public void Build_FluoriteCube_SixSquaresTwelveEdgesPerCa()
    {
        CrystalScene scene = Builder.Build(Fixtures.FluoriteWithCubes(), Options());

        List<MeshItem> meshes = Polyhedra(scene);
        Assert.Equal(4, meshes.Count);
        Assert.Equal(4, scene.PolyhedronCount);
        foreach (MeshItem mesh in meshes)
        {
            // Six squares, two triangles each; the diagonal of each square is not an edge.
            Assert.Equal(12, mesh.TriangleCount);
            Assert.Equal(12, EdgeCount(mesh));
            Assert.Equal(8, mesh.Positions.Length);
            Vector3 center = Mean(mesh.Positions);
            foreach (Vector3 p in mesh.Positions)
                Assert.Equal(Fixtures.FluoriteCaF, Vector3.Distance(p, center), 3);
            AssertOutward(mesh, center);
        }
    }

    [Fact]
    public void Build_Octahedron_UsesTheGivenVertexPositions()
    {
        CrystalScene scene = Builder.Build(RockSaltOctahedron(), Options());

        MeshItem mesh = Assert.Single(Polyhedra(scene));
        Vector3[] expected = [new(2.82f, 0, 0), new(-2.82f, 0, 0), new(0, 2.82f, 0), new(0, -2.82f, 0), new(0, 0, 2.82f), new(0, 0, -2.82f)];
        Assert.Equal(expected.Length, mesh.Positions.Length);
        for (int i = 0; i < expected.Length; i++)
            Assert.True(Vector3.Distance(expected[i], mesh.Positions[i]) < 1e-4f, $"vertex {i} at {mesh.Positions[i]}");
        Assert.Equal(8, mesh.TriangleCount);
        Assert.Equal(12, EdgeCount(mesh));
        AssertOutward(mesh, Vector3.Zero);
    }

    [Fact]
    public void Build_CoordinationEight_TwelveFacesEighteenEdges()
    {
        CrystalScene scene = Builder.Build(Fixtures.HexagonalBipyramid(), Options());

        MeshItem mesh = Assert.Single(Polyhedra(scene));
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(18, EdgeCount(mesh));
        AssertOutward(mesh, new Vector3(5, 5, 5));
    }

    [Fact]
    public void Build_CenterOnTheCellEdge_DrawsTheWholePolyhedron()
    {
        // Only the cube of the Ca at the origin: it and the seven images at the other cell corners, each with the whole cube.
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        s = s with { Polyhedra = [s.Polyhedra[0]] };
        CrystalScene scene = Builder.Build(s, Options() with { ShowBoundaryAtoms = true, CompleteCoordination = true });

        List<MeshItem> meshes = Polyhedra(scene);
        Assert.Equal(8, meshes.Count);
        var spheres = scene.Scene.Items.OfType<SphereItem>().ToList();
        int faded = 0;
        foreach (MeshItem mesh in meshes)
            foreach (Vector3 p in mesh.Positions)
            {
                SphereItem? at = spheres.FirstOrDefault(x => Vector3.Distance(x.Center, p) < 1e-3f);
                Assert.NotNull(at);
                if (scene.Lookup(at.Id)!.IsCompletion)
                    faded++;
            }
        // The corner (1, 1, 1) has seven of its eight vertices outside the cell.
        Assert.True(faded >= 7, $"{faded} faded vertices");
    }

    /// <summary>
    /// With the far faces, every F of the cell is a vertex of a drawn cube (the one at (3/4, 3/4, 3/4) of the Ca at the
    /// corner (1, 1, 1)). The drawn Ca are 4 in the cell, 7 corners and one more face image for each face Ca: 14.
    /// </summary>
    static CrystalSceneOptions WithBoundary() => Options() with { ShowBoundaryAtoms = true };

    const int CaImages = 14;

    [Fact]
    public void Build_HideVertices_KeepsTheCenter()
    {
        CrystalScene scene = Builder.Build(Fixtures.FluoriteWithCubes(),
            WithBoundary() with { ShowPolyhedronVertices = false, IncludeLabels = true });

        var atoms = scene.Scene.Items.OfType<SphereItem>().Select(x => scene.Lookup(x.Id)!).ToList();
        Assert.Equal(CaImages, atoms.Count);
        Assert.All(atoms, a => Assert.True(a.AtomIndex < 4, $"atom {a.AtomIndex} is not a Ca"));
        Assert.Equal(4, scene.Scene.Items.OfType<LabelItem>().Count());
        Assert.All(scene.Scene.Items.OfType<LabelItem>(), l => Assert.Equal("Ca1", l.Text));
        Assert.Empty(scene.Scene.Items.OfType<RodItem>());
        Assert.Equal(CaImages, Polyhedra(scene).Count);
    }

    [Fact]
    public void Build_HideInsideBonds_DropsCenterToVertexRods()
    {
        // Every fluorite bond is Ca-F, inside the cubes: with the option on, they are there.
        CrystalScene on = Builder.Build(Fixtures.FluoriteWithCubes(), WithBoundary());
        CrystalScene off = Builder.Build(Fixtures.FluoriteWithCubes(), WithBoundary() with { ShowBondsInsidePolyhedra = false });

        Assert.NotEmpty(on.Scene.Items.OfType<RodItem>());
        Assert.Empty(off.Scene.Items.OfType<RodItem>());
        Assert.Equal(on.AtomCount, off.AtomCount);
    }

    [Fact]
    public void Build_PolyhedraOnly_DrawsMeshesAndCenters()
    {
        CrystalScene scene = Builder.Build(Fixtures.FluoriteWithCubes(),
            WithBoundary() with { ShowPolyhedronVertices = false, ShowBondsInsidePolyhedra = false });

        Assert.Equal(CaImages, Polyhedra(scene).Count);
        Assert.Equal(CaImages, scene.Scene.Items.OfType<SphereItem>().Count());
        Assert.Empty(scene.Scene.Items.OfType<RodItem>());
        Assert.Equal(CaImages, scene.AtomCount);
    }

    [Fact]
    public void Build_EnabledPolyhedra_DrawsOnlyThose()
    {
        CrystalScene scene = Builder.Build(Fixtures.FluoriteWithCubes(), Options() with { EnabledPolyhedra = new HashSet<int> { 1, 3 } });

        List<MeshItem> meshes = Polyhedra(scene);
        Assert.Equal(2, meshes.Count);
        Assert.Equal([1, 3], meshes.Select(m => scene.Lookup(m.Id)!.PolyhedronIndex).Order());
    }

    [Fact]
    public void Build_PolyhedraWithEllipsoids_CentersAreEllipsoids()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        AtomDisplacement ca = new(null, [0.02, 0.01, 0.005, 0, 0, 0]), f = new(null, [0.01, 0.01, 0.01, 0, 0, 0]);
        s = s with { Atoms = [.. s.Atoms.Select(a => a with { Displacements = [a.Element == "Ca" ? ca : f] })] };

        CrystalScene scene = Builder.Build(s, Options(CrystalStyle.PolyhedraWithEllipsoids));

        Assert.Equal(4, Polyhedra(scene).Count);
        Assert.Equal(4, scene.Scene.Items.OfType<EllipsoidItem>().Count());
        Assert.Equal(ProbabilityScale.For(50), scene.SphereScale, 6);
        Assert.NotEmpty(scene.Scene.Items.OfType<RodItem>());
    }

    [Fact]
    public void Build_BackFacesOff_IsSingleSided()
    {
        CrystalScene both = Builder.Build(Fixtures.FluoriteWithCubes(), Options());
        CrystalScene single = Builder.Build(Fixtures.FluoriteWithCubes(), Options() with { PolyhedronBackFaces = false });

        Assert.All(Polyhedra(both), m => Assert.True(m.DoubleSided));
        Assert.All(Polyhedra(single), m => Assert.False(m.DoubleSided));
    }

    [Fact]
    public void Build_Opacity_SetsTheFaceAlpha()
    {
        CrystalScene scene = Builder.Build(Fixtures.FluoriteWithCubes(), Options() with { PolyhedronOpacity = 0.5 });

        // round(255 x 0.5) = 128; the color is the Ca's (the center), lit like the spheres.
        Assert.All(Polyhedra(scene), m =>
        {
            Assert.Equal(128, m.Color.A);
            Assert.Equal(MeshLighting.KeyLight, m.Lighting);
            Assert.Equal(ElementTable.TryGet("Ca", out ElementInfo ca) ? ca.Color with { A = 128 } : default, m.Color);
        });
    }

    [Theory]
    [InlineData(CrystalStyle.BallAndStick)]
    [InlineData(CrystalStyle.SpaceFilling)]
    [InlineData(CrystalStyle.ThermalEllipsoids)]
    public void Build_OtherStyles_IgnorePolyhedra(CrystalStyle style)
    {
        CrystalScene scene = Builder.Build(Fixtures.FluoriteWithCubes(), Options(style));

        Assert.Empty(Polyhedra(scene));
        Assert.Equal(0, scene.PolyhedronCount);
    }

    [Fact]
    public void Build_RepeatPastTheCeiling_CountsPolyhedra()
    {
        // 50 centers, each with a tetrahedron of images of atom 0: 3 x 3 x 3 gives 1350 spheres, below the ceiling, but
        // 1350 polyhedra count 8 each.
        var atoms = new List<CrystalAtom>();
        for (int i = 0; i < 5; i++)
            for (int j = 0; j < 5; j++)
                for (int k = 0; k < 2; k++)
                    atoms.Add(new CrystalAtom("Ca1", "Ca", 0.1 + 0.18 * i, 0.1 + 0.18 * j, 0.25 + 0.5 * k));
        PolyhedronVertex[] tetrahedron =
        [
            new(0, LatticeShift.Zero), new(0, new LatticeShift(1, 0, 0)), new(0, new LatticeShift(0, 1, 0)), new(0, new LatticeShift(0, 0, 1)),
        ];
        int[][] faces = [[0, 2, 1], [0, 1, 3], [0, 3, 2], [1, 2, 3]];
        var structure = new CrystalStructure(new CrystalCell(10, 10, 10, 90, 90, 90), atoms, [])
        {
            Polyhedra = [.. Enumerable.Range(0, atoms.Count).Select(c => new CrystalPolyhedron(c, tetrahedron, faces))],
        };
        CrystalSceneOptions options = Options() with { RepeatA = 3, RepeatB = 3, RepeatC = 3 };

        CrystalScene polyhedra = Builder.Build(structure, options);
        CrystalScene balls = Builder.Build(structure, options with { Style = CrystalStyle.BallAndStick });

        Assert.NotNull(polyhedra.Warning);
        Assert.Contains("polyhedra", polyhedra.Warning);
        Assert.Equal(50, polyhedra.PolyhedronCount);
        Assert.Null(balls.Warning);
        Assert.Equal(1350, balls.AtomCount);
    }

    [Fact]
    public void Lookup_Polyhedron_ReportsCenterAndIndex()
    {
        // Center given at x = 1: the drawn image is one cell behind the given one.
        CrystalScene scene = Builder.Build(RockSaltOctahedron(centerX: 1), Options());

        MeshItem mesh = Assert.Single(Polyhedra(scene));
        CrystalItemInfo? info = scene.Lookup(mesh.Id);
        Assert.NotNull(info);
        Assert.Equal(CrystalItemKind.Polyhedron, info.Kind);
        Assert.Equal(0, info.AtomIndex);
        Assert.Equal(0, info.PolyhedronIndex);
        Assert.Equal(-1, info.BondIndex);
        Assert.Equal(new LatticeShift(-1, 0, 0), info.Offset);
        AssertOutward(mesh, Vector3.Zero);
    }

    [Fact]
    public void Build_InvalidPolyhedron_IsNotDrawn()
    {
        CrystalStructure s = RockSaltOctahedron();
        CrystalPolyhedron p = s.Polyhedra[0];
        s = s with { Polyhedra = [p, p with { Faces = [.. p.Faces, new[] { 0, 1, 9 }] }, p with { Center = 40 }] };

        CrystalScene scene = Builder.Build(s, Options());

        MeshItem mesh = Assert.Single(Polyhedra(scene));
        Assert.Equal(0, scene.Lookup(mesh.Id)!.PolyhedronIndex);
    }
}
