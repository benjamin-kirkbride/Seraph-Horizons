using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// A diagnostic for walk speed while carrying a trunk: everything that goes into a player's
/// movement speed now, on one side. The client's <c>.trunkspeed</c> (no privilege) prints its own
/// view; the server's <c>/sh trunkspeed</c> (chat privilege) prints the server's view of the
/// caller. Both also write it to that side's log. The game moves a player on the ground by
/// <c>Controls.WalkVector</c> (base speed × <c>Controls.MovespeedMultiplier</c>) ×
/// <c>EntityPlayer.GetWalkSpeedMultiplier()</c>, which is: sneak and sprint factors (from the
/// server controls) × the block under the feet's and the block at the feet's
/// <c>WalkSpeedMultiplier</c> (÷ 2.5 with feet in liquid; the blocks left out in creative) ×
/// <c>EntityPlayer.walkSpeed</c> (the blended <c>walkspeed</c> stat, copied each tick) × the sneak
/// factor again when it cannot stand up. Each of those is printed. A player driving a trunk
/// (mounted on its <see cref="TrunkDriveSeat"/>) does not walk: the trunk moves at its own speed
/// (<see cref="TrunkDrive.Speed"/>) and the seat carries the player along, so the dump then also
/// prints the driven trunk and everything that sets its speed.
/// <para>The client's chat reply has its braces doubled (<see cref="ClientChat"/>): the client's
/// command handler shows a result's message through <c>Lang.Get</c>, which runs it through
/// <c>string.Format</c>, and the dump's JSON (<c>{ "base": 1 }</c>) is no format string. The
/// server's handler sends a message with a line break as it is, so the server's reply is left
/// alone. The logged copies are the dump as it is.</para>
/// </summary>
public class TrunkSpeedProbe : ModSystem
{
    public const string ClientCommand = "trunkspeed";
    public const string ServerSubCommand = "trunkspeed";

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.ChatCommands.Create(ClientCommand)
            .WithDescription("Seraph Horizons: everything in your walk speed now, as this client sees it (for carrying a trunk)")
            .HandleWith(_ =>
            {
                if (api.World.Player?.Entity is not { } entity)
                    return TextCommandResult.Error("no player entity");
                string dump = Dump(api, api.World.Player, entity);
                api.Logger.Notification("[seraphhorizons] .trunkspeed (client):\n{0}", dump);
                return TextCommandResult.Success(ClientChat(dump));
            });
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        var root = api.ChatCommands.GetOrCreate("sh");
        if (string.IsNullOrEmpty(root.Description))
            root.WithDescription("Seraph Horizons commands.");
        root.BeginSubCommand(ServerSubCommand)
                .WithDescription("Everything in your walk speed now, as the server sees it (for carrying a trunk)")
                .RequiresPrivilege(Privilege.chat)
                .RequiresPlayer()
                .HandleWith(args =>
                {
                    if (args.Caller.Player is not { Entity: { } entity } player)
                        return TextCommandResult.Error("no player entity");
                    string dump = Dump(api, player, entity);
                    api.Logger.Notification("[seraphhorizons] /sh trunkspeed for {0} (server):\n{1}", player.PlayerName, dump);
                    // Sent as it is: the server's handler (ChatCommandApi.Execute for a player)
                    // translates and formats a status message only when it is a single line, and
                    // the dump is many, so doubled braces would show doubled.
                    return TextCommandResult.Success(dump);
                })
            .EndSubCommand();
    }

    /// <summary><paramref name="text"/> as a client command's result message. The client's handler
    /// (<c>ChatCommandApi.Execute</c> for an <c>IClientPlayer</c>) shows it as
    /// <c>Lang.Get(message)</c>, which looks it up as a key (none matches, so the text itself) and
    /// formats it with <c>string.Format</c> and no arguments, logging an error for a lone brace.
    /// Doubled braces format back to single ones.</summary>
    public static string ClientChat(string text) => text.Replace("{", "{{").Replace("}", "}}");

    private static string F(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>The dump for <paramref name="entity"/>, as <paramref name="api"/>'s side sees it.</summary>
    public static string Dump(ICoreAPI api, IPlayer player, EntityPlayer entity)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"trunk speed probe, {api.Side} side, {player.PlayerName}");

        // the walkspeed stat
        if (entity.Stats["walkspeed"] is { } stat)
        {
            foreach (var (code, value) in stat.ValuesByKey)
            {
                string who = code == TrunkCarry.SpeedCode ? " (the pack's trunk)"
                    : code.StartsWith("carryon", StringComparison.OrdinalIgnoreCase) ? " (Carry On)" : "";
                sb.AppendLine($"walkspeed {code}{who}: value {F(value.Value)}, weight {F(value.Weight)}, persistent {value.Persistent}");
            }
            sb.AppendLine($"walkspeed blended (BlendType {stat.BlendType}): {F(entity.Stats.GetBlended("walkspeed"))}");
        }
        else
            sb.AppendLine("walkspeed: no stat");
        sb.AppendLine($"EntityPlayer.walkSpeed: {F(entity.walkSpeed)}");

        // controls
        var c = entity.Controls;
        var s = entity.ServerControls;
        sb.AppendLine($"Controls: MovespeedMultiplier {F(c.MovespeedMultiplier)}, Sneak {c.Sneak}, Sprint {c.Sprint}, TriesToMove {c.TriesToMove}, IsFlying {c.IsFlying}, WalkVector {F(c.WalkVector.Length())}");
        sb.AppendLine($"ServerControls: MovespeedMultiplier {F(s.MovespeedMultiplier)}, Sneak {s.Sneak}, Sprint {s.Sprint}, TriesToMove {s.TriesToMove}, IsFlying {s.IsFlying}");
        sb.AppendLine($"WorldData: MoveSpeedMultiplier {F(player.WorldData?.MoveSpeedMultiplier ?? float.NaN)}, game mode {player.WorldData?.CurrentGameMode}");
        sb.AppendLine($"OnGround {entity.OnGround}, FeetInLiquid {entity.FeetInLiquid}, PrevFrameCanStandUp {entity.PrevFrameCanStandUp}, mounted on {entity.MountedOn?.GetType().Name ?? "nothing"}");
        sb.AppendLine($"GlobalConstants: BaseMoveSpeed {F(GlobalConstants.BaseMoveSpeed)}, OverallSpeedMultiplier {F(GlobalConstants.OverallSpeedMultiplier)}, SneakSpeedMultiplier {F(GlobalConstants.SneakSpeedMultiplier)}, SprintSpeedMultiplier {F(GlobalConstants.SprintSpeedMultiplier)}");

        // the blocks GetWalkSpeedMultiplier reads
        var pos = entity.Pos;
        int below = (int)(pos.InternalY - 0.05), at = (int)(pos.InternalY + 0.01);
        var under = entity.World.BlockAccessor.GetBlockRaw((int)pos.X, below, (int)pos.Z);
        var inside = entity.World.BlockAccessor.GetBlock(new Vintagestory.API.MathTools.BlockPos((int)pos.X, at, (int)pos.Z, pos.Dimension));
        sb.AppendLine($"block under feet {under?.Code}: WalkSpeedMultiplier {F(under?.WalkSpeedMultiplier ?? float.NaN)}, DragMultiplier {F(under?.DragMultiplier ?? float.NaN)}");
        sb.AppendLine($"block at feet {inside?.Code}: WalkSpeedMultiplier {F(inside?.WalkSpeedMultiplier ?? float.NaN)} ({(below == at ? "same cell, not counted" : "counted")})");
        double multiplier = entity.GetWalkSpeedMultiplier(0.3);
        sb.AppendLine($"GetWalkSpeedMultiplier(0.3): {F(multiplier)}; × MovespeedMultiplier: {F(multiplier * c.MovespeedMultiplier)}");

        // driving a trunk
        if (entity.MountedOn is TrunkDriveSeat seat)
            DumpDrive(sb, seat);

        // Carry On
        if (TrunkCarry.Available(api))
        {
            var held = TrunkCarry.InHandsAsHeld(entity);
            var real = TrunkCarry.Carried(player);
            sb.AppendLine($"Carry On hands: {held?.Collectible?.Code?.ToString() ?? "nothing"}{(real != null ? $", a trunk of {Trunks.StoredLogs(real, entity.World)} logs ({real.Collectible?.Code})" : "")}");
            if (real != null && TrunkEntitySystem.Of(api).Config is { } config)
                sb.AppendLine($"the pack's carry speed for it: {F(TrunkWeight.CarrySpeed(Trunks.StoredLogs(real, entity.World), config))}");
            sb.AppendLine($"Carry On back: {TrunkCarry.OnBack(entity)?.Collectible?.Code?.ToString() ?? "nothing"}");
        }
        else
            sb.AppendLine("Carry On: not available on this side");

        // anything named speed
        foreach (var (path, value) in Speedy(entity.WatchedAttributes, ""))
            sb.AppendLine($"WatchedAttributes {path}: {value}");
        if (entity.Properties?.Attributes?.Token is JContainer token)
            foreach (var t in token.Descendants().OfType<JProperty>()
                         .Where(p => p.Name.Contains("speed", StringComparison.OrdinalIgnoreCase)))
                sb.AppendLine($"Properties.Attributes {t.Path}: {Short(t.Value.ToString(Newtonsoft.Json.Formatting.None))}");

        // hands
        sb.AppendLine($"active hotbar: {player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "empty"}; offhand: {entity.LeftHandItemSlot?.Itemstack?.Collectible?.Code?.ToString() ?? "empty"}");
        return sb.ToString().TrimEnd();
    }

    // The trunk a player drives: the speed is the trunk's (EntityTrunk.BeforeCollision sets its
    // horizontal motion outright from the seat's keys, TrunkDrive.Speed and Turn eased over
    // TrunkDrive.EaseSeconds; then the step-up and the collision), not the player's walk.
    private static void DumpDrive(StringBuilder sb, TrunkDriveSeat seat)
    {
        if (seat.Entity is not EntityTrunk trunk)
        {
            sb.AppendLine($"driving: a trunk seat on {seat.Entity?.Code?.ToString() ?? "nothing"}, not a trunk");
            return;
        }
        int logs = trunk.Logs;
        bool afloat = trunk.Afloat;
        double speed = TrunkDrive.Speed(logs, afloat), turn = TrunkDrive.Turn(logs, afloat);
        var motion = trunk.Pos.Motion;
        sb.AppendLine($"driving trunk entity {trunk.EntityId} ({trunk.Code}): {trunk.Trunk?.Collectible?.Code?.ToString() ?? "no trunk stack"}, {logs} logs, class {trunk.Class}, boxes {trunk.TypeClass}, drive end {trunk.DriveEnd}");
        sb.AppendLine($"afloat {afloat} (Swimming {trunk.Swimming}, FeetInLiquid {trunk.FeetInLiquid}), OnGround {trunk.OnGround}");
        string floor = afloat ? $", afloat at least {F(TrunkDrive.WaterFloorShare * TrunkDrive.RaftBlocksPerSecond)}" : "";
        sb.AppendLine($"TrunkDrive.Speed({logs}, {afloat}): {F(speed)} blocks/s, {F(speed / TrunkDrive.WalkBlocksPerSecond)} of the walk's {F(TrunkDrive.WalkBlocksPerSecond)} (land share {F(TrunkDrive.Share(logs, TrunkDrive.HeavySpeedShare))}: 1 at 1 log down to {F(TrunkDrive.HeavySpeedShare)} at {TrunkDrive.HeavyLogs}{floor})");
        sb.AppendLine($"TrunkDrive.Turn({logs}, {afloat}): {F(turn)} rad/s");
        var keys = seat.Controls;
        sb.AppendLine($"seat controls: Forward {keys.Forward}, Backward {keys.Backward}, Left {keys.Left}, Right {keys.Right}");
        sb.AppendLine($"eased drive on this side: along {F(trunk.DriveAlong)} blocks/s, turn {F(trunk.DriveTurn)} rad/s (time constant {F(TrunkDrive.EaseSeconds)} s; 0 on a side that does not tick the trunk)");
        sb.AppendLine($"trunk motion: {F(Math.Sqrt(motion.X * motion.X + motion.Z * motion.Z) * 60)} blocks/s horizontal, {F(motion.Y * 60)} vertical");
        string predicting = trunk.Api?.Side == EnumAppSide.Server ? $", client predicting {trunk.ClientPredicting}" : "";
        sb.AppendLine($"physics ticked by: {trunk.Seatable?.Controller?.Code?.ToString() ?? "no controller (the server)"}{predicting}");
    }

    private static string Short(string s) => s.Length > 160 ? s[..160] + "…" : s;

    private static IEnumerable<(string Path, string Value)> Speedy(ITreeAttribute tree, string prefix)
    {
        foreach (var (key, value) in tree)
        {
            string path = prefix + key;
            if (value is ITreeAttribute sub)
            {
                if (key.Contains("speed", StringComparison.OrdinalIgnoreCase))
                    yield return (path, Short(sub.ToJsonToken()));
                else
                    foreach (var inner in Speedy(sub, path + "/"))
                        yield return inner;
            }
            else if (key.Contains("speed", StringComparison.OrdinalIgnoreCase))
                yield return (path, Short(value.ToJsonToken()));
        }
    }
}
