using System.Collections;
using System.Reflection;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The virtual clock's hold for the eidolon's mine planner (<see cref="VirtualClock.Start"/> adds it by
/// default): game time stands still while any job's plan is being made on its worker
/// (<see cref="EidolonMinePlans"/>' <c>Task.Run</c>), so a plan costs no game time however long the runner
/// takes, and the main thread swaps it in on the next pass, every run alike. Read from the planner's private
/// state (its <c>_work</c> list, each entry's <c>Job</c> task); nothing in mods-src changes.
///
/// <para>Not held: the planner's main-thread slices (reading the job's window and its roofs, a few columns
/// at a time), which stop each tick at <see cref="Mod.Eidolon.Core.MinePlanner.BudgetMs"/> of wall time
/// (a const, inlined into a private getter, so the tests cannot widen it). On the virtual clock a slow
/// runner reads fewer columns a tick and so takes more ticks, which is game time, before a plan starts:
/// the one way a plan's latency still varies.</para>
/// </summary>
internal static class MinePlanHold
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    /// <summary>True while any of <paramref name="api"/>'s mine plans is being made off the main thread;
    /// null when the server has no mine planner (the Eidolon switch off).</summary>
    /// <exception cref="NotSupportedException">The planner's private members it reads have changed.</exception>
    public static Func<bool>? For(ICoreServerAPI api)
    {
        if (EidolonMinePlans.Of(api) is not { } plans)
            return null;
        var workField = typeof(EidolonMinePlans).GetField("_work", Private);
        var jobField = typeof(EidolonMinePlans).GetNestedType("Work", BindingFlags.NonPublic)?.GetField("Job");
        if (workField?.GetValue(plans) is not IList work || jobField == null || !typeof(Task).IsAssignableFrom(jobField.FieldType))
            throw new NotSupportedException("the virtual clock's mine plan hold reads EidolonMinePlans._work (a list) and its "
                + "Work.Job (a Task): one has changed; update MinePlanHold to match");
        // _work is readonly: the list itself is the one to watch
        return () =>
        {
            foreach (var w in work)
                if (jobField.GetValue(w) is Task { IsCompleted: false })
                    return true;
            return false;
        };
    }
}
