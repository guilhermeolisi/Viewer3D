using System.Globalization;

namespace Viewer3D.Crystal;

/// <summary>
/// Reads an XYZ trajectory: frames one after the other, each a line with its atom count, a comment line, and one line
/// per atom with the element and the Cartesian position in angstrom. The atom count may change from frame to frame.
/// The comment line of Extended XYZ (as written by ASE and many simulation codes) is understood: <c>Lattice="ax ay az
/// bx by bz cx cy cz"</c> gives the cell of the frame, <c>pbc="F F F"</c> makes it a molecule again, and
/// <c>Properties=species:S:1:pos:R:3:...</c> says which columns hold the element and the position (without it, the
/// element is the first column and the position the next three; a <c>Z</c> column is taken when there is no
/// <c>species</c>). The element is a symbol or an atomic number; one the element table does not know is kept with a
/// warning. Bonds come from the geometry (<see cref="CrystalBondFinder"/>).
/// </summary>
public static class XyzTrajectoryReader
{
    /// <summary>Reads an XYZ trajectory file.</summary>
    /// <exception cref="TrajectoryReadException">The file is malformed, empty or over a limit.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static CrystalTrajectory Read(string path, TrajectoryReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= TrajectoryReadOptions.Default;
        using StreamReader reader = TrajectoryText.Open(path, options);
        return Read(reader, options, cancellationToken);
    }

    /// <summary>Reads an XYZ trajectory from text; the size ceiling counts the characters read.</summary>
    /// <exception cref="TrajectoryReadException">The text is malformed, empty or over a limit.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static CrystalTrajectory Read(TextReader reader, TrajectoryReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= TrajectoryReadOptions.Default;
        var text = new TrajectoryText(reader, options);
        var frames = new List<CartesianFrame>();
        while (text.NextNonBlank() is string countLine)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int number = frames.Count + 1;
            int count = text.AtomCount(TrajectoryText.Fields(countLine)[0], $"atom count of frame {number}");
            string comment = text.Next() ?? throw text.Malformed($"Frame {number} ends after its atom count, without the comment line.");
            Header header = ReadHeader(text, comment, number);
            int needed = Math.Max(header.Element, header.Position + 2) + 1;
            var symbols = new string[count];
            var positions = new double[3 * count];
            for (int i = 0; i < count; i++)
            {
                string line = text.Next()
                    ?? throw text.Malformed($"Frame {number} declares {count} atoms, but the file ends after {i}.");
                string[] fields = TrajectoryText.Fields(line);
                if (fields.Length < needed)
                    throw text.Malformed($"An atom line of frame {number} has {fields.Length} columns; it needs at least {needed}.");
                symbols[i] = text.Element(fields[header.Element]);
                for (int k = 0; k < 3; k++)
                    positions[3 * i + k] = text.Number(fields[header.Position + k], "coordinate");
            }
            frames.Add(new CartesianFrame(symbols, positions, header.Lattice));
        }
        if (frames.Count == 0)
            throw new TrajectoryReadException(TrajectoryReadError.Empty, "The file holds no frame.");
        return CrystalTrajectory.FromCartesian(frames, text.Warnings, options.FrameDuration);
    }

    /// <summary>The columns of the element and of the position, and the lattice of a periodic frame.</summary>
    readonly record struct Header(int Element, int Position, double[]? Lattice);

    static Header ReadHeader(TrajectoryText text, string comment, int number)
    {
        Dictionary<string, string> values = KeyValues(comment);
        double[]? lattice = null;
        if (values.TryGetValue("Lattice", out string? latticeText))
        {
            string[] fields = TrajectoryText.Fields(latticeText);
            if (fields.Length != 9)
                throw text.Malformed($"The Lattice of frame {number} has {fields.Length} numbers; it needs 9.");
            lattice = [.. fields.Select(f => text.Number(f, "Lattice value"))];
            if (!LatticeFrame.Create(lattice).Valid)
                throw text.Malformed($"The Lattice of frame {number} has no volume.");
        }
        if (lattice is not null && values.TryGetValue("pbc", out string? pbc) && TrajectoryText.Fields(pbc).All(IsFalse))
            lattice = null;
        if (!values.TryGetValue("Properties", out string? properties))
            return new Header(0, 1, lattice);

        int column = 0, element = -1, position = -1, z = -1;
        string[] parts = properties.Split(':');
        if (parts.Length % 3 != 0)
            throw text.Malformed($"The Properties of frame {number} are not name:type:count triples.");
        for (int k = 0; k < parts.Length; k += 3)
        {
            if (!int.TryParse(parts[k + 2], NumberStyles.None, CultureInfo.InvariantCulture, out int width) || width < 1)
                throw text.Malformed($"The Properties of frame {number} give '{parts[k + 2]}' columns to '{parts[k]}'.");
            string name = parts[k].ToLowerInvariant();
            if (name == "species")
                element = column;
            else if (name == "z" && width == 1)
                z = column;
            else if (name == "pos" && width == 3)
                position = column;
            column += width;
        }
        element = element >= 0 ? element : z;
        if (element < 0 || position < 0)
            throw text.Malformed($"The Properties of frame {number} name no species (or Z) column, or no pos column of three.");
        return new Header(element, position, lattice);
    }

    static bool IsFalse(string flag) => flag is "F" or "f" or "0" || flag.Equals("false", StringComparison.OrdinalIgnoreCase);

    /// <summary>The key=value pairs of an Extended XYZ comment line, keys without case; values may be in double quotes.</summary>
    static Dictionary<string, string> KeyValues(string comment)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < comment.Length)
        {
            while (i < comment.Length && char.IsWhiteSpace(comment[i]))
                i++;
            int keyStart = i;
            while (i < comment.Length && comment[i] != '=' && !char.IsWhiteSpace(comment[i]))
                i++;
            string key = comment[keyStart..i];
            if (i >= comment.Length || comment[i] != '=')
                continue;
            i++;
            string value;
            if (i < comment.Length && comment[i] == '"')
            {
                int end = comment.IndexOf('"', i + 1);
                end = end < 0 ? comment.Length : end;
                value = comment[(i + 1)..end];
                i = Math.Min(comment.Length, end + 1);
            }
            else
            {
                int start = i;
                while (i < comment.Length && !char.IsWhiteSpace(comment[i]))
                    i++;
                value = comment[start..i];
            }
            if (key.Length > 0)
                values[key] = value;
        }
        return values;
    }
}
