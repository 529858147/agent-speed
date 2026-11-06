namespace AgentSpeed.Core.Unpriced;

/// <summary>
/// Orders strings by their UTF-8 bytes, which is the order of their code points (METRICS 9.10); a lone surrogate counts
/// as its own code point, as Python's <c>surrogatepass</c> encodes it.
/// </summary>
public sealed class Utf8Order : IComparer<string>
{
    public static Utf8Order Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null || y is null)
        {
            return x is null ? -1 : 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var a = Next(x, ref i);
            var b = Next(y, ref j);
            if (a != b)
            {
                return a < b ? -1 : 1;
            }
        }

        return (i < x.Length).CompareTo(j < y.Length);
    }

    private static int Next(string text, ref int index)
    {
        var c = text[index++];
        if (char.IsHighSurrogate(c) && index < text.Length && char.IsLowSurrogate(text[index]))
        {
            return char.ConvertToUtf32(c, text[index++]);
        }

        return c;
    }
}
