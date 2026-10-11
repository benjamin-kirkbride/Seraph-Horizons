using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// Creatures lose an eidolon that is down at 0 HP (<see cref="EntityLaborEidolon.Downed"/>): it is
/// never killed, so it stays <c>Alive</c>, and without this anything it had fought kept on at it, and
/// anything targeting by its tags still picked it. It cannot be made not <c>IsInteractable</c> instead
/// (the game's other "ignore me" for AI targeting), since that also hides it from players' clicks,
/// and repair is a click.
///
/// <para>Two halves. Every vanilla targeting task (both families, <see cref="AiTaskBaseTargetable"/>
/// and <see cref="AiTaskBaseTargetableR"/>) decides whether it may target something, whether found or
/// the attacker it retaliates on, through <c>CanSense</c>, which every override calls first: a postfix
/// there makes a downed eidolon unsensed, so nothing picks it up again while it is down. And what is
/// already going for it is stopped (<see cref="DropTargetsOf"/>), from the eidolon's own checks while
/// it is down. Server side, patched while eidolons are on.</para>
/// </summary>
public static class EidolonDownedTargeting
{
    public const string HarmonyId = "seraphhorizons.eidolondowned";

    /// <summary>How far round a downed eidolon creatures are made to let go of it, in blocks: past any
    /// vanilla seeking range.</summary>
    public const float DropRange = 48;

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(AiTaskBaseTargetable), nameof(AiTaskBaseTargetable.CanSense)),
            postfix: new HarmonyMethod(typeof(EidolonDownedTargeting), nameof(CanSensePostfix)));
        harmony.Patch(AccessTools.Method(typeof(AiTaskBaseTargetableR), "CanSense"),
            postfix: new HarmonyMethod(typeof(EidolonDownedTargeting), nameof(CanSenseRPostfix)));
    }

    private static void CanSensePostfix(Entity e, ref bool __result)
    {
        if (__result && e is EntityLaborEidolon { Downed: true })
            __result = false;
    }

    private static void CanSenseRPostfix(Entity target, ref bool __result)
    {
        if (__result && target is EntityLaborEidolon { Downed: true })
            __result = false;
    }

    /// <summary>Whether <paramref name="task"/> is a targeting task aimed at <paramref name="target"/>.</summary>
    public static bool Targets(IAiTask? task, Entity target) => task switch
    {
        AiTaskBaseTargetable t => t.TargetEntity == target,
        AiTaskBaseTargetableR t => t.TargetEntity == target,
        _ => false,
    };

    /// <summary>Stops every running task of a creature within <see cref="DropRange"/> that is aimed at
    /// <paramref name="eidolon"/> (server side). Returns how many it stopped.</summary>
    public static int DropTargetsOf(EntityLaborEidolon eidolon)
    {
        int stopped = 0;
        foreach (var other in eidolon.World.GetEntitiesAround(eidolon.Pos.XYZ, DropRange, DropRange,
                     e => e != eidolon && e.Alive && e.Pos.Dimension == eidolon.Pos.Dimension))
        {
            if (other.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager is not { } tasks)
                continue;
            var slots = tasks.ActiveTasksBySlot;
            for (int slot = 0; slot < slots.Length; slot++)
            {
                if (!Targets(slots[slot], eidolon))
                    continue;
                tasks.StopTask(slot);
                stopped++;
            }
        }
        return stopped;
    }
}
