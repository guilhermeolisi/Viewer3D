using SkiaSharp;

namespace Viewer3D.Avalonia;

/// <summary>
/// Paints frames of a <see cref="Viewer3DControl"/> into PNG images away from the UI thread, for exporting an animation
/// as numbered pictures. It holds a copy of the camera, the colors and the selection taken when it was created
/// (<see cref="Viewer3DControl.CreateFrameRenderer"/>), the axes frame included, so later changes to the control do not
/// reach it. Every call prepares the frame with its own camera copy and buffers: one instance can be used from several
/// threads at once.
/// </summary>
public sealed class SceneFrameRenderer
{
    readonly SceneSequence? sequence;
    readonly Scene scene;
    readonly Camera camera;
    readonly PaintOptions options;
    readonly bool labels;
    readonly AxesFrameOptions? axesFrame;

    internal SceneFrameRenderer(SceneSequence? sequence, Scene scene, Camera camera, PaintOptions options, bool labels,
        AxesFrameOptions? axesFrame, int width, int height, double scale)
    {
        this.sequence = sequence;
        this.scene = scene;
        this.camera = camera;
        this.options = options;
        this.labels = labels;
        this.axesFrame = axesFrame;
        Width = width;
        Height = height;
        Scale = scale;
    }

    /// <summary>Number of frames: the frames of the sequence, or 1 for a still scene.</summary>
    public int FrameCount => sequence?.Frames.Count ?? 1;

    /// <summary>Width in device-independent pixels.</summary>
    public int Width { get; }

    /// <summary>Height in device-independent pixels.</summary>
    public int Height { get; }

    /// <summary>Pixels per device-independent pixel.</summary>
    public double Scale { get; }

    /// <summary>Paints frame <paramref name="frame"/> (the full one, never the low detail version) into a PNG.</summary>
    public byte[] Render(int frame)
    {
        if ((uint)frame >= (uint)FrameCount)
            throw new ArgumentOutOfRangeException(nameof(frame));
        Scene target = sequence is null ? scene : sequence.Frames[frame];
        return Viewer3DControl.Paint(target, camera.Clone(), options, labels, axesFrame, Width, Height, Scale);
    }
}
