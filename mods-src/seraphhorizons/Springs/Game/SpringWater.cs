using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Springs;

/// <summary>
/// The water a spring gives a player (README "Springs"): Hydrate or Diedrate's fresh well water
/// (<see cref="WellWaterCode"/>, what its <c>wellwater-fresh-clean-natural</c> fills a bucket with),
/// endlessly, into any liquid container (the block's <c>waterTightContainerProps</c>, which the
/// game's <c>BlockLiquidContainerBase.TryFillFromBlock</c> reads for every container), and drunk
/// with an empty hand as Hydrate drinks: a bucket holding a litre of it, emptied by the game's
/// <c>tryEatStop</c> (which Hydrate patches to quench thirst), in Hydrate's own
/// <c>LockedDummySlot</c> (so the empty bucket never lands in an inventory), after Hydrate's own
/// check of the player (<c>DefaultDrinkingInteractionProvider.ValidatePlayer</c>: both hands empty,
/// not full). Hydrate's licence forbids redistributing it: everything is found by name. Without it
/// (or with a member not as expected, one warning) a spring gives the game's plain water and there
/// is nothing to drink.
/// </summary>
public static class SpringWater
{
    public const string HodModId = "hydrateordiedrate";
    public const string WellWaterCode = "hydrateordiedrate:waterportion-fresh-well-clean";
    public const string PlainWaterCode = "game:waterportion";
    public const string BucketCode = "game:woodbucket";
    public const string ValidateType = "HydrateOrDiedrate.Hydration.DefaultDrinkingInteractionProvider";
    public const string LockedSlotType = "HydrateOrDiedrate.Utility.LockedDummySlot";

    /// <summary>Seconds a drink takes, Hydrate's (<c>PlayerDrinkData</c>'s one second).</summary>
    public const float DrinkSeconds = 1f;

    private static MethodInfo? _validate, _eatStop;
    private static ConstructorInfo? _lockedSlot;
    private static bool _warned;
    private static ICoreAPI? _boundFor;

    /// <summary>Binds for <paramref name="api"/> unless it already has (the first spring block to
    /// load on each side).</summary>
    public static void EnsureBound(ICoreAPI api)
    {
        if (!ReferenceEquals(_boundFor, api))
            Bind(api);
    }

    /// <summary>The portion a spring fills a container with on this side.</summary>
    public static string PortionCode { get; private set; } = WellWaterCode;

    /// <summary>Whether drinking from a spring works on this side.</summary>
    public static bool CanDrink => _validate != null && _eatStop != null && _lockedSlot != null;

    /// <summary>Both sides, once the items are loaded: well water when Hydrate's is there, else the
    /// game's water and one warning; and the drinking members.</summary>
    public static void Bind(ICoreAPI api)
    {
        _boundFor = api;
        _validate = _eatStop = null;
        _lockedSlot = null;
        if (api.World.GetItem(new AssetLocation(WellWaterCode)) is { IsMissing: false })
            PortionCode = WellWaterCode;
        else
        {
            PortionCode = PlainWaterCode;
            if (!_warned)
                api.Logger.Warning("[seraphhorizons] Springs: Hydrate or Diedrate's fresh well water ({0}) is not there, so a spring gives plain water and cannot be drunk from", WellWaterCode);
            _warned = true;
            return;
        }
        var validate = AccessTools.TypeByName(ValidateType) is { } vt ? AccessTools.DeclaredMethod(vt, "ValidatePlayer", [typeof(IPlayer)]) : null;
        var locked = AccessTools.TypeByName(LockedSlotType);
        var lockedCtor = locked == null || !typeof(ItemSlot).IsAssignableFrom(locked) ? null : AccessTools.DeclaredConstructor(locked, [typeof(ItemStack)]);
        var eatStop = AccessTools.Method(typeof(BlockLiquidContainerBase), "tryEatStop", [typeof(float), typeof(ItemSlot), typeof(EntityAgent)]);
        if (validate is not { IsStatic: true } || validate.ReturnType != typeof(bool) || lockedCtor == null || eatStop == null)
        {
            api.Logger.Warning("[seraphhorizons] Springs: Hydrate or Diedrate's DefaultDrinkingInteractionProvider.ValidatePlayer or LockedDummySlot, or the game's BlockLiquidContainerBase.tryEatStop, is not as expected, so a spring cannot be drunk from (it still fills containers)");
            return;
        }
        _validate = validate;
        _lockedSlot = lockedCtor;
        _eatStop = eatStop;
    }

    /// <summary>Points <paramref name="block"/>'s <c>waterTightContainerProps.whenFilled</c> at
    /// <see cref="PortionCode"/> (the asset names Hydrate's well water).</summary>
    public static void SetPortion(Block block)
    {
        if (block.Attributes?.Token is not JObject attributes
            || attributes.SelectToken("waterTightContainerProps.whenFilled.stack") is not JObject stack)
            return;
        if ((string?)stack["code"] == PortionCode)
            return;
        stack["code"] = PortionCode;
        block.Attributes = new JsonObject(attributes);
    }

    /// <summary>Hydrate's check of <paramref name="player"/> (it tells the player why not).</summary>
    public static bool MayDrink(IPlayer player)
    {
        if (!CanDrink)
            return false;
        try
        {
            return (bool)_validate!.Invoke(null, [player])!;
        }
        catch (Exception e)
        {
            player.Entity?.Api?.Logger.Warning("[seraphhorizons] Springs: Hydrate or Diedrate's ValidatePlayer failed: {0}", (e.InnerException ?? e).Message);
            return false;
        }
    }

    /// <summary>On the server: <paramref name="player"/> drinks a litre of well water as from
    /// Hydrate's drinking.</summary>
    public static bool Drink(IWorldAccessor world, IPlayer player)
    {
        if (!CanDrink || player.Entity == null || world.GetBlock(new AssetLocation(BucketCode)) is not BlockLiquidContainerBase bucket
            || world.GetItem(new AssetLocation(PortionCode)) is not { IsMissing: false } portion)
            return false;
        try
        {
            var water = new ItemStack(portion);
            var props = BlockLiquidContainerBase.GetContainableProps(water);
            if (props == null)
                return false;
            water.StackSize = Math.Max(1, (int)props.ItemsPerLitre);
            var stack = new ItemStack(bucket);
            bucket.SetContent(stack, water);
            var slot = (ItemSlot)_lockedSlot!.Invoke([stack]);
            _eatStop!.Invoke(bucket, [DrinkSeconds, slot, player.Entity]);
            return true;
        }
        catch (Exception e)
        {
            world.Logger.Warning("[seraphhorizons] Springs: drinking from a spring failed: {0}", (e.InnerException ?? e).Message);
            return false;
        }
    }
}
