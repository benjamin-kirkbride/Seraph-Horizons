using SeraphHorizons.Mod.Trading.Commands;
using SeraphHorizons.Mod.Trading.Visitors.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Visitors;

/// <summary>
/// <c>/sh trade inn ...</c> (privilege controlserver), travelling merchants' admin commands:
/// <list type="bullet">
/// <item><c>check [pos]</c>: the inn around a position (the caller's by default; the nearest inn
/// flag within <see cref="InnSystem.CommandRadius"/> if there is one): each rule, the conditions,
/// and the inn's visit.</item>
/// <item><c>call &lt;general|curio&gt; [now]</c>: a visitor to the nearest inn, skipping the
/// conditions and the cooldown: on its way (arriving in 2–4 days), or with <c>now</c> (or
/// <c>--now</c>) here at once.</item>
/// <item><c>dismiss</c>: the nearest inn's visitor leaves (the cooldown starts), or its pending
/// visit is called off.</item>
/// </list>
/// </summary>
public static class InnCommands
{
    public static void Register(ICoreServerAPI api, InnSystem system)
    {
        if (TradeCommands.Trade is not { } trade)
        {
            api.Logger.Warning("[seraphhorizons] Travelling merchants: no /sh trade command to add to; no inn commands");
            return;
        }
        var parsers = api.ChatCommands.Parsers;
        trade.BeginSubCommand("inn")
                .WithDescription("Travelling merchants at player-built inns")
                .RequiresPrivilege(Privilege.controlserver)
                .BeginSubCommand("check")
                    .WithDescription("Check the inn around a position (default: yours) against every rule and condition")
                    .RequiresPrivilege(Privilege.controlserver)
                    .WithArgs(parsers.OptionalWorldPosition("pos"))
                    .HandleWith(args => Check(api, system, args))
                .EndSubCommand()
                .BeginSubCommand("call")
                    .WithDescription("Call a travelling merchant (general) or curio dealer (curio) to the nearest inn; 'now' brings it at once")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .WithArgs(parsers.Word("kind", [.. VisitorKinds.All]), parsers.OptionalWord("now"))
                    .HandleWith(args => Call(api, system, args))
                .EndSubCommand()
                .BeginSubCommand("dismiss")
                    .WithDescription("Send the nearest inn's visitor away, or call off its pending visit")
                    .RequiresPrivilege(Privilege.controlserver)
                    .RequiresPlayer()
                    .HandleWith(args => Dismiss(api, system, args))
                .EndSubCommand()
            .EndSubCommand();
    }

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    private static BlockPos Origin(ICoreServerAPI api, TextCommandCallingArgs args, Vec3d? given = null) =>
        (given ?? args.Caller.Entity?.Pos.XYZ ?? api.World.DefaultSpawnPosition.XYZ).AsBlockPos;

    private static InnRecord? NearestInn(InnSystem system, BlockPos at) =>
        system.Book.Nearest(new InnPos(at.X, at.Y, at.Z), InnSystem.CommandRadius);

    private static TextCommandResult Check(ICoreServerAPI api, InnSystem system, TextCommandCallingArgs args)
    {
        var at = Origin(api, args, args[0] as Vec3d);
        var record = NearestInn(system, at);
        var origin = record is null ? at : new BlockPos(record.X, record.Y, record.Z);
        string owner = record?.Owner is { Length: > 0 } o ? o : args.Caller.Player?.PlayerUID ?? "";
        var eval = system.Evaluate(origin, owner);
        var lines = new List<string> { L(eval.PassingKinds.Count > 0 ? "trading-inn-header-ready" : "trading-inn-header-notready", system.Rel(origin)) };
        foreach (var c in eval.Report.Checks)
            lines.Add(L("trading-inn-line", c.Passed ? "+" : "-", L("trading-inn-rulename-" + c.Rule.ToString().ToLowerInvariant()),
                L("trading-inn-rule-" + c.Reason, c.At is { } p ? system.Rel(p) : "", c.Value, system.Rules.MinLight)));
        var s = eval.Standing;
        lines.Add(L("trading-inn-line", s.Passed ? "+" : "-", L("trading-inn-rulename-standing"),
            s.Skipped ? L("trading-inn-standing-skipped")
            : owner.Length == 0 ? L("trading-inn-standing-noowner")
            : s.CampId is null ? L("trading-inn-standing-nocamp", system.Conditions.CampRadius)
            : L("trading-inn-standing", s.BestTier, s.CampId, s.Camps, system.Conditions.MinTier)));
        foreach (var (kind, supply) in eval.Supply)
            lines.Add(L("trading-inn-line", supply.Passed ? "+" : "-", L("trading-inn-rulename-supply"),
                supply.Skipped ? L("trading-inn-supply-skipped", InnSystem.KindName(kind))
                : L("trading-inn-supply", InnSystem.KindName(kind), Math.Round(supply.Level, 2), system.Conditions.MinSupply)));
        lines.Add(record is null ? L("trading-inn-state-noflag", InnSystem.CommandRadius) : State(system, record));
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    private static string State(InnSystem system, InnRecord r) => r.Phase switch
    {
        InnPhase.Pending => L("trading-inn-state-pending", InnSystem.KindName(r.Kind), Math.Round(r.ArriveDay - system.Now, 1)),
        InnPhase.Visiting => L("trading-inn-state-visiting", InnSystem.KindName(r.Kind), Math.Round(r.LeaveDay - system.Now, 1)),
        InnPhase.Cooldown => L("trading-inn-state-cooldown", Math.Round(r.CooldownUntil - system.Now, 1)),
        _ => L("trading-inn-state-idle"),
    };

    private static TextCommandResult Call(ICoreServerAPI api, InnSystem system, TextCommandCallingArgs args)
    {
        string kind = (string)args[0];
        if (!VisitorKinds.All.Contains(kind)) return TextCommandResult.Error(L("trading-inn-badkind", kind));
        string? now = args[1] as string;
        if (now is { Length: > 0 } && now.TrimStart('-') != "now") return TextCommandResult.Error(L("trading-inn-badnow", now));
        if (NearestInn(system, Origin(api, args)) is not { } record)
            return TextCommandResult.Error(L("trading-inn-noflag", InnSystem.CommandRadius));
        var pos = new BlockPos(record.X, record.Y, record.Z);
        if (record.Phase == InnPhase.Visiting) system.Dismiss(record);
        VisitPlanner.Schedule(record, kind, system.Now, api.World.Rand.NextDouble(), system.Visits);
        if (now is not { Length: > 0 })
            return TextCommandResult.Success(L("trading-inn-called", InnSystem.KindName(kind), system.Rel(pos), Math.Round(record.ArriveDay - system.Now, 1)));
        return system.Arrive(record, out string error)
            ? TextCommandResult.Success(L("trading-inn-callednow", InnSystem.KindName(kind), system.Rel(pos), record.EntityId))
            : TextCommandResult.Error(L("trading-inn-spawnfailed", InnSystem.KindName(kind), error));
    }

    private static TextCommandResult Dismiss(ICoreServerAPI api, InnSystem system, TextCommandCallingArgs args)
    {
        if (NearestInn(system, Origin(api, args)) is not { } record)
            return TextCommandResult.Error(L("trading-inn-noflag", InnSystem.CommandRadius));
        var pos = system.Rel(record.Pos);
        var phase = record.Phase;
        string kind = record.Kind;
        if (!system.Dismiss(record)) return TextCommandResult.Error(L("trading-inn-nothing", pos));
        return TextCommandResult.Success(phase == InnPhase.Visiting
            ? L("trading-inn-dismissed", InnSystem.KindName(kind), pos)
            : L("trading-inn-cancelled", pos));
    }
}
