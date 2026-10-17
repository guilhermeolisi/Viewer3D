using System.Numerics;

namespace Viewer3D;

/// <summary>
/// Base of everything a <see cref="Scene"/> holds. Items are plain data: the frame preparation reads them and never
/// changes them, so one item can be shared by several scenes. New kinds of item are added as new subclasses with their
/// own branch in <see cref="FramePreparer"/>.
/// </summary>
public abstract class SceneItem
{
    /// <summary>Identifier reported back by picking. Not required to be unique; the owner decides what it means.</summary>
    public int Id { get; init; }
}

/// <summary>How <see cref="FramePreparer"/> lights the faces of a mesh.</summary>
public enum MeshLighting
{
    /// <summary>
    /// A light at the eye: a face turned to the viewer is fully lit, a tilted one darker. Even and flat, so a surface
    /// seen head on shows little relief.
    /// </summary>
    Headlight,

    /// <summary>
    /// A light from the upper left, in front of the scene (<see cref="FramePreparer.KeyLightDirection"/>), with a small
    /// white highlight, a weak fill light from the lower right and a high ambient. Slopes facing the key light are
    /// brighter and the others darker, which shows the relief of a surface from any side, while the darkest side keeps
    /// its colors. The lights turn with the view, so the key light keeps coming from the upper left of the screen.
    /// </summary>
    KeyLight,
}

/// <summary>A triangle mesh. Triangles are counter-clockwise when seen from their front side.</summary>
public sealed class MeshItem : SceneItem
{
    /// <summary>Vertex positions.</summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>Three vertex indices per triangle.</summary>
    public required int[] Indices { get; init; }

    /// <summary>Color per vertex, interpolated across each triangle. Takes precedence over <see cref="FaceColors"/>.</summary>
    public Rgba[]? VertexColors { get; init; }

    /// <summary>Color per triangle, used when <see cref="VertexColors"/> is null.</summary>
    public Rgba[]? FaceColors { get; init; }

    /// <summary>Color of the whole mesh when neither per-vertex nor per-face colors are given.</summary>
    public Rgba Color { get; init; } = Rgba.Gray;

    /// <summary>Optional scalar per vertex (the value a color map was applied to), kept for picking and recoloring.</summary>
    public float[]? Scalars { get; init; }

    /// <summary>When true, back faces are drawn too (open surfaces such as height fields).</summary>
    public bool DoubleSided { get; init; }

    /// <summary>How the faces are lit. The default, <see cref="MeshLighting.Headlight"/>, lights from the eye.</summary>
    public MeshLighting Lighting { get; init; }

    /// <summary>Number of triangles.</summary>
    public int TriangleCount => Indices.Length / 3;
}

/// <summary>A shaded sphere.</summary>
public sealed class SphereItem : SceneItem
{
    /// <summary>Center.</summary>
    public Vector3 Center { get; init; }

    /// <summary>Radius, in scene units.</summary>
    public float Radius { get; init; } = 0.5f;

    /// <summary>Base color.</summary>
    public Rgba Color { get; init; } = Rgba.Gray;

    /// <summary>
    /// Optional sectors: the disc is painted as wedges, one per sector, starting at twelve o'clock and going clockwise,
    /// each wedge taking its <see cref="SphereSector.Fraction"/> of the turn. When the fractions add up to less than 1,
    /// the rest of the disc is left empty and only its outline is drawn, in a shade of <see cref="Color"/>. Null or
    /// empty paints the whole disc with <see cref="Color"/>. Used for a site shared by several elements, or partly
    /// occupied.
    /// </summary>
    public IReadOnlyList<SphereSector>? Sectors { get; init; }
}

/// <summary>One wedge of a <see cref="SphereItem"/> painted in sectors.</summary>
/// <param name="Fraction">Share of the full turn, between 0 and 1.</param>
/// <param name="Color">Color of the wedge.</param>
public readonly record struct SphereSector(float Fraction, Rgba Color);

/// <summary>
/// A rod (a cylinder seen as a thick segment) between two points, drawn as two halves, each with the color of its
/// end. <see cref="StartTrim"/> and <see cref="EndTrim"/> cut the rod where it enters the spheres at its ends.
/// </summary>
public sealed class RodItem : SceneItem
{
    /// <summary>First end.</summary>
    public Vector3 Start { get; init; }

    /// <summary>Second end.</summary>
    public Vector3 End { get; init; }

    /// <summary>Radius, in scene units.</summary>
    public float Radius { get; init; } = 0.1f;

    /// <summary>Color of the half next to <see cref="Start"/>.</summary>
    public Rgba StartColor { get; init; } = Rgba.Gray;

    /// <summary>Color of the half next to <see cref="End"/>.</summary>
    public Rgba EndColor { get; init; } = Rgba.Gray;

    /// <summary>Length cut from the start, usually the radius of the sphere there.</summary>
    public float StartTrim { get; init; }

    /// <summary>Length cut from the end, usually the radius of the sphere there.</summary>
    public float EndTrim { get; init; }
}

/// <summary>A line segment with a width in device-independent pixels.</summary>
public sealed class LineItem : SceneItem
{
    /// <summary>First end.</summary>
    public Vector3 Start { get; init; }

    /// <summary>Second end.</summary>
    public Vector3 End { get; init; }

    /// <summary>Color.</summary>
    public Rgba Color { get; init; } = Rgba.Black;

    /// <summary>Width in device-independent pixels.</summary>
    public float Width { get; init; } = 1f;
}

/// <summary>A text label anchored at a 3D point. Labels are always drawn on top of the scene.</summary>
public sealed class LabelItem : SceneItem
{
    /// <summary>Anchor point.</summary>
    public Vector3 Position { get; init; }

    /// <summary>Text.</summary>
    public required string Text { get; init; }

    /// <summary>Color; null uses the viewer's label color.</summary>
    public Rgba? Color { get; init; }

    /// <summary>Font size in device-independent pixels.</summary>
    public float Size { get; init; } = 12f;

    /// <summary>Offset from the projected anchor, in device-independent pixels (x right, y down).</summary>
    public Vector2 Offset { get; init; }
}
