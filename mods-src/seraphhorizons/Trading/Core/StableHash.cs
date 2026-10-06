namespace SeraphHorizons.Mod.Trading.Core;

/// <summary>
/// A stable 64-bit hash of (seed, salt, x, z, n): the same on every machine, runtime and run
/// (unlike <see cref="string.GetHashCode()"/>), so a world's camps, spots and types follow from its
/// seed alone. SplitMix64's finaliser over each part in turn; the salt is FNV-1a over its UTF-16
/// code units. The ore feature (#435) has its own copy of the same idea; the two may be unified.
/// </summary>
public static class StableHash
{
    public static ulong Of(long seed, string salt, int x, int z, int n = 0)
    {
        ulong h = Mix((ulong)seed ^ 0x9E3779B97F4A7C15UL);
        h = Mix(h ^ Fnv(salt));
        h = Mix(h ^ (uint)x);
        h = Mix(h ^ ((ulong)(uint)z << 1));
        h = Mix(h ^ ((ulong)(uint)n << 2));
        return h;
    }

    /// <summary>A number in [0, 1) from <see cref="Of"/>'s top 53 bits.</summary>
    public static double Unit(long seed, string salt, int x, int z, int n = 0) =>
        (Of(seed, salt, x, z, n) >> 11) * (1.0 / (1UL << 53));

    private static ulong Mix(ulong z)
    {
        z += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static ulong Fnv(string s)
    {
        ulong h = 0xCBF29CE484222325UL;
        foreach (char c in s)
        {
            h ^= c;
            h *= 0x100000001B3UL;
        }
        return h;
    }
}
