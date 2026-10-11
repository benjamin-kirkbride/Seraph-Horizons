using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's charge (Eidolon/README.md, "Charge"): game days left, held in its watched attributes
/// (<see cref="ChargeKey"/>). A temporal gear adds <see cref="EidolonConfig.ChargeYearsPerGear"/> of
/// the world's year, by right-click anywhere, up to <see cref="EidolonConfig.MaxChargeGears"/>; it
/// drains with game time while the eidolon stands (and its chunk is loaded), not while it is slumped;
/// at 0 it slumps (<see cref="EidolonStop.NoCharge"/>) until recharged. A new eidolon starts with
/// <c>initialGears</c> (the entity type's, 1: the mind stage's gear).
/// </summary>
public class EntityBehaviorEidolonCharge(Entity entity) : EntityBehavior(entity), IEidolonUpkeep
{
    public const string Code = "seraphhorizons.eidolonCharge";
    public const string ChargeKey = "seraphhorizons:chargeDays";
    public static readonly AssetLocation TemporalGear = new("game", "gear-temporal");

    private double _initialGears = 1;
    // The calendar day the drain was last counted to; NaN until the first check after loading, so
    // the time the chunk was unloaded is never drained.
    private double _countedTo = double.NaN;

    public override string PropertyName() => Code;

    /// <summary>Game days of charge left.</summary>
    public double ChargeDays
    {
        get => entity.WatchedAttributes.GetDouble(ChargeKey);
        set => entity.WatchedAttributes.SetDouble(ChargeKey, Math.Max(0, value));
    }

    /// <summary>The days one gear runs it, on this world's calendar.</summary>
    public double DaysPerGear => EidolonCharge.DaysPerGear(entity.World.Calendar.DaysPerYear, Settings.ChargeYearsPerGear);

    public EidolonStop? Stop => ChargeDays <= 0 ? EidolonStop.NoCharge : null;

    private EidolonConfig Settings => EidolonSystem.Of(entity.Api)?.Config ?? EidolonConfig.Defaults;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        _initialGears = attributes["initialGears"].AsDouble(1);
    }

    public override void OnEntitySpawn()
    {
        base.OnEntitySpawn();
        if (entity.Api.Side == EnumAppSide.Server && !entity.WatchedAttributes.HasAttribute(ChargeKey))
            ChargeDays = _initialGears * DaysPerGear;
    }

    public void OnCheck(EntityLaborEidolon eidolon, bool running)
    {
        double now = entity.World.Calendar.TotalDays;
        if (running && !double.IsNaN(_countedTo))
        {
            double before = ChargeDays;
            double after = EidolonCharge.Drain(before, now - _countedTo);
            // Written when it moves by a minute's worth or runs out, not every check: each write is synced.
            if (before - after >= 1 / 1440.0 || (after <= 0 && before > 0))
                ChargeDays = after;
            else
                return;
        }
        _countedTo = now;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || itemslot.Itemstack?.Collectible.Code is not { } code || !code.Equals(TemporalGear))
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity.Api.Side != EnumAppSide.Server || entity is not EntityLaborEidolon eidolon)
            return;
        var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
        if (!EidolonCharge.TryAddGear(ChargeDays, DaysPerGear, Settings.MaxChargeGears, out double after))
        {
            player?.SendIngameError("seraphhorizons-eidolon-charged",
                Lang.GetL(player.LanguageCode, "seraphhorizons:eidolon-charge-full"));
            return;
        }
        ChargeDays = after;
        _countedTo = entity.World.Calendar.TotalDays;
        if (player?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            itemslot.TakeOut(1);
            itemslot.MarkDirty();
        }
        entity.World.PlaySoundAt(new AssetLocation("game:sounds/effect/timeswitch"), entity, null, true, 16);
        eidolon.Check();
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        double perGear = DaysPerGear;
        infotext.AppendLine(Lang.Get("seraphhorizons:eidolon-info-charge", ChargeDays, EidolonCharge.Gears(ChargeDays, perGear) * 100));
    }

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled) =>
    [
        new WorldInteraction
        {
            ActionLangCode = "seraphhorizons:eidolon-help-recharge",
            MouseButton = EnumMouseButton.Right,
            Itemstacks = world.GetItem(TemporalGear) is { } gear ? [new ItemStack(gear)] : [],
        },
    ];
}
