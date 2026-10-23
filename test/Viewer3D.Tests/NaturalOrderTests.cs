namespace Viewer3D.Tests;

public class NaturalOrderTests
{
    static int Compare(string? x, string? y) => NaturalOrder.Comparer.Compare(x, y);

    [Fact]
    public void Numbers_CompareByValue()
    {
        Assert.True(Compare("frame_2", "frame_10") < 0);
        Assert.True(Compare("frame_10", "frame_2") > 0);
        Assert.True(Compare("frame_002", "frame_10") < 0);
        Assert.True(Compare("step9.obj", "step10.obj") < 0);
    }

    [Fact]
    public void NumbersLongerThanALong_StillCompareByValue()
    {
        Assert.True(Compare("x99999999999999999999", "x100000000000000000000") < 0);
        Assert.True(Compare("x00000000000000000000000002", "x10") < 0);
    }

    [Fact]
    public void Letters_IgnoreCase_ThenEqualNamesAreStillOrdered()
    {
        Assert.True(Compare("apple", "Banana") < 0);
        // Equal by value and case, but never 0: the order is total, fewer zeros first, then ordinal.
        Assert.True(Compare("frame_2", "frame_02") < 0);
        Assert.True(Compare("frame_02", "frame_2") > 0);
        Assert.NotEqual(0, Compare("A", "a"));
        Assert.Equal(-Math.Sign(Compare("A", "a")), Math.Sign(Compare("a", "A")));
        Assert.Equal(0, Compare("same_1", "same_1"));
    }

    [Fact]
    public void Prefix_ComesFirst_AndNullFirstOfAll()
    {
        Assert.True(Compare("frame", "frame_1") < 0);
        Assert.True(Compare(null, "") < 0);
        Assert.True(Compare("", null) > 0);
    }

    [Fact]
    public void Sorting_GivesPlayingOrder()
    {
        string[] names = ["frame_10.obj", "frame_1.obj", "Frame_3.obj", "frame_2.obj", "frame_0.obj", "frame_20.obj"];

        string[] sorted = [.. names.Order(NaturalOrder.Comparer)];

        Assert.Equal(["frame_0.obj", "frame_1.obj", "frame_2.obj", "Frame_3.obj", "frame_10.obj", "frame_20.obj"], sorted);
    }
}
