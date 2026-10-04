namespace SeraphHorizons.Mod.Core;

/// <summary>One of Logging Expanded's frame stages: what its own interaction takes to turn the
/// frame into the next block, and what it makes.</summary>
/// <param name="FrameClass">The frame's block class, in Logging Expanded's namespace.</param>
/// <param name="FrameCode">The frame blocktype's code (the first part of its block codes).</param>
/// <param name="MainCode">What the main hand holds for it (<c>domain:path</c>).</param>
/// <param name="Count">How many of it.</param>
/// <param name="HammerInOffhand">Whether it also takes a hammer in the offhand.</param>
/// <param name="HoldSeconds">How long it is held; 0 is a click. A hold completes in Logging
/// Expanded's <c>OnBlockInteractStop</c>, a click in its <c>OnBlockInteractStart</c>.</param>
/// <param name="Makes">The blocktype code of what it makes.</param>
public sealed record FrameStage(string FrameClass, string FrameCode, string MainCode, int Count, bool HammerInOffhand,
    float HoldSeconds, string Makes)
{
    public bool IsHold => HoldSeconds > 0;
}

/// <summary>
/// The creative shortcut on the woodworking stations (<c>UnifiedWoodworking</c>), as the game's
/// right-click construction has it for the water wheel (<c>RightClickConstruction</c>): a player in
/// creative mode who right-clicks with Ctrl (the game's <c>ctrl</c> key, sprint by default) held
/// gets the next stage at once, with nothing in hand and nothing taken. It applies to the
/// splitting block's tiers (<see cref="SplittingBlockRules.Creative"/>) and to Logging Expanded's
/// frames (<see cref="Frames"/>). Game-independent, so tests/ runs it without the game.
/// </summary>
public static class CreativeUpgrades
{
    /// <summary>The help line shown, in creative only, on a station that has a next stage.</summary>
    public const string HelpKey = "seraphhorizons:woodworking-help-creative-upgrade";

    /// <summary>The help line's key: the game's <c>ctrl</c>, as its own help lines name it.</summary>
    public const string HotKey = "ctrl";

    /// <summary>The hammer put in the offhand for a frame stage that takes one.</summary>
    public const string HammerCode = "game:hammer-iron";

    public const string FrameDomain = "loggingmod";

    /// <summary>Whether a right click is the creative shortcut: in creative mode, with Ctrl held, as
    /// the water wheel's. Not with Shift too, which Immersive Woodworking's chopping block reads
    /// first (it sticks an axe in), so it means the same on every station.</summary>
    public static bool Applies(bool creative, bool ctrl, bool shift) => creative && ctrl && !shift;

    /// <summary>
    /// Logging Expanded's frames and the stage each makes: every one Logging Expanded turns into
    /// the next block with items, from its own classes (0.3.6). A frame with two ways on takes the
    /// first its own help lists, the one that finishes the station it is a frame of: the large
    /// stick frame becomes the primitive sawhorse (a fired bowl would make a heating rack frame),
    /// the board frame a standard sawhorse frame (4 iron rods would make a storage rack frame),
    /// and the standard sawhorse frame a copper standard sawhorse (2 iron plates would make the
    /// advanced sawhorse frame). The other ways stay as they are, with their items.
    /// </summary>
    public static readonly IReadOnlyList<FrameStage> Frames =
    [
        new("BlockSawhorseFrame", "sawhorseframe", "game:rope", 4, false, 3f, "sawhorse"),
        new("BlockPlankFrame", "plankframe", "game:debarkedlog-oak-ud", 2, false, 0, "standardsawhorseframe"),
        new("BlockStandardSawhorseFrame", "standardsawhorseframe", "game:metalnailsandstrips-copper", 20, true, 3f,
            "sawhorsestandard"),
        new("BlockAdvancedSawhorseFrameA", "advancedsawhorseframea", "game:rod-iron", 4, false, 0, "advancedsawhorseframeb"),
        new("BlockAdvancedSawhorseFrameB", "advancedsawhorseframeb", "game:metalnailsandstrips-iron", 20, true, 3f,
            "sawhorseadvanced"),
        new("BlockStorageRackFrame", "storagerackframe", "game:metalnailsandstrips-iron", 10, true, 3f, "trunkstorage"),
        new("BlockHeatingRackFrame", "heatingrackframe", "game:rope", 6, false, 3f, "resinrack"),
        new("BlockStickStorageFrame", "stickstorageframe", "game:cattailtops", 4, false, 3f, "stickstorage"),
    ];

    /// <summary>The frame stage of a block code, or null for a block that is not one of the
    /// frames.</summary>
    public static FrameStage? Frame(string? domain, string? path)
    {
        if (domain != FrameDomain || string.IsNullOrEmpty(path))
            return null;
        int dash = path.IndexOf('-');
        string first = dash < 0 ? path : path[..dash];
        return Frames.FirstOrDefault(frame => frame.FrameCode == first);
    }
}
