using System.Collections.Immutable;

namespace Viewer3D.Crystal.Tests;

public class CrystalSceneCostTests
{
    static readonly CrystalSceneBuilder Builder = new();

    static CrystalSceneOptions Plain => new() { ShowBoundaryAtoms = false, CompleteCoordination = false };

    /// <summary>120 O atoms in a 20 A cube, none on a face: 120 images per cell.</summary>
    static CrystalStructure Heavy()
    {
        var cell = new CrystalCell(20, 20, 20, 90, 90, 90);
        var atoms = Enumerable.Range(0, 120)
            .Select(k => new CrystalAtom($"O{k}", "O", 0.1 + 0.8 * (k % 5) / 5, 0.1 + 0.8 * (k / 5 % 5) / 5, 0.1 + 0.8 * (k / 25) / 5))
            .ToArray();
        return new CrystalStructure(cell, atoms, []);
    }

    static CrystalSceneOptions Repeat(CrystalSceneOptions options, int a, int b, int c)
        => options with { RepeatA = a, RepeatB = b, RepeatC = c };

    [Fact]
    public void DefaultMaxCost_IsMaxAtoms()
    {
        Assert.Equal(CrystalSceneOptions.MaxAtoms, new CrystalSceneOptions().MaxCost);
        Assert.Equal(CrystalSceneOptions.MaxAtoms, Builder.Estimate(Fixtures.Fluorite(), new CrystalSceneOptions()).Ceiling);
        // A ceiling below 1 takes the default.
        Assert.Equal(CrystalSceneOptions.MaxAtoms, Builder.Estimate(Fixtures.Fluorite(), new CrystalSceneOptions { MaxCost = 0 }).Ceiling);
    }

    [Fact]
    public void FiveCubed_LightStructure_Builds()
    {
        // Fluorite, 12 atoms per cell: 5 x 5 x 5 is 1500, under the ceiling, and is no longer clamped to 3.
        CrystalSceneOptions options = Repeat(Plain, 5, 5, 5);

        bool built = Builder.TryBuild(Fixtures.Fluorite(), options, null, out CrystalScene? scene, out CrystalSceneEstimate estimate);

        Assert.True(built);
        Assert.NotNull(scene);
        Assert.Equal(1500, scene.AtomCount);
        Assert.Null(scene.Warning);
        Assert.True(estimate.Fit);
        Assert.Equal(new LatticeShift(5, 5, 5), estimate.Fits);
        Assert.Equal(1500, Builder.Build(Fixtures.Fluorite(), options).AtomCount);
    }

    [Fact]
    public void Heavy_IsRefused_WithFits()
    {
        // 120 per cell: 5 x 5 x 5 is 15000. Taking the largest axis down one cell at a time stops at 2 x 3 x 3 (2160);
        // the step before it, 3 x 3 x 3, is 3240.
        CrystalSceneOptions options = Repeat(Plain, 5, 5, 5);

        bool built = Builder.TryBuild(Heavy(), options, null, out CrystalScene? scene, out CrystalSceneEstimate estimate);

        Assert.False(built);
        Assert.Null(scene);
        Assert.Equal(15000, estimate.Cost);
        Assert.False(estimate.Fit);
        Assert.Equal(new LatticeShift(2, 3, 3), estimate.Fits);
        Assert.True(Builder.Estimate(Heavy(), Repeat(Plain, 2, 3, 3)).Fit);
        Assert.False(Builder.Estimate(Heavy(), Repeat(Plain, 3, 3, 3)).Fit);
    }

    [Fact]
    public void Heavy_WithAHigherCeiling_Builds()
    {
        CrystalScene scene = Builder.Build(Heavy(), Repeat(Plain, 3, 3, 3) with { MaxCost = 4000 });

        Assert.Equal(3240, scene.AtomCount);
        Assert.Null(scene.Warning);
    }

    [Fact]
    public void Build_PastTheCeiling_FallsBackToOneCell_WithTheCountedAtoms()
    {
        CrystalScene scene = Builder.Build(Heavy(), Repeat(Plain, 10, 10, 10));

        Assert.Equal(120, scene.AtomCount);
        Assert.NotNull(scene.Warning);
        Assert.Contains("10 x 10 x 10 cells would draw 120000 atoms, more than 3000", scene.Warning);
    }

    [Theory]
    [InlineData(1, 1, 1, false)]
    [InlineData(3, 2, 4, false)]
    [InlineData(3, 2, 4, true)]
    [InlineData(4, 1, 2, true)]
    public void Estimate_MatchesBuiltCost_WithoutCompletions(int a, int b, int c, bool boundary)
    {
        CrystalStructure s = Fixtures.Fluorite();
        CrystalSceneOptions options = Repeat(Plain, a, b, c) with { ShowBoundaryAtoms = boundary };

        CrystalSceneEstimate estimate = Builder.Estimate(s, options);
        CrystalScene scene = Builder.Build(s, options);

        Assert.Equal(scene.AtomCount, estimate.Atoms);
        Assert.Equal(0, estimate.Polyhedra);
        Assert.Equal(scene.AtomCount, estimate.Cost);
    }

    [Fact]
    public void Estimate_CountsThePolyhedra()
    {
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        CrystalSceneOptions options = Repeat(Plain, 2, 2, 1) with { Style = CrystalStyle.Polyhedra, ShowBoundaryAtoms = true };

        CrystalSceneEstimate estimate = Builder.Estimate(s, options);
        CrystalScene scene = Builder.Build(s, options);

        Assert.True(scene.PolyhedronCount > 0);
        Assert.Equal(scene.PolyhedronCount, estimate.Polyhedra);
        Assert.Equal(estimate.Atoms + CrystalSceneOptions.PolyhedronCost * estimate.Polyhedra, estimate.Cost);
    }

    [Fact]
    public void Build_WithTheAtomsHidden_IsNotRefusedByTheCount()
    {
        // 3 x 3 x 3 of Heavy is 3240 images, past the ceiling; with the atoms off the scene draws none, and is built as
        // asked, as it was before the count existed.
        CrystalAppearance hidden = CrystalAppearance.Default with { ShowAtoms = false };

        CrystalScene scene = Builder.Build(Heavy(), Repeat(Plain, 3, 3, 3), hidden);
        CrystalSceneEstimate estimate = Builder.Estimate(Heavy(), Repeat(Plain, 3, 3, 3), hidden);

        Assert.Null(scene.Warning);
        Assert.Equal(0, scene.AtomCount);
        Assert.Equal(3240, scene.HiddenAtomCount);
        Assert.Equal(0, estimate.Cost);
        Assert.True(estimate.Fit);
    }

    [Fact]
    public void Build_WithHalfTheAtomsHiddenByRule_IsNotRefusedByTheCount()
    {
        // The first 60 atoms hidden by an atom rule: 3 x 3 x 3 draws 60 x 27 = 1620, under the ceiling.
        CrystalAppearance half = CrystalAppearance.Default with
        {
            ByAtom = Enumerable.Range(0, 60).ToImmutableDictionary(k => k, _ => new AtomStyle { Visible = false }),
        };

        CrystalScene scene = Builder.Build(Heavy(), Repeat(Plain, 3, 3, 3), half);
        bool built = Builder.TryBuild(Heavy(), Repeat(Plain, 3, 3, 3), half, out CrystalScene? tried, out CrystalSceneEstimate estimate);

        Assert.Null(scene.Warning);
        Assert.Equal(1620, scene.AtomCount);
        Assert.True(built);
        Assert.Equal(1620, tried!.AtomCount);
        Assert.Equal(1620, estimate.Cost);
    }

    [Fact]
    public void Estimate_NeverPassesTheBuiltCost_WhenARuleOnAnImageOrALengthLimitMayDropSomething()
    {
        // A rule on one image leaves its atom out of the count, and a length limit leaves the polyhedra out: the count is
        // then below the scene built, never above it.
        CrystalStructure s = Fixtures.FluoriteWithCubes();
        CrystalSceneOptions options = Repeat(Plain, 2, 2, 1) with { Style = CrystalStyle.Polyhedra, MaxBondLength = 3 };
        CrystalAppearance oneImage = CrystalAppearance.Default with
        {
            ByImage = ImmutableDictionary<AtomImage, AtomStyle>.Empty.Add(new AtomImage(0, new LatticeShift(0, 0, 0)),
                new AtomStyle { Visible = false }),
        };

        CrystalSceneEstimate estimate = Builder.Estimate(s, options, oneImage);
        CrystalScene scene = Builder.Build(s, options, oneImage);

        Assert.Null(scene.Warning);
        Assert.Equal(0, estimate.Polyhedra);
        Assert.True(scene.PolyhedronCount > 0);
        Assert.True(estimate.Cost <= scene.AtomCount + CrystalSceneOptions.PolyhedronCost * scene.PolyhedronCount);
        Assert.Equal(1, scene.HiddenAtomCount);
    }

    [Fact]
    public void TryBuild_FadedNeighborsPastTheCeiling_GivesTheBuiltCost()
    {
        // The count leaves out the faded neighbors: a ceiling at the count fits the estimate and not the scene built.
        CrystalStructure s = Fixtures.Fluorite();
        CrystalSceneOptions options = Repeat(new CrystalSceneOptions { ShowBoundaryAtoms = false }, 2, 1, 1);
        int counted = Builder.Estimate(s, options).Cost;

        bool built = Builder.TryBuild(s, options with { MaxCost = counted }, null, out CrystalScene? scene,
            out CrystalSceneEstimate estimate);

        Assert.False(built);
        Assert.Null(scene);
        Assert.Equal(Builder.Build(s, options).AtomCount, estimate.Cost);
        Assert.True(estimate.Cost > counted);
        Assert.Equal(new LatticeShift(1, 1, 1), estimate.Fits);
    }

    [Fact]
    public void SphereRadiusFits()
    {
        // Fluorite, R = 30 A: about 8000 images, past the ceiling and not so far past that it is not counted.
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { Clip = CrystalClip.Sphere, SphereCenter = new FractionalPoint(0, 0, 0), SphereRadius = 30 };

        CrystalSceneEstimate estimate = Builder.Estimate(s, options);

        Assert.False(estimate.Fit);
        Assert.True(estimate.FitsRadius is > 0 and < 30);
        Assert.Equal(0, estimate.FitsRadius * 2 % 1, 9);
        Assert.True(Builder.Estimate(s, options with { SphereRadius = estimate.FitsRadius }).Fit);
        Assert.False(Builder.Estimate(s, options with { SphereRadius = estimate.FitsRadius + 0.5 }).Fit);
        Assert.Equal(new LatticeShift(1, 1, 1), estimate.Fits);
    }

    [Fact]
    public void SphereEstimate_MatchesBuiltCount()
    {
        CrystalStructure s = Fixtures.Fluorite();
        var options = new CrystalSceneOptions { Clip = CrystalClip.Sphere, SphereCenter = new FractionalPoint(0, 0, 0), SphereRadius = 8 };

        CrystalSceneEstimate estimate = Builder.Estimate(s, options);
        CrystalScene scene = Builder.Build(s, options);

        Assert.True(estimate.Fit);
        Assert.Equal(8, estimate.FitsRadius);
        Assert.Null(scene.Warning);
        Assert.Equal(scene.AtomCount, estimate.Atoms);
    }

    [Fact]
    public void Block_HasNoFitsRadius()
    {
        Assert.True(double.IsNaN(Builder.Estimate(Fixtures.Fluorite(), Plain).FitsRadius));
    }
}
