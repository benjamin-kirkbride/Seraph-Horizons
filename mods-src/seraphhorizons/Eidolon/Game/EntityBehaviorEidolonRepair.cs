using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon's repair (Eidolon/README.md, "Repair"): right-click with metal parts or an iron plate,
/// by anyone, one item a click, restores <see cref="EidolonConfig.RepairShare"/> of its most health,
/// times <see cref="EidolonConfig.GantryRepairMultiplier"/> while it stands inside a gantry
/// (<see cref="GantryAround"/>). Works on a slumped eidolon where it lies: it heals through
/// <c>ReceiveDamage</c> with <see cref="EnumDamageType.Heal"/>, and the entity stands it up once past
/// <see cref="EidolonConfig.StandUpHealthShare"/>. A whole one refuses the item.
/// </summary>
public class EntityBehaviorEidolonRepair(Entity entity) : EntityBehavior(entity)
{
    public const string Code = "seraphhorizons.eidolonRepair";

    /// <summary>What repairs it, one item a click (metal parts are a block).</summary>
    public static readonly AssetLocation[] RepairItems = [new("game", "metal-parts"), new("game", "metalplate-iron")];

    private static readonly AssetLocation Sound = new("game", "sounds/effect/anvilhit1");

    public override string PropertyName() => Code;

    private EidolonConfig Settings => EidolonSystem.Of(entity.Api)?.Config ?? EidolonConfig.Defaults;

    /// <summary>Whether <paramref name="stack"/> repairs it.</summary>
    public static bool Repairs(ItemStack? stack) => stack?.Collectible?.Code is { } code && RepairItems.Any(code.Equals);

    /// <summary>
    /// The gantry <paramref name="eidolon"/> stands inside, or null: a gantry cell (the controller's
    /// or a ghost) where its feet are, or the block above (its feet a hair below a cell's floor). The
    /// one lookup of "inside a gantry"; anything else needing it calls this.
    /// </summary>
    public static BEEidolonGantry? GantryAround(Entity eidolon)
    {
        var accessor = eidolon.World.BlockAccessor;
        var feet = eidolon.Pos.AsBlockPos;
        for (int dy = 0; dy <= 1; dy++)
        {
            switch (accessor.GetBlockEntity(feet.UpCopy(dy)))
            {
                case BEEidolonGantry gantry:
                    return gantry;
                case BEEidolonGantryGhost { Gantry: { } owner }:
                    return owner;
            }
        }
        return null;
    }

    /// <summary>The health one item restores now (server side): 0 when it is whole.</summary>
    public float HealPerItem()
    {
        if (entity.GetBehavior<EntityBehaviorHealth>() is not { } health)
            return 0;
        return EidolonRepair.Heal(Settings, health.Health, health.MaxHealth, GantryAround(entity) != null);
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || !Repairs(itemslot.Itemstack))
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity.Api.Side != EnumAppSide.Server)
            return;
        var player = (byEntity as EntityPlayer)?.Player as IServerPlayer;
        float heal = HealPerItem();
        if (heal <= 0)
        {
            player?.SendIngameError("seraphhorizons-eidolon-repaired",
                Lang.GetL(player.LanguageCode, "seraphhorizons:eidolon-repair-full"));
            return;
        }
        entity.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Heal }, heal);
        if (player?.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
            itemslot.TakeOut(1);
            itemslot.MarkDirty();
        }
        entity.World.PlaySoundAt(Sound, entity, null, true, 16);
    }

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled) =>
    [
        new WorldInteraction
        {
            ActionLangCode = "seraphhorizons:eidolon-help-repair",
            MouseButton = EnumMouseButton.Right,
            Itemstacks = RepairItems
                .Select(code => world.GetItem(code) is { } item ? new ItemStack(item)
                    : world.GetBlock(code) is { Id: > 0 } block ? new ItemStack(block) : null)
                .OfType<ItemStack>()
                .ToArray(),
        },
    ];
}
