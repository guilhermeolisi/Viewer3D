using System.Numerics;

namespace Viewer3D;

/// <summary>Builds meshes for surfaces given on regular grids.</summary>
public static class SurfaceBuilder
{
    /// <summary>
    /// Number of vertices of a spherical grid of size <paramref name="n"/>: one per pole and 2N per inner row.
    /// </summary>
    public static int SphericalVertexCount(int n) => 2 * n * (n - 1) + 2;

    /// <summary>Number of triangles of a spherical grid of size <paramref name="n"/>.</summary>
    public static int SphericalTriangleCount(int n) => 4 * n * (n - 1);

    /// <summary>
    /// A closed surface r(theta, phi). <paramref name="radii"/> has N + 1 rows: row i is theta = i·pi/N; rows 0 and N
    /// (the poles) have one value, the others 2N values at phi = j·pi/N, j = 0 .. 2N - 1 (the seam between the last
    /// column and the first is closed). Theta is measured from +z and phi from +x towards +y.
    /// </summary>
    /// <param name="radii">The grid of radii.</param>
    /// <param name="n">N, the number of theta steps.</param>
    /// <param name="scalars">Values to color by, on the same grid; null colors by the radius.</param>
    /// <param name="map">Color map; null leaves the mesh with its single color.</param>
    /// <param name="id">Identifier of the mesh.</param>
    public static MeshItem Spherical(IReadOnlyList<IReadOnlyList<double>> radii, int n, IReadOnlyList<IReadOnlyList<double>>? scalars = null, IColorMap? map = null, int id = 0)
    {
        CheckSphericalGrid(radii, n, nameof(radii));
        if (scalars is not null)
            CheckSphericalGrid(scalars, n, nameof(scalars));

        int vertexCount = SphericalVertexCount(n);
        var positions = new Vector3[vertexCount];
        var values = new float[vertexCount];
        int k = 0;
        for (int i = 0; i <= n; i++)
        {
            double theta = i * Math.PI / n;
            double st = i == 0 || i == n ? 0 : Math.Sin(theta), ct = i == 0 ? 1 : i == n ? -1 : Math.Cos(theta);
            IReadOnlyList<double> row = radii[i];
            for (int j = 0; j < row.Count; j++)
            {
                double phi = j * Math.PI / n;
                double r = row[j];
                positions[k] = new Vector3((float)(r * st * Math.Cos(phi)), (float)(r * st * Math.Sin(phi)), (float)(r * ct));
                values[k] = (float)(scalars is null ? r : scalars[i][j]);
                k++;
            }
        }

        int columns = 2 * n;
        var indices = new int[3 * SphericalTriangleCount(n)];
        int t = 0;
        int south = vertexCount - 1;
        // Counter-clockwise when seen from outside.
        for (int j = 0; j < columns; j++)
        {
            int next = (j + 1) % columns;
            Put(indices, ref t, 0, Inner(1, j, n), Inner(1, next, n));
            Put(indices, ref t, Inner(n - 1, j, n), south, Inner(n - 1, next, n));
        }
        for (int i = 1; i < n - 1; i++)
        {
            for (int j = 0; j < columns; j++)
            {
                int next = (j + 1) % columns;
                int a = Inner(i, j, n), b = Inner(i, next, n), c = Inner(i + 1, j, n), d = Inner(i + 1, next, n);
                Put(indices, ref t, a, c, b);
                Put(indices, ref t, b, c, d);
            }
        }

        return new MeshItem
        {
            Id = id,
            Positions = positions,
            Indices = indices,
            Scalars = values,
            VertexColors = map is null ? null : Colors(values, map),
        };
    }

    /// <summary>
    /// An open surface z(x, y). <paramref name="heights"/> has one row per y (from <paramref name="yMin"/> to
    /// <paramref name="yMax"/>) and one column per x (from <paramref name="xMin"/> to <paramref name="xMax"/>). The mesh
    /// is double-sided.
    /// </summary>
    public static MeshItem HeightField(IReadOnlyList<IReadOnlyList<double>> heights, float xMin, float xMax, float yMin, float yMax, IReadOnlyList<IReadOnlyList<double>>? scalars = null, IColorMap? map = null, int id = 0)
    {
        ArgumentNullException.ThrowIfNull(heights);
        int rows = heights.Count;
        int cols = rows > 0 ? heights[0].Count : 0;
        if (rows < 2 || cols < 2)
            throw new ArgumentException("A height field needs at least 2 x 2 points.", nameof(heights));
        for (int i = 0; i < rows; i++)
            if (heights[i].Count != cols || (scalars is not null && scalars[i].Count != cols))
                throw new ArgumentException("Every row must have the same number of points.", nameof(heights));
        if (scalars is not null && scalars.Count != rows)
            throw new ArgumentException("The scalar grid must have the size of the height grid.", nameof(scalars));

        var positions = new Vector3[rows * cols];
        var values = new float[rows * cols];
        for (int i = 0; i < rows; i++)
        {
            float y = yMin + (yMax - yMin) * i / (rows - 1);
            for (int j = 0; j < cols; j++)
            {
                float x = xMin + (xMax - xMin) * j / (cols - 1);
                double z = heights[i][j];
                positions[i * cols + j] = new Vector3(x, y, (float)z);
                values[i * cols + j] = (float)(scalars is null ? z : scalars[i][j]);
            }
        }
        var indices = new int[6 * (rows - 1) * (cols - 1)];
        int t = 0;
        for (int i = 0; i < rows - 1; i++)
        {
            for (int j = 0; j < cols - 1; j++)
            {
                int a = i * cols + j, b = a + 1, c = a + cols, d = c + 1;
                Put(indices, ref t, a, b, c);
                Put(indices, ref t, b, d, c);
            }
        }
        return new MeshItem
        {
            Id = id,
            Positions = positions,
            Indices = indices,
            Scalars = values,
            VertexColors = map is null ? null : Colors(values, map),
            DoubleSided = true,
        };
    }

    /// <summary>
    /// Keeps every <paramref name="step"/>-th row and column of a spherical grid of size <paramref name="n"/>: the
    /// result is a grid of size N / step at exactly the same angles. N must be a multiple of the step.
    /// </summary>
    public static double[][] Decimate(IReadOnlyList<IReadOnlyList<double>> grid, int n, int step)
    {
        CheckSphericalGrid(grid, n, nameof(grid));
        if (step < 1 || n % step != 0 || n / step < 2)
            throw new ArgumentException("N must be a multiple of the step, and N / step at least 2.", nameof(step));
        int m = n / step;
        var result = new double[m + 1][];
        for (int i = 0; i <= m; i++)
        {
            IReadOnlyList<double> row = grid[i * step];
            if (i == 0 || i == m)
            {
                result[i] = [row[0]];
                continue;
            }
            result[i] = new double[2 * m];
            for (int j = 0; j < 2 * m; j++)
                result[i][j] = row[j * step];
        }
        return result;
    }

    /// <summary>The same mesh (same position and index arrays) colored again from its own scalars.</summary>
    public static MeshItem Recolor(MeshItem mesh, IColorMap map)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.Scalars is null)
            throw new ArgumentException("The mesh has no scalars to color by.", nameof(mesh));
        return Recolor(mesh, mesh.Scalars, map);
    }

    /// <summary>The same mesh (same position and index arrays) with new scalars and the colors they map to.</summary>
    public static MeshItem Recolor(MeshItem mesh, float[] scalars, IColorMap map)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(scalars);
        ArgumentNullException.ThrowIfNull(map);
        if (scalars.Length != mesh.Positions.Length)
            throw new ArgumentException("There must be one scalar per vertex.", nameof(scalars));
        return new MeshItem
        {
            Id = mesh.Id,
            Positions = mesh.Positions,
            Indices = mesh.Indices,
            Scalars = scalars,
            VertexColors = Colors(scalars, map),
            Color = mesh.Color,
            DoubleSided = mesh.DoubleSided,
        };
    }

    static Rgba[] Colors(float[] values, IColorMap map)
    {
        var colors = new Rgba[values.Length];
        for (int i = 0; i < values.Length; i++)
            colors[i] = map.Map(values[i]);
        return colors;
    }

    static int Inner(int row, int column, int n) => row == 0 ? 0 : row == n ? SphericalVertexCount(n) - 1 : 1 + (row - 1) * 2 * n + column;

    static void Put(int[] indices, ref int t, int a, int b, int c)
    {
        indices[t++] = a;
        indices[t++] = b;
        indices[t++] = c;
    }

    static void CheckSphericalGrid(IReadOnlyList<IReadOnlyList<double>> grid, int n, string name)
    {
        ArgumentNullException.ThrowIfNull(grid, name);
        if (n < 2)
            throw new ArgumentException("N must be at least 2.", name);
        if (grid.Count != n + 1)
            throw new ArgumentException($"The grid must have N + 1 = {n + 1} rows.", name);
        for (int i = 0; i <= n; i++)
        {
            int expected = i == 0 || i == n ? 1 : 2 * n;
            if (grid[i] is null || grid[i].Count != expected)
                throw new ArgumentException($"Row {i} must have {expected} values.", name);
        }
    }
}
