using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// Tools on trunk entities: while trunk entities run (<see cref="TrunkEntitySystem.Enabled"/>),
/// every knife, shears, axe and saw, and Immersive Woodworking's bark spud, gets a
/// <see cref="TrunkToolBehavior"/> (both sides, after the game's and the mods' collectibles are
/// final), and the overrides of the held interaction on those tools' classes are prefixed so the
/// behaviour runs first (<see cref="TrunkToolBehavior.Hooks"/>). A client lists the tools in a
/// trunk's interaction help (<see cref="EntityTrunk.HelpProviders"/>). Off, nothing is added.
/// </summary>
public class TrunkToolsSystem : ModSystem
{
    // One id for both sides: a single-player game runs both in one process, and the prefixes find
    // each side's own behaviour on the instance, so each method is patched once, by the first side
    // to get there, and unpatched when the last side goes.
    public const string HarmonyId = "seraphhorizons.trunktools";

    private static readonly object Lock = new();
    private static int _users;

    private static readonly (string Name, Type[] Args, string Prefix)[] Hooked =
    [
        (nameof(CollectibleObject.OnHeldInteractStart),
            [typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(bool), typeof(EnumHandHandling).MakeByRefType()],
            nameof(TrunkToolBehavior.Hooks.StartPrefix)),
        (nameof(CollectibleObject.OnHeldInteractStep),
            [typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection)],
            nameof(TrunkToolBehavior.Hooks.StepPrefix)),
        (nameof(CollectibleObject.OnHeldInteractStop),
            [typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection)],
            nameof(TrunkToolBehavior.Hooks.StopPrefix)),
        (nameof(CollectibleObject.OnHeldInteractCancel),
            [typeof(float), typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(EnumItemUseCancelReason)],
            nameof(TrunkToolBehavior.Hooks.CancelPrefix)),
    ];

    private ICoreAPI? _api;
    private bool _patching;
    private bool? _barkBound;
    private System.Func<EntityTrunk, IClientPlayer, IEnumerable<WorldInteraction>>? _help;

    public static TrunkToolsSystem Of(ICoreAPI api) => api.ModLoader.GetModSystem<TrunkToolsSystem>();

    // After the trunk entities' own system has decided.
    public override double ExecuteOrder() => 0.16;

    /// <summary>Whether Immersive Woodworking's bark roll is bound (bound on first use, one warning
    /// when it cannot be). Without it the spud still debarks, with no bark.</summary>
    public bool BarkBound
    {
        get
        {
            if (_barkBound is { } bound || _api == null)
                return _barkBound ?? false;
            string? problem = !WoodworkingMods.Applies(_api) ? "Immersive Woodworking is not installed"
                : WoodworkingMods.Bind(_api, out string? missing) is { } mods ? BarkDrops.Bind(mods)
                : missing;
            if (problem != null)
                _api.Logger.Warning("[seraphhorizons] Trunk entities: Immersive Woodworking's bark is not as expected, so the bark spud drops no bark: {0}", problem);
            _barkBound = problem == null;
            return _barkBound.Value;
        }
    }

    public override void Start(ICoreAPI api) => _api = api;

    public override void AssetsFinalize(ICoreAPI api)
    {
        if (!TrunkEntitySystem.Of(api).Enabled)
            return;
        var types = new HashSet<Type>();
        int count = 0;
        foreach (var collectible in api.World.Collectibles)
        {
            if (collectible?.Code == null || TrunkHarvest.ToolOf(collectible) == TrunkTool.None
                || collectible.GetCollectibleBehavior<TrunkToolBehavior>(true) != null)
                continue;
            collectible.CollectibleBehaviors = collectible.CollectibleBehaviors.Append(new TrunkToolBehavior(collectible));
            types.Add(collectible.GetType());
            count++;
        }
        int patched = Patch(api, types);
        api.Logger.Notification("[seraphhorizons] Trunk entities: {0} tools work trunks where they lie ({1} overrides of their held interaction prefixed)",
            count, patched);
    }

    // Each class's own override of a held interaction method, which may not call the behaviours.
    private int Patch(ICoreAPI api, IEnumerable<Type> types)
    {
        var methods = new HashSet<MethodInfo>();
        lock (Lock)
        {
            if (!_patching)
            {
                _patching = true;
                _users++;
            }
            var harmony = new Harmony(HarmonyId);
            foreach (var type in types)
            foreach (var (name, args, prefix) in Hooked)
                if (type.GetMethod(name, BindingFlags.Public | BindingFlags.Instance, args) is { } method
                    && method.DeclaringType != typeof(CollectibleObject) && methods.Add(method)
                    && Harmony.GetPatchInfo(method)?.Prefixes.Any(p => p.owner == HarmonyId) != true)
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(TrunkToolBehavior.Hooks), prefix));
        }
        return methods.Count;
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        if (!TrunkEntitySystem.Of(api).Enabled)
            return;
        _help = (trunk, player) => Help(api, trunk);
        EntityTrunk.HelpProviders.Add(_help);
    }

    /// <summary>The tools' help for a trunk: those that would work it now (Logging Expanded lists
    /// all four on a placed trunk; a trunk entity lists what fits its state).</summary>
    private static IEnumerable<WorldInteraction> Help(ICoreClientAPI api, EntityTrunk trunk)
    {
        var system = TrunkEntitySystem.Of(api);
        if (trunk.Trunk is not { } stack || system.Logging is not { } logging)
            yield break;
        int branches = TrunkHarvest.Branches(stack);
        if (branches > 0)
            yield return Entry(api, TrunkTool.Knife, "loggingmod:wi-treetrunk-knife");
        if (branches >= TrunkHarvest.SaplingBranches)
            yield return Entry(api, TrunkTool.Shears, "loggingmod:wi-treetrunk-shears");
        if (branches <= 0 || !logging.RequireBranchRemoval)
        {
            yield return Entry(api, TrunkTool.Axe, "loggingmod:wi-treetrunk-axe");
            yield return Entry(api, TrunkTool.Saw, "loggingmod:wi-treetrunk-saw");
        }
        if (branches <= 0 && !Trunks.IsDebarked(stack) && Tools(api, TrunkTool.Spud).Length > 0
            && TrunkHarvest.PlanFor(TrunkTool.Spud, trunk, logging, system.Config) is { Refused: false })
            yield return Entry(api, TrunkTool.Spud, "seraphhorizons:trunkentities-help-spud");
    }

    private static WorldInteraction Entry(ICoreClientAPI api, TrunkTool tool, string langCode) =>
        new() { ActionLangCode = langCode, MouseButton = EnumMouseButton.Right, Itemstacks = Tools(api, tool) };

    private static ItemStack[] Tools(ICoreClientAPI api, TrunkTool tool) =>
        ObjectCacheUtil.GetOrCreate(api, "seraphhorizons-trunktools-" + tool, () =>
            api.World.Collectibles.Where(c => c?.Code != null && TrunkHarvest.ToolOf(c) == tool).Select(c => new ItemStack(c)).ToArray());

    public override void Dispose()
    {
        if (_help != null)
            EntityTrunk.HelpProviders.Remove(_help);
        _help = null;
        if (!_patching)
            return;
        _patching = false;
        lock (Lock)
            if (--_users == 0)
                new Harmony(HarmonyId).UnpatchAll(HarmonyId);
    }
}
