using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// <c>/sh eidolon</c> (privilege controlserver), for testing before the gantry and the command tool
/// exist, on the eidolon nearest the caller within 64 blocks:
/// <list type="bullet">
/// <item><c>spawn [player] [layer=material ...]</c>: one 4 blocks in front of the caller, owned by
/// the caller (or the named player), with a gear's charge, waking (<c>activate</c>), fitted as the
/// old all-steel build with the layers named changed (<see cref="EidolonBuildSpec"/>, e.g.
/// <c>plates=brass sash=cloth-red</c>, <c>all=stainless</c>).</item>
/// <item><c>fit layer=material ...</c>: changes its fittings so (its look and figures follow).</item>
/// <item><c>come [run]</c>: it walks (or runs) to where the caller stands, by the wide pathfinder.</item>
/// <item><c>stay</c>, <c>clear</c>: the stay order; no order.</item>
/// <item><c>charge &lt;days&gt;</c>: sets its charge.</item>
/// <item><c>health &lt;hp&gt;</c>: sets its health (0 slumps it).</item>
/// <item><c>oil &lt;points&gt;</c>: sets its oil (0 leaves it dry).</item>
/// <item><c>info</c>: its state.</item>
/// </list>
/// </summary>
public static class EidolonCommands
{
    private const double Reach = 64;

    public static void Register(ICoreServerAPI api, EidolonSystem system)
    {
        var sh = api.ChatCommands.GetOrCreate("sh");
        if (string.IsNullOrEmpty(sh.Description))
            sh.WithDescription("Seraph Horizons commands");
        var parsers = api.ChatCommands.Parsers;
        sh.BeginSubCommand("eidolon")
                .WithDescription("Eidolons (testing): spawn [player] [layer=material ...], fit layer=material ..., come [run], stay, clear, charge <days>, health <hp>, oil <points>, info")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.Word("spawn|fit|come|stay|clear|charge|health|oil|info"), parsers.OptionalAll("argument"))
                .HandleWith(args => Run(api, system, args))
            .EndSubCommand();
    }

    private static TextCommandResult Run(ICoreServerAPI api, EidolonSystem system, TextCommandCallingArgs args)
    {
        var caller = (IServerPlayer)args.Caller.Player;
        var at = caller.Entity.Pos.XYZ;
        string verb = (string)args[0];
        string rest = ((args[1] as string) ?? "").Trim();
        if (verb == "spawn")
        {
            var owner = caller as IPlayer;
            var words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string name = string.Join(' ', words.Where(w => !EidolonBuildSpec.IsSpecWord(w)));
            if (name.Length > 0)
            {
                owner = api.World.AllPlayers.FirstOrDefault(p => string.Equals(p.PlayerName, name, StringComparison.OrdinalIgnoreCase));
                if (owner == null)
                    return TextCommandResult.Error($"No player {name}");
            }
            if (EidolonBuildSpec.Parse(words.Where(EidolonBuildSpec.IsSpecWord), EidolonBuild.OldBuild, out string? specError) is not { } build)
                return TextCommandResult.Error(specError ?? "Bad build");
            float yaw = caller.Entity.Pos.Yaw;
            var pos = at.AddCopy(Math.Sin(yaw) * 4, 0, Math.Cos(yaw) * 4);
            var spawned = system.Spawn(api.World, pos, yaw + GameMath.PI, owner, activate: true, build: build);
            return spawned == null
                ? TextCommandResult.Error("Could not spawn an eidolon")
                : TextCommandResult.Success($"Eidolon {spawned.EntityId} spawned at {Pos(pos)}, owned by {owner?.PlayerName}, {build}");
        }
        if (EidolonSystem.Near(api, at, Reach).FirstOrDefault() is not { } e)
            return TextCommandResult.Error($"No eidolon within {Reach} blocks");
        var orders = e.Orders;
        switch (verb)
        {
            case "fit":
                if (EidolonBuildSpec.Parse(rest.Split(' ', StringSplitOptions.RemoveEmptyEntries), e.Build, out string? fitError) is not { } fitted)
                    return TextCommandResult.Error(fitError ?? "fit layer=material ...");
                e.Build = fitted;
                return TextCommandResult.Success(Describe(e));
            case "come":
                orders?.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(at, rest == "run"));
                return TextCommandResult.Success($"Eidolon {e.EntityId} is coming to {Pos(at)}");
            case "stay":
                orders?.SetOrder(StayOrder.OrderCode);
                return TextCommandResult.Success($"Eidolon {e.EntityId} stays");
            case "clear":
                orders?.ClearOrder();
                return TextCommandResult.Success($"Eidolon {e.EntityId} has no order");
            case "charge":
                if (!double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double days) || days < 0)
                    return TextCommandResult.Error("charge <days>");
                if (e.GetBehavior<EntityBehaviorEidolonCharge>() is { } charge)
                    charge.ChargeDays = days;
                e.Check();
                return TextCommandResult.Success($"Eidolon {e.EntityId} has {days} days of charge");
            case "health":
                if (!float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out float hp) || hp < 0)
                    return TextCommandResult.Error("health <hp>");
                if (e.GetBehavior<EntityBehaviorHealth>() is { } health)
                    health.Health = Math.Min(hp, health.MaxHealth);
                e.Check();
                return TextCommandResult.Success($"Eidolon {e.EntityId} has {hp} HP");
            case "oil":
                if (!double.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out double points) || points < 0)
                    return TextCommandResult.Error("oil <points>");
                if (e.Oil?.Tank == null)
                    return TextCommandResult.Error("It has no oil reservoir (MachineOil is off)");
                e.Oil.SetPoints(points);
                return TextCommandResult.Success($"Eidolon {e.EntityId} has {e.Oil.Tank?.Points:0.#} points of oil");
            default:
                return TextCommandResult.Success(Describe(e));
        }
    }

    /// <summary>One line per fact, for <c>info</c> and the scenarios' failures.</summary>
    public static string Describe(EntityLaborEidolon e)
    {
        var s = new StringBuilder();
        var health = e.GetBehavior<EntityBehaviorHealth>();
        var charge = e.GetBehavior<EntityBehaviorEidolonCharge>();
        s.AppendLine(CultureInfo.InvariantCulture, $"Eidolon {e.EntityId} at {Pos(e.Pos.XYZ)}, owner {e.OwnerName ?? "none"}");
        s.AppendLine(CultureInfo.InvariantCulture, $"health {health?.Health:0.#}/{health?.MaxHealth:0.#}, charge {charge?.ChargeDays:0.###} days");
        var stats = e.FittedStats;
        s.AppendLine(CultureInfo.InvariantCulture, $"fittings {e.Build}");
        s.AppendLine(CultureInfo.InvariantCulture,
            $"walk {stats.WalkSpeed:0.####}, run {stats.RunSpeed:0.####}, blows {stats.DefenceDamage:0.#}/{stats.SlamDamage:0.#}, armour {(stats.Armour is { } a ? $"tier {a.ProtectionTier} {a.RelativeProtection:0.##} {a.FlatDamageReduction:0.#}" : "none")}");
        s.AppendLine(CultureInfo.InvariantCulture, $"oil {(e.Oil?.Tank is { } tank ? $"{tank.Points:0.#}/{tank.Capacity:0.#}" : "none")}");
        s.AppendLine(CultureInfo.InvariantCulture, $"pose {e.Pose.State}, stop {e.Stop?.Code ?? "none"}, can work {e.CanWork}");
        s.Append(CultureInfo.InvariantCulture, $"order {e.Orders?.OrderCode ?? "none"}, walking {e.TaskAi?.PathTraverser?.Active}");
        return s.ToString();
    }

    private static string Pos(Vec3d p) => string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} {2:0.#}", p.X, p.Y, p.Z);
}
