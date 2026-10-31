using System.Globalization;
using System.Numerics;

namespace Viewer3D;

/// <summary>The same limits on the three axes of an <see cref="AxesFrame"/>: the box is a cube.</summary>
/// <param name="Min">Lower limit, a multiple of <paramref name="Step"/>.</param>
/// <param name="Max">Upper limit, a multiple of <paramref name="Step"/>.</param>
/// <param name="Step">Distance between two ticks.</param>
public readonly record struct AxesFrameLimits(double Min, double Max, double Step)
{
    /// <summary>Number of ticks from <see cref="Min"/> to <see cref="Max"/>, both included.</summary>
    public int TickCount => Step > 0 && Max > Min ? (int)Math.Round((Max - Min) / Step) + 1 : 0;

    /// <summary>The value of tick <paramref name="index"/>.</summary>
    public double Tick(int index) => Min + index * Step;
}

/// <summary>What an <see cref="AxesFrame"/> shows.</summary>
/// <param name="AxisTitles">Name of the x, y and z axes, in this order (for example "x (nm)").</param>
/// <param name="Limits">Limits given by the caller; null takes <see cref="AxesFrame.AutoLimits"/> of the scene.</param>
/// <param name="ShowGrid">Whether the back planes carry grid lines at the ticks.</param>
/// <param name="MaxTicks">Most ticks wanted on an axis; the round step can give a few less.</param>
/// <param name="Format">Text of a tick value; null uses <see cref="AxesFrame.FormatTick"/>.</param>
/// <param name="Title">Text drawn at the top of the viewport (for example the quantity and its unit); null for none.</param>
public sealed record AxesFrameOptions(
    string[] AxisTitles,
    AxesFrameLimits? Limits = null,
    bool ShowGrid = true,
    int MaxTicks = 5,
    Func<double, string>? Format = null,
    string? Title = null);

/// <summary>Where the camera looks from, as an <see cref="AxesFrame"/> needs it.</summary>
/// <param name="Orientation">World to view rotation (view x right, y up, z towards the viewer).</param>
/// <param name="Target">Point the camera looks at.</param>
/// <param name="EyeDistance">Distance from the target to the eye in perspective; null for an orthographic view.</param>
public readonly record struct AxesFrameView(Quaternion Orientation, Vector3 Target, float? EyeDistance = null)
{
    /// <summary>The view of <paramref name="camera"/>, in its projection.</summary>
    public static AxesFrameView FromCamera(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);
        float? eye = camera.Projection == ProjectionKind.Perspective
            ? Camera.PerspectiveDistanceFactor * camera.ViewRadius
            : null;
        return new AxesFrameView(camera.Orientation, camera.Target, eye);
    }

    /// <summary>World position of the eye in perspective; null in orthographic.</summary>
    public Vector3? Eye => EyeDistance is float d ? Target + ToWorld(new Vector3(0, 0, d)) : null;

    /// <summary>World direction the orthographic view looks along (from the viewer into the scene).</summary>
    public Vector3 ViewDirection => ToWorld(-Vector3.UnitZ);

    /// <summary>World point to view space, relative to the target.</summary>
    public Vector3 ToView(Vector3 world) => Vector3.Transform(world - Target, Orientation);

    Vector3 ToWorld(Vector3 view) => Vector3.Transform(view, Quaternion.Conjugate(Orientation));

    /// <summary>
    /// Height on the screen of a world point, up positive, in scene units at the target depth: the view y, scaled by the
    /// perspective factor when there is an eye.
    /// </summary>
    public float ScreenHeight(Vector3 world)
    {
        Vector3 v = ToView(world);
        if (EyeDistance is not float d)
            return v.Y;
        float depth = d - v.Z;
        return depth > 1e-6f * d ? v.Y * d / depth : v.Y;
    }
}

/// <summary>Kinds of segment in an <see cref="AxesFrameLayout"/>.</summary>
public enum AxesFrameSegmentKind
{
    /// <summary>An edge of a back plane.</summary>
    Edge,

    /// <summary>A grid line on a back plane.</summary>
    Grid,

    /// <summary>A tick mark on the edge that carries the numbers of an axis.</summary>
    Tick,
}

/// <summary>A segment of the frame, in world coordinates.</summary>
/// <param name="Start">First end.</param>
/// <param name="End">Second end.</param>
/// <param name="Kind">What the segment is.</param>
/// <param name="Axis">Axis it belongs to (0 x, 1 y, 2 z): the axis it runs along, or the axis a tick marks.</param>
public readonly record struct AxesFrameSegment(Vector3 Start, Vector3 End, AxesFrameSegmentKind Kind, int Axis);

/// <summary>A text of the frame, centered on a world point.</summary>
/// <param name="Position">World point the text is centered on.</param>
/// <param name="Text">The text.</param>
/// <param name="Axis">Axis it belongs to (0 x, 1 y, 2 z).</param>
/// <param name="IsTitle">True for the axis name, false for a tick value.</param>
public readonly record struct AxesFrameLabel(Vector3 Position, string Text, int Axis, bool IsTitle);

/// <summary>
/// The frame for one view, in world coordinates: what is painted behind the scene (<see cref="Back"/>, the back planes
/// and their grid) and what is painted over it (<see cref="Front"/> and <see cref="Labels"/>, the ticks, the numbers and
/// the axis names).
/// </summary>
/// <param name="Limits">The limits used.</param>
/// <param name="BackFaces">The faces drawn as back planes, as (axis, side): side -1 is the face at the lower limit.</param>
/// <param name="TickEdges">
/// For each axis, the edge chosen for its ticks as (side on the next axis, side on the one after), or null when the axis
/// points almost along the view and has no ticks.
/// </param>
/// <param name="Back">Edges and grid of the back planes.</param>
/// <param name="Front">Tick marks.</param>
/// <param name="Labels">Tick values and axis names.</param>
/// <param name="Title">The title of the options, to be drawn at the top of the viewport.</param>
public sealed record AxesFrameLayout(
    AxesFrameLimits Limits,
    IReadOnlyList<(int Axis, int Side)> BackFaces,
    IReadOnlyList<(int SideA, int SideB)?> TickEdges,
    IReadOnlyList<AxesFrameSegment> Back,
    IReadOnlyList<AxesFrameSegment> Front,
    IReadOnlyList<AxesFrameLabel> Labels,
    string? Title);

/// <summary>
/// An axes frame around a scene, as in the 3D plots of matplotlib: the three planes at the back of a box, with an
/// optional grid, and on each axis ticks and numbers on a visible edge chosen from the view (on the outline of the box
/// and the lowest on the screen, so the text stays clear of the scene). The box is centered on the origin of the world,
/// with the same round limits on the three axes. It works the same in orthographic and in perspective, from the view
/// alone; it does not change the camera.
/// </summary>
public static class AxesFrame
{
    /// <summary>An axis whose direction is closer than this (cosine) to the view direction gets no ticks.</summary>
    public const float AlongViewCosine = 0.97f;

    static readonly int[] Sides = [-1, 1];

    /// <summary>Length of a tick mark, as a fraction of the box side.</summary>
    public const float TickLength = 0.035f;

    /// <summary>Distance from the edge to the center of a tick value, as a fraction of the box side.</summary>
    public const float NumberOffset = 0.11f;

    /// <summary>Distance from the edge to the center of the axis name, as a fraction of the box side.</summary>
    public const float TitleOffset = 0.24f;

    /// <summary>
    /// Round limits symmetric about zero that cover ±<paramref name="maxAbs"/>, with at most about
    /// <paramref name="maxTicks"/> ticks: the step is 1, 2 or 5 times a power of ten, and the limit is the multiple of
    /// the step at or beyond <paramref name="maxAbs"/>. The rule is Heckbert's "nice numbers" (Graphics Gems, 1990,
    /// "Nice numbers for graph labels"): the range is rounded up to 1, 2, 5 or 10 times a power of ten, and the step
    /// (range over ticks minus one) to the nearest of them with the thresholds 1.5, 3 and 7. The same rule is used by
    /// other plotting code that is not a dependency of this library, so it is written here.
    /// </summary>
    public static AxesFrameLimits NiceLimits(double maxAbs, int maxTicks = 5)
    {
        if (!double.IsFinite(maxAbs) || maxAbs <= 0)
            maxAbs = 1;
        double range = NiceNumber(2 * maxAbs, round: false);
        double step = NiceNumber(range / Math.Max(1, maxTicks - 1), round: true);
        double max = Math.Ceiling(maxAbs / step) * step;
        return new AxesFrameLimits(-max, max, step);
    }

    /// <summary>The largest absolute coordinate of the items of <paramref name="scene"/>, sizes included.</summary>
    public static double MaxAbsCoordinate(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        float m = 0;
        void Add(Vector3 p, float r) => m = MathF.Max(m, MathF.Max(MathF.Abs(p.X), MathF.Max(MathF.Abs(p.Y), MathF.Abs(p.Z))) + r);
        foreach (SceneItem item in scene.Items)
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
                case EllipsoidItem ellipsoid:
                    Add(ellipsoid.Center, ellipsoid.BoundingRadius);
                    break;
                case RodItem rod:
                    Add(rod.Start, rod.Radius);
                    Add(rod.End, rod.Radius);
                    break;
                case LineItem line:
                    Add(line.Start, 0);
                    Add(line.End, 0);
                    break;
            }
        }
        return m;
    }

    /// <summary>
    /// <see cref="NiceLimits"/> of the largest absolute coordinate of the scene (labels are left out: they are drawn
    /// beyond what they name).
    /// </summary>
    public static AxesFrameLimits AutoLimits(Scene scene, int maxTicks = 5) => NiceLimits(MaxAbsCoordinate(scene), maxTicks);

    /// <summary>
    /// Radius of a sphere around the origin that holds the box of <paramref name="limits"/> with its numbers and axis
    /// names, to frame the camera on the whole frame (<see cref="Camera.Frame(Vector3, float)"/>).
    /// </summary>
    public static float FrameRadius(in AxesFrameLimits limits)
    {
        double half = Math.Max(Math.Abs(limits.Min), Math.Abs(limits.Max)), side = limits.Max - limits.Min;
        // A number sits beyond an edge (two coordinates at ±half) along the diagonal of the other two axes, and at most
        // at ±half on the axis itself; an axis name sits at the middle of the edge, further out.
        double number = half + NumberOffset * side / Math.Sqrt(2);
        double title = half + TitleOffset * side / Math.Sqrt(2);
        double r = Math.Max(Math.Sqrt(2 * number * number + half * half), Math.Sqrt(2) * title);
        return (float)Math.Max(r, half * Math.Sqrt(3));
    }

    /// <summary>Text of a tick, in the invariant culture, with the decimals the step needs and no "-0".</summary>
    public static string FormatTick(double value, double step)
    {
        if (Math.Abs(value) < step * 1e-9)
            value = 0;
        if (Math.Abs(value) >= 1e6)
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        int decimals = step >= 1 ? 0 : Math.Clamp((int)Math.Ceiling(-Math.Log10(step) - 1e-9), 0, 6);
        return value.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Whether the face of the box with outward normal along <paramref name="axis"/> on <paramref name="side"/> is turned
    /// away from the viewer: n · d &gt; 0 for the orthographic view direction d, or n · (face center - eye) &gt; 0 in
    /// perspective, where each face is seen from the eye and not along one direction.
    /// </summary>
    public static bool IsBackFace(int axis, int side, in AxesFrameLimits limits, in AxesFrameView view)
    {
        Vector3 n = Unit(axis) * side;
        if (view.Eye is Vector3 eye)
        {
            Vector3 center = Unit(axis) * (float)(side < 0 ? limits.Min : limits.Max);
            return Vector3.Dot(n, center - eye) > 0;
        }
        return Vector3.Dot(n, view.ViewDirection) > 0;
    }

    /// <summary>The frame for <paramref name="view"/>.</summary>
    public static AxesFrameLayout Layout(AxesFrameOptions options, AxesFrameLimits limits, in AxesFrameView view)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!(limits.Max > limits.Min) || !(limits.Step > 0))
            throw new ArgumentException($"Invalid limits [{limits.Min}, {limits.Max}] with step {limits.Step}.", nameof(limits));
        float min = (float)limits.Min, max = (float)limits.Max, side = max - min;
        var backFaces = new List<(int, int)>(3);
        bool[,] isBack = new bool[3, 2];
        for (int axis = 0; axis < 3; axis++)
        {
            for (int s = 0; s < 2; s++)
            {
                isBack[axis, s] = IsBackFace(axis, s == 0 ? -1 : 1, limits, view);
                if (isBack[axis, s])
                    backFaces.Add((axis, s == 0 ? -1 : 1));
            }
        }

        var back = new List<AxesFrameSegment>();
        var edges = new HashSet<(int Axis, int SideA, int SideB)>();
        int ticks = limits.TickCount;
        foreach ((int axis, int faceSide) in backFaces)
        {
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            // The four edges of the face, each along a or b: an edge along a sits at faceSide on axis and at ±1 on b.
            foreach (int sb in Sides)
                edges.Add(EdgeKey(a, axis, faceSide, b, sb));
            foreach (int sa in Sides)
                edges.Add(EdgeKey(b, axis, faceSide, a, sa));
            if (!options.ShowGrid)
                continue;
            float fixedValue = faceSide < 0 ? min : max;
            for (int t = 1; t < ticks - 1; t++)
            {
                float value = (float)limits.Tick(t);
                back.Add(new AxesFrameSegment(
                    Point(axis, fixedValue, a, value, b, min), Point(axis, fixedValue, a, value, b, max), AxesFrameSegmentKind.Grid, b));
                back.Add(new AxesFrameSegment(
                    Point(axis, fixedValue, b, value, a, min), Point(axis, fixedValue, b, value, a, max), AxesFrameSegmentKind.Grid, a));
            }
        }
        foreach ((int axis, int sa, int sb) in edges.OrderBy(e => e.Axis).ThenBy(e => e.SideA).ThenBy(e => e.SideB))
        {
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            float va = sa < 0 ? min : max, vb = sb < 0 ? min : max;
            back.Add(new AxesFrameSegment(Point(a, va, b, vb, axis, min), Point(a, va, b, vb, axis, max), AxesFrameSegmentKind.Edge, axis));
        }

        var front = new List<AxesFrameSegment>();
        var labels = new List<AxesFrameLabel>();
        var tickEdges = new (int, int)?[3];
        Func<double, string> format = options.Format ?? (v => FormatTick(v, limits.Step));
        Vector3 lookAt = view.Eye is Vector3 eyePoint ? Vector3.Normalize(-eyePoint) : view.ViewDirection;
        for (int axis = 0; axis < 3; axis++)
        {
            if (MathF.Abs(Vector3.Dot(Unit(axis), lookAt)) > AlongViewCosine)
                continue;
            int a = (axis + 1) % 3, b = (axis + 2) % 3;
            (int, int)? best = null;
            float bestHeight = float.MaxValue;
            foreach (int sa in Sides)
            {
                foreach (int sb in Sides)
                {
                    // On the outline: one of the two faces that meet at the edge is turned to the viewer, the other away.
                    if (isBack[a, sa < 0 ? 0 : 1] == isBack[b, sb < 0 ? 0 : 1])
                        continue;
                    float va = sa < 0 ? min : max, vb = sb < 0 ? min : max;
                    float height = view.ScreenHeight(Point(a, va, b, vb, axis, 0.5f * (min + max)));
                    if (height < bestHeight - 1e-6f * side)
                    {
                        bestHeight = height;
                        best = (sa, sb);
                    }
                }
            }
            if (best is not (int bestA, int bestB))
                continue;
            tickEdges[axis] = best;
            float ea = bestA < 0 ? min : max, eb = bestB < 0 ? min : max;
            Vector3 outward = Vector3.Normalize(Unit(a) * bestA + Unit(b) * bestB);
            for (int t = 0; t < ticks; t++)
            {
                Vector3 at = Point(a, ea, b, eb, axis, (float)limits.Tick(t));
                front.Add(new AxesFrameSegment(at, at + outward * (TickLength * side), AxesFrameSegmentKind.Tick, axis));
                labels.Add(new AxesFrameLabel(at + outward * (NumberOffset * side), format(limits.Tick(t)), axis, IsTitle: false));
            }
            string title = axis < options.AxisTitles.Length ? options.AxisTitles[axis] : string.Empty;
            if (title.Length > 0)
            {
                Vector3 middle = Point(a, ea, b, eb, axis, 0.5f * (min + max));
                labels.Add(new AxesFrameLabel(middle + outward * (TitleOffset * side), title, axis, IsTitle: true));
            }
        }
        return new AxesFrameLayout(limits, backFaces, tickEdges, back, front, labels, options.Title);
    }

    static (int, int, int) EdgeKey(int along, int axis1, int side1, int axis2, int side2)
    {
        int a = (along + 1) % 3;
        return a == axis1 ? (along, side1, side2) : (along, side2, side1);
    }

    static Vector3 Unit(int axis) => axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };

    static Vector3 Point(int axis1, float value1, int axis2, float value2, int axis3, float value3)
    {
        Span<float> c = stackalloc float[3];
        c[axis1] = value1;
        c[axis2] = value2;
        c[axis3] = value3;
        return new Vector3(c[0], c[1], c[2]);
    }

    static double NiceNumber(double value, bool round)
    {
        double exponent = Math.Floor(Math.Log10(value));
        double fraction = value / Math.Pow(10, exponent);
        double nice = round
            ? fraction < 1.5 ? 1 : fraction < 3 ? 2 : fraction < 7 ? 5 : 10
            : fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 5 ? 5 : 10;
        return nice * Math.Pow(10, exponent);
    }
}

/// <summary>A segment of the frame on the screen, in device-independent pixels.</summary>
/// <param name="Start">First end.</param>
/// <param name="End">Second end.</param>
/// <param name="Kind">What the segment is.</param>
/// <param name="Axis">Axis it belongs to (0 x, 1 y, 2 z).</param>
public readonly record struct AxesFrameLine(Vector2 Start, Vector2 End, AxesFrameSegmentKind Kind, int Axis);

/// <summary>A text of the frame on the screen, centered on <paramref name="Center"/>.</summary>
/// <param name="Center">Point the text is centered on, in device-independent pixels.</param>
/// <param name="Text">The text.</param>
/// <param name="Axis">Axis it belongs to (0 x, 1 y, 2 z).</param>
/// <param name="IsTitle">True for the axis name, false for a tick value.</param>
public readonly record struct AxesFrameText(Vector2 Center, string Text, int Axis, bool IsTitle);

/// <summary>
/// An <see cref="AxesFrameLayout"/> projected by the camera of a <see cref="DrawList"/>: <see cref="Back"/> is painted
/// before the scene, <see cref="Front"/> and <see cref="Texts"/> after it, and <see cref="Title"/> at the top of the
/// viewport. A piece whose end falls behind the perspective eye is left out.
/// </summary>
/// <param name="Layout">The frame in world coordinates.</param>
/// <param name="Back">Edges and grid of the back planes.</param>
/// <param name="Front">Tick marks.</param>
/// <param name="Texts">Tick values and axis names.</param>
/// <param name="Title">Text for the top of the viewport, or null.</param>
public sealed record PreparedAxesFrame(
    AxesFrameLayout Layout,
    IReadOnlyList<AxesFrameLine> Back,
    IReadOnlyList<AxesFrameLine> Front,
    IReadOnlyList<AxesFrameText> Texts,
    string? Title)
{
    /// <summary>Projects <paramref name="layout"/> with <paramref name="projector"/>.</summary>
    public static PreparedAxesFrame Project(AxesFrameLayout layout, in Projector projector)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var back = new List<AxesFrameLine>(layout.Back.Count);
        var front = new List<AxesFrameLine>(layout.Front.Count);
        var texts = new List<AxesFrameText>(layout.Labels.Count);
        foreach (AxesFrameSegment s in layout.Back)
            if (TryLine(projector, s, out AxesFrameLine line))
                back.Add(line);
        foreach (AxesFrameSegment s in layout.Front)
            if (TryLine(projector, s, out AxesFrameLine line))
                front.Add(line);
        foreach (AxesFrameLabel l in layout.Labels)
            if (projector.Project(l.Position, out float x, out float y, out _))
                texts.Add(new AxesFrameText(new Vector2(x, y), l.Text, l.Axis, l.IsTitle));
        return new PreparedAxesFrame(layout, back, front, texts, layout.Title);
    }

    static bool TryLine(in Projector projector, in AxesFrameSegment s, out AxesFrameLine line)
    {
        line = default;
        if (!projector.Project(s.Start, out float x1, out float y1, out _) || !projector.Project(s.End, out float x2, out float y2, out _))
            return false;
        line = new AxesFrameLine(new Vector2(x1, y1), new Vector2(x2, y2), s.Kind, s.Axis);
        return true;
    }
}
