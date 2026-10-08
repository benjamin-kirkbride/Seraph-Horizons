using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Handcar;

/// <summary>
/// The handcar's Harmony patches on Yang's classes (<see cref="YangBridge"/>). Each only acts on a
/// car that has the <see cref="EntityBehaviorHumanPowered"/> behaviour; every other vehicle goes
/// through untouched.
/// <list type="bullet">
/// <item><b>The drive</b> (server): a prefix on <c>EntityStandardGaugeLocomotive.TryComputeConvoyDrive</c>
/// gives a handcar lead its riders' drive instead of a steam engine's.</item>
/// <item><b>The seat's facing</b> (both sides): Yang turns every rider to the car's front; a postfix on
/// <c>RailVehicleSeat.SeatPosition</c> turns a handcar seat's rider by its <c>mountRotation</c>, so the
/// front rider faces back, across the beam. The game turns its own player to that (the seat's
/// <c>FixateYaw</c>).</item>
/// <item><b>Other players' riders</b> (client): the game draws a mounted player other than its own
/// turned to the car's yaw, whatever the seat says (<c>EntityPlayerShapeRenderer.loadModelMatrixForPlayer</c>),
/// but takes the seat's <c>RenderTransform</c> first; a postfix on it adds the seat's turn there, for
/// another player only.</item>
/// </list>
/// </summary>
public static class HandcarPatches
{
    public const string ServerHarmonyId = "seraphhorizons.handcar.server";
    public const string ClientHarmonyId = "seraphhorizons.handcar.client";
    /// <summary>The seat's facing, patched once per process (singleplayer runs both sides in one).</summary>
    public const string SeatHarmonyId = "seraphhorizons.handcar.seat";

    private static YangBridge? _bridge;
    private static ICoreClientAPI? _capi;
    private static readonly ConditionalWeakTable<EntitySeat, Matrixf> Turned = new();

    public static void PatchServer(Harmony harmony, YangBridge bridge)
    {
        _bridge = bridge;
        harmony.Patch(bridge.Drive, prefix: new HarmonyMethod(typeof(HandcarPatches), nameof(DrivePrefix)));
    }

    public static void PatchSeat(YangBridge bridge)
    {
        if (Harmony.HasAnyPatches(SeatHarmonyId))
            return;
        new Harmony(SeatHarmonyId).Patch(bridge.SeatPosition, postfix: new HarmonyMethod(typeof(HandcarPatches), nameof(SeatPositionPostfix)));
    }

    public static void PatchClient(Harmony harmony, YangBridge bridge, ICoreClientAPI capi)
    {
        _capi = capi;
        harmony.Patch(bridge.RenderTransform, postfix: new HarmonyMethod(typeof(HandcarPatches), nameof(RenderTransformPostfix)));
    }

    public static void Unpatch(Harmony? harmony, string id, bool client)
    {
        harmony?.UnpatchAll(id);
        if (client)
            _capi = null;
        else
            _bridge = null;
    }

    public static void UnpatchSeat()
    {
        if (Harmony.HasAnyPatches(SeatHarmonyId))
            new Harmony(SeatHarmonyId).UnpatchAll(SeatHarmonyId);
    }

    /// <summary>The seat's turn about the vertical, radians, for a handcar's seat; 0 for any other.</summary>
    public static float TurnOf(EntitySeat seat)
    {
        float deg = seat.Config?.MountRotation?.Y ?? 0f;
        if (deg == 0f)
            return 0f;
        var car = (seat.MountSupplier as EntityBehaviorSeatable)?.entity;
        return car?.GetBehavior<EntityBehaviorHumanPowered>() != null ? deg * GameMath.DEG2RAD : 0f;
    }

    public static bool DrivePrefix(Entity leadVehicle, int convoyWeight, ref double normalizedThrottle, ref double accelerationBPSPerSec,
                                   ref double maxSpeedBPS, ref bool __result)
    {
        if (_bridge == null || leadVehicle?.GetBehavior<EntityBehaviorHumanPowered>() is not { } pump)
            return true;
        var command = pump.Drive(_bridge.Speed(leadVehicle), _bridge.Motion(leadVehicle), convoyWeight);
        normalizedThrottle = command.Throttle;
        accelerationBPSPerSec = command.Acceleration;
        maxSpeedBPS = command.TopSpeed;
        __result = true;
        return false;
    }

    public static void SeatPositionPostfix(EntitySeat __instance, EntityPos __result)
    {
        if (__result == null)
            return;
        float turn = TurnOf(__instance);
        if (turn != 0f)
            __result.Yaw = GameMath.Mod(__result.Yaw + turn, GameMath.TWOPI);
    }

    public static void RenderTransformPostfix(EntitySeat __instance, ref Matrixf __result)
    {
        if (_capi?.World?.Player?.Entity is not { } self || __instance.Passenger == null || __instance.Passenger.EntityId == self.EntityId)
            return;
        float turn = TurnOf(__instance);
        if (turn == 0f)
            return;
        var m = Turned.GetValue(__instance, _ => new Matrixf());
        if (__result != null)
            m.Set(__result.Values);
        else
            m.Identity();
        m.RotateY(turn);
        __result = m;
    }
}
