using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>What a mode is given to make one eidolon's order: who orders, and what they marked (the
/// block for <see cref="EidolonMarkKind.Block"/>, the area for <see cref="EidolonMarkKind.Area"/>).</summary>
public sealed record EidolonCommandContext(EntityLaborEidolon Eidolon, IServerPlayer Player, BlockPos? Target, MarkArea? Area);

/// <summary>A mode's answer for one eidolon: the order to give it (code as registered with
/// <see cref="EidolonOrders.Register"/>, and its arguments), or why it refuses (a lang key, with
/// arguments), told to the player.</summary>
public sealed record EidolonCommand(string? OrderCode, ITreeAttribute? Args, string? RefusalKey = null, object[]? RefusalArgs = null)
{
    public static EidolonCommand Order(string code, ITreeAttribute? args = null) => new(code, args);

    public static EidolonCommand Refuse(string langKey, params object[] args) => new(null, null, langKey, args);
}

/// <summary>
/// One mode of the command tool's wheel (README "Eidolon", the command tool). Its name is the lang
/// key <c>seraphhorizons:eidoloncommander-mode-{Code}</c>, its icon an SVG asset (the game's own
/// icons are under <c>game:textures/icons/</c>). <see cref="Mark"/> says what the player marks before
/// the order goes out; <see cref="Command"/> makes the order for each bound eidolon in range.
/// </summary>
public sealed class EidolonCommandMode
{
    public required string Code { get; init; }

    /// <summary>Its place on the wheel, lowest first (follow 10, stay 20; carry, set down, fell,
    /// haul, crew and guard after, in tens).</summary>
    public required double Order { get; init; }

    public required AssetLocation Icon { get; init; }

    public EidolonMarkKind Mark { get; init; } = EidolonMarkKind.None;

    /// <summary>For an area: its longest side, in blocks.</summary>
    public int MaxAreaSide { get; init; } = 48;

    public required System.Func<EidolonCommandContext, EidolonCommand> Command { get; init; }

    public string NameKey => "seraphhorizons:eidoloncommander-mode-" + Code;
}

/// <summary>
/// The command tool's modes, in wheel order. Registered on both sides in a mod system's
/// <c>Start</c> (the wheel is chosen by index, so both sides must hold the same list): follow and stay
/// by <see cref="EidolonCommanderSystem"/>, the jobs by their tasks with <see cref="Register"/>. A
/// mode is always on the wheel; one whose job cannot run (a bridge's mod missing) refuses in
/// <see cref="EidolonCommandMode.Command"/>.
/// </summary>
public static class EidolonCommandModes
{
    private static readonly object Lock = new();
    private static IReadOnlyList<EidolonCommandMode> _all = [];

    public static IReadOnlyList<EidolonCommandMode> All => _all;

    /// <summary>Adds (or replaces, by code) a mode.</summary>
    public static void Register(EidolonCommandMode mode)
    {
        lock (Lock)
            _all = _all.Where(m => m.Code != mode.Code).Append(mode)
                .OrderBy(m => m.Order).ThenBy(m => m.Code, StringComparer.Ordinal).ToList();
    }

    public static EidolonCommandMode? Get(string? code) => _all.FirstOrDefault(m => m.Code == code);

    public static int IndexOf(string? code)
    {
        for (int i = 0; i < _all.Count; i++)
            if (_all[i].Code == code)
                return i;
        return -1;
    }
}
