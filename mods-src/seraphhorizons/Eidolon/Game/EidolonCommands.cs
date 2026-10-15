using System.Globalization;
using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Client;
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
/// <item><c>mineplan [dump|draw|clear]</c>: its mining job's look-ahead routes (README "Eidolon", working
/// together, *Routes*): <c>dump</c> each member's stops, the narrow ways and their slots, and the last repairs,
/// to chat and the server log; <c>draw</c> them as outlines for the caller (each member's stands and ways in its
/// colour, its head brighter, the narrow ways yellow); <c>clear</c> the outlines.</item>
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
                .WithDescription("Eidolons (testing): spawn [player] [layer=material ...], fit layer=material ..., come [run], stay, clear, charge <days>, health <hp>, oil <points>, info, mineplan [dump|draw|clear]")
                .RequiresPrivilege(Privilege.controlserver)
                .RequiresPlayer()
                .WithArgs(parsers.Word("spawn|fit|come|stay|clear|charge|health|oil|info|mineplan"), parsers.OptionalAll("argument"))
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
            case "mineplan":
                return MinePlan(api, caller, e, rest.Length == 0 ? "dump" : rest);
            default:
                return TextCommandResult.Success(Describe(e));
        }
    }

    /// <summary>The highlight slot the routes are drawn in (next to the command tool's,
    /// <c>EidolonCommanderSystem.HighlightSlot</c>).</summary>
    public const int MineDebugSlot = 6751;

    private static readonly int[] Palette =
    [
        ColorUtil.ToRgba(160, 60, 120, 255), ColorUtil.ToRgba(160, 255, 120, 60), ColorUtil.ToRgba(160, 80, 220, 80), ColorUtil.ToRgba(160, 220, 80, 220),
        ColorUtil.ToRgba(160, 60, 220, 220), ColorUtil.ToRgba(160, 240, 160, 40), ColorUtil.ToRgba(160, 140, 140, 140), ColorUtil.ToRgba(160, 255, 255, 255),
    ];

    private static TextCommandResult MinePlan(ICoreServerAPI api, IServerPlayer caller, EntityLaborEidolon e, string verb)
    {
        if (verb == "clear")
        {
            api.World.HighlightBlocks(caller, MineDebugSlot, [], [], EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cube);
            return TextCommandResult.Success("Mine plan outlines cleared");
        }
        if (e.Orders?.Current is not MineOrder order || order.Job?.Shared is not { } cells || cells.Plan is not { } plan)
            return TextCommandResult.Error("Not mining");
        double now = EidolonJobSystem.Now(api.World);
        var routes = cells.Routes;
        if (verb == "draw")
        {
            if (routes == null)
                return TextCommandResult.Error("No routes (today's dispatch, or not made yet)");
            var positions = new List<BlockPos>();
            var colours = new List<int>();
            int k = 0;
            foreach (var route in routes.Routes.Values.OrderBy(r => r.Member))
            {
                int colour = Palette[k++ % Palette.Length];
                int faint = (colour & 0x00FFFFFF) | (60 << 24);
                bool first = true;
                foreach (var stop in route.Stops)
                {
                    foreach (var n in stop.Leg)
                    {
                        positions.Add(new BlockPos(n.X, n.Y - 1, n.Z, e.Pos.Dimension));
                        colours.Add(faint);
                    }
                    positions.Add(new BlockPos(stop.At.X, stop.At.Y - 1, stop.At.Z, e.Pos.Dimension));
                    colours.Add(first ? colour | unchecked((int)0xFF000000) : colour);
                    first = false;
                }
            }
            foreach (var zone in routes.Zones)
            foreach (var n in zone.Nodes)
            {
                positions.Add(new BlockPos(n.X, n.Y - 1, n.Z, e.Pos.Dimension));
                colours.Add(ColorUtil.ToRgba(140, 40, 230, 255));
            }
            api.World.HighlightBlocks(caller, MineDebugSlot, positions, colours, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Cube);
            return TextCommandResult.Success($"Drew {routes.Routes.Count} routes and {routes.Zones.Count} narrow ways ({positions.Count} blocks)");
        }
        var s = new StringBuilder();
        s.AppendLine(CultureInfo.InvariantCulture, $"Mining {EidolonMinePlans.Describe(plan.Area)}: plan {cells.PlanVersion}, routes version {cells.RoutesVersion}, routing {(EidolonMinePlans.Routing ? "on" : "off")}");
        if (routes == null)
            s.AppendLine("No routes: today's dispatch (a lone member, routing off, or not made yet)");
        else
        {
            string chosen = routes.Today ? "left to today's dispatch" : $"variant {routes.VariantIndex}";
            s.AppendLine(CultureInfo.InvariantCulture, $"made {now - routes.MadeAt:0.0} s ago for plan {routes.PlanVersion}, {chosen}, J {routes.Score.J:0.0} s, walk {routes.Score.Walk:0.0} s, wait {routes.Score.Wait:0.0} s");
            s.AppendLine(CultureInfo.InvariantCulture, $"stats {routes.Stats}");
            foreach (var route in routes.Routes.Values.OrderBy(r => r.Member))
            {
                s.Append(CultureInfo.InvariantCulture, $"{route.Member}:");
                foreach (var stop in route.Stops)
                    s.Append(stop.Kind == MineStopKind.Wait
                        ? string.Format(CultureInfo.InvariantCulture, " wait at ({0},{1},{2}) until +{3:0.0}", stop.At.X, stop.At.Y, stop.At.Z, stop.Finish - now)
                        : string.Format(CultureInfo.InvariantCulture, " #{0}{1} ({2},{3},{4}) cells {5} depart +{6:0.0} start +{7:0.0} finish +{8:0.0}",
                            stop.Station, stop.Ahead ? " ahead" : "", stop.At.X, stop.At.Y, stop.At.Z, stop.Cells.Count, stop.Depart - now, stop.Start - now, stop.Finish - now));
                s.AppendLine();
            }
            foreach (var zone in routes.Zones)
            {
                s.Append(CultureInfo.InvariantCulture, $"zone {zone.Id} {zone.Kind}, {zone.Nodes.Count} nodes from ({zone.Key.X},{zone.Key.Y},{zone.Key.Z}):");
                foreach (var slot in routes.Slots.Of(zone.Id))
                    s.Append(CultureInfo.InvariantCulture, $" {slot.Member} +{slot.From - now:0.0}..{(double.IsPositiveInfinity(slot.To) ? "on" : $"+{slot.To - now:0.0}")}");
                s.AppendLine();
            }
            foreach (var line in routes.Log.TakeLast(10))
                s.AppendLine(line);
        }
        string text = s.ToString().TrimEnd();
        api.Logger.Notification("[eidolon-mine] mineplan\n{0}", text.Replace("{", "(").Replace("}", ")"));
        return TextCommandResult.Success(text);
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
