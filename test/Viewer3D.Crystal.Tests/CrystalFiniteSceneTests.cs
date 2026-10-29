using System.Collections.Immutable;
using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class CrystalFiniteSceneTests
{
    static readonly CrystalSceneBuilder Builder = new();
    // Scene positions use floats, including the oblique-cell conversion, within 1e-4 angstrom.
    const float Tolerance = 1e-4f;
    static readonly CrystalCell Cell = new(3, 4, 5, 70, 80, 75);
    static readonly CrystalSceneOptions Finite = new()
    {
        Clip = CrystalClip.Finite,
        RepeatA = 3,
        RepeatB = 2,
        RepeatC = 4,
        ShowBoundaryAtoms = true,
        CompleteCoordination = true,
    };

    static CrystalStructure Structure() => new(Cell,
        [new("O1", "O", -1.2, 0.25, 0.4) { SourceIndex = 42 }, new("O2", "O", 2.2, 1.4, -0.1)],
        [new(0, 1, LatticeShift.Zero)]);

    [Theory]
    [InlineData(CrystalStyle.BallAndStick)]
    [InlineData(CrystalStyle.SpaceFilling)]
    [InlineData(CrystalStyle.ThermalEllipsoids)]
    public void SuppliedCoordinates_AreNeitherWrappedNorRepeated(CrystalStyle style)
    {
        CrystalStructure source = Structure();
        CrystalScene scene = Builder.Build(source, Finite with { Style = style });
        Assert.Equal(Cell, scene.Cell);
        Assert.Equal(2, scene.AtomCount);
        foreach (SphereItem sphere in scene.Scene.Items.OfType<SphereItem>())
        {
            CrystalItemInfo info = scene.Lookup(sphere.Id)!;
            CrystalAtom atom = source.Atoms[info.AtomIndex];
            Assert.Equal(LatticeShift.Zero, info.Offset);
            Assert.False(info.IsCompletion);
            Assert.InRange(Vector3.Distance(sphere.Center, Cell.ToCartesian(atom.X, atom.Y, atom.Z)), 0, Tolerance);
        }
        Assert.Equal(42, source.Atoms[scene.Lookup(scene.Scene.Items.OfType<SphereItem>().First().Id)!.AtomIndex].SourceIndex);
        Assert.Equal(12, scene.Scene.Items.OfType<LineItem>().Count());
        Assert.InRange(Vector3.Distance(Cell.BVector, scene.Axes[1].Direction), 0, Tolerance);
    }

    [Fact]
    public void Bonds_JoinSuppliedPositions_WithoutPeriodicCompletion()
    {
        CrystalStructure source = Structure();
        source = source with { Bonds = [.. source.Bonds, new(0, 1, new LatticeShift(1, 0, 0))] };
        CrystalScene scene = Builder.Build(source, Finite);
        RodItem rod = Assert.Single(scene.Scene.Items.OfType<RodItem>());
        CrystalAtom a = source.Atoms[0], b = source.Atoms[1];
        Assert.InRange(Vector3.Distance(rod.Start, Cell.ToCartesian(a.X, a.Y, a.Z)), 0, Tolerance);
        Assert.InRange(Vector3.Distance(rod.End, Cell.ToCartesian(b.X, b.Y, b.Z)), 0, Tolerance);
        Assert.Equal(2, scene.AtomCount);
    }

    [Fact]
    public void Cost_IsOfTheFiniteSet_AndDoesNotFallBackToACell()
    {
        CrystalStructure source = Structure();
        CrystalSceneOptions tooSmall = Finite with { MaxCost = 1 };
        Assert.Equal(2, Builder.Estimate(source, tooSmall).Cost);
        Assert.False(Builder.TryBuild(source, tooSmall, null, out CrystalScene? rejected, out CrystalSceneEstimate estimate));
        Assert.Null(rejected);
        Assert.False(estimate.Fit);
        Assert.Equal(new LatticeShift(1, 1, 1), estimate.Fits);
        CrystalScene refused = Builder.Build(source, tooSmall);
        Assert.Empty(refused.Scene.Items);
        Assert.Contains("Reduce the number of atoms", refused.Warning);
        Assert.True(Builder.TryBuild(source, Finite with { MaxCost = 2 }, null, out CrystalScene? accepted, out _));
        Assert.Equal(2, accepted!.AtomCount);
    }

    [Fact]
    public void ImageRules_AndVoidMarkers_UseTheOnlyImage()
    {
        CrystalStructure source = Structure();
        source = source with { Atoms = [.. source.Atoms, new("void", "Va", -2, 3, 0) { IsVoid = true }] };
        var app = new CrystalAppearance
        {
            ByImage = ImmutableDictionary<AtomImage, AtomStyle>.Empty
                .Add(new AtomImage(0, LatticeShift.Zero), new AtomStyle { Visible = false }),
        };
        CrystalScene scene = Builder.Build(source, Finite, app);
        Assert.Equal(2, scene.AtomCount);
        Assert.Equal(1, scene.HiddenAtomCount);
        Assert.Equal(2, Builder.Estimate(source, Finite, app).Atoms);
        Assert.Contains(scene.Scene.Items.OfType<SphereItem>(), s =>
            Vector3.Distance(s.Center, Cell.ToCartesian(-2, 3, 0)) < Tolerance);
        Assert.DoesNotContain(scene.Scene.Items.OfType<SphereItem>(), s => scene.Lookup(s.Id)!.AtomIndex == 0);
    }

    [Fact]
    public void SpaceFilling_WithoutBonds_DoesNotUsePeriodicNeighbours()
    {
        var source = new CrystalStructure(new CrystalCell(2, 2, 2, 90, 90, 90),
            [new("O1", "O", -2, 0, 0) { Radius = 1 }, new("O2", "O", 2, 0, 0) { Radius = 1 }], []);
        CrystalScene scene = Builder.Build(source, Finite with { Style = CrystalStyle.SpaceFilling });
        Assert.Equal(4, scene.SphereScale, 1e-12);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FrameSequences_KeepSuppliedPositions(bool periodic)
    {
        CrystalStructure first = Structure();
        CrystalStructure second = first with { Atoms = [.. first.Atoms.Select(a => a with { X = a.X + 0.3 })] };
        var trajectory = new CrystalTrajectory([new(first, periodic), new(second, periodic)], [], TimeSpan.FromMilliseconds(50));
        SceneSequence sequence = trajectory.ToSequence(Builder, Finite, cancellationToken: TestContext.Current.CancellationToken);
        for (int k = 0; k < 2; k++)
        {
            SphereItem[] atoms = sequence.Frames[k].Items.OfType<SphereItem>().ToArray();
            Assert.Equal(2, atoms.Length);
            CrystalAtom atom = trajectory.Frames[k].Structure.Atoms[0];
            Assert.InRange(Vector3.Distance(atoms[0].Center, Cell.ToCartesian(atom.X, atom.Y, atom.Z)), 0, Tolerance);
        }
    }

    [Fact]
    public void DefaultPeriodicMode_StillWrapsAndRepeats()
    {
        CrystalScene scene = Builder.Build(Structure(), new CrystalSceneOptions
        {
            RepeatA = 2,
            ShowBoundaryAtoms = false,
            CompleteCoordination = false,
        });
        Assert.Equal(4, scene.AtomCount);
        Assert.Contains(scene.Scene.Items.OfType<SphereItem>(), s => scene.Lookup(s.Id)!.Offset != LatticeShift.Zero);
    }

    [Fact]
    public void Polyhedra_AndTheirCost_ReferOnlyToSuppliedVertices()
    {
        var p = new CrystalPolyhedron(0,
            [new(1, LatticeShift.Zero), new(2, LatticeShift.Zero), new(3, LatticeShift.Zero)], [[0, 1, 2]]);
        var source = new CrystalStructure(Cell,
            [new("O1", "O", -1, -1, -1), new("O2", "O", -1, 0, -1),
             new("O3", "O", -1, -1, 0), new("O4", "O", 0, -1, -1)], [])
        { Polyhedra = [p] };
        CrystalSceneOptions options = Finite with { Style = CrystalStyle.Polyhedra, ShowPolyhedronVertices = false };
        var appearance = new CrystalAppearance { ShowPolyhedronCenters = false };
        CrystalScene scene = Builder.Build(source, options, appearance);
        Assert.Equal(1, scene.PolyhedronCount);
        Assert.Equal(0, scene.AtomCount);
        Assert.Equal(CrystalSceneOptions.PolyhedronCost, Builder.Estimate(source, options, appearance).Cost);
        MeshItem mesh = Assert.Single(scene.Scene.Items.OfType<MeshItem>());
        Assert.Equal(3, mesh.Positions.Length);
        Assert.InRange(Vector3.Distance(mesh.Positions[0], Cell.ToCartesian(-1, 0, -1)), 0, Tolerance);
        source = source with { Polyhedra = [p with { Vertices = [new(1, new LatticeShift(1, 0, 0)), .. p.Vertices.Skip(1)] }] };
        Assert.Equal(0, Builder.Build(source, options).PolyhedronCount);
        Assert.Equal(4, Builder.Estimate(source, options).Atoms);
    }

    [Fact]
    public void AnnotationsAndOffsets_StayInTheCellCartesianFrame()
    {
        var annotation = new LineItem { Id = -1, Start = new(-10, 2, 3), End = new(12, 4, 5) };
        CrystalStructure source = Structure() with { Annotations = [annotation] };
        CrystalScene scene = Builder.Build(source, Finite with { AtomOffsets = [new(1, 2, 3), Vector3.Zero] });
        Assert.Same(annotation, scene.Scene.Items.Single(i => i.Id == -1));
        Assert.Equal(CrystalItemKind.Annotation, scene.Lookup(-1)!.Kind);
        SphereItem first = scene.Scene.Items.OfType<SphereItem>().Single(s => scene.Lookup(s.Id)!.AtomIndex == 0);
        CrystalAtom atom = source.Atoms[0];
        Assert.InRange(Vector3.Distance(first.Center, Cell.ToCartesian(atom.X, atom.Y, atom.Z) + new Vector3(1, 2, 3)), 0, Tolerance);
    }

    [Fact]
    public void PublicFractionalInverse_PreservesDoublePrecision_WithoutWrapping()
    {
        Cell.Basis().ToCartesian(-2.123456789, 3.987654321, -0.456789123, out double x, out double y, out double z);
        (double u, double v, double w) = Cell.ToFractional(x, y, z);
        Assert.Equal(-2.123456789, u, 1e-12);
        Assert.Equal(3.987654321, v, 1e-12);
        Assert.Equal(-0.456789123, w, 1e-12);
        Assert.Equal((0, 0, 0), new CrystalCell(0, 0, 0, 90, 90, 90).ToFractional(x, y, z));
    }
}
