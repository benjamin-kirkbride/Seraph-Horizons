using System.Text;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.CrucibleFurnace;

/// <summary>What a melting pot stack carries: the heats it has been through and, when it was pulled
/// before its charge melted, the charge.</summary>
public static class PotStack
{
    public const string HeatsKey = "seraphhorizonsPotHeats";
    public const string ChargeKey = "seraphhorizonsPotCharge";

    public static int Heats(ItemStack? stack) => stack?.Attributes.GetInt(HeatsKey) ?? 0;

    public static void SetHeats(ItemStack stack, int heats)
    {
        if (heats > 0)
            stack.Attributes.SetInt(HeatsKey, heats);
        else
            stack.Attributes.RemoveAttribute(HeatsKey);
    }

    public static List<ChargeItem> Charge(ItemStack? stack)
    {
        if (stack?.Attributes[ChargeKey] is not ITreeAttribute tree)
            return [];
        return tree.Select(e => new ChargeItem(e.Key, (e.Value as IntAttribute)?.value ?? 0)).Where(i => i.Count > 0).ToList();
    }

    public static void SetCharge(ItemStack stack, IReadOnlyList<ChargeItem> charge)
    {
        stack.Attributes.RemoveAttribute(ChargeKey);
        if (charge.Count == 0)
            return;
        var tree = new TreeAttribute();
        foreach (var item in charge)
            tree.SetInt(item.Code, item.Count);
        stack.Attributes[ChargeKey] = tree;
    }

    /// <summary>The charge as a line: "12x Iron bits, 1x Iron ingot".</summary>
    public static string Describe(IWorldAccessor world, IEnumerable<ChargeItem> charge) =>
        string.Join(", ", charge.Select(i =>
            $"{i.Count}x {(CrucibleFurnaceSystem.Resolve(world, i.Code) is { } c ? new ItemStack(c).GetName() : i.Code)}"));

    /// <summary>The heats line of a pot's tooltip and block info.</summary>
    public static string HeatsLine(int heats, int of) =>
        Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-heats", heats, of);
}

/// <summary>
/// The fired melting pot (<c>meltingpot-fired</c>): goes into the crucible furnace (two to its hole),
/// where it is charged and fired. Its tooltip gives its heats and any charge it carries (a pot pulled
/// before its charge melted keeps it, and takes it back into the furnace). Held, cold enough to hold, a
/// charged pot is emptied as the game's bucket is (<c>BlockLiquidContainerBase</c>): Ctrl and right-click
/// on a block, which drops the charge's items where the click landed, as a bucket drops what it spills as
/// items, and leaves the pot in the hand with its heats; one pot of a stack at a time, the emptied one
/// going to the inventory, as the bucket's does.
/// </summary>
public class BlockMeltingPot : Block
{
    private static readonly AssetLocation EmptySound = new("game", "sounds/block/gravel");

    /// <summary>Whether the pot in <paramref name="stack"/> can be emptied now: it has a charge and is not
    /// too hot to touch.</summary>
    public static bool Emptiable(IWorldAccessor world, ItemStack? stack) =>
        stack != null && PotEmpty.Emptiable(PotStack.Charge(stack).Count,
            stack.Collectible.GetTemperature(world, stack), GlobalConstants.TooHotToTouchTemperature);

    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        // the bucket's empty: Ctrl without Shift, on a block (in the air it does nothing)
        if (blockSel == null || !byEntity.Controls.CtrlKey || byEntity.Controls.ShiftKey || !Emptiable(byEntity.World, slot.Itemstack))
        {
            base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handHandling);
            return;
        }
        var player = (byEntity as EntityPlayer)?.Player;
        if (!byEntity.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
        {
            slot.MarkDirty();
            return;
        }
        handHandling = EnumHandHandling.PreventDefaultAction;
        if (byEntity.World.Side == EnumAppSide.Server)
            Empty(slot, byEntity, player, blockSel);
    }

    /// <summary>Empties one pot of the stack in <paramref name="slot"/> (server side): its charge dropped
    /// at the click, as items, the pot kept with its heats.</summary>
    private static void Empty(ItemSlot slot, EntityAgent byEntity, IPlayer? player, BlockSelection blockSel)
    {
        var world = byEntity.World;
        if (slot.Itemstack is not { } stack)
            return;
        var charge = PotStack.Charge(stack);
        var pot = stack;
        if (stack.StackSize > 1)
        {
            pot = stack.Clone();
            pot.StackSize = 1;
            stack.StackSize--;
        }
        PotStack.SetCharge(pot, []);
        if (pot != stack && !byEntity.TryGiveItemStack(pot))
            world.SpawnItemEntity(pot, byEntity.Pos.XYZ);
        slot.MarkDirty();

        var at = blockSel.Position.ToVec3d().Add(blockSel.HitPosition);
        foreach (var item in charge)
        {
            if (CrucibleFurnaceSystem.Resolve(world, item.Code) is not { } collectible)
                continue;
            foreach (int n in PotEmpty.Stacks(item.Count, collectible.MaxStackSize))
            {
                var dropped = world.SpawnItemEntity(new ItemStack(collectible, n), at);
                if (player != null)
                    dropped?.WatchedAttributes.SetString("byPlayerUid", player.PlayerUID);
            }
        }
        world.PlaySoundAt(EmptySound, at.X, at.Y, at.Z, null);
    }

    public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot) =>
        base.GetHeldInteractionHelp(inSlot).Append(new WorldInteraction
        {
            ActionLangCode = CrucibleFurnaceSystem.Domain + ":heldhelp-meltingpot-empty",
            HotKeyCode = "ctrl",
            MouseButton = EnumMouseButton.Right,
            ShouldApply = (_, _, _) => api != null && Emptiable(api.World, inSlot.Itemstack),
        });

    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
    {
        base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
        if (api == null)
            return;
        var config = CrucibleFurnaceSystem.Of(api).Config;
        dsc.AppendLine(PotStack.HeatsLine(PotStack.Heats(inSlot.Itemstack), config.PotHeats));
        var charge = PotStack.Charge(inSlot.Itemstack);
        if (charge.Count > 0)
            dsc.AppendLine(Lang.Get(CrucibleFurnaceSystem.Domain + ":meltingpot-charge", PotStack.Describe(world, charge)));
    }
}
