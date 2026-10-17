using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace Viewer3D.Avalonia;

/// <summary>Arguments of <see cref="Viewer3DControl.ItemHovered"/>.</summary>
/// <param name="pick">What is under the pointer, or null when nothing is.</param>
/// <param name="position">Pointer position in the control.</param>
public sealed class ItemHoveredEventArgs(PickResult? pick, Point position) : EventArgs
{
    /// <summary>What is under the pointer, or null when nothing is.</summary>
    public PickResult? Pick { get; } = pick;

    /// <summary>Pointer position in the control.</summary>
    public Point Position { get; } = position;
}

/// <summary>Arguments of <see cref="Viewer3DControl.ItemClicked"/>.</summary>
/// <param name="pick">What was clicked, or null when the click fell on the background.</param>
/// <param name="position">Pointer position in the control.</param>
public sealed class ItemClickedEventArgs(PickResult? pick, Point position) : EventArgs
{
    /// <summary>What was clicked, or null when the click fell on the background.</summary>
    public PickResult? Pick { get; } = pick;

    /// <summary>Pointer position in the control.</summary>
    public Point Position { get; } = position;
}

/// <summary>
/// Shows a <see cref="Scene"/> painted by software through Skia. Drag to rotate, right button or Shift and drag to
/// move, wheel to zoom at the pointer, double click to frame the scene, arrows to rotate and + or - to zoom.
/// While a gesture lasts the control paints <see cref="LowDetailScene"/>, when given, and goes back to
/// <see cref="Scene"/> when it ends. Colors come from the resources <c>Viewer3DBackgroundColor</c>,
/// <c>Viewer3DLabelColor</c> and <c>Viewer3DAxisColor0</c> to <c>Viewer3DAxisColor2</c> (a Color or a solid brush),
/// with built-in light and dark defaults.
/// </summary>
public class Viewer3DControl : Control
{
    /// <summary>Delay without wheel events after which a wheel gesture ends.</summary>
    public static readonly TimeSpan WheelGestureTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>Defines <see cref="Scene"/>.</summary>
    public static readonly StyledProperty<Scene?> SceneProperty =
        AvaloniaProperty.Register<Viewer3DControl, Scene?>(nameof(Scene));

    /// <summary>Defines <see cref="LowDetailScene"/>.</summary>
    public static readonly StyledProperty<Scene?> LowDetailSceneProperty =
        AvaloniaProperty.Register<Viewer3DControl, Scene?>(nameof(LowDetailScene));

    /// <summary>Defines <see cref="Camera"/>.</summary>
    public static readonly StyledProperty<Camera?> CameraProperty =
        AvaloniaProperty.Register<Viewer3DControl, Camera?>(nameof(Camera));

    /// <summary>Defines <see cref="SelectedItemId"/>.</summary>
    public static readonly StyledProperty<int?> SelectedItemIdProperty =
        AvaloniaProperty.Register<Viewer3DControl, int?>(nameof(SelectedItemId));

    /// <summary>Defines <see cref="AxisDirections"/>.</summary>
    public static readonly StyledProperty<IReadOnlyList<AxisDirection>?> AxisDirectionsProperty =
        AvaloniaProperty.Register<Viewer3DControl, IReadOnlyList<AxisDirection>?>(nameof(AxisDirections));

    /// <summary>Defines <see cref="ShowAxisIndicator"/>.</summary>
    public static readonly StyledProperty<bool> ShowAxisIndicatorProperty =
        AvaloniaProperty.Register<Viewer3DControl, bool>(nameof(ShowAxisIndicator), true);

    /// <summary>Defines <see cref="ShowLabels"/>.</summary>
    public static readonly StyledProperty<bool> ShowLabelsProperty =
        AvaloniaProperty.Register<Viewer3DControl, bool>(nameof(ShowLabels), true);

    /// <summary>Defines <see cref="Projection"/>.</summary>
    public static readonly StyledProperty<ProjectionKind> ProjectionProperty =
        AvaloniaProperty.Register<Viewer3DControl, ProjectionKind>(nameof(Projection));

    /// <summary>
    /// How far, in device-independent pixels, the pointer may move between press and release for the gesture to still
    /// count as a click.
    /// </summary>
    public const double ClickTolerance = 4;

    static readonly SKColor[] LightAxes = [new(0xFFD0342C), new(0xFF2E9E3E), new(0xFF2F5FD0)];
    static readonly SKColor[] DarkAxes = [new(0xFFFF6B60), new(0xFF5FD06A), new(0xFF6F9BFF)];

    readonly FramePreparer preparer = new();
    readonly DrawList[] lists = [new(), new()];
    readonly DispatcherTimer wheelTimer;
    int nextList;
    DrawList? current;
    Camera? subscribed;
    bool dragging, panning, wheeling, clicking;
    Point last, pressed;
    PickResult? lastHover;

    static Viewer3DControl()
    {
        AffectsRender<Viewer3DControl>(SceneProperty, LowDetailSceneProperty, SelectedItemIdProperty,
            AxisDirectionsProperty, ShowAxisIndicatorProperty, ShowLabelsProperty);
        FocusableProperty.OverrideDefaultValue<Viewer3DControl>(true);
        ClipToBoundsProperty.OverrideDefaultValue<Viewer3DControl>(true);
    }

    /// <summary>Creates the control with a camera of its own.</summary>
    public Viewer3DControl()
    {
        SetCurrentValue(CameraProperty, new Camera());
        wheelTimer = new DispatcherTimer { Interval = WheelGestureTimeout };
        wheelTimer.Tick += (_, _) =>
        {
            wheelTimer.Stop();
            if (wheeling)
            {
                wheeling = false;
                EndGesture();
            }
        };
    }

    /// <summary>The scene to show.</summary>
    public Scene? Scene
    {
        get => GetValue(SceneProperty);
        set => SetValue(SceneProperty, value);
    }

    /// <summary>A lighter version of <see cref="Scene"/> painted while a gesture lasts; null paints the full scene.</summary>
    public Scene? LowDetailScene
    {
        get => GetValue(LowDetailSceneProperty);
        set => SetValue(LowDetailSceneProperty, value);
    }

    /// <summary>The camera. Each control starts with its own; give two controls the same instance to link them.</summary>
    public Camera? Camera
    {
        get => GetValue(CameraProperty);
        set => SetValue(CameraProperty, value);
    }

    /// <summary>Identifier of the item drawn as selected, or null.</summary>
    public int? SelectedItemId
    {
        get => GetValue(SelectedItemIdProperty);
        set => SetValue(SelectedItemIdProperty, value);
    }

    /// <summary>Directions shown by the axis indicator in the lower left corner.</summary>
    public IReadOnlyList<AxisDirection>? AxisDirections
    {
        get => GetValue(AxisDirectionsProperty);
        set => SetValue(AxisDirectionsProperty, value);
    }

    /// <summary>Whether the axis indicator is shown.</summary>
    public bool ShowAxisIndicator
    {
        get => GetValue(ShowAxisIndicatorProperty);
        set => SetValue(ShowAxisIndicatorProperty, value);
    }

    /// <summary>Whether labels are shown.</summary>
    public bool ShowLabels
    {
        get => GetValue(ShowLabelsProperty);
        set => SetValue(ShowLabelsProperty, value);
    }

    /// <summary>Projection kind, applied to the camera.</summary>
    public ProjectionKind Projection
    {
        get => GetValue(ProjectionProperty);
        set => SetValue(ProjectionProperty, value);
    }

    /// <summary>Raised when the pointer moves over the control without a gesture.</summary>
    public event EventHandler<ItemHoveredEventArgs>? ItemHovered;

    /// <summary>
    /// Raised when the left button is pressed and released without moving the pointer more than
    /// <see cref="ClickTolerance"/> (and without Shift, which moves the view). A double click frames the scene and is
    /// not reported.
    /// </summary>
    public event EventHandler<ItemClickedEventArgs>? ItemClicked;

    /// <summary>Raised when a drag or a wheel gesture starts.</summary>
    public event EventHandler? GestureStarted;

    /// <summary>Raised when a drag or a wheel gesture ends.</summary>
    public event EventHandler? GestureEnded;

    /// <summary>Whether a gesture is in progress.</summary>
    public bool IsGestureActive => dragging || wheeling;

    /// <summary>The scene of the last frame prepared for the screen (for tests).</summary>
    internal Scene? LastPaintedScene { get; private set; }

    /// <summary>Number of frames prepared for the screen (for tests).</summary>
    internal int PaintCount { get; private set; }

    /// <summary>Frames the camera on the scene, keeping its orientation.</summary>
    public void FrameScene()
    {
        if (Scene is { } scene && Camera is { } camera)
            camera.Frame(scene);
    }

    /// <summary>
    /// Paints <see cref="Scene"/> (never the low detail one) into a PNG of <paramref name="width"/> by
    /// <paramref name="height"/> device-independent pixels, multiplied by <paramref name="scale"/>, with the current
    /// camera and theme colors.
    /// </summary>
    public byte[] RenderToPng(int width, int height, double scale = 1)
    {
        if (width <= 0 || height <= 0 || scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Size and scale must be positive.");
        var info = new SKImageInfo((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        SKCanvas canvas = surface.Canvas;
        canvas.Scale((float)scale);
        Camera camera = Camera?.Clone() ?? new Camera();
        DrawList list = new FramePreparer { IncludeLabels = ShowLabels }.Prepare(Scene ?? Scene.Empty, camera, width, height);
        SkiaPainter.Paint(canvas, list, CreateOptions(camera));
        canvas.Flush();
        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CameraProperty)
        {
            if (VisualRoot is not null)
                Subscribe(change.GetNewValue<Camera?>());
            if (change.GetNewValue<Camera?>() is { } camera)
                camera.Projection = Projection;
            InvalidateVisual();
        }
        else if (change.Property == ProjectionProperty && Camera is { } camera)
        {
            camera.Projection = change.GetNewValue<ProjectionKind>();
        }
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Subscribe(Camera);
        ActualThemeVariantChanged += OnThemeChanged;
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Subscribe(null);
        ActualThemeVariantChanged -= OnThemeChanged;
        wheelTimer.Stop();
        dragging = panning = wheeling = clicking = false;
    }

    void OnThemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    void Subscribe(Camera? camera)
    {
        if (subscribed is not null)
            subscribed.Changed -= OnCameraChanged;
        subscribed = camera;
        if (camera is not null)
            camera.Changed += OnCameraChanged;
    }

    void OnCameraChanged(object? sender, EventArgs e) => InvalidateVisual();

    /// <inheritdoc/>
    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || Camera is not { } camera)
            return;
        Scene scene = (IsGestureActive ? LowDetailScene : null) ?? Scene ?? Scene.Empty;
        DrawList list = lists[nextList];
        nextList = 1 - nextList;
        // The render thread may still be painting this list from two frames ago.
        lock (list)
        {
            preparer.IncludeLabels = ShowLabels;
            preparer.Prepare(scene, camera, (float)size.Width, (float)size.Height, list);
        }
        current = list;
        LastPaintedScene = scene;
        PaintCount++;
        context.Custom(new DrawOperation(new Rect(size), list, CreateOptions(camera)));
    }

    PaintOptions CreateOptions(Camera camera)
    {
        bool dark = ActualThemeVariant == ThemeVariant.Dark;
        SKColor background = FindColor("Viewer3DBackgroundColor") ?? (dark ? new SKColor(0xFF1E1E1E) : SKColors.White);
        SKColor label = FindColor("Viewer3DLabelColor") ?? (dark ? new SKColor(0xFFE6E6E6) : new SKColor(0xFF202020));
        SKColor[] defaults = dark ? DarkAxes : LightAxes;
        var axes = new SKColor[defaults.Length];
        for (int i = 0; i < axes.Length; i++)
            axes[i] = FindColor($"Viewer3DAxisColor{i}") ?? defaults[i];
        return new PaintOptions(background, label, axes, AxisDirections, camera.Orientation, ShowAxisIndicator, ShowLabels, SelectedItemId);
    }

    SKColor? FindColor(string key)
    {
        if (!this.TryFindResource(key, ActualThemeVariant, out object? value))
            return null;
        return value switch
        {
            Color c => new SKColor(c.R, c.G, c.B, c.A),
            ISolidColorBrush b => new SKColor(b.Color.R, b.Color.G, b.Color.B, (byte)(b.Color.A * Math.Clamp(b.Opacity, 0, 1))),
            _ => null,
        };
    }

    /// <inheritdoc/>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Focus();
        if (e.ClickCount == 2)
        {
            FrameScene();
            e.Handled = true;
            return;
        }
        PointerPoint point = e.GetCurrentPoint(this);
        panning = point.Properties.IsRightButtonPressed || e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        clicking = point.Properties.IsLeftButtonPressed && !panning;
        last = pressed = point.Position;
        e.Pointer.Capture(this);
        bool started = !IsGestureActive;
        dragging = true;
        if (started)
            GestureStarted?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point p = e.GetPosition(this);
        if (dragging && Camera is { } camera)
        {
            float dx = (float)(p.X - last.X), dy = (float)(p.Y - last.Y);
            last = p;
            if (clicking && Point.Distance(p, pressed) > ClickTolerance)
                clicking = false;
            if (panning)
                camera.Pan(dx, dy, (float)Bounds.Width, (float)Bounds.Height);
            else
                camera.Orbit(dx, dy);
            return;
        }
        PickResult? pick = current is null ? null : Picker.Pick(current, (float)p.X, (float)p.Y);
        if (pick is not null || lastHover is not null)
            ItemHovered?.Invoke(this, new ItemHoveredEventArgs(pick, p));
        lastHover = pick;
    }

    /// <inheritdoc/>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging)
            return;
        Point p = e.GetPosition(this);
        if (clicking && e.InitialPressMouseButton == MouseButton.Left && Point.Distance(p, pressed) <= ClickTolerance)
        {
            PickResult? pick = current is null ? null : Picker.Pick(current, (float)p.X, (float)p.Y);
            ItemClicked?.Invoke(this, new ItemClickedEventArgs(pick, p));
        }
        clicking = false;
        // Before releasing the capture, whose loss would end the gesture a second time.
        StopDrag();
        e.Pointer.Capture(null);
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (dragging)
            StopDrag();
    }

    /// <inheritdoc/>
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (lastHover is not null)
        {
            lastHover = null;
            ItemHovered?.Invoke(this, new ItemHoveredEventArgs(null, e.GetPosition(this)));
        }
    }

    void StopDrag()
    {
        dragging = false;
        if (!wheeling)
            EndGesture();
    }

    void EndGesture()
    {
        GestureEnded?.Invoke(this, EventArgs.Empty);
        // The full scene comes back.
        InvalidateVisual();
    }

    /// <inheritdoc/>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Camera is not { } camera || e.Delta.Y == 0)
            return;
        if (!IsGestureActive)
            GestureStarted?.Invoke(this, EventArgs.Empty);
        wheeling = true;
        wheelTimer.Stop();
        wheelTimer.Start();
        Point p = e.GetPosition(this);
        camera.ZoomAt((float)Math.Pow(1.15, e.Delta.Y), (float)p.X, (float)p.Y, (float)Bounds.Width, (float)Bounds.Height);
        e.Handled = true;
    }

    /// <inheritdoc/>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Camera is not { } camera)
            return;
        const float step = MathF.PI / 36;
        float w = (float)Bounds.Width, h = (float)Bounds.Height;
        switch (e.Key)
        {
            case Key.Left: camera.Rotate(Vector3.UnitY, -step); break;
            case Key.Right: camera.Rotate(Vector3.UnitY, step); break;
            case Key.Up: camera.Rotate(Vector3.UnitX, -step); break;
            case Key.Down: camera.Rotate(Vector3.UnitX, step); break;
            case Key.Add or Key.OemPlus: camera.ZoomAt(1.2f, w / 2, h / 2, w, h); break;
            case Key.Subtract or Key.OemMinus: camera.ZoomAt(1 / 1.2f, w / 2, h / 2, w, h); break;
            default: return;
        }
        e.Handled = true;
    }

    sealed class DrawOperation(Rect bounds, DrawList list, PaintOptions options) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

        public bool HitTest(Point p) => bounds.Contains(p);

        public bool Equals(ICustomDrawOperation? other) => false;

        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null)
                return;
            using var lease = feature.Lease();
            lock (list)
                SkiaPainter.Paint(lease.SkCanvas, list, options);
        }
    }
}
