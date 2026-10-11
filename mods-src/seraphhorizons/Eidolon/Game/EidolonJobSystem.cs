using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The shared jobs (README "Eidolon", working together): one <see cref="JobBoard{TUnit, TShared}"/> per
/// kind of work, which the orders join and reserve on (mining's, <see cref="Mining"/>: its units block
/// columns), and the server's sweep every <see cref="SweepMs"/>, which takes out of its job any eidolon
/// that is unloaded, gone, or whose order is no longer the one that joined (given another order, or
/// none), freeing its reservations, and tidies lapsed ones. Nothing here is saved: each order saves its
/// own area, home and mates, and the jobs form again as the eidolons load. Server side.
/// </summary>
public class EidolonJobSystem : ModSystem
{
    public const int SweepMs = 1000;

    private ICoreServerAPI? _api;
    private readonly Dictionary<(object Board, long Member), IEidolonOrder> _orders = new();
    private readonly Dictionary<long, (int X, int Z)> _rims = new();

    public static EidolonJobSystem? Of(ICoreAPI? api) => api?.ModLoader.GetModSystem<EidolonJobSystem>();

    /// <summary>The mining jobs; a unit is a block column (x, z) of the square a miner works.</summary>
    public JobBoard<(int X, int Z), MineJobCells> Mining { get; } = new();

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        _api = api;
        api.Event.RegisterGameTickListener(_ => Sweep(), SweepMs);
    }

    /// <summary>The job key of <paramref name="eidolon"/>'s order of <paramref name="kind"/> over <paramref name="area"/>.</summary>
    public static JobKey KeyOf(EntityLaborEidolon eidolon, string kind, MarkArea area) => new(eidolon.OwnerUid ?? "", kind, area);

    /// <summary>The server's time in seconds, which reservations are reckoned in.</summary>
    public static double Now(IWorldAccessor world) => world.ElapsedMilliseconds / 1000.0;

    /// <summary>Notes the order an eidolon joined a job on <paramref name="board"/> with, so the sweep
    /// takes it out once that order is no longer its current one.</summary>
    public void Track(object board, EntityLaborEidolon eidolon, IEidolonOrder order) => _orders[(board, eidolon.EntityId)] = order;

    /// <summary>Where a member waits at its area's rim (x, z: <see cref="EidolonTeamwork.Rim"/>), or none,
    /// so the next spare one waits elsewhere.</summary>
    public void SetRim(long member, (int X, int Z)? rim)
    {
        if (rim is { } r)
            _rims[member] = r;
        else
            _rims.Remove(member);
    }

    /// <summary>Where the members other than <paramref name="member"/> wait at a rim.</summary>
    public IEnumerable<(int X, int Z)> RimsBut(long member) => _rims.Where(r => r.Key != member).Select(r => r.Value);

    /// <summary>A member, if loaded.</summary>
    public EntityLaborEidolon? Member(long id) => _api?.World.GetEntityById(id) as EntityLaborEidolon;

    /// <summary>Takes the members no longer working their job out of it. Every <see cref="SweepMs"/>;
    /// at once for a test.</summary>
    public void Sweep()
    {
        if (_api == null)
            return;
        Sweep(Mining);
    }

    private void Sweep<TUnit, TShared>(JobBoard<TUnit, TShared> board) where TUnit : notnull where TShared : new()
    {
        foreach (long id in board.AllMembers)
        {
            var eidolon = Member(id);
            if (eidolon is { Alive: true } && _orders.TryGetValue((board, id), out var order) && ReferenceEquals(eidolon.Orders?.Current, order))
                continue;
            board.Leave(id);
            _orders.Remove((board, id));
            _rims.Remove(id);
        }
        board.Expire(Now(_api!.World));
    }
}
