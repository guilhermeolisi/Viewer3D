using System.Numerics;

namespace Viewer3D;

/// <summary>
/// Turns a <see cref="Scene"/> and a <see cref="Camera"/> into a <see cref="DrawList"/>: rotates and projects, drops
/// the back faces of single-sided meshes, lights each face with a light attached to the viewer (plus a minimum of
/// ambient light), and orders everything back to front. One instance keeps its scratch buffers between frames, so
/// it is not thread-safe: use one per thread.
/// </summary>
public sealed class FramePreparer
{
    /// <summary>Light that reaches faces turned away from the viewer.</summary>
    public const float Ambient = 0.35f;

    float[] vx = [], vy = [], vz = [], sx = [], sy = [];
    bool[] ok = [];

    /// <summary>Whether labels are added to the list.</summary>
    public bool IncludeLabels { get; set; } = true;

    /// <summary>Prepares a new draw list.</summary>
    public DrawList Prepare(Scene scene, Camera camera, float width, float height)
    {
        var list = new DrawList();
        Prepare(scene, camera, width, height, list);
        return list;
    }

    /// <summary>Prepares into <paramref name="list"/>, reusing its buffers.</summary>
    public void Prepare(Scene scene, Camera camera, float width, float height, DrawList list)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(list);
        list.Reset(scene, width, height);
        Projector p = camera.CreateProjector(width, height);
        float bias = 1e-4f * camera.ViewRadius;
        IReadOnlyList<SceneItem> items = scene.Items;
        for (int i = 0; i < items.Count; i++)
        {
            switch (items[i])
            {
                case MeshItem mesh:
                    AddMesh(list, p, mesh, i);
                    break;
                case SphereItem sphere:
                    AddSphere(list, p, sphere, i);
                    break;
                case RodItem rod:
                    AddRod(list, p, rod, i, bias);
                    break;
                case LineItem line:
                    AddLine(list, p, line, i);
                    break;
                case LabelItem label when IncludeLabels:
                    AddLabel(list, p, label);
                    break;
            }
        }
        Array.Sort(list.OrderDepth, list.Order, 0, list.CommandCount);
    }

    void AddMesh(DrawList list, in Projector p, MeshItem mesh, int itemIndex)
    {
        Vector3[] positions = mesh.Positions;
        int n = positions.Length;
        if (vx.Length < n)
        {
            vx = new float[n]; vy = new float[n]; vz = new float[n];
            sx = new float[n]; sy = new float[n]; ok = new bool[n];
        }
        for (int k = 0; k < n; k++)
        {
            Vector3 v = p.ToView(positions[k]);
            vx[k] = v.X; vy[k] = v.Y; vz[k] = v.Z;
            ok[k] = p.ProjectView(v, out sx[k], out sy[k], out _);
        }

        int[] idx = mesh.Indices;
        int triangles = idx.Length / 3;
        list.EnsureTriangles(list.TriangleCount + triangles);
        list.EnsureCommands(list.CommandCount + triangles);
        Rgba[]? vertexColors = mesh.VertexColors;
        Rgba[]? faceColors = mesh.FaceColors;
        Rgba fallback = mesh.Color;
        int tc = list.TriangleCount, cc = list.CommandCount;
        float[] xy = list.TriXY;
        uint[] colors = list.TriColor;
        for (int t = 0; t < triangles; t++)
        {
            int a = idx[3 * t], b = idx[3 * t + 1], c = idx[3 * t + 2];
            if (!ok[a] || !ok[b] || !ok[c])
                continue;
            // Screen y points down, so a triangle that is counter-clockwise in view space has negative area here.
            float area = (sx[b] - sx[a]) * (sy[c] - sy[a]) - (sy[b] - sy[a]) * (sx[c] - sx[a]);
            if (area == 0 || (!mesh.DoubleSided && area > 0))
                continue;
            float ux = vx[b] - vx[a], uy = vy[b] - vy[a], uz = vz[b] - vz[a];
            float wx = vx[c] - vx[a], wy = vy[c] - vy[a], wz = vz[c] - vz[a];
            float nx = uy * wz - uz * wy, ny = uz * wx - ux * wz, nz = ux * wy - uy * wx;
            float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
            float light = len > 0 ? Ambient + (1 - Ambient) * MathF.Abs(nz) / len : 1f;

            int o = 6 * tc;
            xy[o] = sx[a]; xy[o + 1] = sy[a];
            xy[o + 2] = sx[b]; xy[o + 3] = sy[b];
            xy[o + 4] = sx[c]; xy[o + 5] = sy[c];
            int q = 3 * tc;
            if (vertexColors is not null)
            {
                colors[q] = vertexColors[a].Shade(light).ToArgb();
                colors[q + 1] = vertexColors[b].Shade(light).ToArgb();
                colors[q + 2] = vertexColors[c].Shade(light).ToArgb();
            }
            else
            {
                uint face = (faceColors is not null ? faceColors[t] : fallback).Shade(light).ToArgb();
                colors[q] = colors[q + 1] = colors[q + 2] = face;
            }
            list.TriItem[tc] = itemIndex;
            list.TriSource[tc] = t;
            list.Order[cc] = DrawList.Encode(DrawKind.Triangle, tc);
            list.OrderDepth[cc] = (vz[a] + vz[b] + vz[c]) / 3;
            tc++;
            cc++;
        }
        list.TriangleCount = tc;
        list.CommandCount = cc;
    }

    static void AddSphere(DrawList list, in Projector p, SphereItem sphere, int itemIndex)
    {
        Vector3 v = p.ToView(sphere.Center);
        if (!p.ProjectView(v, out float x, out float y, out float factor))
            return;
        int i = list.CircleCount;
        list.EnsureCircles(i + 1);
        list.CircleData[3 * i] = x;
        list.CircleData[3 * i + 1] = y;
        list.CircleData[3 * i + 2] = sphere.Radius * p.Scale * factor;
        list.CircleColor[i] = sphere.Color.ToArgb();
        list.CircleItem[i] = itemIndex;
        list.CircleCount = i + 1;
        list.AddCommand(DrawKind.Circle, i, v.Z);
    }

    static void AddRod(DrawList list, in Projector p, RodItem rod, int itemIndex, float bias)
    {
        Vector3 axis = rod.End - rod.Start;
        float length = axis.Length();
        if (length <= rod.StartTrim + rod.EndTrim || length == 0)
            return;
        axis /= length;
        Vector3 a = rod.Start + axis * rod.StartTrim;
        Vector3 b = rod.End - axis * rod.EndTrim;
        Vector3 middle = (rod.Start + rod.End) / 2;
        float along = Vector3.Dot(middle - a, axis);
        if (along < 0) middle = a;
        else if (along > Vector3.Distance(a, b)) middle = b;

        Vector3 va = p.ToView(a), vb = p.ToView(b), vm = p.ToView(middle);
        if (!p.ProjectView(va, out float ax, out float ay, out float fa)
            || !p.ProjectView(vb, out float bx, out float by, out float fb)
            || !p.ProjectView(vm, out float mx, out float my, out _))
            return;
        float width = 2 * rod.Radius * p.Scale * (fa + fb) / 2;
        // Each half takes the depth of its own end, slightly pushed back so it is painted before a sphere there.
        AddSegment(list, ax, ay, mx, my, width, rod.StartColor.ToArgb(), itemIndex, true, va.Z - bias);
        AddSegment(list, mx, my, bx, by, width, rod.EndColor.ToArgb(), itemIndex, true, vb.Z - bias);
    }

    static void AddLine(DrawList list, in Projector p, LineItem line, int itemIndex)
    {
        Vector3 va = p.ToView(line.Start), vb = p.ToView(line.End);
        if (!p.ProjectView(va, out float ax, out float ay, out _) || !p.ProjectView(vb, out float bx, out float by, out _))
            return;
        AddSegment(list, ax, ay, bx, by, line.Width, line.Color.ToArgb(), itemIndex, false, (va.Z + vb.Z) / 2);
    }

    static void AddSegment(DrawList list, float x1, float y1, float x2, float y2, float width, uint color, int item, bool rod, float depth)
    {
        int i = list.SegmentCount;
        list.EnsureSegments(i + 1);
        int o = 5 * i;
        list.SegData[o] = x1; list.SegData[o + 1] = y1;
        list.SegData[o + 2] = x2; list.SegData[o + 3] = y2;
        list.SegData[o + 4] = width;
        list.SegColor[i] = color;
        list.SegItem[i] = item;
        list.SegRound[i] = rod;
        list.SegmentCount = i + 1;
        list.AddCommand(DrawKind.Segment, i, depth);
    }

    static void AddLabel(DrawList list, in Projector p, LabelItem label)
    {
        if (!p.Project(label.Position, out float x, out float y, out _))
            return;
        int i = list.LabelCount;
        list.EnsureLabels(i + 1);
        list.LabelData[3 * i] = x + label.Offset.X;
        list.LabelData[3 * i + 1] = y + label.Offset.Y;
        list.LabelData[3 * i + 2] = label.Size;
        list.LabelText[i] = label.Text;
        list.LabelHasColor[i] = label.Color.HasValue;
        list.LabelColor[i] = label.Color?.ToArgb() ?? 0;
        list.LabelCount = i + 1;
    }
}
