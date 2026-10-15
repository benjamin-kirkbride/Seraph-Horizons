using System.Collections;
using System.Reflection;
using SeraphHorizons.Mod.Eidolon;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The virtual clock's hold for the eidolon's mine planner (<see cref="VirtualClock.Start"/> adds it by
/// default): game time stands still while any job's plan, or its look-ahead routes, is being made on its
/// worker (<see cref="EidolonMinePlans"/>' <c>Task.Run</c>s), so a plan or a route solve costs no game time
/// however long the runner takes, and the main thread swaps it in on the next pass, every run alike. Read
/// from the planner's private state (its <c>_work</c> list, each entry's <c>Job</c> and <c>Routing</c>
/// tasks); nothing in mods-src changes.
///
/// <para>Not held, nor need they be: the planner's main-thread slices (reading the job's window and its
/// roofs, a few columns at a time), which the virtual clock counts in blocks read instead of
/// <see cref="Mod.Eidolon.Core.MinePlanner.BudgetMs"/> of wall time (<see cref="EidolonMinePlans.BudgetReads"/>),
/// so a plan starts on the same tick on any runner.</para>
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
        var workType = typeof(EidolonMinePlans).GetNestedType("Work", BindingFlags.NonPublic);
        var jobField = workType?.GetField("Job");
        var routingField = workType?.GetField("Routing");
        if (workField?.GetValue(plans) is not IList work || jobField == null || !typeof(Task).IsAssignableFrom(jobField.FieldType)
            || routingField == null || !typeof(Task).IsAssignableFrom(routingField.FieldType))
            throw new NotSupportedException("the virtual clock's mine plan hold reads EidolonMinePlans._work (a list) and its "
                + "Work.Job and Work.Routing (Tasks): one has changed; update MinePlanHold to match");
        // _work is readonly: the list itself is the one to watch
        return () =>
        {
            foreach (var w in work)
                if (jobField.GetValue(w) is Task { IsCompleted: false } || routingField.GetValue(w) is Task { IsCompleted: false })
                    return true;
            return false;
        };
    }
}
