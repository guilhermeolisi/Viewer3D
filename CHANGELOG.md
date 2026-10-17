# Changelog

## 0.1.0 - 10/17/2026

First version.

- `Viewer3D`: scene items (meshes, spheres, rods, lines, labels), orbiting camera with orthographic and perspective
  projection, frame preparation with back-face culling, a light attached to the viewer and back-to-front ordering
  (lines are cut where they pass inside a sphere and split into short pieces, so a cell edge behind an atom is never
  painted over it), picking, the axis indicator, level and function color maps, and builders for spherical surfaces and height fields.
- `Viewer3D.Avalonia`: `Viewer3DControl` for Avalonia 12.1, painted with SkiaSharp, with a low detail scene during
  gestures, theme colors from resources and `RenderToPng`.
