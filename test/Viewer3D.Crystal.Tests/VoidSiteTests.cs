namespace Viewer3D.Crystal.Tests;

/// <summary>Void sites (<see cref="CrystalAtom.IsVoid"/>) in the scene and in the packing.</summary>
public class VoidSiteTests
{
    // Radii in float (SphereItem.Radius), in angstrom: 1e-6 covers the rounding from double to float.
    const float Tolerance = 1e-6f;

    // The textbook fractions are exact: the double arithmetic errs far below 1e-9.
    const double Exact = 1e-9;

    static readonly CrystalSceneBuilder Builder = new();

    /// <summary>
    /// FCC copper (a = 3.615 A) with the octahedral void at the cell center: r_h = a / (2 sqrt 2), r_v = a/2 - r_h
    /// (Callister, ch. 12; Smallman and Ngan, ch. 2).
    /// </summary>
    static CrystalStructure CopperWithOctahedralVoid(out double voidRadius)
    {
        CrystalStructure copper = Fixtures.Copper();
        double a = copper.Cell.A;
        voidRadius = a / 2 - a / (2 * Math.Sqrt(2));
        var atoms = new List<CrystalAtom>(copper.Atoms)
        {
            new("VaO", "Va", 0.5, 0.5, 0.5) { IsVoid = true, Radius = voidRadius, SiteIndex = 1 },
        };
        return copper with { Atoms = atoms };
    }

    static SphereItem VoidSphere(CrystalScene scene, CrystalStructure s)
        => scene.Scene.Items.OfType<SphereItem>().Single(x => s.Atoms[scene.Lookup(x.Id)!.AtomIndex].IsVoid);

    [Theory]
    [InlineData(CrystalStyle.BallAndStick)]
    [InlineData(CrystalStyle.SpaceFilling)]
    [InlineData(CrystalStyle.ThermalEllipsoids)]
    [InlineData(CrystalStyle.Polyhedra)]
    public void Build_ShouldDrawVoidAsTranslucentMarker_WithoutStyleScale(CrystalStyle style)
    {
        CrystalStructure s = CopperWithOctahedralVoid(out _);

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions { Style = style });

        SphereItem marker = VoidSphere(scene, s);
        Assert.Equal(0.1f, marker.Radius, Tolerance);
        Assert.Equal(new Rgba(150, 150, 150, 128), marker.Color);
        Assert.Null(marker.Sectors);
        Assert.False(marker.Dashed);
    }

    [Fact]
    public void Build_ShouldDrawVoidSphereOfItsRadius_WhenShowVoidSpheres()
    {
        CrystalStructure s = CopperWithOctahedralVoid(out double voidRadius);

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions { ShowVoidSpheres = true });

        Assert.Equal((float)voidRadius, VoidSphere(scene, s).Radius, Tolerance);
    }

    [Fact]
    public void Build_ShouldTakeVoidColorAndOpacityFromOptions()
    {
        CrystalStructure s = CopperWithOctahedralVoid(out _);
        var options = new CrystalSceneOptions { VoidColor = new Rgba(10, 200, 30), VoidOpacity = 1, VoidMarkerRadius = 0.25 };

        SphereItem marker = VoidSphere(Builder.Build(s, options), s);

        Assert.Equal(new Rgba(10, 200, 30), marker.Color);
        Assert.Equal(0.25f, marker.Radius, Tolerance);
    }

    [Fact]
    public void Build_ShouldLeaveVoidOutOfLegends()
    {
        CrystalStructure s = CopperWithOctahedralVoid(out _);

        CrystalScene byElement = Builder.Build(s, new CrystalSceneOptions());
        CrystalScene bySite = Builder.Build(s, new CrystalSceneOptions(), new CrystalAppearance { ColorScheme = AtomColorScheme.Site });

        Assert.Equal(["Cu"], byElement.Legend.Select(e => e.Element));
        Assert.Equal(["Cu"], byElement.ColorLegend.Select(e => e.Text));
        Assert.Equal(["Cu1"], bySite.ColorLegend.Select(e => e.Text));
    }

    [Fact]
    public void Build_ShouldKeepSpaceFillingScale_WhenVoidPresent()
    {
        CrystalStructure s = CopperWithOctahedralVoid(out _);
        var options = new CrystalSceneOptions { Style = CrystalStyle.SpaceFilling };

        double withVoid = Builder.Build(s, options).SphereScale;
        double without = Builder.Build(Fixtures.Copper(), options).SphereScale;

        Assert.Equal(without, withVoid, Exact);
    }

    [Fact]
    public void Measure_ShouldIgnoreVoid()
    {
        PackingMeasure m = CrystalPacking.Measure(CopperWithOctahedralVoid(out _));

        Assert.Equal(Math.PI / (3 * Math.Sqrt(2)), m.Fraction, Exact);
    }

    [Fact]
    public void Build_ShouldDrawOctahedronAroundVoid_InPolyhedraStyle()
    {
        CrystalStructure s = CopperWithOctahedralVoid(out _);
        int center = s.Atoms.Count - 1;
        // The 6 Cu of the octahedron are a/2 = 1.8075 A from the center; the next Cu, a sqrt(3)/2 ~ 3.13 A.
        CrystalPolyhedron octahedron = Fixtures.PolyhedronAround(s.Cell, s.Atoms, center, 2.0, a => !a.IsVoid);
        s = s with { Polyhedra = [octahedron] };

        CrystalScene scene = Builder.Build(s, new CrystalSceneOptions { Style = CrystalStyle.Polyhedra });

        Assert.Equal(6, octahedron.Vertices.Count);
        Assert.Equal(8, octahedron.Faces.Count);
        Assert.Equal(1, scene.PolyhedronCount);
        Assert.Equal(CrystalItemKind.Polyhedron, scene.Lookup(scene.Scene.Items.OfType<MeshItem>().Single().Id)!.Kind);
    }
}
