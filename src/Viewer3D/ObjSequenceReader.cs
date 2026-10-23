using System.Globalization;

namespace Viewer3D;

/// <summary>
/// Reads a series of Wavefront OBJ files as an animation, one file per frame, in <see cref="NaturalOrder"/> of their
/// names (<c>step_2.obj</c> before <c>step_10.obj</c>). Each file is read by <see cref="ObjReader"/> with the same
/// options, so every frame keeps the triangle ceiling of a single model; the frames together keep the weight ceiling of
/// <see cref="SceneSequence"/>, summed file by file so a series past it stops at the file that passes it. Give the
/// meshes the same names and order in every file: the item ids then stay the same from frame to frame.
/// </summary>
public static class ObjSequenceReader
{
    /// <summary>Reads the given files as the frames of an animation, sorted by file name (not by folder).</summary>
    /// <param name="paths">The OBJ files; at least two.</param>
    /// <param name="frameDuration">How long each frame stays on screen. Positive.</param>
    /// <param name="options">Limits of each file; null uses <see cref="ObjReadOptions.Default"/>.</param>
    /// <param name="maxWeight">Ceiling of the weight of all frames (<see cref="SceneSequence.Weight"/>).</param>
    /// <param name="cancellationToken">Checked between files and inside each one.</param>
    /// <exception cref="ObjReadException">
    /// Fewer than two files (<see cref="ObjReadError.TooFewFrames"/>), or a file refused by <see cref="ObjReader"/>, with
    /// its name at the start of the message.
    /// </exception>
    /// <exception cref="SceneSequenceTooLargeException">The frames weigh more than <paramref name="maxWeight"/>.</exception>
    /// <exception cref="OperationCanceledException">The read was canceled.</exception>
    public static SceneSequence Read(IEnumerable<string> paths, TimeSpan frameDuration, ObjReadOptions? options = null,
        long maxWeight = SceneSequence.DefaultMaxWeight, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (frameDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(frameDuration), "A frame lasts a positive time.");
        // By the file name, so a folder never decides the order; the whole path only breaks a tie.
        string[] files = [.. paths.OrderBy(p => Path.GetFileName(p) ?? p, NaturalOrder.Comparer).ThenBy(p => p, NaturalOrder.Comparer)];
        if (files.Length < 2)
            throw new ObjReadException(ObjReadError.TooFewFrames, string.Format(CultureInfo.InvariantCulture,
                "An animation needs at least two OBJ files; {0} found.", files.Length), count: files.Length, limit: 2);
        var frames = new Scene[files.Length];
        long weight = 0;
        for (int k = 0; k < files.Length; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                frames[k] = ObjReader.Read(files[k], options, cancellationToken).ToScene();
            }
            catch (ObjReadException e)
            {
                string name = Path.GetFileName(files[k]);
                throw new ObjReadException(e.Reason, name + ": " + e.Message, e.Line, e.Count, e.Limit, name);
            }
            weight += SceneSequence.WeightOf(frames[k]);
            SceneSequence.CheckWeight(weight, maxWeight);
        }
        return new SceneSequence(frames, frameDuration, maxWeight: maxWeight);
    }

    /// <summary>Reads every <c>*.obj</c> file of <paramref name="folder"/> (not its subfolders) as the frames of an animation.</summary>
    /// <inheritdoc cref="Read(IEnumerable{string}, TimeSpan, ObjReadOptions?, long, CancellationToken)"/>
    /// <param name="folder">The folder of the frames.</param>
    /// <param name="frameDuration">How long each frame stays on screen. Positive.</param>
    /// <param name="options">Limits of each file; null uses <see cref="ObjReadOptions.Default"/>.</param>
    /// <param name="maxWeight">Ceiling of the weight of all frames (<see cref="SceneSequence.Weight"/>).</param>
    /// <param name="cancellationToken">Checked between files and inside each one.</param>
    public static SceneSequence ReadFolder(string folder, TimeSpan frameDuration, ObjReadOptions? options = null,
        long maxWeight = SceneSequence.DefaultMaxWeight, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var match = new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive, RecurseSubdirectories = false };
        return Read(Directory.EnumerateFiles(folder, "*.obj", match), frameDuration, options, maxWeight, cancellationToken);
    }
}
