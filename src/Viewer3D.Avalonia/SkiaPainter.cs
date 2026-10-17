using System.Numerics;
using SkiaSharp;

namespace Viewer3D.Avalonia;

/// <summary>Colors and switches for one paint.</summary>
internal sealed record PaintOptions(
    SKColor Background,
    SKColor Label,
    SKColor[] AxisColors,
    IReadOnlyList<AxisDirection>? Axes,
    Quaternion Orientation,
    bool ShowAxisIndicator,
    bool ShowLabels,
    int? SelectedItemId);

/// <summary>
/// Paints a <see cref="DrawList"/> on a Skia canvas. The same code paints the screen and <c>RenderToPng</c>, so what
/// is saved is what is seen. Stateless: every call creates and frees its own paints, so it is safe on any thread.
/// </summary>
internal static class SkiaPainter
{
    /// <summary>Length of each arrow of the axis indicator, in device-independent pixels.</summary>
    internal const float AxisLength = 34f;

    /// <summary>Distance from the indicator to the left and bottom edges.</summary>
    internal const float AxisMargin = 14f;

    internal static Vector2 AxisOrigin(float height) => new(AxisMargin + AxisLength, height - AxisMargin - AxisLength);

    public static void Paint(SKCanvas canvas, DrawList list, PaintOptions options)
    {
        float w = list.Width, h = list.Height;
        canvas.Save();
        canvas.ClipRect(new SKRect(0, 0, w, h));
        canvas.Clear(options.Background);

        using var fill = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        using var stroke = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
        using var vertexPaint = new SKPaint { Color = SKColors.White };
        Scene? scene = list.Scene;

        ReadOnlySpan<uint> commands = list.Commands;
        int c = 0;
        while (c < commands.Length)
        {
            DrawKind kind = DrawList.KindOf(commands[c]);
            if (kind == DrawKind.Triangle)
            {
                // Consecutive triangles go in one call.
                int end = c;
                while (end < commands.Length && DrawList.KindOf(commands[end]) == DrawKind.Triangle)
                    end++;
                PaintTriangles(canvas, list, commands[c..end], vertexPaint);
                c = end;
                continue;
            }
            int i = DrawList.IndexOf(commands[c]);
            if (kind == DrawKind.Circle)
            {
                bool selected = IsSelected(scene, list.CircleItems[i], options.SelectedItemId);
                var sphere = scene?.Items[list.CircleItems[i]] as SphereItem;
                PaintCircle(canvas, list, i, sphere?.Sectors, sphere?.Dashed == true, fill, stroke,
                    selected ? options.Label : (SKColor?)null);
            }
            else if (kind == DrawKind.Ellipse)
            {
                bool selected = IsSelected(scene, list.EllipseItems[i], options.SelectedItemId);
                var sectors = (scene?.Items[list.EllipseItems[i]] as EllipsoidItem)?.Sectors;
                PaintEllipse(canvas, list, i, sectors, fill, stroke, selected ? options.Label : (SKColor?)null);
            }
            else
            {
                bool selected = IsSelected(scene, list.SegmentItems[i], options.SelectedItemId);
                PaintSegment(canvas, list, i, stroke, selected ? options.Label : (SKColor?)null);
            }
            c++;
        }

        if (options.ShowLabels && list.LabelCount > 0)
            PaintLabels(canvas, list, options.Label, fill);
        if (options.ShowAxisIndicator && options.Axes is { Count: > 0 })
            PaintAxes(canvas, h, options, fill, stroke);
        canvas.Restore();
    }

    static bool IsSelected(Scene? scene, int itemIndex, int? selectedId)
        => selectedId is int id && scene is not null && scene.Items[itemIndex].Id == id;

    static void PaintTriangles(SKCanvas canvas, DrawList list, ReadOnlySpan<uint> commands, SKPaint paint)
    {
        var positions = new SKPoint[3 * commands.Length];
        var colors = new SKColor[3 * commands.Length];
        ReadOnlySpan<float> xy = list.TrianglePositions;
        ReadOnlySpan<uint> argb = list.TriangleColors;
        for (int k = 0; k < commands.Length; k++)
        {
            int t = DrawList.IndexOf(commands[k]);
            int o = 6 * t, q = 3 * t, p = 3 * k;
            positions[p] = new SKPoint(xy[o], xy[o + 1]);
            positions[p + 1] = new SKPoint(xy[o + 2], xy[o + 3]);
            positions[p + 2] = new SKPoint(xy[o + 4], xy[o + 5]);
            colors[p] = argb[q];
            colors[p + 1] = argb[q + 1];
            colors[p + 2] = argb[q + 2];
        }
        using SKVertices vertices = SKVertices.CreateCopy(SKVertexMode.Triangles, positions, null, colors);
        canvas.DrawVertices(vertices, SKBlendMode.Dst, paint);
    }

    static void PaintCircle(SKCanvas canvas, DrawList list, int i, IReadOnlyList<SphereSector>? sectors, bool dashed,
        SKPaint fill, SKPaint stroke, SKColor? outline)
    {
        ReadOnlySpan<float> data = list.Circles;
        float x = data[3 * i], y = data[3 * i + 1], r = data[3 * i + 2];
        if (r <= 0)
            return;
        Rgba baseColor = Rgba.FromArgb(list.CircleColors[i]);
        if (dashed)
        {
            using var dash = SKPathEffect.CreateDash([4f, 3f], 0f);
            stroke.PathEffect = dash;
            stroke.Color = new SKColor(baseColor.Shade(0.6f).ToArgb());
            stroke.StrokeWidth = 1.5f;
            stroke.StrokeCap = SKStrokeCap.Butt;
            canvas.DrawCircle(x, y, r, stroke);
            stroke.PathEffect = null;
        }
        else
        {
            PaintDisc(canvas, x, y, r, baseColor, sectors, fill, stroke, 1f);
        }
        if (outline is SKColor o)
        {
            stroke.Color = o;
            stroke.StrokeWidth = 2f;
            stroke.StrokeCap = SKStrokeCap.Butt;
            canvas.DrawCircle(x, y, r + 1.5f, stroke);
        }
    }

    /// <summary>
    /// The shaded disc of a sphere, whole or in sectors. <paramref name="strokeScale"/> is how many canvas units make a
    /// pixel, so the outline of an unfilled sector keeps its width when the canvas is transformed (an ellipse is the unit
    /// disc under its matrix).
    /// </summary>
    static void PaintDisc(SKCanvas canvas, float x, float y, float r, Rgba baseColor, IReadOnlyList<SphereSector>? sectors,
        SKPaint fill, SKPaint stroke, float strokeScale)
    {
        if (sectors is { Count: > 0 })
        {
            PaintSectors(canvas, x, y, r, baseColor, sectors, fill, stroke, strokeScale);
            return;
        }
        using var shader = SphereShader(x, y, r, baseColor);
        fill.Shader = shader;
        canvas.DrawCircle(x, y, r, fill);
        fill.Shader = null;
    }

    /// <summary>
    /// An ellipse: the unit disc with the sphere shading under the matrix of the ellipse, a thin outline, and the visible
    /// halves of the three principal ellipses, all lines with a width in pixels.
    /// </summary>
    static void PaintEllipse(SKCanvas canvas, DrawList list, int i, IReadOnlyList<SphereSector>? sectors, SKPaint fill,
        SKPaint stroke, SKColor? outline)
    {
        ReadOnlySpan<float> d = list.Ellipses.Slice(DrawList.EllipseStride * i, DrawList.EllipseStride);
        float cx = d[0], cy = d[1];
        var matrix = new SKMatrix(d[2], d[3], cx, d[4], d[5], cy, 0, 0, 1);
        float det = MathF.Abs(d[2] * d[5] - d[3] * d[4]);
        if (det <= 0)
            return;
        Rgba baseColor = Rgba.FromArgb(list.EllipseColors[i]);

        canvas.Save();
        canvas.Concat(in matrix);
        PaintDisc(canvas, 0, 0, 1, baseColor, sectors, fill, stroke, 1f / MathF.Sqrt(det));
        canvas.Restore();

        using var shape = new SKPath();
        shape.AddCircle(0, 0, 1);
        shape.Transform(matrix);
        stroke.StrokeCap = SKStrokeCap.Butt;
        stroke.Color = new SKColor(baseColor.Shade(0.45f).ToArgb());
        stroke.StrokeWidth = 1f;
        canvas.DrawPath(shape, stroke);

        if (list.EllipseHasArcs[i])
        {
            var unit = new SKRect(-1, -1, 1, 1);
            stroke.StrokeWidth = 1.2f;
            for (int k = 0; k < 3; k++)
            {
                int o = 6 + 5 * k;
                using var arc = new SKPath();
                arc.AddArc(unit, d[o + 4] * 180f / MathF.PI, 180f);
                arc.Transform(new SKMatrix(d[o], d[o + 2], cx, d[o + 1], d[o + 3], cy, 0, 0, 1));
                canvas.DrawPath(arc, stroke);
            }
        }

        if (outline is SKColor highlight)
        {
            stroke.Color = highlight;
            stroke.StrokeWidth = 2f;
            canvas.DrawPath(shape, stroke);
        }
    }

    /// <summary>A light from the upper left, as a sphere looks under the viewer's light.</summary>
    static SKShader SphereShader(float x, float y, float r, Rgba color)
    {
        Rgba light = Rgba.Lerp(color, Rgba.White, 0.55f) with { A = color.A };
        Rgba dark = color.Shade(0.45f);
        return SKShader.CreateRadialGradient(
            new SKPoint(x - 0.35f * r, y - 0.35f * r), 1.35f * r,
            [new SKColor(light.ToArgb()), new SKColor(color.ToArgb()), new SKColor(dark.ToArgb())],
            [0f, 0.45f, 1f], SKShaderTileMode.Clamp);
    }

    /// <summary>
    /// Wedges from twelve o'clock, clockwise on the screen, each with the sphere shading in its own color. What the
    /// fractions leave of the turn stays empty, with the outline of the whole disc so the atom is still seen.
    /// </summary>
    static void PaintSectors(SKCanvas canvas, float x, float y, float r, Rgba baseColor,
        IReadOnlyList<SphereSector> sectors, SKPaint fill, SKPaint stroke, float strokeScale)
    {
        var oval = new SKRect(x - r, y - r, x + r, y + r);
        float start = -90f, total = 0f;
        foreach (SphereSector sector in sectors)
        {
            float fraction = MathF.Min(MathF.Max(sector.Fraction, 0f), 1f - total);
            if (fraction <= 0f)
                continue;
            float sweep = 360f * fraction;
            using var path = new SKPath();
            if (fraction >= 1f - 1e-6f)
            {
                path.AddCircle(x, y, r);
            }
            else
            {
                path.MoveTo(x, y);
                path.ArcTo(oval, start, sweep, false);
                path.Close();
            }
            using var shader = SphereShader(x, y, r, sector.Color);
            fill.Shader = shader;
            canvas.DrawPath(path, fill);
            fill.Shader = null;
            start += sweep;
            total += fraction;
        }
        if (total < 1f - 1e-6f)
        {
            stroke.Color = new SKColor(baseColor.Shade(0.45f).ToArgb());
            stroke.StrokeWidth = 1.5f * strokeScale;
            stroke.StrokeCap = SKStrokeCap.Butt;
            canvas.DrawCircle(x, y, r, stroke);
        }
    }

    static void PaintSegment(SKCanvas canvas, DrawList list, int i, SKPaint stroke, SKColor? outline)
    {
        int o = 5 * i;
        ReadOnlySpan<float> seg = list.Segments;
        float x1 = seg[o], y1 = seg[o + 1], x2 = seg[o + 2], y2 = seg[o + 3], width = seg[o + 4];
        uint argb = list.SegmentColors[i];
        bool rod = list.SegmentIsRod[i];
        stroke.StrokeCap = rod ? SKStrokeCap.Round : SKStrokeCap.Butt;
        if (outline is SKColor highlight)
        {
            stroke.Color = highlight;
            stroke.StrokeWidth = width + 4f;
            canvas.DrawLine(x1, y1, x2, y2, stroke);
        }
        stroke.Color = new SKColor(argb);
        stroke.StrokeWidth = MathF.Max(width, 0.5f);
        canvas.DrawLine(x1, y1, x2, y2, stroke);
        if (rod && width > 3f)
        {
            // A lighter core gives the rod some roundness.
            Rgba core = Rgba.Lerp(Rgba.FromArgb(argb), Rgba.White, 0.35f);
            stroke.Color = new SKColor(core.ToArgb());
            stroke.StrokeWidth = width * 0.35f;
            canvas.DrawLine(x1, y1, x2, y2, stroke);
        }
    }

    static void PaintLabels(SKCanvas canvas, DrawList list, SKColor defaultColor, SKPaint fill)
    {
        using var font = new SKFont(SKTypeface.Default);
        ReadOnlySpan<string> texts = list.LabelTexts;
        ReadOnlySpan<float> data = list.Labels;
        for (int i = 0; i < texts.Length; i++)
        {
            float x = data[3 * i], y = data[3 * i + 1], size = data[3 * i + 2];
            font.Size = size;
            fill.Color = list.LabelColorAt(i) is uint argb ? new SKColor(argb) : defaultColor;
            canvas.DrawText(texts[i], x, y + size, SKTextAlign.Left, font, fill);
        }
    }

    static void PaintAxes(SKCanvas canvas, float height, PaintOptions options, SKPaint fill, SKPaint stroke)
    {
        IReadOnlyList<AxisDirection> axes = options.Axes!;
        AxisGlyph[] glyphs = AxisIndicator.Compute(axes, options.Orientation, AxisOrigin(height), AxisLength);
        using var font = new SKFont(SKTypeface.Default, 13f) { Embolden = true };
        stroke.StrokeCap = SKStrokeCap.Round;
        stroke.StrokeWidth = 2.5f;
        foreach (AxisGlyph g in glyphs)
        {
            SKColor color = AxisColor(axes[g.Index], g.Index, options.AxisColors);
            stroke.Color = color;
            fill.Color = color;
            if (g.IsPoint)
            {
                canvas.DrawCircle(g.Start.X, g.Start.Y, 3.5f, fill);
            }
            else
            {
                canvas.DrawLine(g.Start.X, g.Start.Y, g.End.X, g.End.Y, stroke);
                Vector2 dir = Vector2.Normalize(g.End - g.Start);
                var normal = new Vector2(-dir.Y, dir.X);
                Vector2 tip = g.End, left = tip - dir * 7f + normal * 4f, right = tip - dir * 7f - normal * 4f;
                using var path = new SKPath();
                path.MoveTo(tip.X, tip.Y);
                path.LineTo(left.X, left.Y);
                path.LineTo(right.X, right.Y);
                path.Close();
                canvas.DrawPath(path, fill);
            }
            canvas.DrawText(axes[g.Index].Label, g.LabelPosition.X, g.LabelPosition.Y + 4.5f, SKTextAlign.Center, font, fill);
        }
    }

    static SKColor AxisColor(AxisDirection axis, int index, SKColor[] palette)
        => axis.Color is Rgba c ? new SKColor(c.ToArgb()) : palette.Length == 0 ? SKColors.Gray : palette[index % palette.Length];
}
