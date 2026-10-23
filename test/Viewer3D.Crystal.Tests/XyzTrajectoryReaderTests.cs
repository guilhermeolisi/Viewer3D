using System.Globalization;

namespace Viewer3D.Crystal.Tests;

public class XyzTrajectoryReaderTests
{
    // Agua: O-H 0,9572 A, H-O-H 104,52 graus; o segundo quadro desloca a molecula 0,5 A em x.
    const string Water = """
        3
        water, frame 1
        O    0.000000    0.000000    0.000000
        H    0.756950    0.585882    0.000000
        H   -0.756950    0.585882    0.000000
        3
        water, frame 2
        O    0.500000    0.000000    0.000000
        H    1.256950    0.585882    0.000000
        H   -0.256950    0.585882    0.000000
        """;

    // NaCl, a = 5,64 A, celula convencional. Escrito a mao no leiaute do ase.io.write(format="extxyz") (sem o ASE nesta
    // maquina): Lattice e pbc entre aspas, Properties com as forcas, uma energia do calculo. Um arquivo de corrida real
    // fica para a validacao manual.
    const string SaltExtended = """
        8
        Lattice="5.64 0.0 0.0 0.0 5.64 0.0 0.0 0.0 5.64" Properties=species:S:1:pos:R:3:forces:R:3 energy=-27.41 pbc="T T T"
        Na       0.00000000       0.00000000       0.00000000       0.01000000       0.00000000       0.00000000
        Na       0.00000000       2.82000000       2.82000000       0.00000000       0.00000000       0.00000000
        Na       2.82000000       0.00000000       2.82000000       0.00000000       0.00000000       0.00000000
        Na       2.82000000       2.82000000       0.00000000       0.00000000       0.00000000       0.00000000
        Cl       2.82000000       0.00000000       0.00000000       0.00000000       0.00000000       0.00000000
        Cl       2.82000000       2.82000000       2.82000000       0.00000000       0.00000000       0.00000000
        Cl       0.00000000       0.00000000       2.82000000       0.00000000       0.00000000       0.00000000
        Cl       0.00000000       2.82000000       0.00000000       0.00000000       0.00000000       0.00000000
        """;

    static CrystalTrajectory Read(string text, TrajectoryReadOptions? options = null)
        => XyzTrajectoryReader.Read(new StringReader(text), options, TestContext.Current.CancellationToken);

    static TrajectoryReadException Refused(string text, TrajectoryReadOptions? options = null)
        => Assert.Throws<TrajectoryReadException>(() => Read(text, options));

    static double Distance(CrystalStructure s, int i, int j, LatticeShift shift = default)
        => Fixtures.Distance(s.Cell, s.Atoms[i], s.Atoms[j], shift);

    [Fact]
    public void Water_TwoFrames_AMoleculeWithTwoOHBonds()
    {
        CrystalTrajectory trajectory = Read(Water);

        Assert.Equal(2, trajectory.Frames.Count);
        Assert.All(trajectory.Frames, f => Assert.False(f.IsPeriodic));
        CrystalStructure first = trajectory.Frames[0].Structure;
        Assert.Equal(["O", "H", "H"], first.Atoms.Select(a => a.Element));
        Assert.Equal(0.9572, Distance(first, 0, 1), 1e-5);
        Assert.Equal([(0, 1), (0, 2)], first.Bonds.Select(b => (b.First, b.Second)));
        Assert.Equal(TrajectoryReadOptions.Default.FrameDuration, trajectory.FrameDuration);
        Assert.Empty(trajectory.Warnings);
    }

    [Fact]
    public void AtomCount_MayChangeFromFrameToFrame()
    {
        CrystalTrajectory trajectory = Read("3\n\nO 0 0 0\nH 0.96 0 0\nH -0.24 0.93 0\n\n2\nhydroxyl\nO 0 0 0\nH 0.97 0 0\n");

        Assert.Equal([3, 2], trajectory.Frames.Select(f => f.Structure.Atoms.Count));
    }

    [Fact]
    public void ExtendedXyz_GivesTheCell_TheFractionalPositions_AndSixClPerNa()
    {
        CrystalTrajectory trajectory = Read(SaltExtended);

        CrystalTrajectoryFrame frame = Assert.Single(trajectory.Frames);
        CrystalStructure salt = frame.Structure;
        Assert.True(frame.IsPeriodic);
        Assert.Equal(5.64, salt.Cell.A, 1e-9);
        Assert.Equal(90, salt.Cell.Gamma, 1e-9);
        Assert.Equal(0.5, salt.Atoms[1].Y, 1e-9);
        Assert.Equal(0.5, salt.Atoms[1].Z, 1e-9);
        Assert.Equal(24, salt.Bonds.Count);
        for (int na = 0; na < 4; na++)
            Assert.Equal(6, salt.Bonds.Count(b => b.First == na || b.Second == na));
        Assert.All(salt.Bonds, b => Assert.Equal(2.82, Distance(salt, b.First, b.Second, b.Shift), 1e-6));
    }

    [Fact]
    public void ExtendedXyz_PbcFalse_IsAMolecule()
    {
        string text = "2\nLattice=\"10 0 0 0 10 0 0 0 10\" pbc=\"F F F\"\nO 0 0 0\nH 0.96 0 0\n";

        Assert.False(Assert.Single(Read(text).Frames).IsPeriodic);
    }

    [Fact]
    public void ExtendedXyz_RotatedLattice_UsesTheVectorsAsGiven()
    {
        // b along x and a along y, c along -z: (2,5; 1; -3) is (0,25; 0,5; 0,5) of a 4 x 5 x 6 cell.
        string text = "2\nLattice=\"0 4 0 5 0 0 0 0 -6\" Properties=pos:R:3:Z:I:1\n2.5 1 -3 11\n2.5 3 -3 17\n";

        CrystalStructure s = Assert.Single(Read(text).Frames).Structure;

        Assert.Equal((4, 5, 6), (s.Cell.A, s.Cell.B, s.Cell.C));
        Assert.Equal(0.25, s.Atoms[0].X, 1e-12);
        Assert.Equal(0.5, s.Atoms[0].Y, 1e-12);
        Assert.Equal(0.5, s.Atoms[0].Z, 1e-12);
        Assert.Equal(["Na", "Cl"], s.Atoms.Select(a => a.Element));
        Assert.Equal(2, Distance(s, 0, 1), 1e-9);
    }

    [Fact]
    public void Elements_AsNumbersOrInAnyCase_AndAnUnknownOneWarnsOnce()
    {
        CrystalTrajectory trajectory = Read("4\n\n8 0 0 0\nh 0.96 0 0\nQq 3 0 0\nQq 5 0 0\n");

        Assert.Equal(["O", "H", "Qq", "Qq"], trajectory.Frames[0].Structure.Atoms.Select(a => a.Element));
        Assert.Contains("'Qq'", Assert.Single(trajectory.Warnings));
    }

    [Theory]
    [InlineData("x\n\nO 0 0 0\n", 1)]
    [InlineData("2\n\nO 0 0 0\nH 0.96 0\n", 4)]
    [InlineData("3\n\nO 0 0 0\nH 0.96 0 0\n", 4)]
    [InlineData("1\n\nO 0 zero 0\n", 3)]
    [InlineData("1\nLattice=\"5 0 0 0 5 0 0 0\"\nO 0 0 0\n", 2)]
    [InlineData("1\nLattice=\"5 0 0 0 5 0 1 1 0\"\nO 0 0 0\n", 2)]
    [InlineData("1\nProperties=species:S:1\nO 0 0 0\n", 2)]
    [InlineData("1\n", 1)]
    public void Malformed_IsRefusedWithItsLine(string text, int line)
    {
        TrajectoryReadException refused = Refused(text);

        Assert.Equal(TrajectoryReadError.Malformed, refused.Reason);
        Assert.Equal(line, refused.Line);
        Assert.StartsWith(string.Create(CultureInfo.InvariantCulture, $"Line {line}: "), refused.Message);
    }

    [Fact]
    public void NoFrame_IsEmpty()
    {
        Assert.Equal(TrajectoryReadError.Empty, Refused("\n  \n").Reason);
    }

    [Fact]
    public void TooManyAtoms_IsRefusedBeforeReadingThem()
    {
        TrajectoryReadException refused = Refused("1000001\n\n", TrajectoryReadOptions.Default with { MaxAtomsPerFrame = 1_000_000 });

        Assert.Equal(TrajectoryReadError.TooManyAtoms, refused.Reason);
        Assert.Equal(1_000_001, refused.Count);
        Assert.Equal(1_000_000, refused.Limit);
    }

    [Fact]
    public void PastTheSizeCeiling_IsRefused_FromTextAndFromAFile()
    {
        var small = TrajectoryReadOptions.Default with { MaxFileBytes = 40 };
        Assert.Equal(TrajectoryReadError.FileTooLarge, Refused(Water, small).Reason);

        string path = Path.Combine(Directory.CreateTempSubdirectory("viewer3d-xyz-").FullName, "water.xyz");
        try
        {
            File.WriteAllText(path, Water);
            var refused = Assert.Throws<TrajectoryReadException>(() => XyzTrajectoryReader.Read(path, small, TestContext.Current.CancellationToken));
            Assert.Equal(TrajectoryReadError.FileTooLarge, refused.Reason);
            Assert.Equal(new FileInfo(path).Length, refused.Count);
            Assert.Equal(2, XyzTrajectoryReader.Read(path, cancellationToken: TestContext.Current.CancellationToken).Frames.Count);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(path)!, true);
        }
    }

    [Fact]
    public void Canceled_Throws()
    {
        Assert.ThrowsAny<OperationCanceledException>(() => XyzTrajectoryReader.Read(new StringReader(Water), null,
            new CancellationToken(canceled: true)));
    }

    [Fact]
    public void ReadThenBuilt_EveryAtomOfTheFileIsDrawn_AndEveryBondOfTheGeometry()
    {
        // Interacao: o texto lido pelo leitor e montado pelo builder de verdade, quadro a quadro.
        var builder = new CrystalSceneBuilder();
        var options = new CrystalSceneOptions { IncludeLabels = false, ShowBoundaryAtoms = false, CompleteCoordination = false };
        CrystalTrajectory water = Read(Water);
        CrystalTrajectory salt = Read(SaltExtended);

        SceneSequence waterFrames = water.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);
        SceneSequence saltFrames = salt.ToSequence(builder, options, cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(waterFrames.Frames, f => Assert.Equal(3, f.Items.OfType<SphereItem>().Count()));
        Assert.Equal(8, saltFrames.Frames[0].Items.OfType<SphereItem>().Count());
        // Com a coordenacao completa, cada uma das 24 ligacoes do NaCl aparece em ao menos um bastao.
        CrystalScene built = builder.Build(salt.Frames[0].Structure, options with { CompleteCoordination = true });
        Assert.Equal(24, built.Scene.Items.Select(i => built.Lookup(i.Id)).Where(i => i?.Kind == CrystalItemKind.Bond)
            .Select(i => i!.BondIndex).Distinct().Count());
        CrystalScene molecule = builder.Build(water.Frames[1].Structure, options, CrystalAppearance.Default with { ShowCell = false });
        Assert.Equal(2, molecule.Scene.Items.Select(i => molecule.Lookup(i.Id)).Where(i => i?.Kind == CrystalItemKind.Bond)
            .Select(i => i!.BondIndex).Distinct().Count());
    }
}
