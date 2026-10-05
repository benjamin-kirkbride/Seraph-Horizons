using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// The debarked tree trunk, the rosser's output: Logging Expanded's own trunk block with a third
/// state of its <c>branches</c> variant, <c>loggingmod:treetrunk-{wood}-{size}-debarked-{side}</c>,
/// added by a JSON patch (<c>patches/rosser-debarkedtrunk.json</c>: the state, the clean trunk's
/// shape, <c>block/wood/debarked/&lt;wood&gt;</c> on the bark faces and
/// <c>block/wood/treetrunk/debarked/&lt;wood&gt;</c> on the ends) and named in the mod's lang
/// file. Part of the <c>Rosser</c> switch.
///
/// Logging Expanded (0.3.6) carries the state as it is wherever it carries the block: placing and
/// picking up keep the <c>branches</c> variant as read, the Trunk Storage Rack and the heating rack
/// keep whole stacks, Carry On and Cartwright's Caravan patch the whole blocktype. A debarked trunk
/// has no branch count, so the knife does nothing to it. Three things it does not know, which this
/// patches on the server, by name:
/// - a sawhorse keeps only the trunk's log stack (<c>BEWorkstation.Inventory</c>) and rebuilds a
///   clean trunk when unloaded. The debarked trunk's log stack carries <see cref="MarkerKey"/>
///   (<see cref="Mark"/>, set by <c>Trunks.Debark</c>), which survives the sawhorse, and a postfix
///   on <c>BEWorkstation.BuildUnloadStack</c> gives back the debarked trunk.
/// - its axe gives the wood's upright placed log, with bark (<c>TreeManager.GetPlacedLogCode</c>).
///   While <c>BlockTreeTrunk.OnBlockInteractStop</c> works a debarked trunk, or a sawhorse's
///   <c>ProcessWithTool</c> works a marked load, that call answers the wood's debarked log
///   (<c>TreeManager.GetDebarkedLogCode</c>) instead, at Logging Expanded's own yields. Its saw
///   is unchanged, and so is the axe with a hammer in the offhand (already debarked logs).
/// - the bucking mill reads the variant itself (<c>Trunks.IsDebarked</c>).
///
/// With the switch off, without Logging Expanded, or with its trunk asset or any member above not
/// as expected (one warning), <see cref="DisablePatches"/> empties the patch file in <c>Start</c>,
/// before the game's patch loader runs, and nothing is patched: there is no debarked trunk.
/// </summary>
public static class DebarkedTrunks
{
    public const string ModId = "loggingmod";

    /// <summary>On a debarked trunk's stored log stack: the logs are debarked. Read only where the
    /// trunk block is gone, on a sawhorse.</summary>
    public const string MarkerKey = "seraphhorizons:debarked";

    public static readonly AssetLocation PatchAsset = new("seraphhorizons", "patches/rosser-debarkedtrunk.json");
    public static readonly AssetLocation TrunkAsset = new(ModId, "blocktypes/treetrunk.json");

    private static MethodInfo? _placedLogCode, _debarkedLogCode, _trunkStop, _process, _processAdvanced, _buildUnload;
    private static PropertyInfo? _workstationInventory;

    // Set while Logging Expanded works a debarked trunk or load, on the thread doing it.
    [ThreadStatic] private static bool _debarking;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Checks Logging Expanded's trunk asset against the patch and finds every member the
    /// patches need. False, with one warning, when anything is not as expected. Runs in Start:
    /// the assets and the mod's types are there, and the patch loader has not run.</summary>
    public static bool Bind(ICoreAPI api)
    {
        string? problem;
        try
        {
            problem = CheckAssets(api) ?? BindMembers();
        }
        catch (Exception e)
        {
            problem = e.Message;
        }
        if (problem == null)
            return true;
        api.Logger.Warning($"[seraphhorizons] Rosser: Logging Expanded changed {problem}, so there is no debarked trunk");
        return false;
    }

    /// <summary>Empties the patch file, so the patch loader applies none of it.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        var asset = api.Assets.TryGet(PatchAsset);
        if (asset != null)
            asset.Data = "[]"u8.ToArray();
    }

    /// <summary>Server side.</summary>
    public static void Patch(Harmony harmony)
    {
        harmony.Patch(_placedLogCode, prefix: new HarmonyMethod(typeof(DebarkedTrunks), nameof(PlacedLogPrefix)));
        var finalizer = new HarmonyMethod(typeof(DebarkedTrunks), nameof(EndDebarking));
        harmony.Patch(_trunkStop, prefix: new HarmonyMethod(typeof(DebarkedTrunks), nameof(TrunkStopPrefix)), finalizer: finalizer);
        var process = new HarmonyMethod(typeof(DebarkedTrunks), nameof(ProcessPrefix));
        harmony.Patch(_process, prefix: process, finalizer: finalizer);
        harmony.Patch(_processAdvanced, prefix: process, finalizer: finalizer);
        harmony.Patch(_buildUnload, postfix: new HarmonyMethod(typeof(DebarkedTrunks), nameof(UnloadPostfix)));
    }

    /// <summary>Marks a debarked trunk's stored log stack.</summary>
    public static void Mark(ItemStack logs) => logs.Attributes.SetBool(MarkerKey, true);

    public static bool IsMarked(ItemStack? logs) => logs?.Attributes.GetBool(MarkerKey) == true;

    /// <summary>Whether a sawhorse's trunk inventory holds a debarked trunk's logs.</summary>
    public static bool IsDebarkedLoad(InventoryBase? inventory) => inventory is { Empty: false } && IsMarked(inventory[0].Itemstack);

    /// <summary>What the patch file expects of the trunk asset: the branches variant third with
    /// states yes and no, the shape by size and branches with no shapeByType, and a texture entry
    /// with a bark texture for each wood the patch gives a debarked one, and no other.</summary>
    private static string? CheckAssets(ICoreAPI api)
    {
        if (api.Assets.TryGet(TrunkAsset) is not { } trunkAsset)
            return $"{TrunkAsset} is missing";
        if (api.Assets.TryGet(PatchAsset) is not { } patchAsset)
            return null;
        var trunk = JObject.Parse(trunkAsset.ToText());
        var patch = JArray.Parse(patchAsset.ToText());
        var branches = (trunk["variantgroups"] as JArray)?.ElementAtOrDefault(2);
        if ((string?)branches?["code"] != TrunkVariants.Group
            || branches["states"] is not JArray states
            || !states.Select(s => (string?)s).SequenceEqual([TrunkVariants.Branchy, TrunkVariants.Clean]))
            return $"its trunk's variants (the third is not {TrunkVariants.Group} with states yes and no)";
        if ((string?)trunk["shape"]?["base"] != "loggingmod:treetrunk-{size}-{branches}" || trunk["shapeByType"] != null)
            return "its trunk's shape";
        var patched = patch.Select(op => (string?)op["path"])
            .Where(p => p != null && p.StartsWith("/texturesByType/", StringComparison.Ordinal))
            .Select(p => p!.Split('/')[2])
            .ToHashSet();
        if (trunk["texturesByType"] is not JObject textures
            || !textures.Properties().Select(p => p.Name).ToHashSet().SetEquals(patched)
            || textures.Properties().Any(p => p.Value["wood-h"] is not JObject))
            return "its trunk's textures";
        // the end grain is replaced only where the trunk has one of its own
        var ends = patch.Select(op => (string?)op["path"])
            .Where(p => p != null && p.StartsWith("/texturesByType/", StringComparison.Ordinal) && p.EndsWith("/woodByType", StringComparison.Ordinal))
            .Select(p => p!.Split('/')[2]);
        if (ends.Any(name => textures[name]?["wood"] is not JObject))
            return "its trunk's end grain textures";
        return null;
    }

    private static string? BindMembers()
    {
        var treeManager = WoodworkingMods.LeType("TreeManager");
        var trunk = WoodworkingMods.LeType("BlockTreeTrunk");
        var sawhorse = WoodworkingMods.LeType("BlockSawhorse");
        var advanced = WoodworkingMods.LeType("BlockSawhorseAdvanced");
        var workstation = WoodworkingMods.LeType("BEWorkstation");
        if (treeManager == null || trunk == null || sawhorse == null || advanced == null || workstation == null)
            return "its tree manager, trunk, sawhorse or workstation classes";
        _placedLogCode = AccessTools.DeclaredMethod(treeManager, "GetPlacedLogCode", [typeof(string)]);
        _debarkedLogCode = AccessTools.DeclaredMethod(treeManager, "GetDebarkedLogCode", [typeof(string)]);
        if (_placedLogCode?.ReturnType != typeof(AssetLocation) || _debarkedLogCode?.ReturnType != typeof(AssetLocation)
            || _placedLogCode.IsStatic || _debarkedLogCode.IsStatic)
            return "TreeManager.GetPlacedLogCode or GetDebarkedLogCode";
        _trunkStop = AccessTools.DeclaredMethod(trunk, nameof(Block.OnBlockInteractStop),
            [typeof(float), typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection)]);
        if (_trunkStop == null)
            return "BlockTreeTrunk.OnBlockInteractStop";
        Type[] process = [typeof(IWorldAccessor), typeof(IPlayer), typeof(BlockSelection), workstation, typeof(ItemSlot), typeof(ItemStack)];
        _process = AccessTools.DeclaredMethod(sawhorse, Sawhorses.ProcessMethod, process);
        _processAdvanced = AccessTools.DeclaredMethod(advanced, Sawhorses.ProcessMethod, process);
        if (_process?.ReturnType != typeof(bool) || _processAdvanced?.ReturnType != typeof(bool))
            return $"BlockSawhorse or BlockSawhorseAdvanced.{Sawhorses.ProcessMethod}";
        _buildUnload = AccessTools.DeclaredMethod(workstation, "BuildUnloadStack", [typeof(IWorldAccessor)]);
        _workstationInventory = AccessTools.DeclaredProperty(workstation, "Inventory");
        if (_buildUnload?.ReturnType != typeof(ItemStack) || _workstationInventory == null
            || !typeof(InventoryBase).IsAssignableFrom(_workstationInventory.PropertyType))
            return "BEWorkstation.BuildUnloadStack or Inventory";
        return null;
    }

    private static InventoryBase? Inventory(BlockEntity? be) =>
        be != null && _workstationInventory!.DeclaringType!.IsInstanceOfType(be) ? _workstationInventory.GetValue(be) as InventoryBase : null;

    /// <summary>While debarking: the wood's debarked log in place of its placed log, when it has
    /// one. Argument by position: (wood).</summary>
    public static bool PlacedLogPrefix(object __instance, string __0, ref AssetLocation? __result)
    {
        if (!_debarking || _debarkedLogCode!.Invoke(__instance, [__0]) is not AssetLocation debarked)
            return true;
        __result = debarked;
        return false;
    }

    /// <summary>A completed hold on a debarked trunk. Arguments by position: (secondsUsed, world,
    /// byPlayer, blockSel); a multiblock's other cells hand on the trunk's own position.</summary>
    public static void TrunkStopPrefix(IWorldAccessor __1, BlockSelection __3)
    {
        if (__1.Side == EnumAppSide.Server && __3?.Position != null
            && TrunkVariants.IsDebarked(__1.BlockAccessor.GetBlock(__3.Position).Variant[TrunkVariants.Group]))
            _debarking = true;
    }

    /// <summary>A sawhorse's completed hold on a debarked load. Arguments by position: (world,
    /// byPlayer, blockSel, be, toolSlot, toolStack).</summary>
    public static void ProcessPrefix(IWorldAccessor __0, BlockEntity __3)
    {
        if (__0.Side == EnumAppSide.Server && IsDebarkedLoad(Inventory(__3)))
            _debarking = true;
    }

    public static void EndDebarking() => _debarking = false;

    /// <summary>A debarked load comes off the sawhorse as the debarked trunk of its wood and size
    /// (Logging Expanded rebuilds a clean one); its log stack, and so the marker, goes with it.</summary>
    public static void UnloadPostfix(BlockEntity __instance, IWorldAccessor __0, ref ItemStack? __result)
    {
        if (__result?.Block?.Code is not { Domain: ModId } code || !IsDebarkedLoad(Inventory(__instance))
            || TrunkVariants.DebarkedPath(code.Path) is not { } path
            || __0.GetBlock(new AssetLocation(ModId, path)) is not { Id: > 0 } debarked)
            return;
        __result = new ItemStack(debarked) { Attributes = __result.Attributes };
    }
}
