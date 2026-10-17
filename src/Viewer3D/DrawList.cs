namespace Viewer3D;

/// <summary>Kinds of primitive in a <see cref="DrawList"/>.</summary>
public enum DrawKind : byte
{
    /// <summary>A filled triangle with a color per vertex.</summary>
    Triangle = 0,

    /// <summary>A shaded circle (a sphere seen from the camera).</summary>
    Circle = 1,

    /// <summary>A thick segment (a rod half or a line).</summary>
    Segment = 2,
}

/// <summary>
/// The 2D result of preparing a frame: primitives in screen pixels with their colors already lit, and the order to
/// paint them (back to front). It does not depend on any graphics library; a painter walks <see cref="Commands"/> and
/// draws. Labels are kept apart and are painted last, on top of everything. Buffers grow and are reused across frames.
/// </summary>
public sealed class DrawList
{
    const int KindShift = 30;
    const uint IndexMask = (1u << KindShift) - 1;

    internal float[] TriXY = [];
    internal uint[] TriColor = [];
    internal int[] TriItem = [];
    internal int[] TriSource = [];

    internal float[] CircleData = []; // x, y, r
    internal uint[] CircleColor = [];
    internal int[] CircleItem = [];

    internal float[] SegData = []; // x1, y1, x2, y2, width
    internal uint[] SegColor = [];
    internal int[] SegItem = [];
    internal bool[] SegRound = [];

    internal float[] LabelData = []; // x, y, size
    internal string[] LabelText = [];
    internal uint[] LabelColor = [];
    internal bool[] LabelHasColor = [];

    internal uint[] Order = [];
    internal float[] OrderDepth = [];

    /// <summary>Viewport width, in device-independent pixels.</summary>
    public float Width { get; internal set; }

    /// <summary>Viewport height, in device-independent pixels.</summary>
    public float Height { get; internal set; }

    /// <summary>The scene this list was prepared from.</summary>
    public Scene? Scene { get; internal set; }

    /// <summary>Number of triangles.</summary>
    public int TriangleCount { get; internal set; }

    /// <summary>Number of circles.</summary>
    public int CircleCount { get; internal set; }

    /// <summary>Number of segments.</summary>
    public int SegmentCount { get; internal set; }

    /// <summary>Number of labels.</summary>
    public int LabelCount { get; internal set; }

    /// <summary>Number of commands in <see cref="Commands"/>.</summary>
    public int CommandCount { get; internal set; }

    /// <summary>Paint order, back to front. Decode each with <see cref="KindOf"/> and <see cref="IndexOf"/>.</summary>
    public ReadOnlySpan<uint> Commands => Order.AsSpan(0, CommandCount);

    /// <summary>Six floats per triangle: x and y of its three corners, in the mesh vertex order.</summary>
    public ReadOnlySpan<float> TrianglePositions => TriXY.AsSpan(0, 6 * TriangleCount);

    /// <summary>Three lit colors (0xAARRGGBB) per triangle.</summary>
    public ReadOnlySpan<uint> TriangleColors => TriColor.AsSpan(0, 3 * TriangleCount);

    /// <summary>Index in <see cref="Viewer3D.Scene.Items"/> of the item each triangle came from.</summary>
    public ReadOnlySpan<int> TriangleItems => TriItem.AsSpan(0, TriangleCount);

    /// <summary>Index in <see cref="Viewer3D.Scene.Items"/> of the item each circle came from.</summary>
    public ReadOnlySpan<int> CircleItems => CircleItem.AsSpan(0, CircleCount);

    /// <summary>Index in <see cref="Viewer3D.Scene.Items"/> of the item each segment came from.</summary>
    public ReadOnlySpan<int> SegmentItems => SegItem.AsSpan(0, SegmentCount);

    /// <summary>Three floats per circle: center x, center y, radius.</summary>
    public ReadOnlySpan<float> Circles => CircleData.AsSpan(0, 3 * CircleCount);

    /// <summary>Base color (0xAARRGGBB) per circle; the painter adds the shading.</summary>
    public ReadOnlySpan<uint> CircleColors => CircleColor.AsSpan(0, CircleCount);

    /// <summary>Five floats per segment: x1, y1, x2, y2, width.</summary>
    public ReadOnlySpan<float> Segments => SegData.AsSpan(0, 5 * SegmentCount);

    /// <summary>Lit color (0xAARRGGBB) per segment.</summary>
    public ReadOnlySpan<uint> SegmentColors => SegColor.AsSpan(0, SegmentCount);

    /// <summary>Whether each segment is a rod (round, shaded) or a plain line.</summary>
    public ReadOnlySpan<bool> SegmentIsRod => SegRound.AsSpan(0, SegmentCount);

    /// <summary>Three floats per label: x, y (top left after the offset) and font size.</summary>
    public ReadOnlySpan<float> Labels => LabelData.AsSpan(0, 3 * LabelCount);

    /// <summary>Text of each label.</summary>
    public ReadOnlySpan<string> LabelTexts => LabelText.AsSpan(0, LabelCount);

    /// <summary>Color of a label, or null to use the viewer's label color.</summary>
    public uint? LabelColorAt(int index) => LabelHasColor[index] ? LabelColor[index] : null;

    /// <summary>Kind of a command.</summary>
    public static DrawKind KindOf(uint command) => (DrawKind)(command >> KindShift);

    /// <summary>Index of a command inside the arrays of its kind.</summary>
    public static int IndexOf(uint command) => (int)(command & IndexMask);

    internal static uint Encode(DrawKind kind, int index) => ((uint)kind << KindShift) | (uint)index;

    internal void Reset(Scene scene, float width, float height)
    {
        Scene = scene;
        Width = width;
        Height = height;
        TriangleCount = CircleCount = SegmentCount = LabelCount = CommandCount = 0;
    }

    internal void EnsureTriangles(int count)
    {
        if (TriItem.Length >= count)
            return;
        int n = Grow(TriItem.Length, count);
        Array.Resize(ref TriXY, 6 * n);
        Array.Resize(ref TriColor, 3 * n);
        Array.Resize(ref TriItem, n);
        Array.Resize(ref TriSource, n);
    }

    internal void EnsureCircles(int count)
    {
        if (CircleItem.Length >= count)
            return;
        int n = Grow(CircleItem.Length, count);
        Array.Resize(ref CircleData, 3 * n);
        Array.Resize(ref CircleColor, n);
        Array.Resize(ref CircleItem, n);
    }

    internal void EnsureSegments(int count)
    {
        if (SegItem.Length >= count)
            return;
        int n = Grow(SegItem.Length, count);
        Array.Resize(ref SegData, 5 * n);
        Array.Resize(ref SegColor, n);
        Array.Resize(ref SegItem, n);
        Array.Resize(ref SegRound, n);
    }

    internal void EnsureLabels(int count)
    {
        if (LabelText.Length >= count)
            return;
        int n = Grow(LabelText.Length, count);
        Array.Resize(ref LabelData, 3 * n);
        Array.Resize(ref LabelText, n);
        Array.Resize(ref LabelColor, n);
        Array.Resize(ref LabelHasColor, n);
    }

    internal void EnsureCommands(int count)
    {
        if (Order.Length >= count)
            return;
        int n = Grow(Order.Length, count);
        Array.Resize(ref Order, n);
        Array.Resize(ref OrderDepth, n);
    }

    internal void AddCommand(DrawKind kind, int index, float depth)
    {
        EnsureCommands(CommandCount + 1);
        Order[CommandCount] = Encode(kind, index);
        OrderDepth[CommandCount] = depth;
        CommandCount++;
    }

    static int Grow(int current, int needed) => Math.Max(needed, Math.Max(16, current * 2));
}
