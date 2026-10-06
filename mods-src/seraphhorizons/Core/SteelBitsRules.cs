namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Steel bits recovery (<c>SteelBitsRecovery</c>, README "Steel bits back into steel"), the parts
/// that need no game: how many bits make one place in the stone coffin, and Steelmaking Expanded's
/// scrap list with the steel bit in it.
/// </summary>
public static class SteelBitsRules
{
    /// <summary>The steel bit's code, as Steelmaking Expanded lists it.</summary>
    public const string SteelBit = "game:metalbit-steel";

    /// <summary>Bits that take one ingot's place in the coffin and come out one blister steel
    /// ingot: the bit's own <c>smeltedRatio</c> (20 to a steel ingot).</summary>
    public const int BitsPerCharge = 20;

    /// <summary>Ingot places in a stone coffin: it fires only when all are filled.</summary>
    public const int CoffinPlaces = 16;

    /// <summary>Whether Steelmaking Expanded's comma-separated <c>BessemerScrapCodes</c> lists the
    /// steel bit. Entries are matched as it matches them, as asset locations: trimmed, no domain
    /// meaning <c>game</c>, case-insensitive.</summary>
    public static bool ListsSteelBit(string? codes) =>
        (codes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Any(code => Normalise(code) == SteelBit);

    /// <summary>The list with the steel bit added at the end, or <c>null</c> when it already lists
    /// it.</summary>
    public static string? WithSteelBit(string? codes)
    {
        if (ListsSteelBit(codes))
            return null;
        var trimmed = (codes ?? "").Trim().TrimEnd(',').Trim();
        return trimmed.Length == 0 ? SteelBit : trimmed + "," + SteelBit;
    }

    private static string Normalise(string code)
    {
        code = code.ToLowerInvariant();
        return code.Contains(':') ? code : "game:" + code;
    }
}
