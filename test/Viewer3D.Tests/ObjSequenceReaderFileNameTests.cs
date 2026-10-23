namespace Viewer3D.Tests;

public class ObjSequenceReaderFileNameTests
{
    static readonly TimeSpan Tenth = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void ABrokenFrame_CarriesItsFileName_ApartFromTheMessage()
    {
        string folder = Directory.CreateTempSubdirectory("viewer3d-objseq-name-").FullName;
        File.WriteAllText(Path.Combine(folder, "a_1.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
        File.WriteAllText(Path.Combine(folder, "a_2.obj"), "v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 99\n");
        try
        {
            var e = Assert.Throws<ObjReadException>(() => ObjSequenceReader.ReadFolder(folder, Tenth,
                cancellationToken: TestContext.Current.CancellationToken));

            Assert.Equal(ObjReadError.IndexOutOfRange, e.Reason);
            Assert.Equal("a_2.obj", e.FileName);
            Assert.Equal(4, e.Line);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void ASingleFileRefusedByObjReader_HasNoFileName()
    {
        var e = Assert.Throws<ObjReadException>(() => ObjReader.Read(new StringReader("v 0 0 0\n"),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ObjReadError.Empty, e.Reason);
        Assert.Null(e.FileName);
    }
}
