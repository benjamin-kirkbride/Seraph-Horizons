using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The axe the eidolon fells with (#677; README "Eidolon", felling), held in its right hand. One who
/// may command it gives it one by right-clicking it with an axe (the game's <see cref="ItemAxe"/>, what
/// Logging Expanded fells with), the whole stack; it holds one at a time. Its commander takes it back by
/// sneaking and right-clicking it with an empty hand. The axe is kept in the watched attributes
/// (<see cref="AxeKey"/>), saved with it and sent to clients, where <see cref="EntityLaborEidolon"/>'s
/// <c>RightHandItemSlot</c> hands it to the game's shape renderer, which draws it at the shape's
/// <c>RightHand</c> point. Felling wears it as a player's axe (<see cref="EidolonFeller"/>); when it
/// breaks the slot is empty and the fell order waits for another.
/// </summary>
public class EntityBehaviorEidolonAxe(Entity entity) : EntityBehavior(entity)
{
    public const string Code = "seraphhorizons.eidolonAxe";
    public const string AxeKey = "seraphhorizons:axe";

    private static readonly AssetLocation GiveSound = new("game", "sounds/player/build");

    private ItemStack[]? _axes;

    private readonly DummySlot _slot = new();
    private ItemStack? _read;

    public override string PropertyName() => Code;

    /// <summary>Whether <paramref name="stack"/> is an axe it fells with.</summary>
    public static bool IsAxe(ItemStack? stack) => stack?.Collectible is ItemAxe;

    /// <summary>Its right hand: the axe, or empty. On a client, as the server last sent it.</summary>
    public ItemSlot Slot
    {
        get
        {
            var stored = entity.WatchedAttributes.GetItemstack(AxeKey);
            if (!ReferenceEquals(stored, _read))
            {
                _read = stored;
                stored?.ResolveBlockOrItem(entity.World);
                _slot.Itemstack = stored;
            }
            return _slot;
        }
    }

    /// <summary>The axe it holds, or null.</summary>
    public ItemStack? Axe => IsAxe(Slot.Itemstack) ? Slot.Itemstack : null;

    /// <summary>Puts <paramref name="stack"/> in its hand (null: empties it). Server side.</summary>
    public void Hold(ItemStack? stack)
    {
        _slot.Itemstack = stack;
        Save();
    }

    /// <summary>Writes the hand back to the watched attributes after the axe changed in place (worn,
    /// broken), so it is saved and clients see it. Server side.</summary>
    public void Save()
    {
        var stack = _slot.Itemstack is { StackSize: > 0 } s ? s : null;
        _slot.Itemstack = stack;
        if (stack == null)
            entity.WatchedAttributes.RemoveAttribute(AxeKey);
        else
            entity.WatchedAttributes.SetItemstack(AxeKey, stack);
        entity.WatchedAttributes.MarkPathDirty(AxeKey);
        _read = stack;
    }

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || byEntity is not EntityPlayer playerEntity)
            return;
        bool giving = IsAxe(itemslot.Itemstack);
        bool taking = itemslot.Empty && byEntity.Controls.Sneak && Axe != null;
        if (!giving && !taking)
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity.Api.Side != EnumAppSide.Server || entity is not EntityLaborEidolon eidolon
            || playerEntity.Player is not IServerPlayer player || !eidolon.RefuseUnlessCommander(player))
            return;
        if (giving)
        {
            if (Axe != null)
            {
                player.SendIngameError("seraphhorizons-eidolon-axe-has", Lang.GetL(player.LanguageCode, "seraphhorizons:eidolon-axe-has"));
                return;
            }
            Hold(itemslot.TakeOutWhole());
            itemslot.MarkDirty();
        }
        else
        {
            var axe = Axe!;
            Hold(null);
            if (!player.InventoryManager.TryGiveItemstack(axe, true))
                entity.World.SpawnItemEntity(axe, byEntity.Pos.XYZ);
        }
        entity.World.PlaySoundAt(GiveSound, entity, null, true, 16);
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        if (Axe is not { } axe)
        {
            infotext.AppendLine(Lang.Get("seraphhorizons:eidolon-info-noaxe"));
            return;
        }
        int max = axe.Collectible.GetMaxDurability(axe);
        infotext.AppendLine(max > 0
            ? Lang.Get("seraphhorizons:eidolon-info-axe", axe.GetName(), axe.Collectible.GetRemainingDurability(axe), max)
            : Lang.Get("seraphhorizons:eidolon-info-axe-unbreaking", axe.GetName()));
    }

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
    {
        var help = new List<WorldInteraction>();
        if (Axe == null)
            help.Add(new WorldInteraction
            {
                ActionLangCode = "seraphhorizons:eidolon-help-axe-give",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = _axes ??= world.SearchItems(new AssetLocation("game", "axe-*")).Where(i => i is ItemAxe).Select(i => new ItemStack(i)).ToArray(),
            });
        else
            help.Add(new WorldInteraction
            {
                ActionLangCode = "seraphhorizons:eidolon-help-axe-take",
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "sneak",
                RequireFreeHand = true,
            });
        return help.ToArray();
    }
}
