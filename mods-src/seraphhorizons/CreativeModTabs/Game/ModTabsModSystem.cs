using HarmonyLib;
using SeraphHorizons.Mod.CreativeModTabs.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.Common;

namespace SeraphHorizons.Mod.CreativeModTabs;

/// <summary>The server's plan: the tab list it announces, and what its tabs are made of.</summary>
public sealed class ModTabsAuthority
{
    public required CreativeStacks Stacks { get; init; }
    public required List<ModTabSpec> Tabs { get; init; }
    public required List<int>[] Members { get; init; }
    public required int DefaultTabCount { get; init; }
    /// <summary>Domain to owner, for every domain with a creative stack.</summary>
    public required Dictionary<string, TabOwner> Owners { get; init; }

    public ModTabsPacket ToPacket() => ModTabsPacket.From(DefaultTabCount, Tabs);
}

/// <summary>
/// Creative mod tabs (switch <see cref="SeraphHorizonsConfig.CreativeModTabs"/>; docs/variant-grouping/creative-mod-tabs.md): a
/// button over the creative inventory's right-hand tabs flips between the game's tabs and one tab per mod,
/// each holding every creative-listed stack of that mod.
///
/// The mod tabs are real creative tabs, appended after the default ones on both sides, because the server
/// resolves a creative click by tab index and slot id against its own inventory. The server decides which
/// domain goes to which tab (<see cref="Authority"/>) and sends the list to each client
/// (<see cref="ModTabsPacket"/>); both build the tabs' slots from it the same way (<see cref="CreativeStacks"/>).
/// The server appends them in a postfix on <c>InventoryPlayerCreative.UpdateFromWorld</c>; the client keeps them
/// apart from the default tabs and swaps them in only while the mod tabs are shown (<see cref="ModTabsClient"/>).
///
/// Patches are applied by hand, per side with their own Harmony id (in singleplayer both sides share the
/// process): the server's on <c>UpdateFromWorld</c>, the client's on the GUI (<see cref="ModTabsPatches"/>).
/// With the switch off nothing is patched, appended, sent or shown on that side. The network channel is
/// registered either way, so a server and a client with different settings still talk.
/// </summary>
public sealed class ModTabsModSystem : ModSystem
{
    public const string Channel = "seraphhorizons-creativemodtabs";
    public const string ServerHarmonyId = "seraphhorizons.modtabs";

    private static ModTabsAuthority? _authority;
    private Harmony? _harmony;
    private ICoreServerAPI? _sapi;
    private bool _client;

    /// <summary>The server's plan in this process, or null (not built yet, failed, switched off, or a client).</summary>
    public static ModTabsAuthority? Authority => _authority;

    /// <summary>Why the server has no mod tabs, or null.</summary>
    public static string? AuthorityError { get; private set; }

    /// <summary>Late, so other mods' changes to creative tabs and stacks are in.</summary>
    public override double ExecuteOrder() => 10;

    internal static bool Enabled(ICoreAPI api) => SeraphHorizonsSystem.ConfigFor(api).CreativeModTabs;

    public override void Start(ICoreAPI api)
    {
        api.Network.RegisterChannel(Channel).RegisterMessageType<ModTabsPacket>();
        if (!Enabled(api))
            api.Logger.Notification("[seraphhorizons] Creative mod tabs are off (CreativeModTabs in ModConfig/{0})", SeraphHorizonsSystem.ConfigFile);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        if (!Enabled(api)) return;
        _sapi = api;
        _harmony = new Harmony(ServerHarmonyId);
        var target = AccessTools.DeclaredMethod(typeof(InventoryPlayerCreative), "UpdateFromWorld", [typeof(IWorldAccessor)]);
        if (target is null)
        {
            api.Logger.Warning("[seraphhorizons] Creative mod tabs: InventoryPlayerCreative.UpdateFromWorld not found; no mod tabs");
            return;
        }
        try { _harmony.Patch(target, postfix: new HarmonyMethod(typeof(ModTabsModSystem), nameof(UpdateFromWorldPostfix))); }
        catch (Exception ex)
        {
            api.Logger.Error("[seraphhorizons] Creative mod tabs: could not patch UpdateFromWorld; no mod tabs: {0}", ex);
            return;
        }
        // Collectibles' creative tabs and stacks are final by WorldReady (see TidyVariantsModSystem).
        api.Event.ServerRunPhase(EnumServerRunPhase.WorldReady, () => EnsureAuthority(api));
        api.Event.PlayerNowPlaying += player =>
        {
            if (EnsureAuthority(api) is { } a)
                api.Network.GetChannel(Channel).SendPacket(a.ToPacket(), player);
        };
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!Enabled(api))
        {
            // Registered handler, so a server with the tabs on doesn't make the client log unknown packets.
            api.Network.GetChannel(Channel).SetMessageHandler<ModTabsPacket>(_ => { });
            return;
        }
        _client = true;
        ModTabsClient.Init(api);
        api.Network.GetChannel(Channel).SetMessageHandler<ModTabsPacket>(ModTabsClient.OnPacket);
    }

    /// <summary>Builds the server's plan once; null after a failure (logged once).</summary>
    internal static ModTabsAuthority? EnsureAuthority(ICoreServerAPI api)
    {
        if (_authority is not null || AuthorityError is not null) return _authority;
        try
        {
            var stacks = CreativeStacks.Scan(api.World);
            string gameName = Lang.Get("seraphhorizons:creativemodtabs-tab-game");
            var owners = DomainOwners.ResolveAll(stacks.Refs.Select(r => r.Domain), ModAssetsOf(api), gameName);
            var tabs = ModTabPlanner.Plan(stacks.Refs, owners);
            var members = stacks.Members(tabs, ModTabPlanner.Assign(stacks.Refs, tabs));
            _authority = new ModTabsAuthority
            {
                Stacks = stacks, Tabs = tabs, Members = members, DefaultTabCount = stacks.DefaultTabCodes.Count, Owners = owners,
            };
            api.Logger.Notification("[seraphhorizons] Creative mod tabs: {0} tabs for {1} creative stacks from {2} domains, after {3} default tabs",
                tabs.Count, stacks.Refs.Count, owners.Count, stacks.DefaultTabCodes.Count);
            foreach (var t in tabs)
                api.Logger.Debug("[seraphhorizons] Creative mod tabs: {0} \"{1}\": {2} stacks from {3}", t.Code, t.Name, t.Count, string.Join(", ", t.Domains));
        }
        catch (Exception ex)
        {
            AuthorityError = ex.Message;
            api.Logger.Error("[seraphhorizons] Creative mod tabs: could not plan the tabs; the creative inventory stays as it is: {0}", ex);
        }
        return _authority;
    }

    /// <summary>
    /// Each loaded mod's assets by domain. A mod's assets come from its own folder (a zip is unpacked to one),
    /// which the game adds as an asset origin at <c>&lt;folder&gt;/assets</c>; every loaded asset knows its origin.
    /// </summary>
    internal static List<ModAssets> ModAssetsOf(ICoreAPI api)
    {
        var byOrigin = new Dictionary<string, (Vintagestory.API.Common.Mod Mod, Dictionary<string, DomainAssets> Domains)>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in api.ModLoader.Mods)
        {
            if (mod is not ModContainer { FolderPath: { } folder }) continue;
            byOrigin.TryAdd(NormalizePath(Path.Combine(folder, "assets")), (mod, new Dictionary<string, DomainAssets>(StringComparer.Ordinal)));
        }
        foreach (var (loc, asset) in api.Assets.AllAssets)
        {
            if (asset?.Origin?.OriginPath is not { } origin || !byOrigin.TryGetValue(NormalizePath(origin), out var entry)) continue;
            bool defining = loc.Path.StartsWith("blocktypes/", StringComparison.Ordinal) || loc.Path.StartsWith("itemtypes/", StringComparison.Ordinal);
            entry.Domains.TryGetValue(loc.Domain, out var a);
            entry.Domains[loc.Domain] = new DomainAssets(a.Defining + (defining ? 1 : 0), a.Total + 1);
        }
        return byOrigin.Values
            .Select(e => new ModAssets(e.Mod.Info.ModID, e.Mod.Info.Name, e.Domains))
            .ToList();
    }

    static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // internal void InventoryPlayerCreative.UpdateFromWorld(IWorldAccessor world). Builds the default tabs once
    // per inventory (it returns early once they exist); the mod tabs go after them, server side only. In
    // singleplayer the client's inventory runs this too: it is left alone (the client keeps its mod tabs apart).
    public static void UpdateFromWorldPostfix(InventoryPlayerCreative __instance)
    {
        try
        {
            if (__instance.Api?.Side != EnumAppSide.Server) return;
            // Players' inventories are built after WorldReady, but plan here too rather than leave one without tabs.
            if ((_authority ?? (__instance.Api is ICoreServerAPI sapi ? EnsureAuthority(sapi) : null)) is not { } a) return;
            var tabs = __instance.tabs;
            if (a.Tabs.Count == 0 || tabs.TabsByCode.ContainsKey(a.Tabs[0].Code)) return;
            // A click on a tab index this inventory lacks would make SetTab set no tab and the server's packet handler
            // throw. That can't come from a client showing mod tabs: every inventory's default tabs are built from the
            // same collectibles as the client's, and the client compares its own count with the announced
            // DefaultTabCount and shows nothing if they differ. So a mismatch here means the plan is wrong for every
            // inventory and for every client alike; it is logged and the tabs are left out.
            if (tabs.TabsByCode.Count != a.DefaultTabCount)
            {
                __instance.Api.Logger.Warning("[seraphhorizons] Creative mod tabs: {0} default tabs, planned for {1}; no mod tabs for {2}",
                    tabs.TabsByCode.Count, a.DefaultTabCount, __instance.InventoryID);
                return;
            }
            // CreativeTabs.Add numbers the tabs in order, after the default ones: the indices the client uses.
            foreach (var tab in a.Stacks.BuildTabs(__instance, __instance.Api, a.Tabs, a.Members, a.DefaultTabCount))
                tabs.Add(tab);
        }
        catch (Exception ex)
        {
            __instance.Api?.Logger.Error("[seraphhorizons] Creative mod tabs: could not add the mod tabs to {0}: {1}", __instance.InventoryID, ex);
        }
    }

    public override void Dispose()
    {
        if (_harmony is not null)
        {
            try { _harmony.UnpatchAll(ServerHarmonyId); } catch { /* shutting down */ }
            _harmony = null;
        }
        if (_sapi is not null)
        {
            _authority = null;
            AuthorityError = null;
            _sapi = null;
        }
        if (_client)
        {
            ModTabsClient.Reset();
            _client = false;
        }
    }
}
