namespace Viewer3D;

/// <summary>Why <see cref="ObjReader"/> refused a file.</summary>
public enum ObjReadError
{
    /// <summary>A line could not be read: a missing or non-numeric value, or a face with fewer than three corners.</summary>
    Malformed,

    /// <summary>A face refers to a vertex or normal that does not exist.</summary>
    IndexOutOfRange,

    /// <summary>The model has more triangles than <see cref="ObjReadOptions.MaxTriangles"/>.</summary>
    TooManyTriangles,

    /// <summary>The model declares more vertices than <see cref="ObjReadOptions.MaxVertices"/>.</summary>
    TooManyVertices,

    /// <summary>The file is larger than <see cref="ObjReadOptions.MaxFileBytes"/>.</summary>
    FileTooLarge,

    /// <summary>The file has no faces to draw.</summary>
    Empty,
}

/// <summary>A Wavefront OBJ file that <see cref="ObjReader"/> refused, with the reason and, when there is one, the line.</summary>
public sealed class ObjReadException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ObjReadException(ObjReadError reason, string message, int line = 0, long count = 0, long limit = 0) : base(message)
    {
        Reason = reason;
        Line = line;
        Count = count;
        Limit = limit;
    }

    /// <summary>Why the file was refused.</summary>
    public ObjReadError Reason { get; }

    /// <summary>The 1-based line of the problem, or 0 when it is not tied to a line.</summary>
    public int Line { get; }

    /// <summary>For a limit that was passed: how many triangles, vertices or bytes the file has. Otherwise 0.</summary>
    public long Count { get; }

    /// <summary>For a limit that was passed: the limit. Otherwise 0.</summary>
    public long Limit { get; }
}
