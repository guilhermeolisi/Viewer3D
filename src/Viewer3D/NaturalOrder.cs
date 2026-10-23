namespace Viewer3D;

/// <summary>
/// Orders names the way people number files: a run of digits compares by its value, so <c>frame_2</c> comes before
/// <c>frame_10</c> and <c>frame_002</c> before <c>frame_10</c>; letters compare without case. Names equal under those
/// rules (<c>frame_02</c> and <c>frame_2</c>, <c>A</c> and <c>a</c>) are then ordered by fewer leading zeros, and
/// last ordinally, so the order is total and the same on every machine.
/// </summary>
public static class NaturalOrder
{
    /// <summary>The comparer. Null comes first.</summary>
    public static IComparer<string> Comparer { get; } = new NaturalComparer();

    sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x is null)
                return -1;
            if (y is null)
                return 1;
            int zeros = 0;
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    int endX = RunEnd(x, i), endY = RunEnd(y, j);
                    int startX = SkipZeros(x, i, endX), startY = SkipZeros(y, j, endY);
                    // A longer run of significant digits is a larger number, however many digits it has.
                    int order = (endX - startX).CompareTo(endY - startY);
                    for (int k = 0; order == 0 && k < endX - startX; k++)
                        order = x[startX + k].CompareTo(y[startY + k]);
                    if (order != 0)
                        return order;
                    if (zeros == 0)
                        zeros = (startX - i).CompareTo(startY - j);
                    i = endX;
                    j = endY;
                    continue;
                }
                int letters = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
                if (letters != 0)
                    return letters;
                i++;
                j++;
            }
            int rest = (x.Length - i).CompareTo(y.Length - j);
            if (rest != 0)
                return rest;
            return zeros != 0 ? zeros : string.CompareOrdinal(x, y);
        }

        static int RunEnd(string s, int start)
        {
            while (start < s.Length && char.IsAsciiDigit(s[start]))
                start++;
            return start;
        }

        // The first significant digit of the run, or its end when the run is all zeros.
        static int SkipZeros(string s, int start, int end)
        {
            while (start < end && s[start] == '0')
                start++;
            return start;
        }
    }
}
