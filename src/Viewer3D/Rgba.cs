namespace Viewer3D;

/// <summary>An 8-bit RGBA color, independent of any graphics library.</summary>
/// <param name="R">Red, 0 to 255.</param>
/// <param name="G">Green, 0 to 255.</param>
/// <param name="B">Blue, 0 to 255.</param>
/// <param name="A">Alpha, 0 (transparent) to 255 (opaque).</param>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Opaque white.</summary>
    public static Rgba White => new(255, 255, 255);

    /// <summary>Opaque black.</summary>
    public static Rgba Black => new(0, 0, 0);

    /// <summary>Opaque mid gray, the default color of scene items.</summary>
    public static Rgba Gray => new(160, 160, 160);

    /// <summary>Creates a color from a packed 0xAARRGGBB value.</summary>
    public static Rgba FromArgb(uint argb)
        => new((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb, (byte)(argb >> 24));

    /// <summary>The color packed as 0xAARRGGBB.</summary>
    public uint ToArgb() => ((uint)A << 24) | ((uint)R << 16) | ((uint)G << 8) | B;

    /// <summary>Multiplies the RGB channels by <paramref name="factor"/> (clamped to 0..1), keeping alpha.</summary>
    public Rgba Shade(float factor)
    {
        factor = Math.Clamp(factor, 0f, 1f);
        return new((byte)(R * factor + 0.5f), (byte)(G * factor + 0.5f), (byte)(B * factor + 0.5f), A);
    }

    /// <summary>Linear interpolation between two colors, <paramref name="t"/> clamped to 0..1.</summary>
    public static Rgba Lerp(Rgba a, Rgba b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new(
            (byte)(a.R + (b.R - a.R) * t + 0.5f),
            (byte)(a.G + (b.G - a.G) * t + 0.5f),
            (byte)(a.B + (b.B - a.B) * t + 0.5f),
            (byte)(a.A + (b.A - a.A) * t + 0.5f));
    }
}
