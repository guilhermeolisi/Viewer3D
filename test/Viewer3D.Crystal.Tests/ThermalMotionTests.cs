using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class ThermalMotionTests
{
    // Tensor anisotropico com termos cruzados, positivo definido (autovalores ~0,0083, 0,0143, 0,0274 A^2).
    static readonly double[] Anisotropic = [0.020, 0.012, 0.018, 0.004, -0.003, 0.006];

    static CrystalStructure OneAtom(params double[]?[] tensors)
        => new(new CrystalCell(5, 5, 5, 90, 90, 90),
            [new CrystalAtom("X1", "Fe", 0.5, 0.5, 0.5) { Displacements = [.. tensors.Select(t => new AtomDisplacement(null, t))] }], []);

    // Media de d d^T sobre o laco, em double a partir dos offsets em float: (11, 22, 33, 12, 13, 23).
    static double[] Covariance(Vector3[][] offsets, int atom)
    {
        var c = new double[6];
        foreach (Vector3[] frame in offsets)
        {
            Vector3 d = frame[atom];
            c[0] += (double)d.X * d.X;
            c[1] += (double)d.Y * d.Y;
            c[2] += (double)d.Z * d.Z;
            c[3] += (double)d.X * d.Y;
            c[4] += (double)d.X * d.Z;
            c[5] += (double)d.Y * d.Z;
        }
        return [.. c.Select(v => v / offsets.Length)];
    }

    [Theory]
    [InlineData(8, 1.0)]
    [InlineData(30, 1.7)]
    [InlineData(120, 3.0)]
    public void OverTheLoop_TheCovarianceIsKSquaredU(int frames, double k)
    {
        Vector3[][] offsets = ThermalMotion.Offsets(OneAtom(Anisotropic), new ThermalMotionOptions { FramesPerPeriod = frames, Amplitude = k });

        double[] c = Covariance(offsets, 0);

        Assert.Equal(frames, offsets.Length);
        // Exata na aritmetica (funcoes ortogonais em N >= 5 pontos): sobra o arredondamento a float, ~2,4e-7 k^2 tr(U)
        // no pior caso; 1e-5 da margem de ~40x e fica quatro ordens abaixo de um sqrt(lambda) no lugar de sqrt(2 lambda).
        double trace = Anisotropic[0] + Anisotropic[1] + Anisotropic[2];
        for (int m = 0; m < 6; m++)
            Assert.InRange(Math.Abs(c[m] - k * k * Anisotropic[m]), 0, 1e-5 * k * k * trace);
    }

    [Fact]
    public void TheLoopIsContinuous_TheLastFrameStepsToTheFirstLikeAnyOther()
    {
        Vector3[][] offsets = ThermalMotion.Offsets(OneAtom(Anisotropic));

        int n = offsets.Length;
        double largest = Enumerable.Range(0, n - 1).Max(f => Vector3.Distance(offsets[f + 1][0], offsets[f][0]));
        double closing = Vector3.Distance(offsets[0][0], offsets[n - 1][0]);

        Assert.True(largest > 0);
        Assert.InRange(closing, 0, 1.0001 * largest);
    }

    [Fact]
    public void SameSeed_SameLoop_OtherSeed_OtherLoop()
    {
        CrystalStructure s = OneAtom(Anisotropic);

        Vector3[][] a = ThermalMotion.Offsets(s);
        Vector3[][] b = ThermalMotion.Offsets(s);
        Vector3[][] c = ThermalMotion.Offsets(s, ThermalMotionOptions.Default with { Seed = 2 });

        Assert.Equal(a.SelectMany(f => f), b.SelectMany(f => f));
        Assert.NotEqual(a.SelectMany(f => f), c.SelectMany(f => f));
    }

    [Fact]
    public void FourTimesU_MovesTwiceAsFar()
    {
        double[] fourfold = [.. Anisotropic.Select(v => 4 * v)];

        static double Rms(Vector3[][] o) => Math.Sqrt(o.Average(f => (double)f[0].LengthSquared()));
        double ratio = Rms(ThermalMotion.Offsets(OneAtom(fourfold))) / Rms(ThermalMotion.Offsets(OneAtom(Anisotropic)));

        Assert.Equal(2, ratio, 1e-5);
    }

    [Fact]
    public void Isotropic_MovesAlikeAlongXYAndZ()
    {
        const double u = 0.01;
        double[] c = Covariance(ThermalMotion.Offsets(OneAtom([u, u, u, 0, 0, 0])), 0);

        for (int m = 0; m < 3; m++)
            Assert.Equal(u, c[m], 1e-8);
        for (int m = 3; m < 6; m++)
            Assert.Equal(0, c[m], 1e-8);
    }

    [Fact]
    public void WithoutATensor_NotPositive_OrVoid_TheAtomStaysAtRest()
    {
        var cell = new CrystalCell(5, 5, 5, 90, 90, 90);
        CrystalAtom moving = new("A", "Fe", 0, 0, 0) { Displacements = [new AtomDisplacement(null, Anisotropic)] };
        var structure = new CrystalStructure(cell,
        [
            moving,
            new CrystalAtom("B", "Fe", 0.5, 0, 0),
            new CrystalAtom("C", "Fe", 0, 0.5, 0) { Displacements = [new AtomDisplacement(null, [0.01, 0.01, -0.01, 0, 0, 0])] },
            new CrystalAtom("D", "Fe", 0, 0, 0.5) { Displacements = [new AtomDisplacement(null, null)] },
            new CrystalAtom("V", "Fe", 0.5, 0.5, 0.5) { IsVoid = true, Displacements = [new AtomDisplacement(null, Anisotropic)] },
        ], []);

        Vector3[][] offsets = ThermalMotion.Offsets(structure);

        Assert.True(ThermalMotion.HasMotion(structure));
        Assert.Contains(offsets, f => f[0] != Vector3.Zero);
        for (int atom = 1; atom < 5; atom++)
            Assert.All(offsets, f => Assert.Equal(Vector3.Zero, f[atom]));
        Assert.False(ThermalMotion.HasMotion(new CrystalStructure(cell, structure.Atoms.Skip(1).ToArray(), [])));
    }

    [Fact]
    public void SharedSite_MovesWithItsFirstValidTensor()
    {
        double[] c = Covariance(ThermalMotion.Offsets(OneAtom(null, Anisotropic, [0.05, 0.05, 0.05, 0, 0, 0])), 0);

        Assert.Equal(Anisotropic[0], c[0], 1e-6);
        Assert.Equal(Anisotropic[5], c[5], 1e-6);
    }

    [Theory]
    [InlineData(0, 30)]
    [InlineData(3.01, 30)]
    [InlineData(double.NaN, 30)]
    [InlineData(1, 7)]
    [InlineData(1, 121)]
    public void OptionsOutOfRange_AreRefused(double amplitude, int frames)
    {
        var options = new ThermalMotionOptions { Amplitude = amplitude, FramesPerPeriod = frames };

        Assert.Throws<ArgumentOutOfRangeException>(() => ThermalMotion.Offsets(OneAtom(Anisotropic), options));
    }

    [Fact]
    public void BuildSequence_OfAStructureAtRest_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => ThermalMotion.BuildSequence(new CrystalSceneBuilder(), Fixtures.Fluorite(),
            new CrystalSceneOptions(), cancellationToken: TestContext.Current.CancellationToken));
    }

    // NaCl, a = 5,64 A, Na no canto (imagens de borda em todas as faces), U isotropico de 0,012 A^2 no Na e anisotropico
    // no Cl, todas as ligacoes Na-Cl.
    static CrystalStructure Salt()
    {
        double[] na = [0.012, 0.012, 0.012, 0, 0, 0];
        var atoms = new List<CrystalAtom>();
        foreach ((double x, double y, double z) in Fixtures.Ca4a)
            atoms.Add(new CrystalAtom("Na1", "Na", x, y, z) { SiteIndex = 0, Displacements = [new AtomDisplacement(null, na)] });
        foreach ((double x, double y, double z) in Fixtures.Ca4a)
        {
            atoms.Add(new CrystalAtom("Cl1", "Cl", (x + 0.5) % 1, y, z)
            {
                SiteIndex = 1,
                Displacements = [new AtomDisplacement(null, Anisotropic)],
            });
        }
        return Fixtures.WithBondsBelow(new CrystalCell(5.64, 5.64, 5.64, 90, 90, 90), atoms, 2.9, (a, b) => a.Element == "Na" && b.Element == "Cl");
    }

    [Theory]
    [InlineData(CrystalStyle.BallAndStick)]
    [InlineData(CrystalStyle.ThermalEllipsoids)]
    public void BuildSequence_EveryFrameHasTheItemsOfTheSceneAtRest_EachAtomMovedByItsOffset(CrystalStyle style)
    {
        // Interacao com o builder de verdade: Na no canto com ShowBoundaryAtoms, o caso em que mover a posicao (e nao o
        // ponto desenhado) faria imagens de borda sumirem e os Id mudarem.
        var builder = new CrystalSceneBuilder();
        CrystalStructure salt = Salt();
        var options = new CrystalSceneOptions { Style = style, ShowBoundaryAtoms = true, CompleteCoordination = true };
        var motion = new ThermalMotionOptions { Amplitude = 3, FramesPerPeriod = 12 };
        CrystalScene rest = builder.Build(salt, options);
        Dictionary<int, Vector3> restCenters = Centers(rest.Scene);
        Vector3[][] offsets = ThermalMotion.Offsets(salt, motion);

        SceneSequence sequence = ThermalMotion.BuildSequence(builder, salt, options, motion: motion,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(12, sequence.Frames.Count);
        Assert.Equal(TimeSpan.FromSeconds(2) / 12, sequence.FrameDurations[0]);
        Assert.Equal(SequenceRepeat.Loop, sequence.Repeat);
        int boundary = rest.Scene.Items.OfType<SphereItem>().Count(s => rest.Lookup(s.Id) is { AtomIndex: 0, IsCompletion: false });
        Assert.Equal(8, boundary);
        for (int f = 0; f < 12; f++)
        {
            Scene frame = sequence.Frames[f];
            Assert.Equal(rest.Scene.Items.Select(i => i.Id), frame.Items.Select(i => i.Id));
            Dictionary<int, Vector3> centers = Centers(frame);
            Assert.Equal(restCenters.Count, centers.Count);
            foreach ((int id, Vector3 center) in centers)
            {
                int atom = rest.Lookup(id)!.AtomIndex;
                // 1e-5 A: o ulp de float perto de 6 A e ~5e-7, a soma de dois vetores em float fica abaixo de 2e-6.
                Assert.InRange(Vector3.Distance(center - restCenters[id], offsets[f][atom]), 0, 1e-5f);
            }
        }
    }

    // Centro de cada esfera e elipsoide de atomo, pelo Id.
    static Dictionary<int, Vector3> Centers(Scene scene)
        => scene.Items.Select(i => i switch
            {
                SphereItem s => (s.Id, (Vector3?)s.Center),
                EllipsoidItem e => (e.Id, e.Center),
                _ => (i.Id, null),
            })
            .Where(x => x.Item2 is not null)
            .ToDictionary(x => x.Id, x => x.Item2!.Value);

    [Fact]
    public void BuildSequence_PastTheWeight_StopsAtTheFrameThatPassesIt()
    {
        var builder = new CrystalSceneBuilder();
        CrystalStructure salt = Salt();
        var options = new CrystalSceneOptions { ShowBoundaryAtoms = true };
        var motion = new ThermalMotionOptions { FramesPerPeriod = 20 };
        long frameWeight = SceneSequence.WeightOf(builder.Build(salt, options).Scene);
        // O teto e o que a estimativa (so as esferas, 20 quadros) deixa passar; os bastoes e a cela fazem o peso
        // montado passar dele antes do ultimo quadro.
        long limit = builder.Estimate(salt, options, null).Atoms * 20L;
        Assert.True(frameWeight * 20 > limit);

        var refused = Assert.Throws<SceneSequenceTooLargeException>(() => ThermalMotion.BuildSequence(builder, salt, options,
            motion: motion, maxWeight: limit, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(limit, refused.Limit);
        Assert.InRange(refused.Weight, limit + 1, limit + frameWeight);
    }

    [Fact]
    public void BuildSequence_LowDetail_ForTheEllipsoids()
    {
        var options = new CrystalSceneOptions { Style = CrystalStyle.ThermalEllipsoids };

        SceneSequence sequence = ThermalMotion.BuildSequence(new CrystalSceneBuilder(), Salt(), options,
            motion: new ThermalMotionOptions { FramesPerPeriod = 8 }, lowDetail: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(8, sequence.LowDetailFrames!.Count);
        Assert.All(sequence.LowDetailFrames, Assert.NotNull);
    }
}
