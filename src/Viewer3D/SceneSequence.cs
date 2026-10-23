using System.Globalization;
using System.Numerics;

namespace Viewer3D;

/// <summary>What a <see cref="SceneSequence"/> does when it reaches its last frame.</summary>
public enum SequenceRepeat
{
    /// <summary>Plays once and stops on the last frame.</summary>
    Once,

    /// <summary>Starts again from the first frame.</summary>
    Loop,

    /// <summary>Plays backwards to the first frame, then forwards again (0, 1, 2, 1, 0, 1...).</summary>
    PingPong,
}

/// <summary>Thrown when a <see cref="SceneSequence"/> would hold more than the viewer keeps in memory.</summary>
public sealed class SceneSequenceTooLargeException : Exception
{
    /// <summary>Creates the exception for a sequence of <paramref name="weight"/> over <paramref name="limit"/>.</summary>
    public SceneSequenceTooLargeException(long weight, long limit)
        : base(string.Format(CultureInfo.InvariantCulture,
            "This animation holds about {0:N0} items and mesh vertices over its frames; the viewer keeps at most {1:N0}. " +
            "Use fewer frames or a smaller model.", weight, limit))
    {
        Weight = weight;
        Limit = limit;
    }

    /// <summary>Items plus mesh vertices of the frames counted so far (see <see cref="SceneSequence.WeightOf"/>).</summary>
    public long Weight { get; }

    /// <summary>The ceiling that was passed.</summary>
    public long Limit { get; }
}

/// <summary>
/// An animation made of ready scenes (a flipbook): each frame is a whole immutable <see cref="Scene"/>, shown for its
/// own duration. Playing it only swaps the scene, so a frame costs what a still picture costs and nothing is built
/// while it plays. Frames may share items and meshes. The bounds are the union of the bounds of every frame, so the
/// camera framed on them does not shake from frame to frame. Immutable: it can be built on any thread.
/// </summary>
public sealed class SceneSequence
{
    /// <summary>
    /// Default ceiling of <see cref="Weight"/>: items plus mesh vertices over all frames, low detail ones included.
    /// Measured with the sample's <c>--bench-sequence-memory 2700</c>: a unit holds from 36 managed bytes (an OBJ mesh)
    /// to 128 (ellipsoids, the worst kind), so 4,000,000 units keep the frames under about 490 MiB, inside a budget of
    /// 512 MiB.
    /// </summary>
    public const long DefaultMaxWeight = 4_000_000;

    readonly Scene[] frames;
    readonly Scene?[]? lowDetailFrames;
    readonly TimeSpan[] durations;
    // Start of each frame from the beginning of a forward pass, in ticks; one more entry holds the whole pass.
    readonly long[] starts;

    /// <summary>Creates a sequence whose frames all last <paramref name="frameDuration"/>.</summary>
    /// <param name="frames">The frames, in playing order. At least one.</param>
    /// <param name="frameDuration">How long each frame stays on screen. Positive.</param>
    /// <param name="repeat">What happens after the last frame.</param>
    /// <param name="lowDetailFrames">
    /// Optional lighter version of each frame (null entries allowed), painted while it plays or while the view moves.
    /// Same count as <paramref name="frames"/>.
    /// </param>
    /// <param name="maxWeight">Ceiling of <see cref="Weight"/>.</param>
    /// <exception cref="ArgumentException">No frame, a duration not positive, or low detail frames of another count.</exception>
    /// <exception cref="SceneSequenceTooLargeException">The frames weigh more than <paramref name="maxWeight"/>.</exception>
    public SceneSequence(IEnumerable<Scene> frames, TimeSpan frameDuration, SequenceRepeat repeat = SequenceRepeat.Loop,
        IEnumerable<Scene?>? lowDetailFrames = null, long maxWeight = DefaultMaxWeight)
        : this(frames, null, frameDuration, repeat, lowDetailFrames, maxWeight)
    {
    }

    /// <summary>Creates a sequence with a duration per frame.</summary>
    /// <param name="frames">The frames, in playing order. At least one.</param>
    /// <param name="frameDurations">How long each frame stays on screen, one per frame, each positive.</param>
    /// <param name="repeat">What happens after the last frame.</param>
    /// <param name="lowDetailFrames">Optional lighter version of each frame, same count as <paramref name="frames"/>.</param>
    /// <param name="maxWeight">Ceiling of <see cref="Weight"/>.</param>
    /// <exception cref="ArgumentException">No frame, a duration not positive, or a list of another count.</exception>
    /// <exception cref="SceneSequenceTooLargeException">The frames weigh more than <paramref name="maxWeight"/>.</exception>
    public SceneSequence(IEnumerable<Scene> frames, IEnumerable<TimeSpan> frameDurations,
        SequenceRepeat repeat = SequenceRepeat.Loop, IEnumerable<Scene?>? lowDetailFrames = null,
        long maxWeight = DefaultMaxWeight)
        : this(frames, frameDurations, default, repeat, lowDetailFrames, maxWeight)
    {
    }

    SceneSequence(IEnumerable<Scene> frames, IEnumerable<TimeSpan>? frameDurations, TimeSpan frameDuration,
        SequenceRepeat repeat, IEnumerable<Scene?>? lowDetailFrames, long maxWeight)
    {
        ArgumentNullException.ThrowIfNull(frames);
        this.frames = [.. frames];
        if (this.frames.Length == 0)
            throw new ArgumentException("A sequence needs at least one frame.", nameof(frames));
        if (Array.IndexOf(this.frames, null) >= 0)
            throw new ArgumentException("A frame is null.", nameof(frames));
        durations = frameDurations is null ? Enumerable.Repeat(frameDuration, this.frames.Length).ToArray() : [.. frameDurations];
        if (durations.Length != this.frames.Length)
            throw new ArgumentException("There must be one duration per frame.", nameof(frameDurations));
        foreach (TimeSpan d in durations)
        {
            if (d <= TimeSpan.Zero)
                throw new ArgumentException("Frame durations must be positive.", frameDurations is null ? nameof(frameDuration) : nameof(frameDurations));
        }
        if (lowDetailFrames is not null)
        {
            this.lowDetailFrames = [.. lowDetailFrames];
            if (this.lowDetailFrames.Length != this.frames.Length)
                throw new ArgumentException("There must be one low detail frame (or null) per frame.", nameof(lowDetailFrames));
        }
        if (!Enum.IsDefined(repeat))
            throw new ArgumentOutOfRangeException(nameof(repeat));
        Repeat = repeat;

        long weight = 0;
        for (int i = 0; i < this.frames.Length; i++)
        {
            weight += WeightOf(this.frames[i]) + WeightOf(this.lowDetailFrames?[i]);
            CheckWeight(weight, maxWeight);
        }
        Weight = weight;

        starts = new long[this.frames.Length + 1];
        for (int i = 0; i < durations.Length; i++)
            starts[i + 1] = starts[i] + durations[i].Ticks;
        (Center, Radius) = UnionBounds(this.frames);
    }

    SceneSequence(SceneSequence source, SequenceRepeat repeat)
    {
        frames = source.frames;
        lowDetailFrames = source.lowDetailFrames;
        durations = source.durations;
        starts = source.starts;
        Weight = source.Weight;
        Center = source.Center;
        Radius = source.Radius;
        Repeat = repeat;
    }

    /// <summary>The frames, in playing order.</summary>
    public IReadOnlyList<Scene> Frames => frames;

    /// <summary>The lighter version of each frame (entries may be null), or null when there is none.</summary>
    public IReadOnlyList<Scene?>? LowDetailFrames => lowDetailFrames;

    /// <summary>How long each frame stays on screen.</summary>
    public IReadOnlyList<TimeSpan> FrameDurations => durations;

    /// <summary>What happens after the last frame.</summary>
    public SequenceRepeat Repeat { get; }

    /// <summary>Length of one forward pass over all frames.</summary>
    public TimeSpan Duration => TimeSpan.FromTicks(starts[^1]);

    /// <summary>Items plus mesh vertices over all frames, low detail ones included.</summary>
    public long Weight { get; }

    /// <summary>Center of the union of the bounds of all frames.</summary>
    public Vector3 Center { get; }

    /// <summary>Radius around <see cref="Center"/> that contains every frame.</summary>
    public float Radius { get; }

    /// <summary>The same frames with another repeat mode (the frames are shared, not copied).</summary>
    public SceneSequence WithRepeat(SequenceRepeat repeat)
    {
        if (!Enum.IsDefined(repeat))
            throw new ArgumentOutOfRangeException(nameof(repeat));
        return repeat == Repeat ? this : new SceneSequence(this, repeat);
    }

    /// <summary>Time from the beginning of a forward pass at which <paramref name="frame"/> starts.</summary>
    public TimeSpan StartOf(int frame)
    {
        if ((uint)frame >= (uint)frames.Length)
            throw new ArgumentOutOfRangeException(nameof(frame));
        return TimeSpan.FromTicks(starts[frame]);
    }

    /// <summary>
    /// The frame shown at <paramref name="playhead"/> (time since the sequence started, negative read as zero).
    /// <paramref name="finished"/> is true when a <see cref="SequenceRepeat.Once"/> sequence has passed its end; the
    /// frame is then the last one.
    /// </summary>
    public int FrameAt(TimeSpan playhead, out bool finished)
    {
        long t = Math.Max(playhead.Ticks, 0);
        long pass = starts[^1];
        int n = frames.Length;
        finished = false;
        switch (Repeat)
        {
            case SequenceRepeat.Once:
                if (t >= pass)
                {
                    finished = true;
                    return n - 1;
                }
                return Locate(t);
            case SequenceRepeat.Loop:
                return Locate(t % pass);
            default:
                // Forward over every frame, then backward over the inner ones (n - 2 down to 1).
                long back = starts[n - 1] - starts[Math.Min(1, n - 1)];
                long u = t % (pass + back);
                if (u < pass)
                    return Locate(u);
                // Frame j covers (starts[j], starts[j + 1]] when read from the end of frame n - 2 backwards.
                long x = starts[n - 1] - (u - pass);
                return Locate(x - 1);
        }
    }

    /// <summary>
    /// File name of frame <paramref name="index"/> (from 0) out of <paramref name="count"/>: the prefix, an underscore
    /// and the number from 1 with leading zeros (at least four digits, more when the count needs them), and ".png".
    /// Names sort in playing order.
    /// </summary>
    public static string FrameFileName(string prefix, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if ((uint)index >= (uint)count)
            throw new ArgumentOutOfRangeException(nameof(index));
        int width = Math.Max(4, count.ToString(CultureInfo.InvariantCulture).Length);
        return prefix + "_" + (index + 1).ToString("D" + width.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + ".png";
    }

    /// <summary>
    /// What a scene weighs against the ceiling: its items plus the vertices of its meshes (a mesh of many triangles is
    /// one item but holds most of the memory). Zero for null.
    /// </summary>
    public static long WeightOf(Scene? scene)
    {
        if (scene is null)
            return 0;
        long weight = scene.Items.Count;
        foreach (SceneItem item in scene.Items)
        {
            if (item is MeshItem mesh)
                weight += mesh.Positions.Length;
        }
        return weight;
    }

    /// <summary>
    /// Throws <see cref="SceneSequenceTooLargeException"/> when <paramref name="weight"/> passes
    /// <paramref name="maxWeight"/>. Builders of long sequences call it as they add frames, so they stop before
    /// building the rest.
    /// </summary>
    public static void CheckWeight(long weight, long maxWeight)
    {
        if (weight > maxWeight)
            throw new SceneSequenceTooLargeException(weight, maxWeight);
    }

    // Last frame whose start is at or before t (t within one forward pass).
    int Locate(long t)
    {
        int i = Array.BinarySearch(starts, 0, frames.Length, t);
        return i >= 0 ? i : ~i - 1;
    }

    static (Vector3 Center, float Radius) UnionBounds(Scene[] frames)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (Scene scene in frames)
        {
            min = Vector3.Min(min, scene.Center - new Vector3(scene.Radius));
            max = Vector3.Max(max, scene.Center + new Vector3(scene.Radius));
        }
        Vector3 center = (min + max) / 2;
        float radius = 0;
        foreach (Scene scene in frames)
            radius = MathF.Max(radius, Vector3.Distance(scene.Center, center) + scene.Radius);
        return (center, MathF.Max(radius, 1e-6f));
    }
}
