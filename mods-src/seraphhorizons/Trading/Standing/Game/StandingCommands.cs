using System.Globalization;
using SeraphHorizons.Mod.Trading.Commands;
using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Standing;

/// <summary>
/// Standing commands.
/// <list type="bullet">
/// <item><c>/sh company [group]</c> (any player): shows your company, or makes one of your groups it.</item>
/// <item><c>/sh trade standing &lt;player&gt; [trader]</c> (controlserver): a player's standing with
/// every trader they have a record with (personal and company), or with one in detail.</item>
/// <item><c>/sh trade standing set &lt;player&gt; &lt;trader&gt; &lt;points&gt;</c>,
/// <c>/sh trade standing reset &lt;player&gt; [trader]</c>: a player's own record.</item>
/// <item><c>/sh trade company &lt;player&gt; [group]</c>: a player's company and its records, or
/// makes one of their groups their company.</item>
/// </list>
/// A trader is its id (<c>camp:x,z</c>, <c>entity:n</c>, as the views list them) or <c>near</c>, the
/// pack's trader nearest the caller within 10 blocks. The game parses a command's arguments before
/// its subcommands, so <c>set</c> and <c>reset</c> are read from the first argument.
/// </summary>
public static class StandingCommands
{
    private const double NearRange = 10;

    private static string L(string key, params object[] args) => Lang.Get("seraphhorizons:" + key, args);

    public static void Register(ICoreServerAPI api, StandingSystem system)
    {
        var parsers = api.ChatCommands.Parsers;
        var sh = api.ChatCommands.GetOrCreate("sh");
        if (string.IsNullOrEmpty(sh.Description)) sh.WithDescription("Seraph Horizons commands").RequiresPrivilege(Privilege.controlserver);
        sh.BeginSubCommand("company")
            .WithDescription("Show your company, or make one of your groups your company (its standing with traders is pooled)")
            .RequiresPrivilege(Privilege.chat)
            .RequiresPlayer()
            .WithArgs(parsers.OptionalWord("group"))
            .HandleWith(args => PlayerCompany(api, system, args))
        .EndSubCommand();
        var trade = TradeCommands.Trade ?? sh.BeginSubCommand("trade").WithDescription("Traders").RequiresPrivilege(Privilege.controlserver);
        trade.BeginSubCommand("standing")
                .WithDescription("A player's standing with traders: /sh trade standing <player> [trader], set <player> <trader> <points>, reset <player> [trader]")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("player|set|reset"), parsers.OptionalAll("arguments"))
                .HandleWith(args => Standing(api, system, args))
            .EndSubCommand()
            .BeginSubCommand("company")
                .WithDescription("A player's company and its standing, or make one of their groups their company")
                .RequiresPrivilege(Privilege.controlserver)
                .WithArgs(parsers.Word("player"), parsers.OptionalWord("group"))
                .HandleWith(args => AdminCompany(api, system, args))
            .EndSubCommand();
    }

    private static TextCommandResult PlayerCompany(ICoreServerAPI api, StandingSystem system, TextCommandCallingArgs args)
    {
        string uid = args.Caller.Player.PlayerUID;
        if (args[0] is string name && name.Length > 0)
        {
            var group = api.Groups.GetPlayerGroupByName(name);
            if (group is null || system.Designate(uid, group.Uid) is null)
                return TextCommandResult.Error(L("trading-company-notmember", name));
            return TextCommandResult.Success(L("trading-company-set", group.Name));
        }
        return TextCommandResult.Success(CompanyLine(api, system, uid, args.Caller.Player.PlayerName));
    }

    private static string CompanyLine(ICoreServerAPI api, StandingSystem system, string uid, string playerName)
    {
        int? company = system.CompanyOf(uid);
        return company is int c ? L("trading-company-is", playerName, GroupName(api, c)) : L("trading-company-none", playerName);
    }

    private static string GroupName(ICoreServerAPI api, int uid) =>
        api.Groups.PlayerGroupsById.TryGetValue(uid, out var g) ? g.Name : "#" + uid;

    private static IServerPlayerData? FindPlayer(ICoreServerAPI api, string name) =>
        api.PlayerData.GetPlayerDataByLastKnownName(name) ?? api.PlayerData.GetPlayerDataByUid(name);

    private static TextCommandResult Standing(ICoreServerAPI api, StandingSystem system, TextCommandCallingArgs args)
    {
        string first = (string)args[0];
        var rest = ((args[1] as string) ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (first)
        {
            case "set":
            {
                if (rest.Length != 3 || !double.TryParse(rest[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double points))
                    return TextCommandResult.Error(L("trading-standing-setusage"));
                if (FindPlayer(api, rest[0]) is not { } p) return TextCommandResult.Error(L("trading-standing-noplayer", rest[0]));
                if (TraderArg(api, system, args, rest[1]) is not { } trader) return TextCommandResult.Error(L("trading-standing-notrader", rest[1]));
                system.Ledger.Set(p.PlayerUID, trader, points, api.World.Calendar.TotalDays);
                return TextCommandResult.Success(Detail(api, system, p, trader));
            }
            case "reset":
            {
                if (rest.Length is < 1 or > 2) return TextCommandResult.Error(L("trading-standing-resetusage"));
                if (FindPlayer(api, rest[0]) is not { } p) return TextCommandResult.Error(L("trading-standing-noplayer", rest[0]));
                string? trader = null;
                if (rest.Length == 2 && (trader = TraderArg(api, system, args, rest[1])) is null)
                    return TextCommandResult.Error(L("trading-standing-notrader", rest[1]));
                system.Ledger.Reset(p.PlayerUID, trader);
                return TextCommandResult.Success(L("trading-standing-reset", p.LastKnownPlayername, trader ?? L("trading-standing-alltraders")));
            }
            default:
            {
                if (FindPlayer(api, first) is not { } p) return TextCommandResult.Error(L("trading-standing-noplayer", first));
                if (rest.Length == 0) return TextCommandResult.Success(Summary(api, system, p));
                if (TraderArg(api, system, args, rest[0]) is not { } trader) return TextCommandResult.Error(L("trading-standing-notrader", rest[0]));
                return TextCommandResult.Success(Detail(api, system, p, trader));
            }
        }
    }

    private static TextCommandResult AdminCompany(ICoreServerAPI api, StandingSystem system, TextCommandCallingArgs args)
    {
        string name = (string)args[0];
        if (FindPlayer(api, name) is not { } p) return TextCommandResult.Error(L("trading-standing-noplayer", name));
        if (args[1] is string groupName && groupName.Length > 0)
        {
            var group = api.Groups.GetPlayerGroupByName(groupName);
            if (group is null || system.Designate(p.PlayerUID, group.Uid) is null)
                return TextCommandResult.Error(L("trading-company-adminnotmember", p.LastKnownPlayername, groupName));
        }
        var lines = new List<string> { CompanyLine(api, system, p.PlayerUID, p.LastKnownPlayername) };
        if (system.CompanyOf(p.PlayerUID) is int c && system.Ledger.Companies.Record(c) is { } record)
        {
            lines.Add(L("trading-company-members", record.Merged.Count == 0 ? "-" :
                string.Join(", ", record.Merged.Select(uid => api.PlayerData.GetPlayerDataByUid(uid)?.LastKnownPlayername ?? uid))));
            foreach (var (trader, r) in record.Traders.OrderBy(kv => kv.Key))
                lines.Add($"  {trader} {TraderName(api, system, trader)}: {Math.Floor(r.Points)}");
        }
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    /// <summary>Every trader the player or their company has a record with.</summary>
    private static string Summary(ICoreServerAPI api, StandingSystem system, IServerPlayerData p)
    {
        int? company = system.CompanyOf(p.PlayerUID);
        var traders = system.Ledger.PersonalRecords(p.PlayerUID).Keys.ToHashSet();
        if (company is int c && system.Ledger.Companies.Record(c) is { } record) traders.UnionWith(record.Traders.Keys);
        var lines = new List<string> { CompanyLine(api, system, p.PlayerUID, p.LastKnownPlayername) };
        if (traders.Count == 0) lines.Add(L("trading-standing-nothing"));
        foreach (string trader in traders.OrderBy(t => t))
            lines.Add("  " + ViewLine(api, system, trader, system.ViewFor(p.PlayerUID, trader)));
        return string.Join("\n", lines);
    }

    private static string Detail(ICoreServerAPI api, StandingSystem system, IServerPlayerData p, string trader)
    {
        var view = system.ViewFor(p.PlayerUID, trader);
        var lines = new List<string> { p.LastKnownPlayername + ": " + ViewLine(api, system, trader, view) };
        lines.Add("  " + (view.Next is { } next
            ? L("trading-standing-next", StandingText.TierName(next), next.Points)
            : L("trading-standing-top")));
        foreach (var e in system.Ledger.Personal(p.PlayerUID, trader)?.Events ?? [])
            lines.Add(string.Format(CultureInfo.InvariantCulture, "  day {0:0.0} {1} {2:+0.#;-0.#;0}", e.Day, e.Kind, e.Points));
        return string.Join("\n", lines);
    }

    /// <summary>"camp:1,2 (smith): regular 312 (personal 300, company acme 120, nearby +12)".</summary>
    private static string ViewLine(ICoreServerAPI api, StandingSystem system, string trader, StandingView v)
    {
        string company = v.CompanyUid is int c ? L("trading-standing-companypart", GroupName(api, c), Math.Floor(v.Company ?? 0)) : "";
        return L("trading-standing-view", trader, TraderName(api, system, trader), StandingText.TierName(v.Tier), Math.Floor(v.Effective),
            Math.Floor(v.Personal), company, Math.Floor(v.Spill));
    }

    private static string TraderName(ICoreServerAPI api, StandingSystem system, string trader)
    {
        string? type = null;
        if (trader.StartsWith("camp:", StringComparison.Ordinal) && CellKey.TryParse(trader["camp:".Length..], out var cell))
            type = TradingSystem.Of(api)?.Camps?.Registry.Get(cell)?.Type;
        else if (trader.StartsWith("entity:", StringComparison.Ordinal) && long.TryParse(trader["entity:".Length..], out long id))
            type = (api.World.GetEntityById(id) as EntitySeraphTrader)?.TraderType;
        return string.IsNullOrEmpty(type) ? "?" : L("trading-type-" + type);
    }

    private static string? TraderArg(ICoreServerAPI api, StandingSystem system, TextCommandCallingArgs args, string text)
    {
        if (text == "near")
        {
            var pos = args.Caller.Entity?.Pos.XYZ ?? args.Caller.Pos;
            if (pos is null) return null;
            var near = api.World.GetNearestEntity(pos, (float)NearRange, (float)NearRange, e => e is EntitySeraphTrader) as EntitySeraphTrader;
            return near is null ? null : system.TraderIdOf(near);
        }
        return TraderIds.IsValid(text) ? text : null;
    }
}
