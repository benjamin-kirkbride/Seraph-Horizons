using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Food Shelves (<c>foodshelves</c>): its barrel rack (<c>BEBarrelRack</c>) also takes Hydrate or
/// Diedrate's keg (<c>hydrateordiedrate:keg-*</c>, <c>BlockKeg</c>), holding a keg's worth.
///
/// What the rack takes is data: <c>foodshelves:config/restrictions/barrels/barrelrack.json</c>, which
/// Food Shelves reads in <c>AssetsLoaded</c> (after the patch loader) and turns into an
/// <c>fsbarrelrack</c> attribute on each matching collectible in <c>AssetsFinalize</c>. A JSON patch
/// shipped here (<see cref="PatchAssets"/>) adds the keg's code to it. With the switch off, or
/// either mod missing or changed, <see cref="DisablePatches"/> empties it in <c>Start</c>.
///
/// The rest are patches, because a keg is not a barrel in three ways:
/// <list type="bullet">
/// <item>A keg item carries its liquid (Hydrate's <c>KegDropWithLiquid</c>), and the rack keeps the
/// liquid in its own slot 1 and the cask in slot 0. A racked keg's liquid is moved out of the item
/// into slot 1 (<see cref="OnInteractPostfix"/>), and back into the item when the keg is taken out
/// with an empty hand or the rack is broken (<see cref="OnInteractPrefix"/>,
/// <see cref="OnBlockBrokenPrefix"/>), as long as Hydrate keeps a broken keg's liquid. Rot is left
/// in the rack, as Hydrate spills it when a keg breaks; Food Shelves' own path takes it out.</item>
/// <item>The rack's capacity is its block's (50 L, <c>capacityLitres</c>), read by
/// <c>TryPutLiquid(BlockPos, ...)</c>, which every pour into it goes through. For a rack holding a
/// keg that call runs with the keg's capacity (<c>BlockKeg.CapacityLitres</c>, Hydrate's
/// <c>KegCapacityLitres</c>) in the rack block's field (<see cref="TryPutLiquidPrefix"/>).</item>
/// <item>The rack halves perishing for everything in it; a keg already slows it, by Hydrate's
/// <c>SpoilRateUntapped</c> or <c>SpoilRateTapped</c> (whichever the racked keg is). The two
/// multiply (<see cref="AcquireTransitionSpeedPostfix"/>), as they do for a barrel (whose own rate
/// is 1), so a racked keg keeps twice as long as the same keg standing, as the rack's text says.</item>
/// </list>
///
/// Neither mod is referenced at build time: their types are found by name, and if one is gone or
/// changed the tweak logs a warning and leaves the rack as it ships. Patched on both sides (the
/// client predicts the interactions and pours, and would refuse what the server allows), once per
/// process with its own Harmony id, since a singleplayer game runs both sides in one.
/// </summary>
public static class BarrelRackKegs
{
    public const string FoodShelvesId = "foodshelves";
    public const string HydrateId = "hydrateordiedrate";
    public const string HarmonyId = "seraphhorizons.barrelrackkegs";

    public const string RackEntityType = "FoodShelves.BEBarrelRack";
    public const string RackBlockType = "FoodShelves.BlockBarrelRack";
    public const string FsContainerType = "FoodShelves.BEBaseFSContainer";
    public const string KegType = "HydrateOrDiedrate.Keg.BlockKeg";
    public const string HydrateConfigType = "HydrateOrDiedrate.Config.ModConfig";

    /// <summary>A tapped keg's code path; any other keg is untapped (as <c>BlockEntityKeg</c> tells them apart).</summary>
    public const string TappedPath = "keg-tapped";

    public static readonly AssetLocation[] PatchAssets =
    [
        new("seraphhorizons", "patches/barrelrack-kegs.json"),
    ];

    private static Type? _rackEntity, _rackBlock, _keg;
    private static MethodInfo? _onInteract, _onBlockBroken, _acquireSpeed, _perishRate, _tryPutLiquid;
    private static PropertyInfo? _configInstance, _containers;
    private static PropertyInfo? _spoilTapped, _spoilUntapped, _dropWithLiquid;

    public static bool Applies(ICoreAPI api) =>
        api.ModLoader.IsModEnabled(FoodShelvesId) && api.ModLoader.IsModEnabled(HydrateId);

    /// <summary>Finds the rack's, the keg's and Hydrate's config members. Returns whether all are
    /// there as expected; if not, logs which mod changed.</summary>
    public static bool Bind(ILogger logger)
    {
        _rackEntity = AccessTools.TypeByName(RackEntityType);
        _rackBlock = AccessTools.TypeByName(RackBlockType);
        var fsContainer = AccessTools.TypeByName(FsContainerType);
        _onInteract = _rackEntity == null ? null
            : AccessTools.DeclaredMethod(_rackEntity, "OnInteract", [typeof(IPlayer), typeof(BlockSelection), typeof(string)]);
        _onBlockBroken = _rackBlock == null ? null
            : AccessTools.DeclaredMethod(_rackBlock, nameof(Block.OnBlockBroken),
                [typeof(IWorldAccessor), typeof(BlockPos), typeof(IPlayer), typeof(float)]);
        _acquireSpeed = fsContainer == null ? null
            : AccessTools.DeclaredMethod(fsContainer, "Inventory_OnAcquireTransitionSpeed",
                [typeof(EnumTransitionType), typeof(ItemStack), typeof(float)]);
        _perishRate = fsContainer == null ? null : AccessTools.DeclaredMethod(fsContainer, "GetPerishRate", []);
        if (_rackEntity == null || !typeof(BlockEntityContainer).IsAssignableFrom(_rackEntity)
            || _rackBlock == null || !typeof(BlockLiquidContainerBase).IsAssignableFrom(_rackBlock)
            || fsContainer == null || !fsContainer.IsAssignableFrom(_rackEntity)
            || _onInteract?.ReturnType != typeof(bool) || _onBlockBroken == null
            || _acquireSpeed?.ReturnType != typeof(float) || _perishRate?.ReturnType != typeof(float))
        {
            logger.Warning("[seraphhorizons] Food Shelves' barrel rack does not look as expected; Food Shelves changed, so "
                           + "its barrel rack does not take kegs");
            return false;
        }

        _keg = AccessTools.TypeByName(KegType);
        var config = AccessTools.TypeByName(HydrateConfigType);
        _configInstance = config == null ? null : AccessTools.Property(config, "Instance");
        _containers = config == null ? null : AccessTools.Property(config, "Containers");
        var containers = _containers?.PropertyType;
        _spoilTapped = containers == null ? null : AccessTools.Property(containers, "SpoilRateTapped");
        _spoilUntapped = containers == null ? null : AccessTools.Property(containers, "SpoilRateUntapped");
        _dropWithLiquid = containers == null ? null : AccessTools.Property(containers, "KegDropWithLiquid");
        if (_keg == null || !typeof(BlockLiquidContainerBase).IsAssignableFrom(_keg)
            || _configInstance?.GetMethod?.IsStatic != true
            || _spoilTapped?.PropertyType != typeof(float) || _spoilUntapped?.PropertyType != typeof(float)
            || _dropWithLiquid?.PropertyType != typeof(bool))
        {
            logger.Warning("[seraphhorizons] Hydrate or Diedrate's keg or its keg settings do not look as expected; Hydrate "
                           + "or Diedrate changed, so Food Shelves' barrel rack does not take kegs");
            return false;
        }

        _tryPutLiquid = AccessTools.DeclaredMethod(typeof(BlockLiquidContainerBase), nameof(BlockLiquidContainerBase.TryPutLiquid),
            [typeof(BlockPos), typeof(ItemStack), typeof(float)]);
        return true;
    }

    /// <summary>Empties this mod's barrel rack patch, so the rack's restriction file stays as Food
    /// Shelves ships it. Runs in Start: the patches are applied in AssetsLoaded.</summary>
    public static void DisablePatches(ICoreAPI api)
    {
        foreach (var location in PatchAssets)
        {
            var asset = api.Assets.TryGet(location);
            if (asset != null)
                asset.Data = "[]"u8.ToArray();
        }
    }

    /// <summary>Applies the patches with <paramref name="harmony"/>, unless they are in already
    /// (the other side of a singleplayer game applied them).</summary>
    public static void Patch(Harmony harmony)
    {
        if (Harmony.HasAnyPatches(HarmonyId))
            return;
        harmony.Patch(_onInteract, prefix: Method(nameof(OnInteractPrefix)), postfix: Method(nameof(OnInteractPostfix)));
        harmony.Patch(_onBlockBroken, prefix: Method(nameof(OnBlockBrokenPrefix)));
        harmony.Patch(_acquireSpeed, postfix: Method(nameof(AcquireTransitionSpeedPostfix)));
        harmony.Patch(_perishRate, postfix: Method(nameof(PerishRatePostfix)));
        harmony.Patch(_tryPutLiquid, prefix: Method(nameof(TryPutLiquidPrefix)), finalizer: Method(nameof(TryPutLiquidFinalizer)));
    }

    private static HarmonyMethod Method(string name) => new(typeof(BarrelRackKegs), name);

    /// <summary>The keg in a barrel rack's cask slot, or null when <paramref name="be"/> is not a
    /// barrel rack or holds no keg.</summary>
    public static BlockLiquidContainerBase? RackedKeg(BlockEntity? be) =>
        be is BlockEntityContainer rack && _rackEntity!.IsInstanceOfType(rack) && rack.Inventory.Count > 1
        && rack.Inventory[0].Itemstack?.Block is BlockLiquidContainerBase keg && _keg!.IsInstanceOfType(keg)
            ? keg
            : null;

    /// <summary>Hydrate's spoil rate for <paramref name="keg"/>, tapped or untapped (its config is
    /// read each time: ConfigLib can change it in game). 1 when the config is not loaded.</summary>
    public static float SpoilRate(Block keg)
    {
        var containers = Containers();
        if (containers == null)
            return 1f;
        return (float)(keg.Code.Path == TappedPath ? _spoilTapped! : _spoilUntapped!).GetValue(containers)!;
    }

    /// <summary>Hydrate's <c>KegDropWithLiquid</c>: a keg keeps its liquid as an item.</summary>
    public static bool KegKeepsLiquid() => Containers() is { } containers && (bool)_dropWithLiquid!.GetValue(containers)!;

    private static object? Containers() => _configInstance!.GetValue(null) is { } config ? _containers!.GetValue(config) : null;

    private static bool IsRot(ItemStack stack) => stack.Collectible?.Code.Path.StartsWith("rot") == true;

    /// <summary>Moves a racked keg item's liquid into the rack's liquid slot, when that is empty.
    /// The keg holds at most a keg's worth, which the rack then holds too.</summary>
    public static void MoveLiquidIntoRack(BlockEntity be)
    {
        if (RackedKeg(be) is not { } keg)
            return;
        var inv = ((BlockEntityContainer)be).Inventory;
        ItemSlot cask = inv[0], liquid = inv[1];
        if (!liquid.Empty || keg.GetContent(cask.Itemstack!) is not { } content)
            return;
        keg.SetContent(cask.Itemstack!, null!);
        liquid.Itemstack = content;
        cask.MarkDirty();
        liquid.MarkDirty();
        be.MarkDirty(true);
    }

    /// <summary>Moves the rack's liquid back into the racked keg item, so the keg leaves with it,
    /// if Hydrate keeps a keg's liquid and the liquid is not rot.</summary>
    public static void MoveLiquidIntoKeg(BlockEntity be)
    {
        if (RackedKeg(be) is not { } keg)
            return;
        var inv = ((BlockEntityContainer)be).Inventory;
        ItemSlot cask = inv[0], liquid = inv[1];
        if (liquid.Empty || IsRot(liquid.Itemstack) || keg.GetContent(cask.Itemstack!) != null || !KegKeepsLiquid())
            return;
        keg.SetContent(cask.Itemstack!, liquid.TakeOutWhole());
        cask.MarkDirty();
        liquid.MarkDirty();
        be.MarkDirty(true);
    }

    /// <summary>With an empty hand the rack hands its cask out, but only once the liquid slot is
    /// empty: for a keg, the liquid goes into the keg first.</summary>
    public static void OnInteractPrefix(BlockEntity __instance, IPlayer __0)
    {
        if (__0.InventoryManager.ActiveHotbarSlot?.Empty == true)
            MoveLiquidIntoKeg(__instance);
    }

    /// <summary>After any interaction (a keg put in, or one that stayed), a racked keg's liquid is
    /// in the rack, never in the item.</summary>
    public static void OnInteractPostfix(BlockEntity __instance) => MoveLiquidIntoRack(__instance);

    /// <summary>The rack drops its slots when broken: the keg's liquid goes in the keg first.</summary>
    public static void OnBlockBrokenPrefix(IWorldAccessor __0, BlockPos __1)
    {
        if (__0.Side == EnumAppSide.Server)
            MoveLiquidIntoKeg(__0.BlockAccessor.GetBlockEntity(__1));
    }

    /// <summary>Multiplies the rack's perish speed by the racked keg's own spoil rate.</summary>
    public static void AcquireTransitionSpeedPostfix(BlockEntity __instance, EnumTransitionType __0, ref float __result)
    {
        if (__0 == EnumTransitionType.Perish && RackedKeg(__instance) is { } keg)
            __result *= SpoilRate(keg);
    }

    /// <summary>The same for the perish speed the rack shows.</summary>
    public static void PerishRatePostfix(BlockEntity __instance, ref float __result)
    {
        if (RackedKeg(__instance) is { } keg)
            __result *= SpoilRate(keg);
    }

    /// <summary>A pour into a rack holding a keg runs with the keg's capacity in the rack block's
    /// capacity field, which <c>CapacityLitres</c> returns; the finalizer puts it back. The field
    /// rather than a postfix on the getter, which the JIT may inline into its callers.</summary>
    public static void TryPutLiquidPrefix(BlockLiquidContainerBase __instance, BlockPos __0, ICoreAPI ___api,
        ref float ___capacityLitresFromAttributes, out float __state)
    {
        __state = -1f;
        if (!_rackBlock!.IsInstanceOfType(__instance) || ___api?.World.BlockAccessor.GetBlockEntity(__0) is not { } be
            || RackedKeg(be) is not { } keg)
            return;
        __state = ___capacityLitresFromAttributes;
        ___capacityLitresFromAttributes = keg.CapacityLitres;
    }

    public static Exception? TryPutLiquidFinalizer(Exception? __exception, float __state, ref float ___capacityLitresFromAttributes)
    {
        if (__state >= 0f)
            ___capacityLitresFromAttributes = __state;
        return __exception;
    }

    /// <summary>The Food Shelves text that says what the rack takes (English, as it ships).</summary>
    public static readonly LangEdit[] LangEdits =
    [
        new("en", "foodshelves:blockdesc-barrelrack-text",
            "  -Barrels. These barrels can only hold liquids.",
            "  -Barrels. These barrels can only hold liquids.<br>Hydrate or Diedrate:<br>  -Kegs, holding a keg's worth."),
        new("en", "foodshelves:Only barrels can be placed on this rack.",
            "Only barrels can be placed on this rack.",
            "Only barrels and kegs can be placed on this rack."),
    ];
}
