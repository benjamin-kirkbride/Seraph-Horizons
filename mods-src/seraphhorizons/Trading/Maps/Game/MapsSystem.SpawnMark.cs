using SeraphHorizons.Mod.Trading.Core;
using SeraphHorizons.Mod.Trading.Maps.Core;
using SeraphHorizons.Mod.Trading.Standing.Core;
using SeraphHorizons.Mod.Trading.Window.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Trading.Maps;

/// <summary>
/// The spawn mark (switch <see cref="SeraphHorizonsConfig.SpawnProspectorMark"/>, with
/// <c>TraderMaps</c> and the grid): on a player's first join, the nearest prospector's camp to where
/// they joined (<see cref="SpawnMark.Target"/>, by real distance, within <see cref="SpawnMark.Reach"/>
/// rings) is settled as a lead's is (<see cref="ResolveCamp"/>, loading its next spots' chunks; a
/// cell that places none joins <see cref="_noCamp"/> and the next is tried, at most
/// <see cref="SpawnMarkTries"/>) and marked at a lead's precision, titled as a lead, so it counts as had
/// wherever leads look, and meeting its trader makes it exact. Once per player
/// (<see cref="LeadBook.SpawnMarkGiven"/>, on their own key); a player from before it with a trader
/// camp marked has it spent without a marker (<see cref="SpawnMark.Decide"/>).
/// </summary>
public partial class MapsSystem
{
    /// <summary>Cells a spawn mark tries at most (each one whose camp places none is skipped for the
    /// next-nearest).</summary>
    public const int SpawnMarkTries = 16;

    /// <summary>Players whose spawn mark is being settled (their camp's chunks loading).</summary>
    private readonly HashSet<string> _spawnMarking = new();

    /// <summary>Whether a spawn mark is being settled for the player.</summary>
    public bool SpawnMarkPending(string uid) => _spawnMarking.Contains(uid);

    private void OnNowPlaying(IServerPlayer player)
    {
        if (!Active || _trading is not { GridReady: true } || !SeraphHorizonsSystem.ConfigFor(_sapi!).SpawnProspectorMark) return;
        string uid = player.PlayerUID;
        if (_spawnMarking.Contains(uid)) return;
        var marks = MapMarksSystem.Of(_sapi!);
        switch (SpawnMark.Decide(Leads.SpawnMarkGiven(uid), marks?.Book.Of(uid).Select(r => r.Key) ?? []))
        {
            case SpawnMarkDecision.Done:
                return;
            case SpawnMarkDecision.Veteran:
                // From before the mark, with camps found already: nothing to point them to.
                Leads.RecordSpawnMark(uid);
                return;
        }
        // From where they joined (their spawn), whatever they do while it settles.
        double x = player.Entity.Pos.X, z = player.Entity.Pos.Z;
        _spawnMarking.Add(uid);
        void Try(int tries)
        {
            // Offline meanwhile: not given, so their next join tries again.
            if (_sapi!.World.PlayerByUid(uid) is not IServerPlayer p || p.ConnectionState == EnumClientState.Offline)
            {
                _sapi.Logger.Notification("[seraphhorizons] Trader maps: {0} left before their spawn mark settled; tried at their next join", uid);
                _spawnMarking.Remove(uid);
                return;
            }
            if (SpawnTarget(x, z, CampsHad(p)) is not { } target)
            {
                _spawnMarking.Remove(uid);
                Leads.RecordSpawnMark(uid);
                _sapi.Logger.Notification("[seraphhorizons] Trader maps: {0}'s spawn mark: no prospector camp within {1} rings of {2:0},{3:0}",
                    p.PlayerName, SpawnMark.Reach, x, z);
                return;
            }
            _sapi.Logger.Debug("[seraphhorizons] Trader maps: {0}'s spawn mark: settling prospector cell {1} (try {2})", p.PlayerName, target.Cell, tries);
            ResolveCamp(target.Cell, 0, record =>
            {
                if (record is null)
                {
                    _noCamp.Add(target.Cell);
                    if (tries < SpawnMarkTries)
                    {
                        Try(tries + 1);
                        return;
                    }
                    _spawnMarking.Remove(uid);
                    Leads.RecordSpawnMark(uid);
                    return;
                }
                _spawnMarking.Remove(uid);
                GiveSpawnMark(uid, target.Cell, record);
            });
        }
        Try(1);
    }

    /// <summary>The nearest prospector's camp to (x, z) the player lacks, from their own cell out
    /// (<see cref="SpawnMark.Target"/>), its distance measured from there.</summary>
    private CampOption? SpawnTarget(double x, double z, ISet<CellKey> have)
    {
        var own = TraderGrid.CellOf((int)Math.Floor(x), (int)Math.Floor(z));
        return SpawnMark.Target(x, z, SpawnMark.Reach, _trading!.Grid!.IsProspector, c => Option(own, x, z, c), have);
    }

    /// <summary>Marks the settled camp on the player's map as a lead would (or, with no world map,
    /// says where it is in chat) and records the mark given.</summary>
    private void GiveSpawnMark(string uid, CellKey cell, CampRecord record)
    {
        if (_sapi!.World.PlayerByUid(uid) is not IServerPlayer player || player.ConnectionState == EnumClientState.Offline) return;
        string lang = player.LanguageCode ?? Lang.DefaultLocale;
        string title = MapMarksSystem.Title(lang, TraderTitle(lang, record.Type), lead: true);
        var pos = new Vec3d(record.X + 0.5, record.Y > 0 ? record.Y + 0.5 : _sapi.World.SeaLevel, record.Z + 0.5);
        var outcome = MapMarksSystem.Of(_sapi) is { } marks
            ? marks.Mark(player, new MarkTarget(TraderIds.Camp(cell.X, cell.Z), MapMarks.LeadPrecision), pos, title, "trader", MapMarksSystem.TraderColor)
            : MapMarksSystem.Outcome.NoMap;
        Leads.RecordSpawnMark(uid);
        double dx = pos.X - player.Entity.Pos.X, dz = pos.Z - player.Entity.Pos.Z;
        string distance = MapMarksSystem.DistanceText(lang, Math.Sqrt(dx * dx + dz * dz));
        _sapi.Logger.Notification("[seraphhorizons] Trader maps: {0}'s spawn mark: the {1} camp of cell {2} at {3},{4} ({5})",
            player.PlayerName, record.Type, cell, record.X, record.Z, outcome);
        switch (outcome)
        {
            case MapMarksSystem.Outcome.Added:
                Tell(player, "trading-maps-spawnmark", distance, Lang.GetL(lang, "seraphhorizons:" + TradeWindowModel.Direction(dx, dz).Key));
                break;
            case MapMarksSystem.Outcome.NoMap:
                Tell(player, "trading-maps-lead-distance", title, distance);
                break;
        }
    }
}
