using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using Viewer3D.Avalonia.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(HeadlessApp))]

// Avalonia.Headless isolates each test with process-wide state, so tests in this assembly run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Viewer3D.Avalonia.Tests.TestSupport;

/// <summary>
/// Headless application with real Skia drawing (the default headless drawing is a fake that paints nothing) and no
/// theme: windows get a minimal template that only presents their content.
/// </summary>
public sealed class HeadlessApp : Application
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<HeadlessApp>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

internal static class TestWindows
{
    public static Window Show(Control content, double width = 300, double height = 200)
    {
        var window = new Window
        {
            Width = width,
            Height = height,
            Content = content,
            Template = new FuncControlTemplate<Window>((w, _) => new ContentPresenter
            {
                Name = "PART_ContentPresenter",
                [!ContentPresenter.ContentProperty] = w[!ContentControl.ContentProperty],
            }),
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    /// <summary>Renders the window and returns its pixels as 0xAARRGGBB.</summary>
    public static uint[,] Capture(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using ILockedFramebuffer fb = frame.Lock();
        bool bgra = fb.Format == PixelFormat.Bgra8888;
        Assert.True(bgra || fb.Format == PixelFormat.Rgba8888, $"unexpected format {fb.Format}");
        var pixels = new uint[fb.Size.Width, fb.Size.Height];
        for (int y = 0; y < fb.Size.Height; y++)
        {
            for (int x = 0; x < fb.Size.Width; x++)
            {
                uint raw = (uint)Marshal.ReadInt32(fb.Address, y * fb.RowBytes + 4 * x);
                // Little endian: BGRA reads as 0xAARRGGBB, RGBA as 0xAABBGGRR.
                pixels[x, y] = bgra ? raw : (raw & 0xFF00FF00) | ((raw & 0xFF) << 16) | ((raw >> 16) & 0xFF);
            }
        }
        return pixels;
    }

    public static SKBitmap Decode(byte[] png)
    {
        SKBitmap? bitmap = SKBitmap.Decode(png);
        Assert.NotNull(bitmap);
        return bitmap;
    }
}
