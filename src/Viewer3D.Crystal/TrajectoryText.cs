using System.Globalization;

namespace Viewer3D.Crystal;

/// <summary>
/// The lines of a trajectory file as the readers walk them: the line number, the size ceiling counted as the text goes
/// (a <see cref="TextReader"/> has no length), numbers in the invariant culture, element symbols or atomic numbers, and
/// the warnings, each unknown element once.
/// </summary>
internal sealed class TrajectoryText
{
    readonly TextReader reader;
    readonly TrajectoryReadOptions options;
    readonly HashSet<string> unknown = new(StringComparer.Ordinal);
    long characters;

    public TrajectoryText(TextReader reader, TrajectoryReadOptions options)
    {
        this.reader = reader;
        this.options = options;
    }

    /// <summary>The 1-based number of the last line read.</summary>
    public int Line { get; private set; }

    /// <summary>What was accepted but the user should know.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>Opens a file after checking its size against the ceiling.</summary>
    public static StreamReader Open(string path, TrajectoryReadOptions options)
    {
        var info = new FileInfo(path);
        if (info.Length > options.MaxFileBytes)
            throw TooLarge(info.Length, options.MaxFileBytes);
        return new StreamReader(info.FullName);
    }

    /// <summary>The next line, or null at the end of the text.</summary>
    public string? Next()
    {
        string? line = reader.ReadLine();
        if (line is null)
            return null;
        Line++;
        characters += line.Length + 1;
        if (characters > options.MaxFileBytes)
            throw TooLarge(characters, options.MaxFileBytes);
        return line;
    }

    /// <summary>The next line that is not blank, or null at the end of the text.</summary>
    public string? NextNonBlank()
    {
        while (Next() is string line)
        {
            if (!string.IsNullOrWhiteSpace(line))
                return line;
        }
        return null;
    }

    /// <summary>The whitespace-separated fields of a line.</summary>
    public static string[] Fields(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A number of the current line, or a refusal that names it.</summary>
    public double Number(string field, string what)
    {
        if (double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            return value;
        throw Malformed($"The {what} '{field}' is not a number.");
    }

    /// <summary>An atom count of the current line, checked against the ceiling before anything is kept for it.</summary>
    public int AtomCount(string field, string what)
    {
        if (!int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int count))
            throw Malformed($"The {what} '{field}' is not a count of atoms.");
        if (count > options.MaxAtomsPerFrame)
        {
            throw new TrajectoryReadException(TrajectoryReadError.TooManyAtoms, string.Format(CultureInfo.InvariantCulture,
                "Line {0}: a frame of {1:N0} atoms; the reader keeps at most {2:N0} a frame.", Line, count, options.MaxAtomsPerFrame),
                Line, count, options.MaxAtomsPerFrame);
        }
        return count;
    }

    /// <summary>
    /// The element of a field: an atomic number from 1 to 118 or a symbol (normalized, so "CA" and "Ca2+" are calcium).
    /// A field the element table does not know is kept as written, with a warning the first time.
    /// </summary>
    public string Element(string field)
    {
        IReadOnlyList<ElementInfo> all = ElementTable.All;
        if (int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int z) && z >= 1 && z <= all.Count)
            return all[z - 1].Symbol;
        if (ElementTable.TryGet(field, out ElementInfo info))
            return info.Symbol;
        if (unknown.Add(field))
        {
            Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "Line {0}: the element '{1}' is not in the element table; it is drawn in gray and gets no bonds.",
                Line, field));
        }
        return field;
    }

    /// <summary>A refusal of the current line.</summary>
    public TrajectoryReadException Malformed(string message)
        => new(TrajectoryReadError.Malformed, string.Format(CultureInfo.InvariantCulture, "Line {0}: {1}", Line, message), Line);

    static TrajectoryReadException TooLarge(long bytes, long limit)
        => new(TrajectoryReadError.FileTooLarge, string.Format(CultureInfo.InvariantCulture,
            "The file holds {0:N0} bytes or more; the reader takes at most {1:N0}.", bytes, limit), 0, bytes, limit);
}
