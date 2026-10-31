using System.Numerics;

namespace Viewer3D.Tests;

public class SceneSliceTests
{
    static readonly Rgba Red = new(220, 30, 30);
    static readonly Rgba Blue = new(30, 30, 220, 40);

    /// <summary>
    /// Spheres at z = 0 (ids 1, 2) and z = 1 (id 3), a rod inside (id 4), a rod with one end at z = 1 (id 5), a line
    /// inside (id 6), a label at z = 1 (id 7) and a mesh on z = 0 (id 8).
    /// </summary>
    static Scene Sample() => new(
    [
        new SphereItem { Id = 1, Center = new Vector3(0, 0, 0), Color = Red },
        new SphereItem { Id = 2, Center = new Vector3(1, 0, 0.05f), Color = Red, Pickable = false },
        new SphereItem { Id = 3, Center = new Vector3(0, 0, 1), Color = Red },
        new RodItem { Id = 4, Start = new Vector3(0, 0, 0), End = new Vector3(1, 0, 0), StartColor = Red, EndColor = Red },
        new RodItem { Id = 5, Start = new Vector3(0, 0, 0), End = new Vector3(0, 0, 1), StartColor = Red, EndColor = Red },
        new LineItem { Id = 6, Start = new Vector3(0, 1, 0), End = new Vector3(1, 1, 0), Color = Red },
        new LabelItem { Id = 7, Position = new Vector3(0, 0, 1), Text = "far" },
        MeshShapes.Polygon([new(0, 0, 0), new(1, 0, 0), new(1, 1, 0)], Blue, 8),
    ]);

    [Fact]
    public void Apply_Hide_KeepsOnlyTheItemsInTheSlab_AsTheSameObjects()
    {
        Scene scene = Sample();
        Scene slice = SceneSlice.Apply(scene, Vector3.Zero, Vector3.UnitZ, 0.1f, SliceMode.Hide);

        Assert.Equal([1, 2, 4, 6, 8], slice.Items.Select(i => i.Id));
        // Items in the slab are passed through, not copied, and keep their pickability.
        Assert.Same(scene.Items[0], slice.Items[0]);
        Assert.False(slice.Items[1].Pickable);
    }

    [Fact]
    public void Apply_ARodWithOneEndOutside_IsOutside()
    {
        Scene slice = SceneSlice.Apply(Sample(), Vector3.Zero, Vector3.UnitZ, 0.1f, SliceMode.Hide);
        Assert.DoesNotContain(slice.Items, i => i.Id == 5);
    }

    [Fact]
    public void Apply_Fade_KeepsTheItemsOutside_WithTheFadedAlpha_AndDropsLabels()
    {
        Scene slice = SceneSlice.Apply(Sample(), Vector3.Zero, Vector3.UnitZ, 0.1f, SliceMode.Fade, fadedAlpha: 50);

        Assert.Equal([1, 2, 3, 4, 5, 6, 8], slice.Items.Select(i => i.Id));
        var far = (SphereItem)slice.Items.Single(i => i.Id == 3);
        Assert.Equal(Red with { A = 50 }, far.Color);
        var crossing = (RodItem)slice.Items.Single(i => i.Id == 5);
        Assert.Equal(50, crossing.StartColor.A);
        Assert.Equal(50, crossing.EndColor.A);
        Assert.Equal(255, ((SphereItem)slice.Items.Single(i => i.Id == 1)).Color.A);
    }

    [Fact]
    public void Apply_Fade_KeepsTheOwnAlphaOfAnItemMoreTransparentThanTheFade()
    {
        // The mesh at alpha 40, shifted out of the slab, stays at 40 under a fade of 60.
        Scene scene = new([MeshShapes.Polygon([new(0, 0, 2), new(1, 0, 2), new(1, 1, 2)], Blue, 8)]);
        Scene slice = SceneSlice.Apply(scene, Vector3.Zero, Vector3.UnitZ, 0.1f, SliceMode.Fade);

        var mesh = (MeshItem)slice.Items.Single();
        Assert.Equal(40, mesh.Color.A);
        // Its edges were drawn opaque (a null edge color is an opaque shade of the mesh color): they fade to 60.
        Assert.Equal(60, mesh.EdgeColor!.Value.A);
        Assert.Equal(8, mesh.Id);
    }

    [Fact]
    public void Apply_AnObliqueSlab_JudgesByTheDistanceAlongItsNormal()
    {
        // Slab around the plane x + y = 1: (0.5, 0.5, 0) is on it, (1, 1, 0) is 0.707 away.
        Vector3 n = Vector3.Normalize(new Vector3(1, 1, 0));
        Scene scene = new(
        [
            new SphereItem { Id = 1, Center = new Vector3(0.5f, 0.5f, 3) },
            new SphereItem { Id = 2, Center = new Vector3(1, 1, 0) },
        ]);
        Scene slice = SceneSlice.Apply(scene, new Vector3(1, 0, 0), n, 0.2f, SliceMode.Hide);

        Assert.Equal([1], slice.Items.Select(i => i.Id));
    }
}
