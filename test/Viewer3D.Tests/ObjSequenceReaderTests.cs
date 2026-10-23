using System.Globalization;

namespace Viewer3D.Tests;

public class ObjSequenceReaderTests
{
    static readonly TimeSpan Tenth = TimeSpan.FromMilliseconds(100);

    // A triangle whose first corner sits at x = shift: the frame read back tells which file it came from.
    static string Triangle(int shift) => string.Format(CultureInfo.InvariantCulture,
        "v {0} 0 0\nv {1} 0 0\nv {0} 1 0\nf 1 2 3\n", shift, shift + 1);

    static string Folder(params (string Name, string Text)[] files)
    {
        string folder = Directory.CreateTempSubdirectory("viewer3d-objseq-").FullName;
        foreach (var (name, text) in files)
            File.WriteAllText(Path.Combine(folder, name), text);
        return folder;
    }

    static float FirstX(Scene frame) => ((MeshItem)Assert.Single(frame.Items)).Positions[0].X;

    [Fact]
    public void ReadFolder_TakesEveryObjInNaturalOrder_AndNothingElse()
    {
        string folder = Folder(("frame_10.obj", Triangle(10)), ("frame_2.obj", Triangle(2)), ("frame_1.obj", Triangle(1)),
            ("FRAME_3.OBJ", Triangle(3)), ("notes.txt", "not a frame"));
        Directory.CreateDirectory(Path.Combine(folder, "older"));
        File.WriteAllText(Path.Combine(folder, "older", "frame_0.obj"), Triangle(0));
        try
        {
            SceneSequence sequence = ObjSequenceReader.ReadFolder(folder, Tenth, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal([1f, 2f, 3f, 10f], sequence.Frames.Select(FirstX));
            Assert.Equal(TimeSpan.FromMilliseconds(400), sequence.Duration);
            // One mesh and three vertices per frame.
            Assert.Equal(16, sequence.Weight);
            // The same mesh in every file keeps its id, so selection and picking follow it from frame to frame.
            Assert.Single(sequence.Frames.Select(f => f.Items[0].Id).Distinct());
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Read_SortsTheGivenPaths()
    {
        string folder = Folder(("b_10.obj", Triangle(10)), ("b_9.obj", Triangle(9)));
        try
        {
            SceneSequence sequence = ObjSequenceReader.Read([Path.Combine(folder, "b_10.obj"), Path.Combine(folder, "b_9.obj")], Tenth,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal([9f, 10f], sequence.Frames.Select(FirstX));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Read_SortsByFileName_NotByTheFolder()
    {
        // Frame 1 sits in a folder whose name sorts after the folder of frame 2.
        string folder = Folder();
        string late = Directory.CreateDirectory(Path.Combine(folder, "z")).FullName;
        string early = Directory.CreateDirectory(Path.Combine(folder, "a")).FullName;
        File.WriteAllText(Path.Combine(late, "frame_1.obj"), Triangle(1));
        File.WriteAllText(Path.Combine(early, "frame_2.obj"), Triangle(2));
        try
        {
            SceneSequence sequence = ObjSequenceReader.Read([Path.Combine(early, "frame_2.obj"), Path.Combine(late, "frame_1.obj")],
                Tenth, cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal([1f, 2f], sequence.Frames.Select(FirstX));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ABrokenFile_IsRefusedWithItsName()
    {
        string folder = Folder(("step_1.obj", Triangle(1)), ("step_2.obj", "v 0 0 0\nv 1 0\nf 1 2 3\n"), ("step_3.obj", Triangle(3)));
        try
        {
            var refused = Assert.Throws<ObjReadException>(() => ObjSequenceReader.ReadFolder(folder, Tenth,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ObjReadError.Malformed, refused.Reason);
            Assert.StartsWith("step_2.obj: ", refused.Message);
            Assert.Equal(2, refused.Line);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FewerThanTwoFiles_IsRefused()
    {
        string folder = Folder(("only.obj", Triangle(0)));
        try
        {
            var refused = Assert.Throws<ObjReadException>(() => ObjSequenceReader.ReadFolder(folder, Tenth,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ObjReadError.TooFewFrames, refused.Reason);
            Assert.Equal(1, refused.Count);
            Assert.Equal(2, refused.Limit);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void PastTheWeightCeiling_StopsAtTheFileThatPassesIt()
    {
        // Each frame weighs 4. With a ceiling of 5 the second file passes it, and the third, broken, is never read:
        // a read of every file first would refuse the broken one instead.
        string folder = Folder(("w_1.obj", Triangle(1)), ("w_2.obj", Triangle(2)), ("w_3.obj", "f 1 2 3\n"));
        try
        {
            var refused = Assert.Throws<SceneSequenceTooLargeException>(() => ObjSequenceReader.ReadFolder(folder, Tenth, maxWeight: 5,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(8, refused.Weight);
            Assert.Equal(5, refused.Limit);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Canceled_Throws()
    {
        string folder = Folder(("c_1.obj", Triangle(1)), ("c_2.obj", Triangle(2)));
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(() => ObjSequenceReader.ReadFolder(folder, Tenth,
                cancellationToken: new CancellationToken(canceled: true)));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
