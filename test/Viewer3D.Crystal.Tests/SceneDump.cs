using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Viewer3D.Crystal.Tests;

/// <summary>
/// A text dump of a scene, item by item (kind, id, positions, colors, radii, sectors, indices, edges, labels), and its
/// hash, to compare two scenes or a scene with a recorded one.
/// </summary>
internal static class SceneDump
{
    public static string Of(CrystalScene scene)
    {
        var sb = new StringBuilder();
        sb.Append("atoms ").Append(scene.AtomCount).Append(" polyhedra ").Append(scene.PolyhedronCount)
            .Append(" scale ").Append(F(scene.SphereScale)).Append('\n');
        foreach (ElementLegendEntry e in scene.Legend)
            sb.Append("legend ").Append(e.Element).Append(' ').Append(C(e.Color)).Append('\n');
        foreach (SceneItem item in scene.Scene.Items)
        {
            sb.Append(item.GetType().Name).Append(" #").Append(item.Id).Append(' ');
            switch (item)
            {
                case SphereItem s:
                    sb.Append(V(s.Center)).Append(" r ").Append(F(s.Radius)).Append(' ').Append(C(s.Color))
                        .Append(s.Dashed ? " dashed" : "").Append(Sectors(s.Sectors));
                    break;
                case EllipsoidItem e:
                    sb.Append(V(e.Center)).Append(" axes ").Append(string.Join(",", e.Axes.Select(V))).Append(' ')
                        .Append(C(e.Color)).Append(Sectors(e.Sectors)).Append(e.PrincipalEllipses ? " pe" : "");
                    break;
                case RodItem r:
                    sb.Append(V(r.Start)).Append(" -> ").Append(V(r.End)).Append(" r ").Append(F(r.Radius)).Append(' ')
                        .Append(C(r.StartColor)).Append(' ').Append(C(r.EndColor)).Append(" trim ")
                        .Append(F(r.StartTrim)).Append(',').Append(F(r.EndTrim));
                    break;
                case MeshItem m:
                    sb.Append(string.Join(",", m.Positions.Select(V))).Append(" idx ").Append(string.Join(",", m.Indices))
                        .Append(" edges ").Append(m.Edges is null ? "null" : string.Join(",", m.Edges)).Append(' ')
                        .Append(C(m.Color)).Append(" ec ").Append(m.EdgeColor is { } ec ? C(ec) : "null")
                        .Append(" ew ").Append(F(m.EdgeWidth)).Append(m.DoubleSided ? " ds" : "");
                    break;
                case LineItem l:
                    sb.Append(V(l.Start)).Append(" -> ").Append(V(l.End)).Append(' ').Append(C(l.Color)).Append(" w ")
                        .Append(F(l.Width));
                    break;
                case LabelItem t:
                    sb.Append(V(t.Position)).Append(' ').Append(t.Text).Append(" size ").Append(F(t.Size))
                        .Append(t.Color is { } tc ? " " + C(tc) : "");
                    break;
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    public static string Hash(CrystalScene scene)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Of(scene))))[..16];

    static string Sectors(IReadOnlyList<SphereSector>? sectors)
        => sectors is null ? "" : " sectors " + string.Join(",", sectors.Select(s => F(s.Fraction) + ":" + C(s.Color)));

    static string F(double v) => v.ToString("F4", CultureInfo.InvariantCulture);

    static string V(Vector3 v) => $"({F(v.X)} {F(v.Y)} {F(v.Z)})";

    static string C(Rgba c) => $"{c.R:X2}{c.G:X2}{c.B:X2}{c.A:X2}";
}
