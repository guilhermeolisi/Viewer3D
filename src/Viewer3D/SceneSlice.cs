using System.Numerics;

namespace Viewer3D;

/// <summary>What <see cref="SceneSlice.Apply"/> does with the items outside the slab.</summary>
public enum SliceMode
{
    /// <summary>The items outside are left out of the new scene.</summary>
    Hide,

    /// <summary>The items outside stay, with their alpha lowered; labels outside are left out, as faded text is noise.</summary>
    Fade,
}

/// <summary>
/// Keeps the items of a scene that lie in a slab: the points p with |n·(p - point)| at most the half thickness, as the
/// atoms on one lattice plane.
/// </summary>
public static class SceneSlice
{
    /// <summary>
    /// A new scene with the items of <paramref name="scene"/> that lie in the slab, and the others hidden or faded. A
    /// sphere, an ellipsoid and a label are judged by their center, a rod and a line by both ends, and a mesh by all of
    /// its vertices: an item is in the slab only when every point judged is. Ids and <see cref="SceneItem.Pickable"/>
    /// are kept, and the items in the slab are the same objects.
    /// </summary>
    /// <param name="scene">The scene to slice.</param>
    /// <param name="point">A point on the middle plane of the slab.</param>
    /// <param name="unitNormal">Unit normal of the slab.</param>
    /// <param name="halfThickness">Half the thickness of the slab; not negative.</param>
    /// <param name="mode">Whether the items outside are hidden or faded.</param>
    /// <param name="fadedAlpha">Alpha of the faded items; an item already more transparent keeps its own.</param>
    public static Scene Apply(Scene scene, Vector3 point, Vector3 unitNormal, float halfThickness, SliceMode mode,
        byte fadedAlpha = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(halfThickness);
        bool Inside(Vector3 p) => MathF.Abs(Vector3.Dot(unitNormal, p - point)) <= halfThickness;

        var items = new List<SceneItem>(scene.Items.Count);
        foreach (SceneItem item in scene.Items)
        {
            bool inside = item switch
            {
                MeshItem mesh => mesh.Positions.All(Inside),
                SphereItem sphere => Inside(sphere.Center),
                EllipsoidItem ellipsoid => Inside(ellipsoid.Center),
                RodItem rod => Inside(rod.Start) && Inside(rod.End),
                LineItem line => Inside(line.Start) && Inside(line.End),
                LabelItem label => Inside(label.Position),
                _ => true,
            };
            if (inside)
                items.Add(item);
            else if (mode == SliceMode.Fade && Faded(item, fadedAlpha) is SceneItem faded)
                items.Add(faded);
        }
        return new Scene(items);
    }

    static Rgba Fade(Rgba color, byte alpha) => color with { A = Math.Min(color.A, alpha) };

    static Rgba[]? Fade(Rgba[]? colors, byte alpha) => colors?.Select(c => Fade(c, alpha)).ToArray();

    /// <summary>A copy of the item with every color faded, or null for a label (and an item of an unknown kind).</summary>
    static SceneItem? Faded(SceneItem item, byte alpha) => item switch
    {
        MeshItem mesh => new MeshItem
        {
            Id = mesh.Id,
            Pickable = mesh.Pickable,
            Positions = mesh.Positions,
            Indices = mesh.Indices,
            VertexColors = Fade(mesh.VertexColors, alpha),
            FaceColors = Fade(mesh.FaceColors, alpha),
            Color = Fade(mesh.Color, alpha),
            Scalars = mesh.Scalars,
            DoubleSided = mesh.DoubleSided,
            Lighting = mesh.Lighting,
            Edges = mesh.Edges,
            // Null edge color means an opaque shade of the mesh color (FramePreparer.EdgeShade): fade that shade.
            EdgeColor = mesh.Edges is null ? mesh.EdgeColor
                : Fade(mesh.EdgeColor ?? (mesh.Color.Shade(0.55f) with { A = 255 }), alpha),
            EdgeWidth = mesh.EdgeWidth,
        },
        SphereItem sphere => new SphereItem
        {
            Id = sphere.Id,
            Pickable = sphere.Pickable,
            Center = sphere.Center,
            Radius = sphere.Radius,
            Color = Fade(sphere.Color, alpha),
            Sectors = sphere.Sectors?.Select(s => s with { Color = Fade(s.Color, alpha) }).ToArray(),
            Dashed = sphere.Dashed,
            Halo = sphere.Halo is Rgba halo ? Fade(halo, alpha) : null,
        },
        EllipsoidItem ellipsoid => new EllipsoidItem
        {
            Id = ellipsoid.Id,
            Pickable = ellipsoid.Pickable,
            Center = ellipsoid.Center,
            Axes = ellipsoid.Axes,
            Color = Fade(ellipsoid.Color, alpha),
            Sectors = ellipsoid.Sectors?.Select(s => s with { Color = Fade(s.Color, alpha) }).ToArray(),
            PrincipalEllipses = ellipsoid.PrincipalEllipses,
        },
        RodItem rod => new RodItem
        {
            Id = rod.Id,
            Pickable = rod.Pickable,
            Start = rod.Start,
            End = rod.End,
            Radius = rod.Radius,
            StartColor = Fade(rod.StartColor, alpha),
            EndColor = Fade(rod.EndColor, alpha),
            StartTrim = rod.StartTrim,
            EndTrim = rod.EndTrim,
        },
        LineItem line => new LineItem
        {
            Id = line.Id,
            Pickable = line.Pickable,
            Start = line.Start,
            End = line.End,
            Color = Fade(line.Color, alpha),
            Width = line.Width,
            Dash = line.Dash,
        },
        _ => null,
    };
}
