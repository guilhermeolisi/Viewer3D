namespace Viewer3D.Crystal;

/// <summary>Why a trajectory reader (<see cref="XyzTrajectoryReader"/>, <see cref="AxsfReader"/>) refused a file.</summary>
public enum TrajectoryReadError
{
    /// <summary>A line could not be read: a missing or non-numeric value, a count past the end of the file, a cell without volume.</summary>
    Malformed,

    /// <summary>The file is larger than <see cref="TrajectoryReadOptions.MaxFileBytes"/>.</summary>
    FileTooLarge,

    /// <summary>The file has no frame.</summary>
    Empty,

    /// <summary>A frame declares more atoms than <see cref="TrajectoryReadOptions.MaxAtomsPerFrame"/>.</summary>
    TooManyAtoms,
}

/// <summary>A trajectory file that a reader refused, with the reason and, when there is one, the line.</summary>
public sealed class TrajectoryReadException : Exception
{
    /// <summary>Creates the exception.</summary>
    public TrajectoryReadException(TrajectoryReadError reason, string message, int line = 0, long count = 0, long limit = 0)
        : base(message)
    {
        Reason = reason;
        Line = line;
        Count = count;
        Limit = limit;
    }

    /// <summary>Why the file was refused.</summary>
    public TrajectoryReadError Reason { get; }

    /// <summary>The 1-based line of the problem, or 0 when it is not tied to a line.</summary>
    public int Line { get; }

    /// <summary>For a limit that was passed: how many atoms or bytes. Otherwise 0.</summary>
    public long Count { get; }

    /// <summary>For a limit that was passed: the limit. Otherwise 0.</summary>
    public long Limit { get; }
}

/// <summary>Limits and timing of the trajectory readers.</summary>
public sealed record TrajectoryReadOptions
{
    /// <summary>The options with every default.</summary>
    public static TrajectoryReadOptions Default { get; } = new();

    /// <summary>Largest file read, in bytes (64 MiB): a larger one is refused before reading.</summary>
    public long MaxFileBytes { get; init; } = 64L * 1024 * 1024;

    /// <summary>
    /// Most atoms a frame may declare (100,000), checked before its positions are kept. It guards the reading only: what
    /// a scene can hold is the weight ceiling of <see cref="SceneSequence"/> and the cost ceiling of the scene builder.
    /// </summary>
    public int MaxAtomsPerFrame { get; init; } = 100_000;

    /// <summary>How long each frame stays on screen (100 ms, ten frames per second).</summary>
    public TimeSpan FrameDuration { get; init; } = TimeSpan.FromMilliseconds(100);
}
