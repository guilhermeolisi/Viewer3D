using System.Numerics;

namespace Viewer3D.Crystal.Tests;

public class ThermalEllipsoidsTests
{
    static readonly CrystalSceneBuilder Builder = new();

    static CrystalSceneOptions Options(int level = 50) => new()
    {
        Style = CrystalStyle.ThermalEllipsoids,
        ProbabilityLevel = level,
        ShowBoundaryAtoms = false,
        CompleteCoordination = false,
        IncludeLabels = false,
    };

    static CrystalStructure Polonium(params AtomDisplacement[]? displacements)
    {
        CrystalStructure s = Fixtures.Polonium();
        return s with { Atoms = [s.Atoms[0] with { Displacements = displacements }] };
    }

    /// <summary>Fluorita com Ca anisotropico (0,02; 0,01; 0,005 A^2 nos eixos) e F isotropico (0,01 A^2).</summary>
    static CrystalStructure ThermalFluorite()
    {
        CrystalStructure s = Fixtures.Fluorite();
        AtomDisplacement ca = new(null, [0.02, 0.01, 0.005, 0, 0, 0]), f = new(null, [0.01, 0.01, 0.01, 0, 0, 0]);
        return s with { Atoms = [.. s.Atoms.Select(a => a with { Displacements = [a.Element == "Ca" ? ca : f] })] };
    }

    /// <summary>erf pela serie de Taylor: 60 termos bastam ate z = 2 em double.</summary>
    static double Erf(double z)
    {
        double sum = 0, term = z;
        for (int n = 0; n < 60; n++)
        {
            sum += term / (2 * n + 1);
            term *= -z * z / (n + 1);
        }
        return 2 / Math.Sqrt(Math.PI) * sum;
    }

    [Theory]
    [InlineData(30)]
    [InlineData(50)]
    [InlineData(70)]
    [InlineData(90)]
    public void ProbabilityScale_SquaredIsTheChiSquareQuantileWithThreeDegrees(int level)
    {
        // Pega fator trocado: P(chi2_3 <= f^2) = erf(sqrt(x/2)) - sqrt(2x/pi) e^(-x/2), com x = f^2.
        double f = ProbabilityScale.For(level);
        double x = f * f;

        double p = Erf(Math.Sqrt(x / 2)) - Math.Sqrt(2 * x / Math.PI) * Math.Exp(-x / 2);

        // Os fatores tem 4 decimais: 5e-5 em f muda P em menos de 5e-5 (densidade < 0,6 nessa faixa).
        Assert.Equal(level / 100.0, p, 4);
    }

    [Fact]
    public void Isotropic_IsASphereOfTheScaledRootMeanSquare()
    {
        CrystalScene scene = Builder.Build(Polonium(new AtomDisplacement(null, [0.01, 0.01, 0.01, 0, 0, 0])), Options(70));

        SphereItem sphere = Assert.Single(scene.Scene.Items.OfType<SphereItem>());
        Assert.False(sphere.Dashed);
        Assert.Equal((float)(1.9144 * 0.1), sphere.Radius, 5);
        Assert.Equal(1.9144, scene.SphereScale);
        Assert.Equal(CrystalItemKind.Atom, scene.Lookup(sphere.Id)!.Kind);
    }

    [Fact]
    public void Anisotropic_IsAnEllipsoidWithTheScaledRootsOfTheEigenvalues()
    {
        // Tensor com eixos girados de 45 graus no plano xy: autovalores 0,03, 0,01 e 0,005.
        double[] u = [0.02, 0.02, 0.005, 0.01, 0, 0];

        CrystalScene scene = Builder.Build(Polonium(new AtomDisplacement(null, u)), Options());

        EllipsoidItem e = Assert.Single(scene.Scene.Items.OfType<EllipsoidItem>());
        float[] lengths = [.. e.Axes.Select(a => a.Length()).Order()];
        Assert.Equal((float)(1.5382 * Math.Sqrt(0.005)), lengths[0], 5);
        Assert.Equal((float)(1.5382 * Math.Sqrt(0.01)), lengths[1], 5);
        Assert.Equal((float)(1.5382 * Math.Sqrt(0.03)), lengths[2], 5);
        Vector3 longest = e.Axes.MaxBy(a => a.Length());
        Assert.Equal(1f, MathF.Abs(Vector3.Dot(Vector3.Normalize(longest), Vector3.Normalize(new Vector3(1, 1, 0)))), 4);
        Assert.Equal(0f, Vector3.Dot(e.Axes[0], e.Axes[1]), 5);
        Assert.Equal(CrystalItemKind.Atom, scene.Lookup(e.Id)!.Kind);
    }

    [Theory]
    [InlineData(2.5, 2.5)]
    [InlineData(1, 1)]
    [InlineData(0, 1)]
    [InlineData(-2, 1)]
    [InlineData(double.NaN, 1)]
    public void Magnification_ScalesEveryAxisOnTopOfTheProbabilityFactor(double magnification, double expected)
    {
        // Same tensor as above; a magnification that is not finite and positive draws at the probability alone.
        double[] u = [0.02, 0.02, 0.005, 0.01, 0, 0];
        CrystalSceneOptions options = Options() with { DisplacementMagnification = magnification };

        CrystalScene scene = Builder.Build(Polonium(new AtomDisplacement(null, u)), options);

        EllipsoidItem e = Assert.Single(scene.Scene.Items.OfType<EllipsoidItem>());
        float[] lengths = [.. e.Axes.Select(a => a.Length()).Order()];
        Assert.Equal((float)(expected * 1.5382 * Math.Sqrt(0.005)), lengths[0], 5);
        Assert.Equal((float)(expected * 1.5382 * Math.Sqrt(0.03)), lengths[2], 5);
        Assert.Equal(expected * 1.5382, scene.SphereScale, 9);
    }

    [Fact]
    public void Magnification_ShouldNotChangeTheMarkers()
    {
        // The dashed sphere of an atom with no data and the cube are markers in angstrom, not displacements.
        CrystalSceneOptions options = Options() with { DisplacementMagnification = 2.5 };

        CrystalScene scene = Builder.Build(Polonium(new AtomDisplacement(null, [0.01, 0.01, -0.002, 0, 0, 0])), options);

        MeshItem cube = Assert.Single(scene.Scene.Items.OfType<MeshItem>());
        Assert.Equal(0.6f, cube.Positions.Max(p => p.X) - cube.Positions.Min(p => p.X), 5);
    }

    [Fact]
    public void NotPositiveDefinite_IsACube()
    {
        CrystalScene scene = Builder.Build(Polonium(new AtomDisplacement(null, [0.01, 0.01, -0.002, 0, 0, 0])), Options());

        MeshItem cube = Assert.Single(scene.Scene.Items.OfType<MeshItem>());
        Assert.Equal(8, cube.Positions.Length);
        Assert.Equal(12, cube.TriangleCount);
        Assert.Equal(0.6f, cube.Positions.Max(p => p.X) - cube.Positions.Min(p => p.X), 5);
        Assert.Empty(scene.Scene.Items.OfType<EllipsoidItem>());
        Assert.Equal(CrystalItemKind.Atom, scene.Lookup(cube.Id)!.Kind);
    }

    [Fact]
    public void NoData_IsASmallDashedSphere()
    {
        CrystalScene missing = Builder.Build(Polonium(new AtomDisplacement(null, null)), Options());
        CrystalScene none = Builder.Build(Polonium(null), Options());

        foreach (CrystalScene scene in new[] { missing, none })
        {
            SphereItem sphere = Assert.Single(scene.Scene.Items.OfType<SphereItem>());
            Assert.True(sphere.Dashed);
            Assert.Equal(0.3f, sphere.Radius, 5);
            Assert.Equal(CrystalItemKind.Atom, scene.Lookup(sphere.Id)!.Kind);
        }
    }

    [Fact]
    public void TwoDisplacements_AreTwoItemsOfTheSameAtom_InTheColorsOfTheirElements()
    {
        CrystalStructure s = Polonium(
            new AtomDisplacement("Fe", [0.01, 0.01, 0.01, 0, 0, 0]),
            new AtomDisplacement("Mg", [0.03, 0.01, 0.01, 0, 0, 0]));

        CrystalScene scene = Builder.Build(s, Options());

        var items = scene.Scene.Items.Where(i => i is SphereItem or EllipsoidItem).ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(0, scene.Lookup(i.Id)!.AtomIndex));
        ElementTable.TryGet("Fe", out ElementInfo fe);
        ElementTable.TryGet("Mg", out ElementInfo mg);
        Assert.Equal(fe.Color, items.OfType<SphereItem>().Single().Color);
        Assert.Equal(mg.Color, items.OfType<EllipsoidItem>().Single().Color);
    }

    [Fact]
    public void Bonds_AreThinRodsCutAtTheSurfaceOfTheEllipsoids()
    {
        CrystalScene scene = Builder.Build(ThermalFluorite(), Options());

        var rods = scene.Scene.Items.OfType<RodItem>().ToList();
        var ellipsoids = scene.Scene.Items.OfType<EllipsoidItem>().ToList();
        Assert.NotEmpty(rods);
        Assert.Equal(4, ellipsoids.Count);
        foreach (RodItem rod in rods)
        {
            Assert.Equal(0.4f * 0.12f, rod.Radius, 6);
            Vector3 dir = Vector3.Normalize(rod.End - rod.Start);
            // Cada ponta cortada cai na superficie da forma da sua ponta (elipsoide do Ca ou esfera do F).
            EllipsoidItem? atStart = ellipsoids.FirstOrDefault(e => Vector3.Distance(e.Center, rod.Start) < 1e-4f);
            EllipsoidItem? atEnd = ellipsoids.FirstOrDefault(e => Vector3.Distance(e.Center, rod.End) < 1e-4f);
            EllipsoidItem ca = atStart ?? atEnd!;
            Vector3 cut = atStart is not null ? dir * rod.StartTrim : -dir * rod.EndTrim;
            float sphereTrim = atStart is not null ? rod.EndTrim : rod.StartTrim;
            Assert.Equal(1f, EllipseProjection.InverseShapeForm(ca.Axes, cut), 4);
            Assert.Equal((float)(1.5382 * 0.1), sphereTrim, 5);
        }
    }
}
