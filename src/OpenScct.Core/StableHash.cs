namespace OpenScct.Core;

/// <summary>Process-independent 32-bit hashes for ids that are saved or sent over the network.</summary>
public static class StableHash
{
    /// <summary>FNV-1a over the UTF-16 code units of <paramref name="text"/> with ASCII letters lowered. Never 0.</summary>
    public static int OfNameCaseless(string text)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char ch in text)
            {
                char c = ch is >= 'A' and <= 'Z' ? (char)(ch + 32) : ch;
                h ^= (byte)c;
                h *= 16777619u;
                h ^= (byte)(c >> 8);
                h *= 16777619u;
            }

            return h == 0 ? 1 : (int)h;
        }
    }
}
