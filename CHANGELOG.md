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
  (`CrystalAtom.Displacements`) at a probability level of 30, 50, 70, 90 or 99% (`ProbabilityScale`), with thin rods
  cut at the surfaces; a tensor that is not positive definite is a small cube and an atom without data a dashed sphere.
  `CrystalSceneOptions.DisplacementMagnification` draws the shapes larger or smaller than their probability (default 1).
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
- `Viewer3D.Crystal`: `CrystalAppearance`, passed to `CrystalSceneBuilder.Build`, sets the look of a structure in three
  cascades: atoms by element, site, atom and image of an atom in one cell (visible, color, opacity, radius scale,
  label), bonds by element pair and bond (visible, color, opacity, radius), and polyhedra by central element, site and
  index (visible, face color and opacity, faces, edges, edge color and width). Switches hide all atoms, bonds, the cell
  or the polyhedron centers; atoms can be colored by site or occupancy, with `CrystalScene.ColorLegend`. A hidden atom
  takes its label and rods with it and leaves the polyhedra it belongs to whole; `CrystalScene.HiddenAtomCount` counts
  the hidden atoms. Without an appearance, or with `CrystalAppearance.Default`, the scene is the same as before.
- `Viewer3D.Avalonia`: `SelectedItemIds` rings several items in the `Viewer3DSelectionColor` resource; `ItemClicked`
  carries the modifier keys; `HoveredPick` tells what is under the pointer; `SceneBackground` sets the background over
  the theme (alpha 0 gives a transparent PNG); `OutlineWidth` sets the outline of spheres and ellipses.
- `Viewer3D.Sample`: `--bench-appearance N` times the scene build with and without appearance rules; the export writes
  `crystal-polyhedra-only.png`.
- `Viewer3D.Crystal`: `CrystalAtom.IsVoid` marks a void site, an empty interstice. It is drawn in every style as a small
  translucent marker (`CrystalSceneOptions.VoidMarkerRadius`, `VoidColor`, `VoidOpacity`), or as the sphere of its
  `Radius` with `ShowVoidSpheres`, can be the center of a polyhedron, and stays out of the legends, the displacement
  shapes, the space filling scale and `CrystalPacking`.
- `Viewer3D`: `MeshShapes.Arrow` (a shaft and a cone, tip on the end point), `MeshShapes.Polygon` (a flat, double sided
  fan with its outline as edges, translucent by alpha), `LineShapes.Polyline` (one line per segment, with one segment
  highlighted in its own color and width) and `SphereItem.Halo` (a ring a few pixels wide just outside the disc).
- `Viewer3D.Crystal`: `CrystalAtom.Group` puts an atom in one of `CrystalStructure.Groups` (a label and a color), with
  its own level of rules, `CrystalAppearance.ByGroup`, between the site and the atom, and the `AtomColorScheme.Group`
  scheme with the groups in the color legend. `AtomStyle.Halo` rings an atom. `CrystalStructure.Annotations` are scene
  items drawn over the structure (lines, arrows, planes, labels), picked as `CrystalItemKind.Annotation` with
  `CrystalItemInfo.AnnotationIndex`, and hidden by `CrystalAppearance.ShowAnnotations`.
- `Viewer3D.Crystal`: `CrystalAppearance.RemovedBonds` leaves bonds out of the drawing by `CrystalBond.SourceIndex`,
  every image together and with no faded neighbor left behind; `ShowAll()` keeps them.
  `CrystalBondExpansion.EquivalentSources` gives the sources that draw the same bonds after the expansion (the same two
  sites, either direction, the same distance), so removing one removes them all.
- `Viewer3D.Crystal`: the repetition is no longer limited to 3 cells per axis; `CrystalSceneOptions.MaxCost` (default
  `MaxAtoms`, 3000) bounds the cost of a scene, its spheres plus `PolyhedronCost` per polyhedron.
  `CrystalSceneBuilder.Estimate` counts the cost without building and gives the block or radius that fits
  (`CrystalSceneEstimate`), and `TryBuild` builds only within the ceiling. A block far past the ceiling is no longer
  built before falling back to one cell.
- `Viewer3D.Sample`: `--bench-cost 3000,6000,...` times the build and the frame of fluorite blocks of those costs.
- `Viewer3D`: `ObjReader` reads triangle meshes from Wavefront OBJ files (vertices, normals, faces of any polygon,
  negative indices, `mtllib`/`usemtl` with `Kd` and `d`) into an `ObjModel`, one `MeshItem` per material. The file is
  treated as untrusted: `ObjReadOptions` bounds the triangles (100,000 by default), the vertices and the size, a bad
  line is refused with its number (`ObjReadException`), the read can be canceled, and a material file is only read
  from the folder of the model.
- `Viewer3D`: `LineItem.Dash` draws a dashed line whose pattern runs across the pieces it is drawn in
  (`DrawList.SegmentDashes` and `SegmentDashOffsets`). `SceneItem.Pickable` set to false lets picking go through an
  item. `MeasureShapes` builds the overlay of a distance (dashed line and label) and of an angle (dashed arms, arc and
  label).
- `Viewer3D.Sample`: `--bench-obj 25000,100000,...` times the read and the frame of OBJ spheres of about those
  triangle counts; the sample shows an OBJ mesh and a measurement.
- `Viewer3D`: `CameraState` (orientation, target, view radius), `Camera.State` and `Camera.Set`, which applies the
  three with one `Changed`. `CameraAnimation` gives the state of a timed move at any instant (shorter arc, straight
  target, log-scale radius, smooth step or a given easing) and `CameraTurntable` a never ending turn around a view axis.
- `Viewer3D.Avalonia`: `Viewer3DControl.AnimateCamera` moves the camera to a state over a duration, and
  `StartTurntable`/`StopTurntable` turn it continuously; `IsCameraAnimating` tells whether either runs. Frames come from
  `TopLevel.RequestAnimationFrame`, at most 30 per second and by elapsed time, painting `LowDetailScene`; a drag, the
  wheel, a key, a camera change from elsewhere or leaving the visual tree stops them.
- `Viewer3D.Sample`: `--export` also writes four turntable frames.
- `Viewer3D`: `SceneSequence`, an animation made of ready scenes (a flipbook) with a duration per frame, `Once`, `Loop`
  or `PingPong` (`SequenceRepeat`), optional low detail frames and the union of the bounds of every frame. Its `Weight`
  (items plus mesh vertices) has a ceiling, `DefaultMaxWeight` = 4,000,000, measured at up to 128 managed bytes per
  unit; past it the constructor throws `SceneSequenceTooLargeException`. `FrameFileName` names exported frames.
- `Viewer3D.Avalonia`: `Viewer3DControl.Sequence`, `FrameIndex`, `IsPlaying`, `PlaybackRate` and `FrameChanged` play a
  sequence on the camera animation clock, painting the low detail frame while it plays; `CreateFrameRenderer` paints
  any frame to PNG from any thread.
- `Viewer3D`: `ObjSequenceReader` reads a series of OBJ files (a list or every `*.obj` of a folder) as a
  `SceneSequence`, in `NaturalOrder` of the names (`step_2` before `step_10`), summing the weight file by file; a file
  refused carries its name, and fewer than two files is `ObjReadError.TooFewFrames`.
- `Viewer3D.Crystal`: `CrystalTrajectory` holds the structures of a trajectory, one per frame, and builds them into a
  `SceneSequence` (`ToSequence`, with the weight checked from the atom counts before building and frame by frame
  after); a molecule without a cell moves inside one box around the whole trajectory, drawn without edges.
  `CrystalBondFinder` finds bonds from the geometry (the Jmol criterion, d up to r1 + r2 + 0.45 angstrom, with no
  metal-metal bond when the structure has another element). `CrystalCell.FromVectors` makes a cell from three edge
  vectors in any orientation.
- `Viewer3D.Sample`: `--bench-sequence-memory N` counts the bytes a frame holds per unit of weight, and
  `--bench-sequence N` times a frame of a sequence of fluorite blocks.

## 0.1.0 - 10/17/2026

First version.

- `Viewer3D`: scene items (meshes, spheres, rods, lines, labels), orbiting camera with orthographic and perspective
  projection, frame preparation with back-face culling, a light attached to the viewer or a key light from the upper left with a
  small highlight, a weak fill light and a high ambient (`MeshLighting`, chosen per mesh), and back-to-front ordering
  (lines are cut where they pass inside a sphere and split into short pieces, so a cell edge behind an atom is never
  painted over it), picking, the axis indicator, level and function color maps, and builders for spherical surfaces and height fields.
- `Viewer3D.Avalonia`: `Viewer3DControl` for Avalonia 12.1, painted with SkiaSharp, with a low detail scene during
  gestures, theme colors from resources and `RenderToPng`.
