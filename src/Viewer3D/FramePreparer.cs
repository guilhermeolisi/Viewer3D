using System.Numerics;

namespace Viewer3D;

/// <summary>
/// Turns a <see cref="Scene"/> and a <see cref="Camera"/> into a <see cref="DrawList"/>: rotates and projects, drops
/// the back faces of single-sided meshes, lights each face as its mesh asks (<see cref="MeshItem.Lighting"/>: a light
/// attached to the viewer, or a key light from the upper left; both with a minimum of ambient light), and orders
/// everything back to front. One instance keeps its scratch buffers between frames, so it is not thread-safe: use one
/// per thread.
/// </summary>
public sealed class FramePreparer
{
    /// <summary>Light that reaches faces turned away from the headlight.</summary>
    public const float Ambient = 0.35f;

    /// <summary>
    /// Light that reaches faces turned away from both lights of <see cref="MeshLighting.KeyLight"/>. Higher than
    /// <see cref="Ambient"/>, so the darkest side of a surface still shows its colors.
    /// </summary>
    public const float KeyLightAmbient = 0.5f;

    /// <summary>Strength of the key light, added to <see cref="KeyLightAmbient"/> on a face turned to it.</summary>
    public const float KeyLightStrength = 0.4f;

    /// <summary>Strength of the fill light, which softens the side the key light leaves dark.</summary>
    public const float FillLightStrength = 0.15f;

    /// <summary>
    /// Unit direction towards the key light (<see cref="MeshLighting.KeyLight"/>), in view space (x right, y up, z
    /// towards the viewer): from the upper left and in front.
    /// </summary>
    public static readonly Vector3 KeyLightDirection = Vector3.Normalize(new Vector3(-0.45f, 0.55f, 0.70f));

    /// <summary>Unit direction towards the fill light, in view space: from the lower right and in front.</summary>
    public static readonly Vector3 FillLightDirection = Vector3.Normalize(new Vector3(0.5f, -0.4f, 0.75f));

    /// <summary>Strength of the white highlight of the key light, at its peak.</summary>
    public const float Highlight = 0.3f;

    /// <summary>Exponent of the highlight: the larger, the smaller the spot.</summary>
    const float Shininess = 24f;

    /// <summary>Halfway between the key light and the eye (Blinn): a face along it shows the highlight.</summary>
    static readonly Vector3 HalfVector = Vector3.Normalize(KeyLightDirection + Vector3.UnitZ);

    /// <summary>Pieces of a line when the scene has no sphere to size them by.</summary>
    const int LinePieces = 16;

    /// <summary>Most pieces a single line is split into.</summary>
    const int MaxLinePieces = 256;

    /// <summary>
    /// Most pieces a mesh edge is split into. Edges are short and many (a polyhedron has a dozen or more), so they get a
    /// lower cap than a line.
    /// </summary>
    public const int MaxEdgePieces = 8;

    /// <summary>Shade of the mesh color used for its edges when <see cref="MeshItem.EdgeColor"/> is null.</summary>
    const float EdgeShade = 0.55f;

    float[] vx = [], vy = [], vz = [], sx = [], sy = [];
    bool[] ok = [];
    readonly List<SphereItem> spheres = [];
    readonly List<EllipsoidItem> ellipsoids = [];
    List<(float From, float To)> spans = [], spansScratch = [];

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
        spheres.Clear();
        ellipsoids.Clear();
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] is SphereItem sphere && sphere.Radius > 0)
                spheres.Add(sphere);
            else if (items[i] is EllipsoidItem ellipsoid && IsValid(ellipsoid))
                ellipsoids.Add(ellipsoid);
        }
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
                case EllipsoidItem ellipsoid when IsValid(ellipsoid):
                    AddEllipsoid(list, p, ellipsoid, i);
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
        bool keyLight = mesh.Lighting == MeshLighting.KeyLight;
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
            float light, shine = 0;
            if (len <= 0)
                light = 1f;
            else if (keyLight)
                (light, shine) = KeyLight(nx, ny, nz, len);
            else
                light = Ambient + (1 - Ambient) * MathF.Abs(nz) / len;

            int o = 6 * tc;
            xy[o] = sx[a]; xy[o + 1] = sy[a];
            xy[o + 2] = sx[b]; xy[o + 3] = sy[b];
            xy[o + 4] = sx[c]; xy[o + 5] = sy[c];
            int q = 3 * tc;
            if (vertexColors is not null)
            {
                colors[q] = Lit(vertexColors[a], light, shine);
                colors[q + 1] = Lit(vertexColors[b], light, shine);
                colors[q + 2] = Lit(vertexColors[c], light, shine);
            }
            else
            {
                uint face = Lit(faceColors is not null ? faceColors[t] : fallback, light, shine);
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

        if (mesh.Edges is not { Length: >= 2 } edges)
            return;
        Rgba shade = fallback.Shade(EdgeShade);
        uint edgeColor = (mesh.EdgeColor ?? new Rgba(shade.R, shade.G, shade.B)).ToArgb();
        for (int e = 0; e + 1 < edges.Length; e += 2)
        {
            int a = edges[e], b = edges[e + 1];
            if ((uint)a < (uint)n && (uint)b < (uint)n)
                AddSegments(list, p, positions[a], positions[b], edgeColor, mesh.EdgeWidth, itemIndex, MaxEdgePieces);
        }
    }

    /// <summary>
    /// Key light of a face with view-space normal (nx, ny, nz) of length <paramref name="len"/>: ambient plus the key and
    /// fill lights, and the strength of the highlight. The normal is taken on the side that faces the viewer, so the back
    /// of a double-sided surface is lit like its front.
    /// </summary>
    static (float Light, float Shine) KeyLight(float nx, float ny, float nz, float len)
    {
        float s = nz < 0 ? -1f / len : 1f / len;
        var n = new Vector3(nx * s, ny * s, nz * s);
        float key = MathF.Max(0, Vector3.Dot(n, KeyLightDirection));
        float fill = MathF.Max(0, Vector3.Dot(n, FillLightDirection));
        float shine = key > 0 ? Highlight * MathF.Pow(MathF.Max(0, Vector3.Dot(n, HalfVector)), Shininess) : 0;
        return (KeyLightAmbient + KeyLightStrength * key + FillLightStrength * fill, shine);
    }

    /// <summary>A color shaded by <paramref name="light"/>, then pulled towards white by <paramref name="shine"/>.</summary>
    static uint Lit(Rgba color, float light, float shine)
    {
        Rgba shaded = color.Shade(light);
        return (shine > 0 ? Rgba.Lerp(shaded, new Rgba(255, 255, 255, shaded.A), shine) : shaded).ToArgb();
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

    static bool IsValid(EllipsoidItem ellipsoid)
        => ellipsoid.Axes is { Length: 3 } axes && axes[0].LengthSquared() > 0 && axes[1].LengthSquared() > 0
            && axes[2].LengthSquared() > 0;

    /// <summary>Smallest outline the painter gets, in square pixels, so an ellipsoid seen edge on is still a thin line.</summary>
    const float MinOutline = 0.25f;

    /// <summary>
    /// An ellipsoid becomes its outline ellipse, with the depth of its center like a sphere. In perspective the ellipse
    /// is taken at the scale of the center, which is the same approximation the spheres use.
    /// </summary>
    static void AddEllipsoid(DrawList list, in Projector p, EllipsoidItem ellipsoid, int itemIndex)
    {
        Vector3 v = p.ToView(ellipsoid.Center);
        if (!p.ProjectView(v, out float x, out float y, out float factor))
            return;
        float s = p.Scale * factor;
        Vector3[] axes = ellipsoid.Axes;
        // Axes in view space (directions only), then on the screen, where y points down.
        Vector3 a0 = p.ToView(ellipsoid.Center + axes[0]) - v;
        Vector3 a1 = p.ToView(ellipsoid.Center + axes[1]) - v;
        Vector3 a2 = p.ToView(ellipsoid.Center + axes[2]) - v;
        (float xx, float xy, float yy) = EllipseProjection.OutlineShape(a0, a1, a2);
        (float l11, float l12, float l22) = EllipseProjection.SquareRoot(
            xx * s * s + MinOutline, -xy * s * s, yy * s * s + MinOutline);

        int i = list.EllipseCount;
        list.EnsureEllipses(i + 1);
        int o = DrawList.EllipseStride * i;
        float[] data = list.EllipseData;
        data[o] = x; data[o + 1] = y;
        data[o + 2] = l11; data[o + 3] = l12; data[o + 4] = l12; data[o + 5] = l22;
        WriteArc(data, o + 6, a0, a1, s);
        WriteArc(data, o + 11, a0, a2, s);
        WriteArc(data, o + 16, a1, a2, s);
        list.EllipseColor[i] = ellipsoid.Color.ToArgb();
        list.EllipseItem[i] = itemIndex;
        list.EllipseArcs[i] = ellipsoid.PrincipalEllipses;
        list.EllipseCount = i + 1;
        list.AddCommand(DrawKind.Ellipse, i, v.Z);
    }

    static void WriteArc(float[] data, int o, Vector3 aj, Vector3 ak, float s)
    {
        data[o] = aj.X * s; data[o + 1] = -aj.Y * s;
        data[o + 2] = ak.X * s; data[o + 3] = -ak.Y * s;
        data[o + 4] = EllipseProjection.VisibleHalfStart(aj, ak);
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

    /// <summary>
    /// A line is a long object: with one depth for all of it, a cell edge that runs from the back to the front would be
    /// painted over an atom in front of its back end. So the parts inside a sphere or an ellipsoid are cut out (an atom at
    /// a cell corner hides the end of the edge), and the rest is split into pieces no longer than the smallest sphere
    /// radius or ellipsoid semi-axis, each with
    /// the depth of its own middle. Outside every sphere, a point that projects onto a sphere's disc is either wholly in
    /// front of it or wholly behind, so the per piece order is right up to the length of a piece.
    /// </summary>
    void AddLine(DrawList list, in Projector p, LineItem line, int itemIndex)
        => AddSegments(list, p, line.Start, line.End, line.Color.ToArgb(), line.Width, itemIndex, MaxLinePieces);

    /// <summary>The segment from <paramref name="start"/> to <paramref name="end"/>, cut and split as a line.</summary>
    void AddSegments(DrawList list, in Projector p, Vector3 start, Vector3 end, uint color, float width, int itemIndex,
        int maxPieces)
    {
        Vector3 d = end - start;
        float length2 = d.LengthSquared();
        if (length2 == 0)
            return;
        float length = MathF.Sqrt(length2);

        spans.Clear();
        spans.Add((0f, 1f));
        float smallest = float.MaxValue;
        foreach (SphereItem sphere in spheres)
        {
            smallest = MathF.Min(smallest, sphere.Radius);
            // |start + t d - center|^2 = r^2, with t in units of the whole line.
            Vector3 m = start - sphere.Center;
            float b = Vector3.Dot(m, d) / length2;
            float c = (m.LengthSquared() - sphere.Radius * sphere.Radius) / length2;
            float disc = b * b - c;
            if (disc <= 0)
                continue;
            float root = MathF.Sqrt(disc);
            float t0 = -b - root, t1 = -b + root;
            if (t1 <= 0 || t0 >= 1)
                continue;
            CutOut(t0, t1);
            if (spans.Count == 0)
                return;
        }
        foreach (EllipsoidItem ellipsoid in ellipsoids)
        {
            Vector3[] axes = ellipsoid.Axes;
            smallest = MathF.Min(smallest, MathF.Sqrt(MathF.Min(axes[0].LengthSquared(),
                MathF.Min(axes[1].LengthSquared(), axes[2].LengthSquared()))));
            if (!EllipseProjection.LineCrossing(axes, ellipsoid.Center, start, d, out float t0, out float t1)
                || t1 <= 0 || t0 >= 1)
                continue;
            CutOut(t0, t1);
            if (spans.Count == 0)
                return;
        }

        int whole = spheres.Count + ellipsoids.Count > 0 ? (int)MathF.Ceiling(length / smallest) : LinePieces;
        whole = Math.Clamp(whole, 1, maxPieces);
        foreach ((float from, float to) in spans)
        {
            int pieces = Math.Max(1, (int)MathF.Ceiling(whole * (to - from)));
            for (int k = 0; k < pieces; k++)
            {
                float ta = from + (to - from) * k / pieces, tb = from + (to - from) * (k + 1) / pieces;
                Vector3 va = p.ToView(start + d * ta), vb = p.ToView(start + d * tb);
                if (!p.ProjectView(va, out float ax, out float ay, out _) || !p.ProjectView(vb, out float bx, out float by, out _))
                    continue;
                // Pieces of one span overlap by half a pixel, so the antialiased joints leave no lighter dot.
                float dx = bx - ax, dy = by - ay, screen = MathF.Sqrt(dx * dx + dy * dy);
                if (screen > 0)
                {
                    float ox = 0.5f * dx / screen, oy = 0.5f * dy / screen;
                    if (k > 0) { ax -= ox; ay -= oy; }
                    if (k < pieces - 1) { bx += ox; by += oy; }
                }
                AddSegment(list, ax, ay, bx, by, width, color, itemIndex, false, (va.Z + vb.Z) / 2);
            }
        }
    }

    /// <summary>Removes the interval (<paramref name="t0"/>, <paramref name="t1"/>) from the visible spans.</summary>
    void CutOut(float t0, float t1)
    {
        spansScratch.Clear();
        foreach ((float from, float to) in spans)
        {
            if (t1 <= from || t0 >= to)
            {
                spansScratch.Add((from, to));
                continue;
            }
            if (t0 > from)
                spansScratch.Add((from, t0));
            if (t1 < to)
                spansScratch.Add((t1, to));
        }
        (spans, spansScratch) = (spansScratch, spans);
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
