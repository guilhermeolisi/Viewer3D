using System.Globalization;

namespace Viewer3D.Crystal;

/// <summary>Color and sphere radius of one chemical element.</summary>
/// <param name="Z">Atomic number.</param>
/// <param name="Symbol">Element symbol, capitalized (Ca, Fe).</param>
/// <param name="Color">Display color.</param>
/// <param name="Radius">Sphere radius in angstrom: metallic for metals, covalent for the others.</param>
/// <param name="IsMetal">True for metals; false for nonmetals, noble gases and metalloids.</param>
/// <param name="IsEstimated">True when the color or the radius has no reference value and is an estimate.</param>
public sealed record ElementInfo(int Z, string Symbol, Rgba Color, double Radius, bool IsMetal, bool IsEstimated);

/// <summary>
/// Colors and radii of the elements Z 1 to 118, read from <c>Data/elements.csv</c>, whose header cites the sources:
/// the Jmol colors as published by pymatgen (MIT License), the metallic radii of pymatgen for metals, and the covalent
/// radii of Cordero et al. (2008) for the other elements. Elements past Z 96 have estimated radii, and those the Jmol
/// scheme does not list have a neutral gray; both are marked <see cref="ElementInfo.IsEstimated"/>.
/// </summary>
public static class ElementTable
{
    static readonly Lazy<(IReadOnlyList<ElementInfo> All, Dictionary<string, ElementInfo> BySymbol)> data = new(Load);

    /// <summary>Every element, in order of atomic number.</summary>
    public static IReadOnlyList<ElementInfo> All => data.Value.All;

    /// <summary>
    /// Looks up an element by symbol. The symbol is normalized first (<see cref="Normalize"/>), so "CA", "ca" and
    /// "Ca2+" all find calcium.
    /// </summary>
    public static bool TryGet(string? symbol, out ElementInfo info)
    {
        string normalized = Normalize(symbol);
        if (normalized.Length > 0 && data.Value.BySymbol.TryGetValue(normalized, out ElementInfo? found))
        {
            info = found;
            return true;
        }
        info = null!;
        return false;
    }

    /// <summary>
    /// The leading letters of <paramref name="symbol"/> (at most two), first upper case and the rest lower case. Empty
    /// when the text does not start with a letter.
    /// </summary>
    public static string Normalize(string? symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return string.Empty;
        ReadOnlySpan<char> s = symbol.AsSpan().Trim();
        int n = 0;
        while (n < s.Length && n < 2 && char.IsAsciiLetter(s[n]))
            n++;
        if (n == 0)
            return string.Empty;
        return n == 1
            ? char.ToUpperInvariant(s[0]).ToString()
            : string.Concat(char.ToUpperInvariant(s[0]).ToString(), char.ToLowerInvariant(s[1]).ToString());
    }

    static (IReadOnlyList<ElementInfo>, Dictionary<string, ElementInfo>) Load()
    {
        using Stream stream = typeof(ElementTable).Assembly.GetManifestResourceStream("Viewer3D.Crystal.elements.csv")
            ?? throw new InvalidOperationException("The element table resource is missing.");
        using var reader = new StreamReader(stream);
        var all = new List<ElementInfo>(118);
        bool header = true;
        while (reader.ReadLine() is string line)
        {
            if (line.Length == 0 || line[0] == '#')
                continue;
            if (header)
            {
                header = false;
                continue;
            }
            string[] f = line.Split(',');
            uint rgb = uint.Parse(f[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            all.Add(new ElementInfo(
                int.Parse(f[0], CultureInfo.InvariantCulture),
                f[1],
                new Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb),
                double.Parse(f[3], CultureInfo.InvariantCulture),
                f[4] == "1",
                f[5] == "1"));
        }
        return (all, all.ToDictionary(e => e.Symbol, StringComparer.Ordinal));
    }
}
