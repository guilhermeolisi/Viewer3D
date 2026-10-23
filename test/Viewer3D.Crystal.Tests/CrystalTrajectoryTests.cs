namespace Viewer3D.Crystal.Tests;

public class CrystalTrajectoryTests
{
    static readonly TimeSpan Tenth = TimeSpan.FromMilliseconds(100);

    // Water in angstrom: O at the origin, H at 0.9572 A with H-O-H of 104.52 degrees, moved by (dx, 0, 0).
    static CartesianFrame Water(double dx)
    {
        double half = 104.52 / 2 * Math.PI / 180, oh = 0.9572;
        return new CartesianFrame(["O", "H", "H"],
            [dx, 0, 0, dx + oh * Math.Sin(half), oh * Math.Cos(half), 0, dx - oh * Math.Sin(half), oh * Math.Cos(half), 0], null);
    }

    static CrystalTrajectory Read(params CartesianFrame[] frames) => CrystalTrajectory.FromCartesian(frames, [], Tenth);

    static double CartesianDistance(double[] p, int i, int j)
        => Math.Sqrt(Math.Pow(p[3 * i] - p[3 * j], 2) + Math.Pow(p[3 * i + 1] - p[3 * j + 1], 2) + Math.Pow(p[3 * i + 2] - p[3 * j + 2], 2));

    static double Distance(CrystalStructure s, int i, int j) => Fixtures.Distance(s.Cell, s.Atoms[i], s.Atoms[j], LatticeShift.Zero);

    [Fact]
    public void Molecule_LivesInOneBoxForTheWholeTrajectory_WithItsGeometry()
    {
        CrystalTrajectory trajectory = Read(Water(0), Water(3));

        CrystalStructure first = trajectory.Frames[0].Structure, second = trajectory.Frames[1].Structure;
        Assert.False(trajectory.Frames[0].IsPeriodic);
        Assert.Equal(first.Cell, second.Cell);
        // x from -0.757 to 3.757, y from 0 to 0.586, z 0: the extents plus 1 A on each side.
        Assert.Equal(3 + 2 * 0.9572 * Math.Sin(104.52 / 2 * Math.PI / 180) + 2, first.Cell.A, 1e-9);
        Assert.Equal(0.9572 * Math.Cos(104.52 / 2 * Math.PI / 180) + 2, first.Cell.B, 1e-9);
        Assert.Equal(2, first.Cell.C, 1e-9);
        Assert.Equal(0.9572, Distance(first, 0, 1), 1e-9);
        // The second frame is the first moved by 3 A along x.
        Assert.Equal(3, (second.Atoms[0].X - first.Atoms[0].X) * first.Cell.A, 1e-9);
        Assert.Equal(["O1", "H2", "H3"], first.Atoms.Select(a => a.Label));
        Assert.Equal(2, first.Bonds.Count);
    }

    [Fact]
    public void Lattice_GivesFractionalCoordinatesByTheVectorsAsGiven()
    {
        // b along x and a along y (right-handed with c along -z): a Cartesian (2.5, 1, -3) is (0.25, 0.5, 0.5).
        double[] lattice = [0, 4, 0, 5, 0, 0, 0, 0, -6];
        var frame = new CartesianFrame(["Na", "Cl"], [2.5, 1, -3, 2.5, 3, -3], lattice);

        CrystalTrajectory trajectory = Read(frame);

        CrystalStructure s = trajectory.Frames[0].Structure;
        Assert.True(trajectory.Frames[0].IsPeriodic);
        Assert.Equal((4, 5, 6), (s.Cell.A, s.Cell.B, s.Cell.C));
        Assert.Equal(90, s.Cell.Alpha, 1e-9);
        Assert.Equal(90, s.Cell.Beta, 1e-9);
        Assert.Equal(90, s.Cell.Gamma, 1e-9);
        Assert.Equal(0.25, s.Atoms[0].X, 1e-12);
        Assert.Equal(0.5, s.Atoms[0].Y, 1e-12);
        Assert.Equal(0.5, s.Atoms[0].Z, 1e-12);
        Assert.Equal(2, Distance(s, 0, 1), 1e-9);
        Assert.Empty(trajectory.Warnings);
    }

    [Fact]
    public void LeftHandedLattice_KeepsTheDistances_AndWarnsOnce()
    {
        double[] lattice = [4, 0, 0, 1, 5, 0, 0.5, 0.7, -6];
        double[] positions = [0.3, 0.2, -0.5, 2.1, 1.7, -2.9, 3.3, 4.1, -5.2];
        var frame = new CartesianFrame(["O", "H", "H"], positions, lattice);
        var warnings = new List<string>();

        CrystalTrajectory trajectory = CrystalTrajectory.FromCartesian([frame, frame], warnings, Tenth);

        CrystalStructure s = trajectory.Frames[0].Structure;
        Assert.True(s.Cell.IsValid);
        Assert.Equal(CartesianDistance(positions, 0, 1), Distance(s, 0, 1), 1e-9);
        Assert.Equal(CartesianDistance(positions, 0, 2), Distance(s, 0, 2), 1e-9);
        Assert.Equal(CartesianDistance(positions, 1, 2), Distance(s, 1, 2), 1e-9);
        Assert.Contains("left-handed", Assert.Single(trajectory.Warnings));
    }

    [Fact]
    public void ToSequence_DrawsEveryAtom_AndAMoleculeWithoutItsBox()
    {
        CrystalTrajectory molecule = Read(Water(0), Water(1));
        var builder = new CrystalSceneBuilder();
        var options = new CrystalSceneOptions { IncludeLabels = false, ShowBoundaryAtoms = false, CompleteCoordination = false };

        SceneSequence sequence = molecule.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, sequence.Frames.Count);
        Assert.All(sequence.Frames, f => Assert.Equal(3, f.Items.OfType<SphereItem>().Count()));
        Assert.All(sequence.Frames, f => Assert.Empty(f.Items.OfType<LineItem>()));
        Assert.Null(sequence.LowDetailFrames);
        CrystalScene built = builder.Build(molecule.Frames[0].Structure, options, CrystalAppearance.Default with { ShowCell = false });
        Assert.Equal(2, built.Scene.Items.Select(i => built.Lookup(i.Id)).Where(i => i?.Kind == CrystalItemKind.Bond)
            .Select(i => i!.BondIndex).Distinct().Count());
        Assert.Equal(Tenth * 2, sequence.Duration);
    }

    [Fact]
    public void ToSequence_OfAMolecule_DrawsBothBondsInEveryFrame()
    {
        CrystalTrajectory molecule = Read(Water(0), Water(1));
        var builder = new CrystalSceneBuilder();
        var options = new CrystalSceneOptions { IncludeLabels = false, ShowBoundaryAtoms = false, CompleteCoordination = false };

        SceneSequence sequence = molecule.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);

        for (int k = 0; k < molecule.Frames.Count; k++)
            Assert.Equal(2, TrajectoryBonds.InFrame(builder, molecule.Frames[k], options, sequence.Frames[k]));
    }

    [Fact]
    public void ToSequence_OfACrystal_KeepsTheCellEdges_AndBuildsLowDetailForPolyhedra()
    {
        var frame = new CartesianFrame(["Na", "Cl"], [0, 0, 0, 2.82, 0, 0], [5.64, 0, 0, 0, 5.64, 0, 0, 0, 5.64]);
        CrystalTrajectory crystal = Read(frame, frame);
        var builder = new CrystalSceneBuilder();

        SceneSequence plain = crystal.ToSequence(builder, new CrystalSceneOptions(), lowDetail: true,
            cancellationToken: TestContext.Current.CancellationToken);
        SceneSequence polyhedra = crystal.ToSequence(builder, new CrystalSceneOptions { Style = CrystalStyle.Polyhedra }, lowDetail: true,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotEmpty(plain.Frames[0].Items.OfType<LineItem>());
        Assert.Null(plain.LowDetailFrames);
        Assert.Equal(2, polyhedra.LowDetailFrames!.Count(f => f is not null));
    }

    [Fact]
    public void ToSequence_PastTheCeilingFromTheAtomCount_RefusesBeforeBuilding()
    {
        CrystalTrajectory molecule = Read(Water(0), Water(1), Water(2));

        var refused = Assert.Throws<SceneSequenceTooLargeException>(() => molecule.ToSequence(new CrystalSceneBuilder(),
            new CrystalSceneOptions(), maxWeight: 8, cancellationToken: TestContext.Current.CancellationToken));

        // Three atoms a frame at least: the third frame passes 8 before anything is built.
        Assert.Equal(9, refused.Weight);
    }

    [Fact]
    public void ToSequence_PastTheCeilingOnceBuilt_StopsAtTheFrameThatPassesIt()
    {
        CrystalTrajectory molecule = Read(Water(0), Water(1), Water(2));
        var builder = new CrystalSceneBuilder();
        var options = new CrystalSceneOptions();
        long one = SceneSequence.WeightOf(builder.Build(molecule.Frames[0].Structure, options,
            CrystalAppearance.Default with { ShowCell = false }).Scene);
        Assert.True(one > 3);

        var refused = Assert.Throws<SceneSequenceTooLargeException>(() => molecule.ToSequence(builder, options, maxWeight: one + 1,
            cancellationToken: TestContext.Current.CancellationToken));

        // Refused at the second frame, with the weight of two built frames, so the third was never built.
        Assert.Equal(2 * one, refused.Weight);
    }

    [Fact]
    public void ToSequence_Canceled_Throws()
    {
        CrystalTrajectory molecule = Read(Water(0), Water(1));

        Assert.ThrowsAny<OperationCanceledException>(() => molecule.ToSequence(new CrystalSceneBuilder(), new CrystalSceneOptions(),
            cancellationToken: new CancellationToken(canceled: true)));
    }
}
