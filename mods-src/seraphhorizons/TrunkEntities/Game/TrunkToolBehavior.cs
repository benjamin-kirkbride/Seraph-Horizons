using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// On every knife, shears, axe and saw, and Immersive Woodworking's bark spud, while trunk entities
/// run (<see cref="TrunkToolsSystem"/>): a right-click hold on a trunk entity works it as
/// <see cref="TrunkHarvest"/> says. The hold is taken at its start (<see cref="EnumHandHandling.PreventDefault"/>),
/// steps until its time is up, and does the work when it ends at that time (server); a hold let
/// go early, or cancelled, does nothing. A refused one (branches first, already debarked) says why
/// and idles until let go. Anything else passes through, so the tools keep every other use.
///
/// The client shows the hold as Logging Expanded does on a placed trunk: the held item's swing on
/// each step and the game's hold progress bar. The spud plays Immersive Woodworking's debarking
/// animation while held.
///
/// Some tools' classes override the held interaction without calling the behaviours (the game's
/// knife does for its steps), so <see cref="TrunkToolsSystem"/> also prefixes each override on
/// those classes with <see cref="Hooks"/>, which call the same methods here.
/// </summary>
public class TrunkToolBehavior(CollectibleObject collObj) : CollectibleBehavior(collObj)
{
    public const string ClassName = "seraphhorizons.TrunkTool";

    public const string SpudAnimation = Woodworking.SplittingBlockUpgrades.DebarkSpudAnimation;

    /// <summary>A hold Logging Expanded would not start again so soon after the last completed
    /// one, ms (its <c>HarvestHoldInteraction</c>'s pause, so a held button repeats the work at
    /// a pace).</summary>
    public const long RepeatPauseMs = 300;

    private sealed class Hold(long trunkId, TrunkTool tool, float seconds, bool refused)
    {
        public long TrunkId { get; } = trunkId;
        public TrunkTool Tool { get; } = tool;
        public float Seconds { get; } = seconds;
        public bool Refused { get; } = refused;
        public IProgressBar? Bar { get; set; }
    }

    // Per side: each side has its own entity objects.
    private static readonly ConditionalWeakTable<EntityAgent, Hold> Holds = new();
    private static readonly ConditionalWeakTable<EntityAgent, StrongBox<long>> LastDone = new();

    /// <summary>The tool this behaviour's collectible is.</summary>
    public TrunkTool Tool { get; private set; } = TrunkHarvest.ToolOf(collObj);

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        Tool = TrunkHarvest.ToolOf(collObj);
    }

    /// <summary>Whether <paramref name="byEntity"/> holds a trunk tool hold now.</summary>
    public static bool Holding(EntityAgent byEntity) => Holds.TryGetValue(byEntity, out _);

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel,
                                             bool firstEvent, ref EnumHandHandling handHandling, ref EnumHandling handling)
    {
        if (entitySel?.Entity is not EntityTrunk { Alive: true } trunk || byEntity is not EntityPlayer { Player: { } player })
            return;
        var api = byEntity.World.Api;
        var system = TrunkEntitySystem.Of(api);
        if (!system.Enabled || system.Logging is not { } logging)
            return;
        var tool = TrunkHarvest.ToolOf(slot.Itemstack?.Collectible);
        if (TrunkHarvest.PlanFor(tool, trunk, logging, system.Config) is not { } plan)
            return;
        // The client decides whether a held button starts again; the server follows it.
        if (!plan.Refused && byEntity.World.Side == EnumAppSide.Client && LastDone.TryGetValue(byEntity, out var last)
            && Environment.TickCount64 < last.Value + RepeatPauseMs)
            return;
        Stop(byEntity);
        Holds.AddOrUpdate(byEntity, new Hold(trunk.EntityId, tool, plan.Seconds, plan.Refused));
        if (plan.Refused)
        {
            if (firstEvent)
                (player as IServerPlayer)?.SendIngameError("seraphhorizons-trunktool", Lang.Get(plan.Error!));
        }
        else if (tool == TrunkTool.Spud)
            byEntity.StartAnimation(SpudAnimation);
        handHandling = EnumHandHandling.PreventDefault;
        handling = EnumHandling.PreventSubsequent;
    }

    public override bool OnHeldInteractStep(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
                                            EntitySelection entitySel, ref EnumHandling handling)
    {
        if (!Holds.TryGetValue(byEntity, out var hold))
            return true;
        handling = EnumHandling.PreventSubsequent;
        if (hold.Refused)
            return true;
        var world = byEntity.World;
        // The client ends the hold when the player looks off the trunk; the server follows its stop.
        if (world.GetEntityById(hold.TrunkId) is not EntityTrunk { Alive: true }
            || (world.Side == EnumAppSide.Client && entitySel?.Entity?.EntityId != hold.TrunkId))
        {
            ClearBar(world, hold);
            return false;
        }
        if (world.Side == EnumAppSide.Client)
        {
            ((byEntity as EntityPlayer)?.Player as IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemAttack);
            if (hold.Bar == null && world.Api.ModLoader.GetModSystem<ModSystemProgressBar>() is { } bars)
                hold.Bar = bars.AddProgressbar();
            if (hold.Bar != null)
                hold.Bar.Progress = Math.Clamp(secondsUsed / Math.Max(hold.Seconds, 0.01f), 0f, 1f);
        }
        return secondsUsed < hold.Seconds;
    }

    public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
                                            EntitySelection entitySel, ref EnumHandling handling)
    {
        if (!Holds.TryGetValue(byEntity, out var hold))
            return;
        handling = EnumHandling.PreventSubsequent;
        Stop(byEntity);
        if (hold.Refused || !TrunkHarvest.IsDone(secondsUsed, hold.Seconds))
            return;
        LastDone.AddOrUpdate(byEntity, new StrongBox<long>(Environment.TickCount64));
        var world = byEntity.World;
        if (world.Side != EnumAppSide.Server || byEntity is not EntityPlayer { Player: { } player }
            || world.GetEntityById(hold.TrunkId) is not EntityTrunk trunk
            || TrunkHarvest.ToolOf(slot.Itemstack?.Collectible) != hold.Tool)
            return;
        var system = TrunkEntitySystem.Of(world.Api);
        if (system.Logging is { } logging)
            TrunkHarvest.Apply(hold.Tool, trunk, player, slot, logging, system.Config, TrunkToolsSystem.Of(world.Api).BarkBound);
    }

    // A cancelled hold may always end; the game then calls the stop, which does nothing short of time.
    public override bool OnHeldInteractCancel(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
                                              EntitySelection entitySel, EnumItemUseCancelReason cancelReason, ref EnumHandling handled)
    {
        if (!Holds.TryGetValue(byEntity, out var hold))
            return true;
        handled = EnumHandling.PreventSubsequent;
        ClearBar(byEntity.World, hold);
        byEntity.StopAnimation(SpudAnimation);
        return true;
    }

    /// <summary>Ends <paramref name="byEntity"/>'s hold, if any: its bar and animation go.</summary>
    private static void Stop(EntityAgent byEntity)
    {
        if (!Holds.TryGetValue(byEntity, out var hold))
            return;
        Holds.Remove(byEntity);
        ClearBar(byEntity.World, hold);
        if (hold.Tool == TrunkTool.Spud)
            byEntity.StopAnimation(SpudAnimation);
    }

    private static void ClearBar(IWorldAccessor world, Hold hold)
    {
        if (hold.Bar != null)
            world.Api.ModLoader.GetModSystem<ModSystemProgressBar>()?.RemoveProgressbar(hold.Bar);
        hold.Bar = null;
    }

    /// <summary>
    /// Harmony prefixes on a tool class's own overrides of the held interaction (by position:
    /// slot, byEntity, blockSel, entitySel, ...): each runs this collectible's behaviour first,
    /// and skips the override when the behaviour took the hold. A prefix that finds the original
    /// already skipped does nothing (in singleplayer both sides patch the same method).
    /// </summary>
    public static class Hooks
    {
        private static TrunkToolBehavior? Of(CollectibleObject collectible) => collectible.GetCollectibleBehavior<TrunkToolBehavior>(true);

        public static bool StartPrefix(CollectibleObject __instance, ItemSlot __0, EntityAgent __1, BlockSelection __2,
                                       EntitySelection __3, bool __4, ref EnumHandHandling __5, bool __runOriginal)
        {
            if (!__runOriginal || __3?.Entity is not EntityTrunk || Of(__instance) is not { } behavior)
                return true;
            var handHandling = __5;
            var handling = EnumHandling.PassThrough;
            behavior.OnHeldInteractStart(__0, __1, __2, __3, __4, ref handHandling, ref handling);
            if (handling == EnumHandling.PassThrough)
                return true;
            __5 = handHandling;
            return false;
        }

        public static bool StepPrefix(CollectibleObject __instance, float __0, ItemSlot __1, EntityAgent __2, BlockSelection __3,
                                      EntitySelection __4, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal || !Holding(__2) || Of(__instance) is not { } behavior)
                return true;
            var handling = EnumHandling.PassThrough;
            __result = behavior.OnHeldInteractStep(__0, __1, __2, __3, __4, ref handling);
            return handling == EnumHandling.PassThrough;
        }

        public static bool StopPrefix(CollectibleObject __instance, float __0, ItemSlot __1, EntityAgent __2, BlockSelection __3,
                                      EntitySelection __4, bool __runOriginal)
        {
            if (!__runOriginal || !Holding(__2) || Of(__instance) is not { } behavior)
                return true;
            var handling = EnumHandling.PassThrough;
            behavior.OnHeldInteractStop(__0, __1, __2, __3, __4, ref handling);
            return handling == EnumHandling.PassThrough;
        }

        public static bool CancelPrefix(CollectibleObject __instance, float __0, ItemSlot __1, EntityAgent __2, BlockSelection __3,
                                        EntitySelection __4, EnumItemUseCancelReason __5, ref bool __result, bool __runOriginal)
        {
            if (!__runOriginal || !Holding(__2) || Of(__instance) is not { } behavior)
                return true;
            var handling = EnumHandling.PassThrough;
            __result = behavior.OnHeldInteractCancel(__0, __1, __2, __3, __4, __5, ref handling);
            return handling == EnumHandling.PassThrough;
        }
    }
}
