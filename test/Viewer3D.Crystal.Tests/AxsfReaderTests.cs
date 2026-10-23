using System.Globalization;

namespace Viewer3D.Crystal.Tests;

public class AxsfReaderTests
{
    // NaCl, a = 5.64 A, fixed conventional cell, two steps; the second moves the first Na 0.1 A along x. Atoms by
    // atomic number, with the three force columns of the XCrySDen layout (read and ignored).
    const string SaltFixedCell = """
        # NaCl, two steps
        ANIMSTEPS 2
        CRYSTAL
        PRIMVEC
          5.64 0.00 0.00
          0.00 5.64 0.00
          0.00 0.00 5.64
        CONVVEC
          5.64 0.00 0.00
          0.00 5.64 0.00
          0.00 0.00 5.64
        PRIMCOORD 1
        8 1
        11 0.00 0.00 0.00   0.01 0.00 0.00
        11 0.00 2.82 2.82   0.00 0.00 0.00
        11 2.82 0.00 2.82   0.00 0.00 0.00
        11 2.82 2.82 0.00   0.00 0.00 0.00
        17 2.82 0.00 0.00   0.00 0.00 0.00
        17 2.82 2.82 2.82   0.00 0.00 0.00
        17 0.00 0.00 2.82   0.00 0.00 0.00
        17 0.00 2.82 0.00   0.00 0.00 0.00
        PRIMCOORD 2
        8 1
        11 0.10 0.00 0.00   0.00 0.00 0.00
        11 0.00 2.82 2.82   0.00 0.00 0.00
        11 2.82 0.00 2.82   0.00 0.00 0.00
        11 2.82 2.82 0.00   0.00 0.00 0.00
        17 2.82 0.00 0.00   0.00 0.00 0.00
        17 2.82 2.82 2.82   0.00 0.00 0.00
        17 0.00 0.00 2.82   0.00 0.00 0.00
        17 0.00 2.82 0.00   0.00 0.00 0.00
        """;

    // Variable cell: PRIMVEC i before each PRIMCOORD i, a of 4 A and then 4.4 A; symbols instead of numbers.
    const string VariableCell = """
        ANIMSTEPS 2
        CRYSTAL
        PRIMVEC 1
          4.0 0.0 0.0
          0.0 4.0 0.0
          0.0 0.0 4.0
        PRIMCOORD 1
        2 1
        Cs 0.0 0.0 0.0
        Cl 2.0 2.0 2.0
        PRIMVEC 2
          4.4 0.0 0.0
          0.0 4.4 0.0
          0.0 0.0 4.4
        PRIMCOORD 2
        2 1
        Cs 0.0 0.0 0.0
        Cl 2.2 2.2 2.2
        """;

    // Water in ATOMS i, without a count: the atoms of a step run to the next keyword.
    const string WaterMolecule = """
        ANIMSTEPS 2
        ATOMS 1
        8  0.000000 0.000000 0.000000
        1  0.756950 0.585882 0.000000
        1 -0.756950 0.585882 0.000000
        ATOMS 2
        O  0.500000 0.000000 0.000000
        H  1.256950 0.585882 0.000000
        H -0.256950 0.585882 0.000000
        """;

    static CrystalTrajectory Read(string text, TrajectoryReadOptions? options = null)
        => AxsfReader.Read(new StringReader(text), options, TestContext.Current.CancellationToken);

    static TrajectoryReadException Refused(string text, TrajectoryReadOptions? options = null)
        => Assert.Throws<TrajectoryReadException>(() => Read(text, options));

    static double Distance(CrystalStructure s, int i, int j, LatticeShift shift = default)
        => Fixtures.Distance(s.Cell, s.Atoms[i], s.Atoms[j], shift);

    [Fact]
    public void FixedCell_TwoSteps_ForcesIgnored_AndSixClPerNa()
    {
        CrystalTrajectory trajectory = Read(SaltFixedCell);

        Assert.Equal(2, trajectory.Frames.Count);
        Assert.All(trajectory.Frames, f => Assert.True(f.IsPeriodic));
        CrystalStructure first = trajectory.Frames[0].Structure;
        Assert.Equal(5.64, first.Cell.A, 1e-9);
        Assert.Equal(["Na", "Na", "Na", "Na", "Cl", "Cl", "Cl", "Cl"], first.Atoms.Select(a => a.Element));
        Assert.Equal(0.5, first.Atoms[1].Y, 1e-9);
        Assert.Equal(24, first.Bonds.Count);
        for (int na = 0; na < 4; na++)
            Assert.Equal(6, first.Bonds.Count(b => b.First == na || b.Second == na));
        Assert.Equal(0.1 / 5.64, trajectory.Frames[1].Structure.Atoms[0].X, 1e-9);
        Assert.Equal(5.64, trajectory.Frames[1].Structure.Cell.A, 1e-9);
        Assert.Empty(trajectory.Warnings);
    }

    [Fact]
    public void VariableCell_EachStepTakesItsOwnPrimvec()
    {
        CrystalTrajectory trajectory = Read(VariableCell);

        Assert.Equal([4.0, 4.4], trajectory.Frames.Select(f => Math.Round(f.Structure.Cell.A, 9)));
        Assert.All(trajectory.Frames, f => Assert.Equal(0.5, f.Structure.Atoms[1].Z, 1e-9));
        Assert.Equal(["Cs", "Cl"], trajectory.Frames[1].Structure.Atoms.Select(a => a.Element));
        Assert.Equal(Math.Sqrt(3) * 2.2, Distance(trajectory.Frames[1].Structure, 0, 1), 1e-9);
    }

    [Fact]
    public void Atoms_IsAMoleculeWithTwoOHBondsPerStep()
    {
        CrystalTrajectory trajectory = Read(WaterMolecule);

        Assert.Equal(2, trajectory.Frames.Count);
        Assert.All(trajectory.Frames, f => Assert.False(f.IsPeriodic));
        Assert.All(trajectory.Frames, f => Assert.Equal(["O", "H", "H"], f.Structure.Atoms.Select(a => a.Element)));
        Assert.All(trajectory.Frames, f => Assert.Equal([(0, 1), (0, 2)], f.Structure.Bonds.Select(b => (b.First, b.Second))));
        Assert.Equal(0.9572, Distance(trajectory.Frames[1].Structure, 0, 1), 1e-5);
    }

    [Fact]
    public void StillXsf_CommentsAndADataGrid_AreOneFrame()
    {
        string text = """
            # a still XSF file
            CRYSTAL
            PRIMVEC
              3 0 0
              0 3 0
              0 0 3

            PRIMCOORD
            1 1
            Po 0 0 0
            BEGIN_BLOCK_DATAGRID_3D
              density
              BEGIN_DATAGRID_3D_rho
                2 2 2
              END_DATAGRID_3D
            END_BLOCK_DATAGRID_3D
            """;

        CrystalTrajectoryFrame frame = Assert.Single(Read(text).Frames);

        Assert.True(frame.IsPeriodic);
        Assert.Equal("Po", Assert.Single(frame.Structure.Atoms).Element);
    }

    [Fact]
    public void AnimstepsThatDisagree_Warns()
    {
        string text = SaltFixedCell.Replace("ANIMSTEPS 2", "ANIMSTEPS 3", StringComparison.Ordinal);

        CrystalTrajectory trajectory = Read(text);

        Assert.Equal(2, trajectory.Frames.Count);
        Assert.Contains("ANIMSTEPS gives 3 steps; the file holds 2.", trajectory.Warnings);
    }

    [Theory]
    [InlineData("CRYSTAL\nPRIMCOORD 1\n1 1\nNa 0 0 0\n", 2)]
    [InlineData("CRYSTAL\nSPACEGROUP 225\n", 2)]
    [InlineData("CRYSTAL\nPRIMVEC\n3 0 0\n0 3\n0 0 3\n", 4)]
    [InlineData("CRYSTAL\nPRIMVEC\n3 0 0\n0 3 0\n3 3 0\n", 5)]
    [InlineData("CRYSTAL\nPRIMVEC\n3 0 0\n0 3 0\n0 0 3\nPRIMCOORD\n2 1\nNa 0 0 0\n", 8)]
    [InlineData("ATOMS\nO 0 0\n", 2)]
    [InlineData("ATOMS\nO 0 zero 0\n", 2)]
    [InlineData("ANIMSTEPS two\n", 1)]
    [InlineData("CRYSTAL\nPRIMVEC x\n3 0 0\n0 3 0\n0 0 3\n", 2)]
    [InlineData("BEGIN_INFO\nsomething\n", 2)]
    public void Malformed_IsRefusedWithItsLine(string text, int line)
    {
        TrajectoryReadException refused = Refused(text);

        Assert.Equal(TrajectoryReadError.Malformed, refused.Reason);
        Assert.Equal(line, refused.Line);
        Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"Line {line}: "), refused.Message);
    }

    [Fact]
    public void NoCoordinates_IsEmpty()
    {
        Assert.Equal(TrajectoryReadError.Empty, Refused("ANIMSTEPS 1\nCRYSTAL\nPRIMVEC\n3 0 0\n0 3 0\n0 0 3\n").Reason);
    }

    [Fact]
    public void TooManyAtoms_IsRefused_CountedOrNot()
    {
        var two = TrajectoryReadOptions.Default with { MaxAtomsPerFrame = 2 };

        TrajectoryReadException counted = Refused("CRYSTAL\nPRIMVEC\n3 0 0\n0 3 0\n0 0 3\nPRIMCOORD\n3 1\n", two);
        TrajectoryReadException uncounted = Refused("ATOMS\nH 0 0 0\nH 1 0 0\nH 2 0 0\n", two);

        Assert.Equal((TrajectoryReadError.TooManyAtoms, 3L, 2L), (counted.Reason, counted.Count, counted.Limit));
        Assert.Equal((TrajectoryReadError.TooManyAtoms, 3L, 2L), (uncounted.Reason, uncounted.Count, uncounted.Limit));
    }

    [Fact]
    public void PastTheSizeCeiling_IsRefused_FromAFile()
    {
        string path = Path.Combine(Directory.CreateTempSubdirectory("viewer3d-axsf-").FullName, "salt.axsf");
        try
        {
            File.WriteAllText(path, SaltFixedCell);
            var refused = Assert.Throws<TrajectoryReadException>(() => AxsfReader.Read(path,
                TrajectoryReadOptions.Default with { MaxFileBytes = 100 }, TestContext.Current.CancellationToken));
            Assert.Equal(TrajectoryReadError.FileTooLarge, refused.Reason);
            Assert.Equal(2, AxsfReader.Read(path, cancellationToken: TestContext.Current.CancellationToken).Frames.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Canceled_Throws()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => AxsfReader.Read(new StringReader(SaltFixedCell), null,
            new CancellationToken(canceled: true)));
    }

    [Fact]
    public void ReadThenBuilt_EveryAtomOfEveryStepIsDrawn_AndEveryBondOfTheGeometry()
    {
        // Interaction: the text read by the reader is built by the real builder, step by step.
        var builder = new CrystalSceneBuilder();
        var options = new CrystalSceneOptions { IncludeLabels = false, ShowBoundaryAtoms = false, CompleteCoordination = false };
        CrystalTrajectory salt = Read(SaltFixedCell);
        CrystalTrajectory water = Read(WaterMolecule);

        SceneSequence saltFrames = salt.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);
        SceneSequence waterFrames = water.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, saltFrames.Frames.Count);
        Assert.All(saltFrames.Frames, f => Assert.Equal(8, f.Items.OfType<SphereItem>().Count()));
        Assert.All(waterFrames.Frames, f => Assert.Equal(3, f.Items.OfType<SphereItem>().Count()));
        // With the whole coordination, each of the 24 bonds of NaCl shows in at least one rod, in both steps.
        foreach (CrystalTrajectoryFrame frame in salt.Frames)
        {
            CrystalScene built = builder.Build(frame.Structure, options with { CompleteCoordination = true });
            Assert.Equal(24, built.Scene.Items.Select(i => built.Lookup(i.Id)).Where(i => i?.Kind == CrystalItemKind.Bond)
                .Select(i => i!.BondIndex).Distinct().Count());
        }
    }

    [Fact]
    public void ReadThenSequenced_EveryStepOfTheSequenceDrawsEveryBond()
    {
        // The rods are counted in the frames ToSequence returns, each id resolved by a build with the options it applies.
        var builder = new CrystalSceneBuilder();
        var options = new CrystalSceneOptions { IncludeLabels = false, ShowBoundaryAtoms = false, CompleteCoordination = false };
        CrystalTrajectory salt = Read(SaltFixedCell);
        CrystalTrajectory water = Read(WaterMolecule);
        CrystalSceneOptions whole = options with { CompleteCoordination = true };

        SceneSequence saltFrames = salt.ToSequence(builder, whole, cancellationToken: TestContext.Current.CancellationToken);
        SceneSequence waterFrames = water.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, saltFrames.Frames.Count);
        for (int k = 0; k < salt.Frames.Count; k++)
            Assert.Equal(24, TrajectoryBonds.InFrame(builder, salt.Frames[k], whole, saltFrames.Frames[k]));
        Assert.Equal(water.Frames.Count, waterFrames.Frames.Count);
        for (int k = 0; k < water.Frames.Count; k++)
            Assert.Equal(2, TrajectoryBonds.InFrame(builder, water.Frames[k], options, waterFrames.Frames[k]));
    }
}
