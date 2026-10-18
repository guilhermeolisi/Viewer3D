namespace Viewer3D.Crystal;

/// <summary>Ten categorical colors for <see cref="AtomColorScheme.Site"/>, repeated past the tenth site.</summary>
internal static class SitePalette
{
    static readonly Rgba[] Colors =
    [
        new(31, 119, 180), new(255, 127, 14), new(44, 160, 44), new(214, 39, 40), new(148, 103, 189),
        new(140, 86, 75), new(227, 119, 194), new(127, 127, 127), new(188, 189, 34), new(23, 190, 207),
    ];

    public static Rgba Color(int order) => Colors[((order % Colors.Length) + Colors.Length) % Colors.Length];
}
