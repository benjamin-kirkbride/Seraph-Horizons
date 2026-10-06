using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SeraphHorizons.Mod;

/// <summary>
/// MPE Gearbox (<c>mpegearbox</c>): a power source that creates its network through a gearbox
/// takes the ratio of the gearbox side it touches, not the far side's (#462).
///
/// The game's <c>BEBehaviorMPBase.CreateJoinAndDiscoverNetwork(powerOutFacing)</c>, which a rotor
/// runs when it is placed or loaded and <c>MechanicalPowerMod</c> runs for every source when a
/// network is rebuilt after a block on it is removed, spreads a new network into the neighbour at
/// <c>powerOutFacing</c> and then sets its own ratio from
/// <c>neighbour.GetGearedRatio(blockFacing.Opposite)</c>, where <c>blockFacing</c> is
/// <c>powerOutFacing</c> once the neighbour propagates away from it: the neighbour's own connector
/// face. <c>tryConnect</c> asks with the direction from the asker toward the neighbour instead. The
/// two agree for any block with one ratio on every face (every vanilla one), so the game never
/// notices. <c>MPEGearbox.BEBehaviorGearbox12.GetGearedRatio(face)</c> answers for the connector at
/// <c>face.Opposite</c>, tryConnect's way, so a rotor on a 1:5 gearbox's low side gets the high
/// side's ratio (5) and believes it is at its target speed when the network turns at a fifth of it;
/// one on the high side gets the low side's (0.2) and races.
///
/// A postfix asks the gearbox again, tryConnect's way (<c>GetGearedRatio(powerOutFacing)</c>), and
/// sets that ratio when it differs (<see cref="GearboxCoupling.RatioToSet"/>). Only the block's
/// <c>GearedRatio</c> is changed, not its propagation direction, which the game set right; the
/// gearbox and what lies beyond it took their ratios from the spread itself, which is right.
/// A postfix and not a transpiler: the game's method is left whole, and the patch acts only when
/// the neighbour is MPE Gearbox's, so nothing else's ratio can change.
///
/// MPE Gearbox is not referenced at build time: its behavior is found by name, and if it is
/// missing or no longer a <c>BEBehaviorMPBase</c> the tweak logs a warning and patches nothing.
/// </summary>
public static class GearboxSourceRatio
{
    public const string ModId = "mpegearbox";

    private static Type? _gearbox;

    public static bool Applies(ICoreAPI api) => api.ModLoader.IsModEnabled(ModId);

    /// <summary>Postfixes <c>BEBehaviorMPBase.CreateJoinAndDiscoverNetwork</c>. Returns whether the
    /// patch went in.</summary>
    public static bool Patch(Harmony harmony, ILogger logger)
    {
        _gearbox = AccessTools.TypeByName(GearboxCoupling.GearboxBehaviorType);
        var discover = AccessTools.DeclaredMethod(typeof(BEBehaviorMPBase),
            nameof(BEBehaviorMPBase.CreateJoinAndDiscoverNetwork), [typeof(BlockFacing)]);
        if (_gearbox == null || !typeof(BEBehaviorMPBase).IsAssignableFrom(_gearbox) || discover == null)
        {
            logger.Warning($"[seraphhorizons] {GearboxCoupling.GearboxBehaviorType} is not a mechanical power "
                           + "behavior as expected; MPE Gearbox changed, so a power source that discovers its "
                           + "network through a gearbox may still take the wrong ratio");
            _gearbox = null;
            return false;
        }

        harmony.Patch(discover, postfix: new HarmonyMethod(typeof(GearboxSourceRatio), nameof(DiscoverPostfix)));
        return true;
    }

    /// <summary>Corrects the discovering block's ratio when the neighbour it discovered through is
    /// a gearbox on its network. Arguments by position, so a renamed parameter does not break the
    /// patch.</summary>
    public static void DiscoverPostfix(BEBehaviorMPBase __instance, BlockFacing __0)
    {
        BlockFacing powerOutFacing = __0;
        ICoreAPI? api = __instance.Api;
        if (_gearbox == null || powerOutFacing == null || api?.Side != EnumAppSide.Server)
            return;
        BlockPos pos = __instance.Position.AddCopy(powerOutFacing);
        var neighbour = api.World.BlockAccessor.GetBlockEntity(pos)?.GetBehavior<BEBehaviorMPBase>();
        if (neighbour == null || !_gearbox.IsInstanceOfType(neighbour))
            return;
        // Coupled through the face that touches us, not merely next to it.
        if (api.World.BlockAccessor.GetBlock(pos) is not IMechanicalPowerBlock block
            || !block.HasMechPowerConnectorAt(api.World, pos, powerOutFacing.Opposite, __instance.Block as BlockMPBase))
            return;

        float? ratio = GearboxCoupling.RatioToSet(
            neighbourIsGearbox: true,
            selfIsGearbox: _gearbox.IsInstanceOfType(__instance),
            sameNetwork: __instance.Network != null && __instance.Network == neighbour.Network,
            stored: __instance.GearedRatio,
            atTouchingFace: neighbour.GetGearedRatio(powerOutFacing));
        if (ratio is not { } corrected)
            return;
        api.Logger.Debug("[seraphhorizons] {0} at {1} discovered through a gearbox: ratio {2} -> {3}",
            __instance.Block?.Code, __instance.Position, __instance.GearedRatio, corrected);
        __instance.GearedRatio = corrected;
        __instance.Blockentity.MarkDirty();
    }
}
