using System.Globalization;
using System.Numerics;

namespace Viewer3D;

/// <summary>
/// Reads triangle meshes from Wavefront OBJ files: vertices (<c>v</c>), normals (<c>vn</c>), faces (<c>f</c>, any
/// polygon, split into a fan of triangles), negative (relative) indices, the <c>v/vt/vn</c> corner forms, and materials
/// (<c>mtllib</c>, <c>usemtl</c>) with their diffuse color (<c>Kd</c>) and opacity (<c>d</c> or <c>Tr</c>). Texture
/// coordinates, groups, smoothing groups, lines, points and curves are accepted and ignored. Normals only orient the
/// winding of each triangle: the faces are shaded flat. Concave polygons are split as a fan, as most viewers do.
/// </summary>
/// <remarks>
/// The file is treated as untrusted input. Every read is bounded by <see cref="ObjReadOptions"/>, a bad line is
/// refused with its line number, and a material file is only looked for inside the folder of the model.
/// </remarks>
public static class ObjReader
{
    /// <summary>Reads an OBJ file and the material files it names, from the same folder.</summary>
    /// <exception cref="ObjReadException">The file is malformed or over a limit.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static ObjModel Read(string path, ObjReadOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= ObjReadOptions.Default;
        var info = new FileInfo(path);
        if (info.Length > options.MaxFileBytes)
            throw TooLarge(info.Length, options.MaxFileBytes);
        string directory = Path.GetDirectoryName(info.FullName) ?? ".";
        using var reader = new StreamReader(info.FullName);
        return Read(reader, name => OpenMaterialFile(directory, name, options), options, cancellationToken);
    }

    /// <summary>
    /// Reads an OBJ model from text. <paramref name="materialResolver"/> opens a material file by the name written in
    /// <c>mtllib</c>, or returns null when there is none; without it, materials are not read.
    /// </summary>
    /// <exception cref="ObjReadException">The text is malformed or over a limit.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static ObjModel Read(TextReader reader, Func<string, TextReader?>? materialResolver = null, ObjReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var parser = new Parser(options ?? ObjReadOptions.Default, materialResolver, cancellationToken);
        parser.Parse(reader);
        return parser.Build();
    }

    // Only a file inside the folder of the model: a name that is rooted or climbs out of it is not opened.
    static TextReader? OpenMaterialFile(string directory, string name, ObjReadOptions options)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name))
            return null;
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(directory, name));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!full.StartsWith(root, comparison))
            return null;
        var info = new FileInfo(full);
        if (!info.Exists || info.Length > options.MaxFileBytes)
            return null;
        return new StreamReader(full);
    }

    static ObjReadException TooLarge(long count, long limit) =>
        new(ObjReadError.FileTooLarge,
            string.Create(CultureInfo.InvariantCulture,
                $"The file is larger than {limit / (1024.0 * 1024.0):0.#} MB, the most the viewer reads."),
            count: count, limit: limit);

    sealed class Bucket(string material)
    {
        public string Material { get; } = material;
        public List<int> Indices { get; } = [];
    }

    sealed class Parser(ObjReadOptions options, Func<string, TextReader?>? materialResolver, CancellationToken cancellationToken)
    {
        readonly List<Vector3> positions = [];
        readonly List<Vector3> normals = [];
        readonly Dictionary<string, Rgba> materials = new(StringComparer.Ordinal);
        readonly Dictionary<string, Bucket> buckets = new(StringComparer.Ordinal);
        readonly List<Bucket> bucketOrder = [];
        readonly List<string> warnings = [];
        readonly List<(int Vertex, int Normal)> corners = [];
        Bucket? current;
        int lineNumber;
        long characters;
        long vertexCount;
        long triangleCount;

        bool OverLimit => vertexCount > options.MaxVertices || triangleCount > options.MaxTriangles;

        public void Parse(TextReader reader)
        {
            string? line;
            while ((line = ReadLine(reader)) != null)
            {
                // A trailing backslash joins the next line.
                while (line.EndsWith('\\'))
                {
                    string? next = ReadLine(reader);
                    if (next == null)
                    {
                        line = line[..^1];
                        break;
                    }
                    line = string.Concat(line.AsSpan(0, line.Length - 1), " ", next);
                }
                ParseLine(line);
            }
            if (vertexCount > options.MaxVertices)
                throw new ObjReadException(ObjReadError.TooManyVertices,
                    string.Create(CultureInfo.InvariantCulture,
                        $"This model has {vertexCount:N0} vertices; the viewer reads at most {options.MaxVertices:N0}. Simplify the mesh and try again."),
                    count: vertexCount, limit: options.MaxVertices);
            if (triangleCount > options.MaxTriangles)
                throw new ObjReadException(ObjReadError.TooManyTriangles,
                    string.Create(CultureInfo.InvariantCulture,
                        $"This model has {triangleCount:N0} triangles; the viewer draws at most {options.MaxTriangles:N0}. Simplify the mesh and try again."),
                    count: triangleCount, limit: options.MaxTriangles);
        }

        string? ReadLine(TextReader reader)
        {
            string? line = reader.ReadLine();
            if (line == null)
                return null;
            lineNumber++;
            characters += line.Length + 1;
            if (characters > options.MaxFileBytes)
                throw TooLarge(characters, options.MaxFileBytes);
            if ((lineNumber & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            return line;
        }

        void ParseLine(string line)
        {
            ReadOnlySpan<char> rest = StripComment(line);
            if (!NextToken(ref rest, out ReadOnlySpan<char> keyword))
                return;
            switch (keyword)
            {
                case "v":
                    vertexCount++;
                    if (OverLimit)
                        return;
                    positions.Add(ReadVector(ref rest, "a vertex needs three numbers (v x y z)"));
                    break;
                case "vn":
                    if (OverLimit)
                        return;
                    normals.Add(ReadVector(ref rest, "a normal needs three numbers (vn x y z)"));
                    break;
                case "f":
                    ReadFace(rest);
                    break;
                case "usemtl":
                    current = BucketFor(rest.Trim().ToString());
                    break;
                case "mtllib":
                    while (NextToken(ref rest, out ReadOnlySpan<char> name))
                        LoadMaterials(name.ToString());
                    break;
            }
        }

        Vector3 ReadVector(ref ReadOnlySpan<char> rest, string what)
        {
            if (!ReadFloat(ref rest, out float x) || !ReadFloat(ref rest, out float y) || !ReadFloat(ref rest, out float z))
                throw Malformed(what);
            return new Vector3(x, y, z);
        }

        void ReadFace(ReadOnlySpan<char> rest)
        {
            corners.Clear();
            bool counting = OverLimit;
            while (NextToken(ref rest, out ReadOnlySpan<char> token))
            {
                if (counting)
                {
                    corners.Add((0, -1));
                    continue;
                }
                // v, v/vt, v//vn or v/vt/vn.
                int slash = token.IndexOf('/');
                ReadOnlySpan<char> vertexText = slash < 0 ? token : token[..slash];
                int normal = -1;
                if (slash >= 0)
                {
                    ReadOnlySpan<char> after = token[(slash + 1)..];
                    int second = after.IndexOf('/');
                    if (second >= 0 && second + 1 < after.Length)
                        normal = Resolve(after[(second + 1)..], normals.Count, "normal");
                }
                corners.Add((Resolve(vertexText, positions.Count, "vertex"), normal));
            }
            if (corners.Count < 3)
                throw Malformed("a face needs at least three corners (f a b c)");
            triangleCount += corners.Count - 2;
            if (counting || OverLimit)
                return;
            Bucket bucket = current ??= BucketFor("");
            for (int i = 1; i + 1 < corners.Count; i++)
                AddTriangle(bucket, corners[0], corners[i], corners[i + 1]);
        }

        void AddTriangle(Bucket bucket, (int Vertex, int Normal) a, (int Vertex, int Normal) b, (int Vertex, int Normal) c)
        {
            bool flip = false;
            if (a.Normal >= 0 && b.Normal >= 0 && c.Normal >= 0)
            {
                Vector3 face = Vector3.Cross(positions[b.Vertex] - positions[a.Vertex], positions[c.Vertex] - positions[a.Vertex]);
                Vector3 given = normals[a.Normal] + normals[b.Normal] + normals[c.Normal];
                flip = Vector3.Dot(face, given) < 0;
            }
            bucket.Indices.Add(a.Vertex);
            bucket.Indices.Add(flip ? c.Vertex : b.Vertex);
            bucket.Indices.Add(flip ? b.Vertex : c.Vertex);
        }

        // 1-based, or negative counting back from the last one defined so far.
        int Resolve(ReadOnlySpan<char> text, int count, string what)
        {
            if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int index) || index == 0)
                throw Malformed($"'{text}' is not a {what} index");
            int resolved = index > 0 ? index - 1 : count + index;
            if (resolved < 0 || resolved >= count)
                throw new ObjReadException(ObjReadError.IndexOutOfRange,
                    string.Create(CultureInfo.InvariantCulture,
                        $"Line {lineNumber}: the face refers to {what} {index}, but only {count} are defined before it."),
                    lineNumber);
            return resolved;
        }

        Bucket BucketFor(string material)
        {
            if (!buckets.TryGetValue(material, out Bucket? bucket))
            {
                bucket = new Bucket(material);
                buckets.Add(material, bucket);
                bucketOrder.Add(bucket);
            }
            return bucket;
        }

        void LoadMaterials(string name)
        {
            TextReader? reader;
            try
            {
                reader = materialResolver?.Invoke(name);
            }
            catch (IOException)
            {
                reader = null;
            }
            catch (UnauthorizedAccessException)
            {
                reader = null;
            }
            if (reader == null)
            {
                warnings.Add($"The material file '{name}' was not read; its faces use the default color.");
                return;
            }
            using (reader)
                ReadMaterials(reader, name);
        }

        void ReadMaterials(TextReader reader, string file)
        {
            string? name = null;
            Vector3 diffuse = Vector3.One;
            float alpha = 1;
            bool hasD = false;
            long read = 0;
            int number = 0;
            void Close()
            {
                if (name != null)
                    materials[name] = new Rgba(ToByte(diffuse.X), ToByte(diffuse.Y), ToByte(diffuse.Z), ToByte(alpha));
            }
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                number++;
                read += line.Length + 1;
                if (read > options.MaxFileBytes)
                {
                    warnings.Add($"The material file '{file}' is too large; the rest of it was not read.");
                    break;
                }
                ReadOnlySpan<char> rest = StripComment(line);
                if (!NextToken(ref rest, out ReadOnlySpan<char> keyword))
                    continue;
                switch (keyword)
                {
                    case "newmtl":
                        Close();
                        name = rest.Trim().ToString();
                        diffuse = Vector3.One;
                        alpha = 1;
                        hasD = false;
                        break;
                    case "Kd":
                        if (ReadFloat(ref rest, out float r) && ReadFloat(ref rest, out float g) && ReadFloat(ref rest, out float b))
                            diffuse = new Vector3(r, g, b);
                        else
                            warnings.Add($"The material file '{file}', line {number}: the color was not read.");
                        break;
                    case "d":
                        if (ReadFloat(ref rest, out float d))
                        {
                            alpha = d;
                            hasD = true;
                        }
                        break;
                    case "Tr":
                        if (!hasD && ReadFloat(ref rest, out float tr))
                            alpha = 1 - tr;
                        break;
                }
            }
            Close();
        }

        public ObjModel Build()
        {
            if (triangleCount == 0)
                throw new ObjReadException(ObjReadError.Empty, "The file has no faces to draw.");
            var meshes = new List<MeshItem>();
            var local = new int[positions.Count];
            Array.Fill(local, -1);
            foreach (Bucket bucket in bucketOrder)
            {
                if (bucket.Indices.Count == 0)
                    continue;
                var used = new List<Vector3>();
                var indices = new int[bucket.Indices.Count];
                for (int i = 0; i < indices.Length; i++)
                {
                    int global = bucket.Indices[i];
                    if (local[global] < 0)
                    {
                        local[global] = used.Count;
                        used.Add(positions[global]);
                    }
                    indices[i] = local[global];
                }
                foreach (int global in bucket.Indices)
                    local[global] = -1;
                meshes.Add(new MeshItem
                {
                    Positions = [.. used],
                    Indices = indices,
                    Color = ColorOf(bucket.Material),
                    DoubleSided = true,
                    Lighting = MeshLighting.KeyLight,
                });
            }
            return new ObjModel(meshes, (int)vertexCount, warnings);
        }

        Rgba ColorOf(string material)
        {
            if (material.Length == 0)
                return options.DefaultColor;
            if (materials.TryGetValue(material, out Rgba color))
                return color;
            warnings.Add($"The material '{material}' was not found; its faces use the default color.");
            return options.DefaultColor;
        }

        ObjReadException Malformed(string detail) =>
            new(ObjReadError.Malformed, string.Create(CultureInfo.InvariantCulture, $"Line {lineNumber}: {detail}."), lineNumber);
    }

    static byte ToByte(float value) => (byte)MathF.Round(Math.Clamp(value, 0, 1) * 255);

    static ReadOnlySpan<char> StripComment(string line)
    {
        ReadOnlySpan<char> span = line.AsSpan();
        int hash = span.IndexOf('#');
        return hash >= 0 ? span[..hash] : span;
    }

    static bool NextToken(ref ReadOnlySpan<char> rest, out ReadOnlySpan<char> token)
    {
        rest = rest.TrimStart();
        if (rest.IsEmpty)
        {
            token = default;
            return false;
        }
        int end = rest.IndexOfAny(' ', '\t');
        if (end < 0)
            end = rest.Length;
        token = rest[..end];
        rest = rest[end..];
        return true;
    }

    static bool ReadFloat(ref ReadOnlySpan<char> rest, out float value)
    {
        value = 0;
        return NextToken(ref rest, out ReadOnlySpan<char> token)
            && float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && float.IsFinite(value);
    }
}
