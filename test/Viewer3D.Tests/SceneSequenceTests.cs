using System.Numerics;

namespace Viewer3D.Tests;

public class SceneSequenceTests
{
    static readonly TimeSpan Tenth = TimeSpan.FromMilliseconds(100);

    static Scene Sphere(Vector3 center, float radius = 0.5f, int id = 0) =>
        new([new SphereItem { Center = center, Radius = radius, Id = id }]);

    static SceneSequence Three(SequenceRepeat repeat) =>
        new([Sphere(Vector3.Zero), Sphere(Vector3.UnitX), Sphere(2 * Vector3.UnitX)], Tenth, repeat);

    static int[] Walk(SceneSequence sequence, int count)
    {
        var frames = new int[count];
        for (int i = 0; i < count; i++)
            frames[i] = sequence.FrameAt(TimeSpan.FromMilliseconds(100 * i + 50), out _);
        return frames;
    }

    [Fact]
    public void Once_StopsOnTheLastFrame()
    {
        SceneSequence sequence = Three(SequenceRepeat.Once);

        Assert.Equal([0, 1, 2], Walk(sequence, 3));
        Assert.Equal(2, sequence.FrameAt(TimeSpan.FromMilliseconds(250), out bool before));
        Assert.False(before);
        Assert.Equal(2, sequence.FrameAt(TimeSpan.FromMilliseconds(300), out bool after));
        Assert.True(after);
        Assert.Equal(2, sequence.FrameAt(TimeSpan.FromSeconds(10), out _));
    }

    [Fact]
    public void Loop_StartsAgainFromTheFirstFrame()
    {
        Assert.Equal([0, 1, 2, 0, 1, 2, 0], Walk(Three(SequenceRepeat.Loop), 7));
    }

    [Fact]
    public void PingPong_GoesBackOverTheInnerFrames()
    {
        Assert.Equal([0, 1, 2, 1, 0, 1, 2, 1, 0], Walk(Three(SequenceRepeat.PingPong), 9));
    }

    [Fact]
    public void PingPong_WithTwoFrames_Alternates()
    {
        var sequence = new SceneSequence([Sphere(Vector3.Zero), Sphere(Vector3.UnitX)], Tenth, SequenceRepeat.PingPong);

        Assert.Equal([0, 1, 0, 1], Walk(sequence, 4));
    }

    [Fact]
    public void FrameAt_ChangesFrameExactlyAtItsStart()
    {
        SceneSequence sequence = Three(SequenceRepeat.Loop);

        Assert.Equal(0, sequence.FrameAt(TimeSpan.FromMilliseconds(100) - TimeSpan.FromTicks(1), out _));
        Assert.Equal(1, sequence.FrameAt(TimeSpan.FromMilliseconds(100), out _));
        Assert.Equal(0, sequence.FrameAt(TimeSpan.FromMilliseconds(-5), out _));
    }

    [Fact]
    public void FrameDurations_EachFrameKeepsItsOwnTime()
    {
        var sequence = new SceneSequence([Sphere(Vector3.Zero), Sphere(Vector3.UnitX), Sphere(Vector3.UnitY)],
            [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50)], SequenceRepeat.Once);

        Assert.Equal(TimeSpan.FromMilliseconds(300), sequence.Duration);
        Assert.Equal(0, sequence.FrameAt(TimeSpan.FromMilliseconds(49), out _));
        Assert.Equal(1, sequence.FrameAt(TimeSpan.FromMilliseconds(50), out _));
        Assert.Equal(1, sequence.FrameAt(TimeSpan.FromMilliseconds(249), out _));
        Assert.Equal(2, sequence.FrameAt(TimeSpan.FromMilliseconds(250), out _));
    }

    [Fact]
    public void StartOf_GivesThePlayheadOfAFrame()
    {
        var sequence = new SceneSequence([Sphere(Vector3.Zero), Sphere(Vector3.UnitX), Sphere(Vector3.UnitY)],
            [TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50)]);

        Assert.Equal(TimeSpan.Zero, sequence.StartOf(0));
        Assert.Equal(TimeSpan.FromMilliseconds(250), sequence.StartOf(2));
        // Going to a frame and reading it back gives the same frame.
        for (int i = 0; i < 3; i++)
            Assert.Equal(i, sequence.FrameAt(sequence.StartOf(i), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => sequence.StartOf(3));
    }

    [Fact]
    public void Bounds_AreTheUnionOfAllFrames()
    {
        var far = new Vector3(20, 0, 0);
        var sequence = new SceneSequence([Sphere(Vector3.Zero, 1), Sphere(far, 1)], Tenth);

        foreach (Vector3 p in new[] { new Vector3(-1, 0, 0), new Vector3(21, 0, 0), new Vector3(0, 1, 0), new Vector3(20, -1, 0) })
            Assert.True(Vector3.Distance(p, sequence.Center) <= sequence.Radius + 1e-4f, $"{p} outside the bounds");
        Assert.Equal(10f, sequence.Center.X, 4);
        Assert.Equal(11f, sequence.Radius, 4);
    }

    [Fact]
    public void Weight_CountsItemsAndMeshVertices()
    {
        var mesh = new MeshItem { Positions = new Vector3[10], Indices = [0, 1, 2] };
        var scene = new Scene([mesh, new SphereItem()]);

        Assert.Equal(12, SceneSequence.WeightOf(scene));
        Assert.Equal(0, SceneSequence.WeightOf(null));
        var sequence = new SceneSequence([scene, scene], Tenth, lowDetailFrames: [null, Sphere(Vector3.Zero)]);
        Assert.Equal(25, sequence.Weight);
    }

    [Fact]
    public void Weight_AboveTheCeiling_IsRefusedWithBothNumbers()
    {
        Scene scene = new([new SphereItem(), new SphereItem(), new SphereItem()]);

        var e = Assert.Throws<SceneSequenceTooLargeException>(() => new SceneSequence([scene, scene], Tenth, maxWeight: 5));

        Assert.Equal(6, e.Weight);
        Assert.Equal(5, e.Limit);
        Assert.Contains("6", e.Message);
        Assert.Contains("5", e.Message);
        Assert.Contains("items and mesh vertices", e.Message);
    }

    [Fact]
    public void BadArguments_AreRefused()
    {
        Scene scene = Sphere(Vector3.Zero);

        Assert.Throws<ArgumentException>(() => new SceneSequence([], Tenth));
        Assert.Throws<ArgumentException>(() => new SceneSequence([scene], TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new SceneSequence([scene, scene], [Tenth]));
        Assert.Throws<ArgumentException>(() => new SceneSequence([scene, scene], Tenth, lowDetailFrames: [scene]));
    }

    [Fact]
    public void WithRepeat_SharesTheFrames()
    {
        SceneSequence loop = Three(SequenceRepeat.Loop);

        SceneSequence once = loop.WithRepeat(SequenceRepeat.Once);

        Assert.Equal(SequenceRepeat.Once, once.Repeat);
        Assert.Same(loop.Frames[1], once.Frames[1]);
        Assert.Equal(loop.Center, once.Center);
        Assert.Same(loop, loop.WithRepeat(SequenceRepeat.Loop));
    }

    [Fact]
    public void FrameFileName_PadsSoNamesSortInOrder()
    {
        Assert.Equal("frame_0001.png", SceneSequence.FrameFileName("frame", 0, 3));
        Assert.Equal("frame_0010.png", SceneSequence.FrameFileName("frame", 9, 10));
        Assert.Equal("frame_00001.png", SceneSequence.FrameFileName("frame", 0, 10000));
        Assert.Equal("frame_10000.png", SceneSequence.FrameFileName("frame", 9999, 10000));
        Assert.Throws<ArgumentOutOfRangeException>(() => SceneSequence.FrameFileName("frame", 3, 3));
    }
}
