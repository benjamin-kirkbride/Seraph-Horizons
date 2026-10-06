using System.Text;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Machines;

/// <summary>A machine's oil (<c>MachineOil</c>): its tank and the dry multiplier the server
/// applies. Kept by the bucking mill's and the rosser's block entities themselves, and for the
/// game's and Immersive Woodworking's machines beside their block entities
/// (<c>MachineOil/ForeignMachines.cs</c>).</summary>
public sealed class OilState(OilMachine machine, OilTank tank, float dryMultiplier)
{
    public OilMachine Machine { get; } = machine;
    public OilTank Tank { get; set; } = tank;
    public float DryMultiplier { get; set; } = dryMultiplier;
    public bool Dry => Tank.Dry;

    /// <summary>The load the shaft last asked of the machine (what <see cref="Resistance"/> gave),
    /// null until it has. The server syncs it for the block info.</summary>
    public float? Load { get; set; }

    /// <summary>A shaft load as the oil leaves it, remembered as <see cref="Load"/>.</summary>
    public float Resistance(float resistance)
    {
        float load = Tank.Resistance(resistance, DryMultiplier);
        Load = load;
        return load;
    }

    /// <summary>The load the machine takes oiled, from the last one asked: <see cref="Load"/> over
    /// the dry multiplier while dry, else as it is. Null until the shaft has asked.</summary>
    public float? OiledLoad => Load is { } load ? (Dry && DryMultiplier > 0 ? load / DryMultiplier : load) : null;
}

/// <summary>
/// What every oiled machine shares: the tank in the block entity's tree (saved, and synced to
/// clients for the block info and the smoke), pouring oil in from a held liquid container or by
/// the lump, the block info lines and the smoke a dry machine gives off while it turns.
/// </summary>
public static class Oil
{
    public const string Domain = "seraphhorizons";

    /// <summary>The tree attribute holding the tank, in the machine's block entity's tree.</summary>
    public const string TreeKey = "seraphhorizons:oil";

    /// <summary>The shaft speed from which a machine counts as turning, for the smoke.</summary>
    public const float TurningSpeed = 0.05f;

    private static readonly AssetLocation LumpSound = new("game", "sounds/player/squish1");

    public static void Write(ITreeAttribute tree, OilState state)
    {
        var oil = new TreeAttribute();
        oil.SetDouble("points", state.Tank.Points);
        oil.SetDouble("capacity", state.Tank.Capacity);
        oil.SetFloat("dryMultiplier", state.DryMultiplier);
        if (state.Load is { } load)
            oil.SetFloat("load", load);
        tree[TreeKey] = oil;
    }

    /// <summary>The tank in <paramref name="tree"/>, or null when it holds none (a client of a
    /// server without the tweak, or a machine saved before it).</summary>
    public static OilState? Read(ITreeAttribute tree, OilMachine machine)
    {
        if (tree[TreeKey] is not ITreeAttribute oil)
            return null;
        double capacity = oil.GetDouble("capacity");
        return new OilState(machine, new OilTank(Math.Clamp(oil.GetDouble("points"), 0, Math.Max(0, capacity)), capacity),
            oil.GetFloat("dryMultiplier", 1f)) { Load = oil.HasAttribute("load") ? oil.GetFloat("load") : null };
    }

    /// <summary>The server's tank for a machine loaded from <paramref name="tree"/>: what it saved,
    /// in a tank of today's size; empty when it saved none.</summary>
    public static OilState Load(ITreeAttribute tree, OilMachine machine, MachineOilConfig config)
    {
        double capacity = config.For(machine).Tank;
        var saved = Read(tree, machine);
        var tank = saved == null ? OilTank.Empty(capacity) : saved.Tank.WithCapacity(capacity);
        return new OilState(machine, tank, config.DryResistanceMultiplier) { Load = saved?.Load };
    }

    public static OilState New(OilMachine machine, MachineOilConfig config) =>
        new(machine, OilTank.Empty(config.For(machine).Tank), config.DryResistanceMultiplier);

    /// <summary>The oil <paramref name="held"/> carries: a liquid container holding a listed liquid,
    /// or a listed lump. <see cref="OilKind.None"/> for anything else.</summary>
    public static OilKind KindOf(ItemStack? held, MachineOilConfig config, OilCodes liquids)
    {
        if (held?.Collectible == null)
            return OilKind.None;
        if (held.Collectible is ILiquidSource source && source.GetContent(held) is { Collectible: { } content }
            && liquids.Matches(content.Code.ToString()))
            return OilKind.Liquid;
        return config.LumpLitres(held.Collectible.Code?.ToString()) > 0 ? OilKind.Lump : OilKind.None;
    }

    /// <summary>
    /// Pours as much of the held oil as fits into <paramref name="state"/>'s tank (server side):
    /// whole items only, taken from the container as a barrel takes it (one container split off a
    /// stack of them), or lumps from the stack. Says so when the tank is full. Returns the points
    /// that went in.
    /// </summary>
    public static double Pour(IWorldAccessor world, IPlayer player, OilState state, MachineOilConfig config, OilCodes liquids, BlockPos at)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        var held = slot?.Itemstack;
        if (slot == null || held == null)
            return 0;
        switch (KindOf(held, config, liquids))
        {
            case OilKind.Liquid:
            {
                var source = (ILiquidSource)held.Collectible;
                var content = source.GetContent(held)!;
                double per = OilTank.PointsPerItem(BlockLiquidContainerBase.GetContainableProps(content)?.ItemsPerLitre ?? 0);
                int fit = state.Tank.ItemsThatFit(per, content.StackSize);
                if (fit <= 0)
                    return Full(player, state);
                int taken;
                if (held.StackSize > 1 && source is BlockLiquidContainerBase container)
                    taken = container.SplitStackAndPerformAction(player.Entity, slot, one => container.TryTakeContent(one, fit)?.StackSize ?? 0);
                else
                    taken = source.TryTakeContent(held, fit)?.StackSize ?? 0;
                slot.MarkDirty();
                if (taken <= 0)
                    return 0;
                state.Tank = state.Tank.Fill(taken * per);
                if (source is BlockLiquidContainerBase effects)
                    effects.DoLiquidMovedEffects(player, content, taken, BlockLiquidContainerBase.EnumLiquidDirection.Pour);
                return taken * per;
            }
            case OilKind.Lump:
            {
                double per = config.LumpLitres(held.Collectible.Code.ToString()) * OilTank.PointsPerLitre;
                int fit = state.Tank.ItemsThatFit(per, held.StackSize);
                if (fit <= 0)
                    return Full(player, state);
                slot.TakeOut(fit);
                slot.MarkDirty();
                state.Tank = state.Tank.Fill(fit * per);
                world.PlaySoundAt(LumpSound, at.X + 0.5, at.Y + 0.5, at.Z + 0.5, player);
                return fit * per;
            }
            default:
                return 0;
        }
    }

    private static double Full(IPlayer player, OilState state)
    {
        if (player is IServerPlayer sp)
            sp.SendIngameError("machineoil-full", Lang.GetL(sp.LanguageCode, Domain + ":machineoil-error-full", Shown(state.Tank.Capacity)));
        return 0;
    }

    /// <summary>Points as the block info shows them: whole, rounded up, so a tank with any oil in
    /// it never reads 0 (and an empty one reads 0, never -0).</summary>
    public static string Shown(double points) => OilText.Points(points);

    /// <summary>The block info's oil lines: the tank, and while dry how much harder it turns: its
    /// load on the shaft now against the load oiled, once the shaft has asked for it.</summary>
    public static void Info(OilState? state, StringBuilder dsc)
    {
        if (state == null)
            return;
        dsc.AppendLine(Lang.Get(Domain + ":machineoil-info-tank", Shown(state.Tank.Points), Shown(state.Tank.Capacity)));
        if (!state.Dry)
            return;
        string times = state.DryMultiplier.ToString("0.##");
        dsc.AppendLine(state.Load is { } load && state.OiledLoad is { } oiled
            ? Lang.Get(Domain + ":machineoil-info-dry-load", times, OilText.Load(load), OilText.Load(oiled))
            : Lang.Get(Domain + ":machineoil-info-dry", times));
    }

    /// <summary>The shaft asking a machine its load: <paramref name="resistance"/> as the oil leaves
    /// it, remembered on <paramref name="state"/>; the server marks <paramref name="owner"/> dirty
    /// when the figure changes, so the client's block info has it.</summary>
    public static float Asked(OilState state, float resistance, BlockEntity? owner)
    {
        float? before = state.Load;
        float load = state.Resistance(resistance);
        if (before != load && owner?.Api?.Side == EnumAppSide.Server)
            owner.MarkDirty();
        return load;
    }

    // ---- The bucking mill's and the rosser's own tanks ----

    /// <summary>The tank a machine of this mod's starts with on the server: empty, or none with the
    /// switch off.</summary>
    public static OilState? NewOwn(ICoreAPI api, OilMachine machine) =>
        MachineOilSystem.Of(api) is { On: true } system ? New(machine, system.Config) : null;

    /// <summary>The tank a machine of this mod's reads from its tree: on the server what it saved
    /// (none with the switch off), on a client what the server sent.</summary>
    public static OilState? LoadOwn(ITreeAttribute tree, IWorldAccessor world, OilMachine machine)
    {
        if (world.Side != EnumAppSide.Server)
            return Read(tree, machine);
        return MachineOilSystem.Of(world.Api) is { On: true } system ? Load(tree, machine, system.Config) : null;
    }

    /// <summary>A right-click on a machine of this mod's: whether it is oil's (the player holds oil
    /// and the machine has a tank), pouring it on the server. Null when it is not, so the machine's
    /// own handling goes on.</summary>
    public static bool? Interact(BlockEntity be, OilState? state, IPlayer byPlayer)
    {
        if (state == null || MachineOilSystem.Of(be.Api) is not { } system)
            return null;
        if (KindOf(byPlayer.InventoryManager.ActiveHotbarSlot?.Itemstack, system.Config, system.Liquids) == OilKind.None)
            return null;
        if (be.Api.Side == EnumAppSide.Server && Pour(be.Api.World, byPlayer, state, system.Config, system.Liquids, be.Pos) > 0)
            be.MarkDirty(true);
        return true;
    }

    /// <summary>A finished job of a machine of this mod's: drains <paramref name="points"/>
    /// (server side). The caller marks the block entity dirty.</summary>
    public static void Drain(OilState? state, double points)
    {
        if (state != null && points > 0)
            state.Tank = state.Tank.Drain(points);
    }

    /// <summary>What one job costs <paramref name="machine"/> by this side's settings.</summary>
    public static double DrainPerJob(ICoreAPI api, OilMachine machine) => MachineOilSystem.Of(api).Config.For(machine).DrainPerJob;

    private static SimpleParticleProperties? _smoke;

    /// <summary>A puff of dark smoke at <paramref name="at"/> (client side): a dry machine turning.</summary>
    public static void Smoke(ICoreClientAPI capi, Vec3d at)
    {
        _smoke ??= new SimpleParticleProperties(1, 2, ColorUtil.ToRgba(110, 45, 42, 40), new Vec3d(), new Vec3d(),
            new Vec3f(-0.08f, 0.15f, -0.08f), new Vec3f(0.08f, 0.35f, 0.08f), 2.5f, -0.01f, 0.35f, 0.8f, EnumParticleModel.Quad)
        {
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.2f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -40f),
            SelfPropelled = true,
            WindAffected = true,
        };
        _smoke.MinPos.Set(at.X - 0.4, at.Y, at.Z - 0.4);
        _smoke.AddPos.Set(0.8, 0.3, 0.8);
        capi.World.SpawnParticles(_smoke);
    }
}

public enum OilKind
{
    None,
    Liquid,
    Lump,
}
