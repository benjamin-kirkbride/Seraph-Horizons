using System.Text;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// One machine's oil (<c>MachineOil</c>), the piece every oiled machine composes, whoever owns it: the
/// shared machine base keeps one in its block entity (<see cref="BEMachine.OilKind"/>), and the game's and
/// Immersive Woodworking's machines one beside theirs (<see cref="ForeignMachines"/>, by Harmony). It is the
/// tank (<see cref="OilState"/>: points of a capacity, the last load asked) with the machine's settings
/// entry (<see cref="OilMachine"/>, <c>MachineOilSettings.{Machine}</c>: <c>Tank</c>, <c>DrainPerJob</c>) and
/// its dry effect (<see cref="OilDryEffect"/>), and it does what every one does the same way: it is made,
/// saved and synced in the owner's tree (<see cref="Oil.TreeKey"/>), filled by a right-click with oil
/// (<see cref="Interact"/>), drained a job at a time (<see cref="Drain"/>), puts the dry multiplier on the
/// shaft's load (<see cref="Asked"/>), writes the block info's oil lines with the wrench's detail
/// (<see cref="AppendInfo(MachineInfo, ICoreAPI, bool)"/>) and smokes while dry and turning.
/// </summary>
public sealed class MachineOiling
{
    private MachineOiling(OilState state, OilDryEffect effect)
    {
        State = state;
        DryEffect = effect;
    }

    /// <summary>The tank and the last load asked (the data the tree holds).</summary>
    public OilState State { get; }

    public OilMachine Machine => State.Machine;
    public OilDryEffect DryEffect { get; }

    public OilTank Tank
    {
        get => State.Tank;
        set => State.Tank = value;
    }

    public bool Dry => State.Dry;

    /// <summary>The tank's fill, 0..1.</summary>
    public double Fill => OilDrain.Fill(State.Tank);

    /// <summary>The load the shaft last asked of the machine; null until it has.</summary>
    public float? LastLoad => State.Load;

    // ---- Made, saved and synced ----

    /// <summary>An empty tank for <paramref name="machine"/> by <paramref name="config"/>.</summary>
    public static MachineOiling New(OilMachine machine, MachineOilConfig config, OilDryEffect effect = OilDryEffect.Load) =>
        new(Oil.New(machine, config), effect);

    /// <summary>The server's tank for a machine loaded from <paramref name="tree"/>: what it saved, in a tank of
    /// today's size; empty when it saved none.</summary>
    public static MachineOiling Load(ITreeAttribute tree, OilMachine machine, MachineOilConfig config, OilDryEffect effect = OilDryEffect.Load) =>
        new(Oil.Load(tree, machine, config), effect);

    /// <summary>The tank a client was sent in <paramref name="tree"/>, or null when the server sent none.</summary>
    public static MachineOiling? Read(ITreeAttribute tree, OilMachine machine, OilDryEffect effect = OilDryEffect.Load) =>
        Oil.Read(tree, machine) is { } state ? new MachineOiling(state, effect) : null;

    /// <summary>The tank a machine starts with on the server: empty, or none with the <c>MachineOil</c> switch off.</summary>
    public static MachineOiling? NewIfOn(ICoreAPI api, OilMachine machine, OilDryEffect effect = OilDryEffect.Load) =>
        MachineOilSystem.Of(api) is { On: true } system ? New(machine, system.Config, effect) : null;

    /// <summary>The tank a machine reads from its tree: on the server what it saved (none with the switch off),
    /// on a client what the server sent.</summary>
    public static MachineOiling? Restore(ITreeAttribute tree, IWorldAccessor world, OilMachine machine, OilDryEffect effect = OilDryEffect.Load)
    {
        if (world.Side != EnumAppSide.Server)
            return Read(tree, machine, effect);
        return MachineOilSystem.Of(world.Api) is { On: true } system ? Load(tree, machine, system.Config, effect) : null;
    }

    public void Write(ITreeAttribute tree) => Oil.Write(tree, State);

    // ---- Work ----

    /// <summary>The shaft asking the machine its load: <paramref name="resistance"/> times the dry multiplier
    /// while dry when its oil carries the load (<see cref="OilDryEffect.Load"/>), as it is otherwise; remembered,
    /// and the server marks <paramref name="owner"/> dirty when the figure changes, so the block info has it.</summary>
    public float Asked(float resistance, BlockEntity? owner) => Oil.Asked(State, resistance, owner, DryEffect == OilDryEffect.Load);

    /// <summary>What one job costs the machine by this side's settings.</summary>
    public double DrainPerJob(ICoreAPI api) => MachineOilSystem.Of(api).Config.For(Machine).DrainPerJob;

    /// <summary>Drains <paramref name="jobs"/> finished jobs' oil (server side) and marks <paramref name="owner"/>
    /// dirty, redrawn when the tank just ran dry so the client shows it at once.</summary>
    public void Drain(BlockEntity owner, double jobs = 1)
    {
        if (owner.Api?.Side != EnumAppSide.Server || jobs <= 0)
            return;
        double points = DrainPerJob(owner.Api) * jobs;
        if (points <= 0)
            return;
        bool wasDry = Dry;
        State.Tank = State.Tank.Drain(points);
        owner.MarkDirty(Dry != wasDry);
    }

    /// <summary>A right-click on the machine: whether it is oil's (the player holds oil), pouring it on the
    /// server and marking <paramref name="owner"/> dirty. Null when it is not, so the machine's own handling goes on.</summary>
    public bool? Interact(BlockEntity owner, IPlayer byPlayer)
    {
        if (MachineOilSystem.Of(owner.Api) is not { } system)
            return null;
        if (Oil.KindOf(byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack, system.Config, system.Liquids) == OilKind.None)
            return null;
        if (owner.Api.Side == EnumAppSide.Server && Oil.Pour(owner.Api.World, byPlayer, State, system.Config, system.Liquids, owner.Pos) > 0)
            owner.MarkDirty(true);
        return true;
    }

    /// <summary>A puff of dark smoke at <paramref name="at"/> while the machine is dry (client side).</summary>
    public void SmokeIfDry(ICoreClientAPI capi, Vec3d at)
    {
        if (Dry)
            Oil.Smoke(capi, at);
    }

    // ---- The block info ----

    /// <summary>The oil's lines (<see cref="MachineText.AppendOil"/>): the tank, and while dry how much harder it
    /// turns (a machine whose dry tank wears its cutter says only that it is dry); with a wrench, the dry load
    /// against the oiled one, the load on its shaft (<paramref name="withLoad"/>: a machine with no shaft line of
    /// its own), and what a job drains.</summary>
    public void AppendInfo(MachineInfo info, ICoreAPI api, bool withLoad = false) =>
        MachineText.AppendOil(info, State, DryEffect, DrainPerJob(api), withLoad);

    /// <summary>The oil's lines for a machine whose block info is not the base's (the game's, Immersive
    /// Woodworking's), written to <paramref name="dsc"/> as <paramref name="forPlayer"/> sees them: the detail with a
    /// wrench, the hint without.</summary>
    public void AppendInfo(StringBuilder dsc, IPlayer? forPlayer, ICoreAPI api)
    {
        var info = new MachineInfo();
        AppendInfo(info, api, withLoad: true);
        info.WriteTo(dsc, MachineText.HoldsWrench(forPlayer), Lang.Get(MachineText.WrenchHintKey));
    }
}
