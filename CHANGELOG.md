# Changelog

## Unreleased

- `Viewer3D.Crystal`, a new optional package: a crystal structure (cell, atoms, bonds) turned into a scene in ball and
  stick or space filling, with a block of 1 to 3 cells along each axis, the atoms on the far faces, the bonded
  neighbors outside the block as faded spheres, the cell edges, labels, a legend, and the element colors and radii
  for Z 1 to 118. `CrystalBondExpansion` extends bonds given around one atom per site to the equivalent atoms.
- `Viewer3D`: `SphereItem.Sectors` paints a sphere in wedges, one color each, leaving the rest of the turn empty.
- `Viewer3D.Avalonia`: `Viewer3DControl.ItemClicked` reports what was clicked on a left click that did not drag.
- `Viewer3D`: `EllipsoidItem`, a shaded ellipsoid given by its three semi-axes, painted as its outline ellipse with the
  visible halves of its principal ellipses (`DrawKind.Ellipse`, the last kind the command field has room for), cut
  out of the lines that cross it and picked inside its outline. `EllipseProjection` holds the geometry.
  `SphereItem.Dashed` draws only a dashed outline. `MeshShapes.Cube` builds a small cube.
- `Viewer3D.Crystal`: the `ThermalEllipsoids` style draws each atom from its displacement tensors
  (`CrystalAtom.Displacements`) at a probability level of 30, 50, 70 or 90% (`ProbabilityScale`), with thin rods cut
  at the surfaces; a tensor that is not positive definite is a small cube and an atom without data a dashed sphere.
- `Viewer3D.Sample`: `--bench-ellipsoids N` times a frame of N ellipsoids.
- `Viewer3D`: `MeshItem.Edges` draws only the given edges of a mesh (the edges of a polyhedron, not the diagonals of
  its faces), cut out where they enter a sphere and painted after the faces they border; picking one reports the mesh.
  A mesh color with alpha below 255 gives translucent faces, and the picker then prefers an opaque item seen through
  them.
- `Viewer3D.Crystal`: the `Polyhedra` and `PolyhedraWithEllipsoids` styles draw the coordination polyhedra given in
  `CrystalStructure.Polyhedra` (the layer does not compute hulls) as translucent meshes with their edges around every
  drawn image of their centers, with options for the opacity, the vertex atoms, the rods inside, the back faces and
  which polyhedra are drawn. `CrystalItemKind.Polyhedron` and `CrystalItemInfo.PolyhedronIndex` report a click on one.
- `Viewer3D.Sample`: `--bench-polyhedra N` times a frame of N translucent octahedra.
- `Viewer3D.Crystal`: `CrystalPacking.Measure` gives the packing fraction of a structure with the same radii the
  space filling style draws (`PackingMeasure`: the scale at which the nearest spheres touch, the sphere and cell
  volumes, and the fraction); `CellBasis.Volume` gives the cell volume.

## 0.1.0 - 10/17/2026

First version.

- `Viewer3D`: scene items (meshes, spheres, rods, lines, labels), orbiting camera with orthographic and perspective
  projection, frame preparation with back-face culling, a light attached to the viewer or a key light from the upper left with a
  small highlight, a weak fill light and a high ambient (`MeshLighting`, chosen per mesh), and back-to-front ordering
  (lines are cut where they pass inside a sphere and split into short pieces, so a cell edge behind an atom is never
  painted over it), picking, the axis indicator, level and function color maps, and builders for spherical surfaces and height fields.
- `Viewer3D.Avalonia`: `Viewer3DControl` for Avalonia 12.1, painted with SkiaSharp, with a low detail scene during
  gestures, theme colors from resources and `RenderToPng`.
