using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.MachineOil;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's oil (Eidolon/README.md, "Oil"): a reservoir of <see cref="EidolonConfig.OilTank"/>
/// points in its watched attributes, filled by right-click, by anyone, with what oils a machine
/// (MachineOil's oils in any liquid container, or tallow: <see cref="Oil.Pour(IWorldAccessor, IPlayer, ref OilTank, MachineOilConfig, OilCodes, BlockPos)"/>).
/// Each job done drains it (<see cref="Spend"/>, which job orders call through
/// <see cref="EntityLaborEidolon.SpendOil"/>); idling, following, staying and guarding do not. Dry, it
/// stops and waits (<see cref="EidolonOil.Dry"/>, no slump) until oiled. A new eidolon wakes with
/// <see cref="EidolonConfig.InitialOilShare"/> of it. With the MachineOil switch off (the server's) it
/// has no reservoir and is never dry.
/// </summary>
public class EntityBehaviorEidolonOil(Entity entity) : EntityBehavior(entity), IEidolonUpkeep
{
    public const string Code = "seraphhorizons.eidolonOil";
    public const string PointsKey = "seraphhorizons:oilPoints";
    public const string CapacityKey = "seraphhorizons:oilCapacity";

    public override string PropertyName() => Code;

    private EidolonConfig Settings => EidolonSystem.Of(entity.Api)?.Config ?? EidolonConfig.Defaults;

    /// <summary>Whether MachineOil runs on this side (the server's counts).</summary>
    private bool OilOn => MachineOilSystem.Of(entity.Api) is { On: true };

    /// <summary>Its reservoir, or null when it has none (MachineOil off). On a client, what the
    /// server sent.</summary>
    public OilTank? Tank
    {
        get => entity.WatchedAttributes.HasAttribute(CapacityKey)
            ? new OilTank(entity.WatchedAttributes.GetDouble(PointsKey), entity.WatchedAttributes.GetDouble(CapacityKey))
            : null;
        private set
        {
            if (value is not { } tank)
            {
                entity.WatchedAttributes.RemoveAttribute(PointsKey);
                entity.WatchedAttributes.RemoveAttribute(CapacityKey);
                return;
            }
            entity.WatchedAttributes.SetDouble(PointsKey, Math.Clamp(tank.Points, 0, tank.Capacity));
            entity.WatchedAttributes.SetDouble(CapacityKey, tank.Capacity);
        }
    }

    public EidolonStop? Stop => Tank is { Dry: true } ? EidolonOil.Dry : null;

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        if (entity.Api.Side == EnumAppSide.Server)
            Keep();
    }

    public void OnCheck(EntityLaborEidolon eidolon, bool running) => Keep();

    // The reservoir as the server's settings have it: none with MachineOil off; a missing one (an
    // eidolon from before the switch was on) starts as a new eidolon's; a changed size keeps what fits.
    private void Keep()
    {
        if (!OilOn)
        {
            if (Tank != null)
                Tank = null;
            return;
        }
        if (Tank is not { } tank)
            Tank = EidolonOil.Initial(Settings);
        else if (tank.Capacity != Settings.OilTank)
            Tank = tank.WithCapacity(Settings.OilTank);
    }

    /// <summary>Sets the reservoir's points, up to its size (server side; the command and the
    /// scenarios). Nothing without a reservoir.</summary>
    public void SetPoints(double points)
    {
        if (Tank is { } tank)
            Tank = OilTank.Empty(tank.Capacity).Fill(points);
        (entity as EntityLaborEidolon)?.Check();
    }

    /// <summary>A job done (server side): drains its cost (<see cref="EidolonOil.Cost"/>) and checks at
    /// once, so a job that empties it stops it before the next. Nothing without a reservoir.</summary>
    public void Spend(EidolonJob job)
    {
        if (entity.Api.Side != EnumAppSide.Server || Tank is not { } tank)
            return;
        Tank = tank.Drain(EidolonOil.Cost(Settings, job));
        (entity as EntityLaborEidolon)?.Check();
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || Tank is not { } tank || MachineOilSystem.Of(entity.Api) is not { } oil
            || Oil.KindOf(itemslot.Itemstack, oil.Config, oil.Liquids) == OilKind.None)
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity.Api.Side != EnumAppSide.Server || (byEntity as EntityPlayer)?.Player is not { } player)
            return;
        if (Oil.Pour(entity.World, player, ref tank, oil.Config, oil.Liquids, entity.Pos.AsBlockPos) <= 0)
            return;
        Tank = tank;
        (entity as EntityLaborEidolon)?.Check();
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        if (Tank is { } tank)
            infotext.AppendLine(Lang.Get("seraphhorizons:machineoil-info-tank", OilText.Points(tank.Points), OilText.Points(tank.Capacity)));
    }

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled) =>
        Tank == null
            ? null
            :
            [
                new WorldInteraction
                {
                    ActionLangCode = "seraphhorizons:eidolon-help-oil",
                    MouseButton = EnumMouseButton.Right,
                    Itemstacks = world.GetItem(new AssetLocation("game:fat-rendered")) is { } fat ? [new ItemStack(fat)] : [],
                },
            ];
}
