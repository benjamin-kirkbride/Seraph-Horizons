using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// One part of the unified woodworking tweak (<see cref="UnifiedWoodworking"/>), e.g. the
/// splitting block or the sawhorses. A part is registered with one line in
/// <see cref="UnifiedWoodworking.CreateParts"/>; each side's <see cref="SeraphHorizonsSystem"/>
/// makes its own instance of every part, and the coordinator calls the hooks below in the game's
/// phase order. Every hook does nothing unless overridden.
///
/// <see cref="Bind"/> runs for every part before any part changes anything; if one part fails to
/// bind, no part's later hooks run (only <see cref="Off"/>), so Immersive Woodworking and Logging
/// Expanded both stay as shipped. A part must therefore find, in <see cref="Bind"/>, every member
/// it will patch or call and check every asset it will edit, and change nothing there. Every patch
/// goes in from <see cref="Start"/>, where an exception undoes the tweak: the coordinator unpatches,
/// calls <see cref="Undo"/> on each part already started (the throwing one included), then
/// <see cref="Off"/> on all.
///
/// Harmony: the <paramref name="harmony"/> handed to <see cref="Start"/> is the server's under
/// <see cref="UnifiedWoodworking.ServerHarmonyId"/> on the server and the client's under
/// <see cref="UnifiedWoodworking.ClientHarmonyId"/> on the client; the coordinator unpatches each
/// in <see cref="Dispose"/>. In singleplayer both sides run in one process, so a server patch also
/// runs for the client's calls of that method: check the side in the patch where it matters.
/// Patch methods are static, so what they need from <see cref="Bind"/> goes in static fields
/// (the same on both sides).
/// </summary>
public abstract class WoodworkingPart
{
    /// <summary>The part's name in log lines, e.g. "the splitting block".</summary>
    public abstract string Name { get; }

    /// <summary>Start, both sides, whatever the switch: the part's block, block entity and
    /// behavior classes. The server decides whether its blocktypes carry them, and a client must
    /// know the classes then.</summary>
    public virtual void RegisterClasses(ICoreAPI api) { }

    /// <summary>Start, both sides, when the tweak is wanted (the server's switch on; on a client,
    /// the server running it) and both mods are installed: finds what the part needs and changes
    /// nothing. Returns null when all of it is there, else what is missing
    /// or not as expected (one short phrase, for the coordinator's single warning). Mod assets are
    /// indexed by now on the server only, so check assets there
    /// (<c>api.Side == EnumAppSide.Server</c>), before the game's patch loader has run.</summary>
    public virtual string? Bind(ICoreAPI api, WoodworkingMods mods) => null;

    /// <summary>Start, both sides, once every part has bound: settings of the two mods (after
    /// their StartPre, before their AssetsLoaded; Immersive Woodworking's server settings reach
    /// clients from here), every patch of the part (check <c>api.Side</c> for one side's), and
    /// patch assets to empty before the game's patch loader (AssetsLoaded, 0.05).</summary>
    public virtual void Start(ICoreAPI api, Harmony harmony) { }

    /// <summary>Start, after an exception while the tweak was applied, for a part whose
    /// <see cref="Start"/> was called: puts back what it changed other than its patches (the
    /// coordinator has unpatched), such as a setting or an asset. May run after a partial
    /// <see cref="Start"/>. <see cref="Off"/> follows.</summary>
    public virtual void Undo(ICoreAPI api) { }

    /// <summary>Start, both sides, in place of the rest when the tweak does not run (switched off,
    /// or on a client the server does not run it, a mod missing, or a part failed to bind or
    /// apply): undoes what this mod ships whatever the switch,
    /// such as its own patch files or handbook pages. Client mod assets are not indexed yet.</summary>
    public virtual void Off(ICoreAPI api) { }

    /// <summary>AssetsLoaded, both sides: lang edits (<see cref="LangText"/>).</summary>
    public virtual void AssetsLoaded(ICoreAPI api) { }

    /// <summary>AssetsLoaded, server only, after the patch loader (0.05) and before blocktypes and
    /// itemtypes are read (0.2): blocktype and itemtype JSON edits
    /// (<see cref="WoodworkingAssets.Edit"/>). Clients get the types from the server. Another
    /// mod's JSON patch may have changed an asset since <see cref="Bind"/> checked it, so each edit
    /// stands alone: one that fails leaves its asset as the patch loader made it, with a warning
    /// saying what that means in the game. This and the hooks below run on when one throws (the
    /// coordinator logs it).</summary>
    public virtual void EditAssets(ICoreServerAPI api) { }

    /// <summary>AssetsFinalize, both sides: blocks and items are loaded.</summary>
    public virtual void AssetsFinalize(ICoreAPI api) { }

    /// <summary>After the coordinator has unpatched: clear static state.</summary>
    public virtual void Dispose() { }
}
