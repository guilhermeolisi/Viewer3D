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
}

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
