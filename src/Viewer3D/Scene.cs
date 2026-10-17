using System.Numerics;

namespace Viewer3D;

/// <summary>An immutable list of scene items, with its bounding sphere.</summary>
public sealed class Scene
{
    /// <summary>An empty scene.</summary>
    public static Scene Empty { get; } = new([]);

    /// <summary>Creates a scene from its items.</summary>
    public Scene(IEnumerable<SceneItem> items)
    {
        Items = [.. items];
        (Center, Radius) = ComputeBounds(Items);
    }

    /// <summary>The items, in the order they were given.</summary>
    public IReadOnlyList<SceneItem> Items { get; }

    /// <summary>Center of the axis-aligned bounding box of the items.</summary>
    public Vector3 Center { get; }

    /// <summary>Radius of the sphere around <see cref="Center"/> that contains every item (at least a tiny value).</summary>
    public float Radius { get; }

    static (Vector3 Center, float Radius) ComputeBounds(IReadOnlyList<SceneItem> items)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        void Add(Vector3 p, float r)
        {
            min = Vector3.Min(min, p - new Vector3(r));
            max = Vector3.Max(max, p + new Vector3(r));
            any = true;
        }
        foreach (SceneItem item in items)
        {
            switch (item)
            {
                case MeshItem mesh:
                    foreach (Vector3 p in mesh.Positions)
                        Add(p, 0);
                    break;
                case SphereItem sphere:
                    Add(sphere.Center, sphere.Radius);
                    break;
                case RodItem rod:
                    Add(rod.Start, rod.Radius);
                    Add(rod.End, rod.Radius);
                    break;
                case LineItem line:
                    Add(line.Start, 0);
                    Add(line.End, 0);
                    break;
                case LabelItem label:
                    Add(label.Position, 0);
                    break;
            }
        }
        if (!any)
            return (Vector3.Zero, 1f);
        Vector3 center = (min + max) / 2;
        float radius = 0;
        foreach (SceneItem item in items)
        {
            switch (item)
            {
                case MeshItem mesh:
                    foreach (Vector3 p in mesh.Positions)
                        radius = MathF.Max(radius, Vector3.Distance(p, center));
                    break;
                case SphereItem sphere:
                    radius = MathF.Max(radius, Vector3.Distance(sphere.Center, center) + sphere.Radius);
                    break;
                case RodItem rod:
                    radius = MathF.Max(radius, Vector3.Distance(rod.Start, center) + rod.Radius);
                    radius = MathF.Max(radius, Vector3.Distance(rod.End, center) + rod.Radius);
                    break;
                case LineItem line:
                    radius = MathF.Max(radius, Vector3.Distance(line.Start, center));
                    radius = MathF.Max(radius, Vector3.Distance(line.End, center));
                    break;
                case LabelItem label:
                    radius = MathF.Max(radius, Vector3.Distance(label.Position, center));
                    break;
            }
        }
        return (center, MathF.Max(radius, 1e-6f));
    }
}
