using System.Globalization;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// <c>/sh eidolon</c> (privilege controlserver), for testing before the gantry and the command tool
/// exist, on the eidolon nearest the caller within 64 blocks:
/// <list type="bullet">
/// <item><c>spawn [player]</c>: one 4 blocks in front of the caller, owned by the caller (or the
/// named player), with a gear's charge, waking (<c>activate</c>).</item>
/// <item><c>come [run]</c>: it walks (or runs) to where the caller stands, by the wide pathfinder.</item>
/// <item><c>stay</c>, <c>clear</c>: the stay order; no order.</item>
/// <item><c>charge &lt;days&gt;</c>: sets its charge.</item>
/// <item><c>health &lt;hp&gt;</c>: sets its health (0 slumps it).</item>
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
                .WithDescription("Eidolons (testing): spawn [player], come [run], stay, clear, charge <days>, health <hp>, info")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.Word("spawn|come|stay|clear|charge|health|info"), parsers.OptionalAll("argument"))
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
            if (rest.Length > 0)
            {
                owner = api.World.AllPlayers.FirstOrDefault(p => string.Equals(p.PlayerName, rest, StringComparison.OrdinalIgnoreCase));
                if (owner == null)
                    return TextCommandResult.Error($"No player {rest}");
            }
            float yaw = caller.Entity.Pos.Yaw;
            var pos = at.AddCopy(Math.Sin(yaw) * 4, 0, Math.Cos(yaw) * 4);
            var spawned = system.Spawn(api.World, pos, yaw + GameMath.PI, owner, activate: true);
            return spawned == null
                ? TextCommandResult.Error("Could not spawn an eidolon")
                : TextCommandResult.Success($"Eidolon {spawned.EntityId} spawned at {Pos(pos)}, owned by {owner?.PlayerName}");
        }
        if (EidolonSystem.Near(api, at, Reach).FirstOrDefault() is not { } e)
            return TextCommandResult.Error($"No eidolon within {Reach} blocks");
        var orders = e.Orders;
        switch (verb)
        {
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
        s.AppendLine(CultureInfo.InvariantCulture, $"pose {e.Pose.State}, stop {e.Stop?.Code ?? "none"}, can work {e.CanWork}");
        s.Append(CultureInfo.InvariantCulture, $"order {e.Orders?.OrderCode ?? "none"}, walking {e.TaskAi?.PathTraverser?.Active}");
        return s.ToString();
    }

    private static string Pos(Vec3d p) => string.Format(CultureInfo.InvariantCulture, "{0:0.#} {1:0.#} {2:0.#}", p.X, p.Y, p.Z);
}
