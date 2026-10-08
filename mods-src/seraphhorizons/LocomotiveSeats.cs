using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod;

/// <summary>
/// Yang's Transport Tycoon (<c>yangtransport</c> 1.0.3): its standard-gauge locomotives
/// (<c>yangtransport:sglocomotive-*</c>, every tier) keep their riders.
///
/// <para><b>Riders stay on</b> (<c>LocomotiveRidersStayOn</c>). The standard and advanced tiers set
/// <c>CollisionCheckSeats</c> in their type's attributes. While a player rides one, Yang's
/// <c>RailVehicleSeat</c> runs a client tick (every 20 ms) that tests the rider's collision box at
/// the seat for any colliding block, and on one (leaves count: the game's leaves collide) sends the
/// locomotive a packet on which the server unmounts the rider. Passing trees beside the track throws
/// the driver off. Both ends ask Yang's <c>internal static RailVehicleSeat.CollisionChecksEnabled(Entity)</c>
/// first: the seat before it starts the client tick, the locomotive before it acts on the packet.
/// A postfix makes it answer false for a locomotive, so neither runs. Nothing else reads it: a rider
/// who steps off is still placed beside the track by Yang's own placement, which only takes the
/// collision's position when there was one. Patched on both sides (either one alone already keeps
/// the rider on), once per process with its own Harmony id. Other vehicles that set the attribute
/// (this mod's handcar) keep their check.</para>
///
/// <para><b>Riders breathe</b> (<c>LocomotiveRidersBreathe</c>). With the check off a rider's head
/// passes through leaves and walls beside the track, and the game's <c>EntityBehaviorBreathe.Check</c>
/// (server side, once a second) takes the air of anyone whose eye is inside a block's collision box,
/// so they would choke on the way past. A postfix gives the air back to an entity seated in a
/// locomotive, unless the eye is under a liquid's surface: a locomotive driven into deep water still
/// drowns its rider. <c>OnGameTick</c> refills the oxygen from <c>HasAir</c> as usual. Server side
/// only, where <c>Check</c> runs. In game 1.22.7 the block test never takes the air anywhere away from
/// the world's origin (it intersects the block's box, in world coordinates, with
/// <c>Entity.SelectionBox</c>, which is the entity's own), so for now this guards against the game
/// fixing that.</para>
///
/// <para>Yang's mod is not referenced at build time: the seat's type is found by name, and if it or
/// the method is gone the riders-stay-on half logs a warning and leaves the check as Yang ships it.
/// The breathing half needs only the game and the locomotive's entity code. Without Yang's Transport
/// Tycoon neither is patched.</para>
/// </summary>
public static class LocomotiveSeats
{
    public const string ModId = "yangtransport";
    public const string SeatType = "YangTransport.RailVehicleSeat";
    public const string CollisionCheckMethod = "CollisionChecksEnabled";
    /// <summary>The locomotive's entity code path, before its tier: <c>sglocomotive-{tier}</c>.</summary>
    public const string LocomotivePath = "sglocomotive";

    public const string StayOnHarmonyId = "seraphhorizons.locomotiveridersstayon";
    public const string BreatheHarmonyId = "seraphhorizons.locomotiveridersbreathe";

    private static MethodInfo? _collisionChecksEnabled;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Whether <paramref name="entity"/> is one of Yang's standard-gauge locomotives, any tier.</summary>
    public static bool IsLocomotive(Entity? entity) =>
        entity?.Code is { } code && code.Domain == ModId
        && (code.Path == LocomotivePath || code.Path.StartsWith(LocomotivePath + "-", StringComparison.Ordinal));

    /// <summary>Whether <paramref name="entity"/> sits in a seat of a locomotive.</summary>
    public static bool RidesLocomotive(Entity? entity) =>
        entity is EntityAgent { MountedOn: { } seat } && IsLocomotive(seat.Entity ?? seat.MountSupplier?.OnEntity);

    /// <summary>Finds Yang's <c>RailVehicleSeat.CollisionChecksEnabled(Entity)</c>. False, with one
    /// warning, when it is not as expected.</summary>
    public static bool BindStayOn(ILogger logger)
    {
        var seat = AccessTools.TypeByName(SeatType);
        _collisionChecksEnabled = seat == null ? null : AccessTools.DeclaredMethod(seat, CollisionCheckMethod, [typeof(Entity)]);
        if (_collisionChecksEnabled is { IsStatic: true } && _collisionChecksEnabled.ReturnType == typeof(bool))
            return true;
        _collisionChecksEnabled = null;
        logger.Warning($"[seraphhorizons] {SeatType} has no static bool {CollisionCheckMethod}(Entity); Yang's Transport "
                       + "Tycoon changed, so blocks beside the track still throw a locomotive's riders off");
        return false;
    }

    /// <summary>Postfixes <c>CollisionChecksEnabled</c>, unless the other side of a singleplayer game did.</summary>
    public static void PatchStayOn(Harmony harmony)
    {
        if (Harmony.HasAnyPatches(StayOnHarmonyId))
            return;
        harmony.Patch(_collisionChecksEnabled, postfix: new HarmonyMethod(typeof(LocomotiveSeats), nameof(CollisionChecksEnabledPostfix)));
    }

    /// <summary>Server side: postfixes the game's breathing check.</summary>
    public static void PatchBreathe(Harmony harmony) =>
        harmony.Patch(AccessTools.DeclaredMethod(typeof(EntityBehaviorBreathe), nameof(EntityBehaviorBreathe.Check)),
            postfix: new HarmonyMethod(typeof(LocomotiveSeats), nameof(BreatheCheckPostfix)));

    // RailVehicleSeat.CollisionChecksEnabled(Entity entity)
    public static void CollisionChecksEnabledPostfix(Entity __0, ref bool __result)
    {
        if (__result && IsLocomotive(__0))
            __result = false;
    }

    public static void BreatheCheckPostfix(EntityBehaviorBreathe __instance)
    {
        var entity = __instance.entity;
        if (__instance.HasAir || entity?.World?.Side != EnumAppSide.Server || !RidesLocomotive(entity) || EyeUnderLiquid(entity))
            return;
        __instance.HasAir = true;
    }

    /// <summary>The game's own liquid test in <c>EntityBehaviorBreathe.Check</c>: the block at the
    /// eye is a liquid whose level is above the eye.</summary>
    private static bool EyeUnderLiquid(Entity entity)
    {
        EntityPos pos = entity.Pos;
        double eyeHeight = entity.Swimming ? entity.Properties.SwimmingEyeHeight : entity.Properties.EyeHeight;
        var eye = new BlockPos((int)(pos.X + entity.LocalEyePos.X), (int)(pos.Y + eyeHeight), (int)(pos.Z + entity.LocalEyePos.Z), pos.Dimension);
        Block block = entity.World.BlockAccessor.GetBlock(eye, BlockLayersAccess.FluidOrSolid);
        return block.IsLiquid() && block.LiquidLevel / 7f > (pos.Y + eyeHeight) % 1.0;
    }
}
