using System.Globalization;

namespace Viewer3D.Crystal;

/// <summary>
/// Reads an XCrySDen animation file (AXSF), or a still XSF file as one frame. A crystal is <c>CRYSTAL</c> (or
/// <c>SLAB</c>, <c>POLYMER</c>) with its cell in <c>PRIMVEC</c>, once for the whole animation or as <c>PRIMVEC i</c>
/// for each step, and the atoms of each step in <c>PRIMCOORD i</c> (a count line, then one line per atom with the
/// atomic number or the symbol and the Cartesian position in angstrom; the force columns that may follow are read past).
/// A molecule lists the atoms of each step in <c>ATOMS i</c>, without a count. <c>ANIMSTEPS</c> gives the number of
/// steps; <c>CONVVEC</c>, <c>CONVCOORD</c>, data grids and lines starting with <c>#</c> are skipped. Bonds come from the
/// geometry (<see cref="CrystalBondFinder"/>).
/// </summary>
public static class AxsfReader
{
    static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ANIMSTEPS", "CRYSTAL", "SLAB", "POLYMER", "MOLECULE", "PRIMVEC", "CONVVEC", "PRIMCOORD", "CONVCOORD", "ATOMS",
    };

    /// <summary>Reads an AXSF or XSF file.</summary>
    /// <exception cref="TrajectoryReadException">The file is malformed, empty or over a limit.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static CrystalTrajectory Read(string path, TrajectoryReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= TrajectoryReadOptions.Default;
        using StreamReader reader = TrajectoryText.Open(path, options);
        return Read(reader, options, cancellationToken);
    }

    /// <summary>Reads AXSF or XSF text; the size ceiling counts the characters read.</summary>
    /// <exception cref="TrajectoryReadException">The text is malformed, empty or over a limit.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static CrystalTrajectory Read(TextReader reader, TrajectoryReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        options ??= TrajectoryReadOptions.Default;
        var text = new TrajectoryText(reader, options);
        var frames = new List<CartesianFrame>();
        var latticeByStep = new Dictionary<int, double[]>();
        double[]? fixedLattice = null;
        int? steps = null;
        string? pending = null;
        while ((pending ?? Content(text)) is string line)
        {
            pending = null;
            cancellationToken.ThrowIfCancellationRequested();
            string[] fields = TrajectoryText.Fields(line);
            string key = fields[0].ToUpperInvariant();
            if (key.StartsWith("BEGIN_", StringComparison.Ordinal))
            {
                SkipBlock(text, key);
                continue;
            }
            switch (key)
            {
                case "ANIMSTEPS":
                    steps = Step(text, Field(text, fields, 1, "ANIMSTEPS"));
                    break;
                case "CRYSTAL" or "SLAB" or "POLYMER" or "MOLECULE":
                    break;
                case "PRIMVEC":
                    {
                        int? step = fields.Length > 1 ? Step(text, fields[1]) : null;
                        double[] lattice = ReadVectors(text, "PRIMVEC");
                        if (step is { } i)
                            latticeByStep[i] = lattice;
                        else
                            fixedLattice = lattice;
                        break;
                    }
                case "CONVVEC":
                    ReadVectors(text, "CONVVEC");
                    break;
                case "PRIMCOORD":
                    {
                        int step = fields.Length > 1 ? Step(text, fields[1]) : frames.Count + 1;
                        double[] cell = latticeByStep.GetValueOrDefault(step) ?? fixedLattice
                            ?? throw text.Malformed($"PRIMCOORD {step} has no PRIMVEC before it.");
                        frames.Add(ReadCounted(text, "PRIMCOORD", cell));
                        break;
                    }
                case "CONVCOORD":
                    ReadCounted(text, "CONVCOORD", null);
                    break;
                case "ATOMS":
                    (CartesianFrame frame, pending) = ReadUncounted(text);
                    frames.Add(frame);
                    break;
                default:
                    throw text.Malformed($"'{fields[0]}' is not an XSF keyword.");
            }
        }
        if (frames.Count == 0)
            throw new TrajectoryReadException(TrajectoryReadError.Empty, "The file holds no PRIMCOORD or ATOMS block.");
        if (steps is { } declared && declared != frames.Count)
        {
            text.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "ANIMSTEPS gives {0} steps; the file holds {1}.", declared, frames.Count));
        }
        return CrystalTrajectory.FromCartesian(frames, text.Warnings, options.FrameDuration);
    }

    // The next line that is neither blank nor a comment.
    static string? Content(TrajectoryText text)
    {
        while (text.NextNonBlank() is string line)
        {
            if (!line.TrimStart().StartsWith('#'))
                return line;
        }
        return null;
    }

    static string Field(TrajectoryText text, string[] fields, int index, string keyword)
        => index < fields.Length ? fields[index] : throw text.Malformed($"{keyword} needs a value after it.");

    static int Step(TrajectoryText text, string field)
        => int.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out int step) && step >= 1
            ? step
            : throw text.Malformed($"The step '{field}' is not a step number.");

    static double[] ReadVectors(TrajectoryText text, string keyword)
    {
        var vectors = new double[9];
        for (int row = 0; row < 3; row++)
        {
            string line = Content(text) ?? throw text.Malformed($"{keyword} ends after {row} of its three vectors.");
            string[] fields = TrajectoryText.Fields(line);
            if (fields.Length < 3)
                throw text.Malformed($"A vector of {keyword} has {fields.Length} numbers; it needs 3.");
            for (int k = 0; k < 3; k++)
                vectors[3 * row + k] = text.Number(fields[k], $"{keyword} value");
        }
        if (keyword == "PRIMVEC" && !LatticeFrame.Create(vectors).Valid)
            throw text.Malformed("The PRIMVEC vectors have no volume.");
        return vectors;
    }

    static CartesianFrame ReadCounted(TrajectoryText text, string keyword, double[]? lattice)
    {
        string countLine = Content(text) ?? throw text.Malformed($"{keyword} ends before its atom count.");
        int count = text.AtomCount(TrajectoryText.Fields(countLine)[0], $"atom count of {keyword}");
        var symbols = new string[count];
        var positions = new double[3 * count];
        for (int i = 0; i < count; i++)
        {
            string line = Content(text) ?? throw text.Malformed($"{keyword} declares {count} atoms, but the file ends after {i}.");
            ReadAtom(text, TrajectoryText.Fields(line), i, symbols, positions);
        }
        return new CartesianFrame(symbols, positions, lattice);
    }

    // ATOMS has no count: its atoms run to the next keyword, which is handed back.
    static (CartesianFrame Frame, string? Next) ReadUncounted(TrajectoryText text)
    {
        var symbols = new List<string>();
        var positions = new List<double>();
        while (Content(text) is string line)
        {
            string[] fields = TrajectoryText.Fields(line);
            if (Keywords.Contains(fields[0]) || fields[0].StartsWith("BEGIN_", StringComparison.OrdinalIgnoreCase))
                return (Frame(), line);
            if (symbols.Count >= text.AtomCountLimit)
                text.AtomCount((symbols.Count + 1).ToString(CultureInfo.InvariantCulture), "atom count of ATOMS");
            var symbol = new string[1];
            var position = new double[3];
            ReadAtom(text, fields, 0, symbol, position);
            symbols.Add(symbol[0]);
            positions.AddRange(position);
        }
        return (Frame(), null);

        CartesianFrame Frame() => new([.. symbols], [.. positions], null);
    }

    static void ReadAtom(TrajectoryText text, string[] fields, int i, string[] symbols, double[] positions)
    {
        if (fields.Length < 4)
            throw text.Malformed($"An atom line has {fields.Length} columns; it needs the element and three coordinates.");
        symbols[i] = text.Element(fields[0]);
        for (int k = 0; k < 3; k++)
            positions[3 * i + k] = text.Number(fields[k + 1], "coordinate");
    }

    // BEGIN_BLOCK_DATAGRID_3D ... END_BLOCK_DATAGRID_3D, BEGIN_INFO ... END_INFO.
    static void SkipBlock(TrajectoryText text, string begin)
    {
        string end = "END_" + begin["BEGIN_".Length..];
        while (text.Next() is string line)
        {
            if (line.Trim().Equals(end, StringComparison.OrdinalIgnoreCase))
                return;
        }
        throw text.Malformed($"{begin} has no {end}.");
    }
}
