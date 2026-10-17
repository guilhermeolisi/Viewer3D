# Viewer3D

A small 3D viewer for [Avalonia](https://avaloniaui.net/) 12, painted by software through SkiaSharp. It shows
surfaces (closed spherical surfaces and height fields) and ball-and-stick scenes, with orbit, zoom and pan, a fixed
axis indicator, picking, and off-screen export to PNG.

It does not use OpenGL or any GPU API: frames are prepared on the CPU and painted as Skia triangles, circles and
lines. It looks and behaves the same with GPU or software rendering, and in `Avalonia.Headless`, which makes it easy
to test.

| Spherical surface | Balls and sticks |
|---|---|
| ![Spherical surface colored by levels](docs/images/surface.png) | ![Ball-and-stick scene](docs/images/atoms.png) |

## Packages

| Package | What it has |
|---|---|
| `Viewer3D` | Scene, camera, frame preparation, picking, axis indicator, color maps and surface builders. No UI and no graphics library: only `System.Numerics`. |
| `Viewer3D.Avalonia` | The `Viewer3DControl` for Avalonia 12, painted with SkiaSharp. |

## Quick start

```csharp
using System.Numerics;
using Viewer3D;
using Viewer3D.Avalonia;

// A surface r(theta, phi) on a grid of N + 1 rows: one value at each pole, 2N values in between.
int n = 90;
double[][] radii = /* your grid */;
var map = new LevelColorMap(levels, colors);           // or new FuncColorMap(v => ...)
// The key light, from the upper left, shows the relief; the default light, at the eye, is flatter.
MeshItem surface = SurfaceBuilder.Spherical(radii, n, map: map, lighting: MeshLighting.KeyLight);

var viewer = new Viewer3DControl
{
    Scene = new Scene([surface]),
    // Optional: a lighter scene painted while the user drags or zooms.
    LowDetailScene = new Scene([SurfaceBuilder.Spherical(SurfaceBuilder.Decimate(radii, n, 3), n / 3, map: map, lighting: MeshLighting.KeyLight)]),
    AxisDirections = [new(Vector3.UnitX, "a"), new(Vector3.UnitY, "b"), new(Vector3.UnitZ, "c")],
};
viewer.Camera!.Frame(viewer.Scene);

byte[] png = viewer.RenderToPng(1600, 1200);
```

Scenes are lists of items: `MeshItem` (triangles with a color per vertex or per face), `SphereItem`, `RodItem` (a
stick drawn in two halves, each with the color of its end, cut where it enters the spheres), `LineItem` and
`LabelItem`. Each item has an `Id`, which picking reports back. A mesh is lit by a light at the eye
(`MeshLighting.Headlight`, the default) or by a key light from the upper left of the screen with a small highlight
(`MeshLighting.KeyLight`), which shows the relief of a surface seen head on.

## Interaction

| Gesture | Effect |
|---|---|
| Drag | Rotate |
| Right drag, or Shift and drag | Move |
| Wheel | Zoom at the pointer |
| Double click | Frame the scene |
| Arrow keys, `+` and `-` | Rotate and zoom |

The `Camera` raises `Changed` on every change, so two viewers that share one camera instance move together.
`ItemHovered` reports what is under the pointer, with the triangle and barycentric weights for meshes.

## Theme colors

The control reads these resources (a `Color` or a solid brush), with built-in light and dark defaults:
`Viewer3DBackgroundColor`, `Viewer3DLabelColor`, `Viewer3DAxisColor0`, `Viewer3DAxisColor1` and
`Viewer3DAxisColor2`.

## Performance

Measured on a desktop CPU with software rendering: a spherical surface of N = 90 (32 040 triangles) turns at about
40 to 50 frames per second; N = 360 (516 960 triangles) at about 7. Give `LowDetailScene` a coarser surface and the
viewer paints it during gestures and the full one when the gesture ends.

## Building

```
dotnet test Viewer3D.sln -c Release
dotnet run --project samples/Viewer3D.Sample
```

`samples/Viewer3D.Sample --export <dir>` writes the two images above.

## License

[MIT](LICENSE).
