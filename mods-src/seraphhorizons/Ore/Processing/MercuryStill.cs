using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.Ore.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Ore.Processing;

/// <summary>
/// Retorting in the still (#726; README "Ore processing: retorting mercury in the still"). The game's
/// still is its boiler (<c>game:verticalboiler</c>, <see cref="BlockEntityBoiler"/>, a firepit built
/// under it) and a condenser (<c>game:condenser</c>) beside it with a bucket under its spout. The
/// boiler holds one liquid stack, and once that is at 75 °C it drives one portion of its
/// <c>distillationProps</c>' distillate a step to the condenser, which takes <c>1 / ratio</c> items of
/// the liquid for each (1 L of spirits per 20 L of grain cider). That maths cannot give more than one
/// portion an item, and the boiler takes nothing but liquids, so the still's retort is four patches,
/// each acting only on an item the server marked with <see cref="AttributeKey"/> (cinnabar, amalgam):
/// <list type="bullet">
/// <item><see cref="BlockBoiler.OnBlockInteractStart"/>: right-click with the item puts the held stack
/// into an empty boiler (or onto the same item); with an empty hand a solid content comes back out,
/// with the sponge already left;</item>
/// <item><c>BlockEntityBoiler.DistProps</c>: such an item distils mercury, at the configured pace;</item>
/// <item><see cref="BlockEntityCondenser.ReceiveDistillate"/>: each step puts one portion of mercury in
/// the bucket (cooled or not, as the game does) and counts it against the item being retorted
/// (<see cref="MercuryRetort.Portion"/>); once an item's mercury is out it is used up, an amalgam's
/// gold or silver counted toward its sponge, which takes the boiler's slot when the last one is done;</item>
/// <item><see cref="BlockBoiler.GetPlacedBlockInfo"/>: names a solid content by count, not litres.</item>
/// </list>
/// The progress (the portions the current item still holds, the sponge so far) rides on the stack in
/// the boiler, so it survives a save and a broken boiler.
/// </summary>
public static class MercuryStill
{
    public const string HarmonyId = "seraphhorizons.oreprocessing.still";

    /// <summary>The item attribute that marks a retortable item: <c>{ mercury, portions, residue, pace }</c>.</summary>
    public const string AttributeKey = "seraphhorizonsRetort";

    /// <summary>The stack attributes of a stack being retorted: the portions its current item still
    /// holds, and the sponge items its retorted items have left so far.</summary>
    public const string OwedKey = "seraphhorizonsRetortOwed", ResidueKey = "seraphhorizonsRetortResidue";

    /// <summary>A retortable item's figures, read from its attribute.</summary>
    public sealed class Spec
    {
        public required double Portions;
        public required ItemStack Mercury;
        public required CollectibleObject? Residue;
        public required DistillationProps Props;
    }

    private static readonly ConditionalWeakTable<CollectibleObject, Spec?> Specs = new();

    private static readonly AccessTools.FieldRef<BlockEntityCondenser, ItemStack> LastDistillate =
        AccessTools.FieldRefAccess<BlockEntityCondenser, ItemStack>("lastReceivedDistillate");
    private static readonly AccessTools.FieldRef<BlockEntityCondenser, long> LastDistillateMs =
        AccessTools.FieldRefAccess<BlockEntityCondenser, long>("lastReceivedDistillateTotalMs");

    /// <summary>
    /// Marks every input of <paramref name="recovery"/> (<see cref="MercuryRetort.Inputs"/>) on the
    /// server, with the switch on, before the items go to clients. Returns the codes marked; none if the
    /// mercury item is missing (no Expanded Matter).
    /// </summary>
    public static List<string> Mark(IWorldAccessor world, OreRecovery recovery, ILogger logger)
    {
        var marked = new List<string>();
        var retort = recovery.Retort;
        if (world.GetItem(new AssetLocation(retort.Mercury)) is not { IsMissing: false })
        {
            logger.Warning("[seraphhorizons] Ore processing: no mercury item {0}; the still retorts nothing", retort.Mercury);
            return marked;
        }
        float pace = MercuryRetort.Pace(retort.PortionsPerSecond);
        foreach (var input in MercuryRetort.Inputs(recovery))
        {
            if (world.GetItem(new AssetLocation(input.Code)) is not { IsMissing: false } item)
            {
                logger.Warning("[seraphhorizons] Ore processing: the still's input {0} is not an item; left out", input.Code);
                continue;
            }
            if (input.Residue != null && world.GetItem(new AssetLocation(input.Residue)) is not { IsMissing: false })
            {
                logger.Warning("[seraphhorizons] Ore processing: {0}'s residue {1} is not an item; left out", input.Code, input.Residue);
                continue;
            }
            var token = item.Attributes?.Token as JObject ?? new JObject();
            token[AttributeKey] = new JObject
            {
                ["mercury"] = retort.Mercury,
                ["portions"] = input.Portions,
                ["residue"] = input.Residue,
                ["pace"] = pace,
            };
            item.Attributes = new JsonObject(token);
            marked.Add(input.Code);
        }
        return marked;
    }

    /// <summary>Whether any item carries the mark (on a client: whether the server's items do).</summary>
    public static bool AnyMarked(IWorldAccessor world) => world.Items.Any(i => i?.Attributes?[AttributeKey].Exists == true);

    /// <summary>A collectible's figures, or null if it does not retort. Cached per collectible.</summary>
    public static Spec? SpecOf(IWorldAccessor world, CollectibleObject? collectible)
    {
        if (collectible == null)
            return null;
        if (Specs.TryGetValue(collectible, out var cached))
            return cached;
        Spec? spec = null;
        var json = collectible.Attributes?[AttributeKey];
        if (json?.Exists == true && world.GetItem(new AssetLocation(json["mercury"].AsString(""))) is { IsMissing: false } mercury)
        {
            string? residue = json["residue"].AsString(null);
            var distilled = new JsonItemStack { Type = EnumItemClass.Item, Code = mercury.Code, StackSize = 1 };
            distilled.Resolve(world, "seraphhorizons still");
            spec = new Spec
            {
                Portions = Math.Max(1, json["portions"].AsDouble(1)),
                Mercury = new ItemStack(mercury),
                Residue = residue == null ? null : world.GetItem(new AssetLocation(residue)),
                Props = new DistillationProps { DistilledStack = distilled, Ratio = json["pace"].AsFloat(0.4f) },
            };
        }
        Specs.AddOrUpdate(collectible, spec);
        return spec;
    }

    public static void Patch(Harmony harmony)
    {
        harmony.Patch(AccessTools.PropertyGetter(typeof(BlockEntityBoiler), nameof(BlockEntityBoiler.DistProps)),
            postfix: new HarmonyMethod(typeof(MercuryStill), nameof(DistPropsPostfix)));
        harmony.Patch(AccessTools.Method(typeof(BlockEntityCondenser), nameof(BlockEntityCondenser.ReceiveDistillate)),
            prefix: new HarmonyMethod(typeof(MercuryStill), nameof(ReceivePrefix)));
        harmony.Patch(AccessTools.Method(typeof(BlockBoiler), nameof(BlockBoiler.OnBlockInteractStart)),
            prefix: new HarmonyMethod(typeof(MercuryStill), nameof(InteractPrefix)));
        harmony.Patch(AccessTools.Method(typeof(BlockBoiler), nameof(BlockBoiler.GetPlacedBlockInfo)),
            postfix: new HarmonyMethod(typeof(MercuryStill), nameof(InfoPostfix)));
    }

    private static void DistPropsPostfix(BlockEntityBoiler __instance, ref DistillationProps __result)
    {
        if (__result == null && __instance.Api?.World is { } world && SpecOf(world, __instance.InputStack?.Collectible) is { } spec)
            __result = spec.Props;
    }

    private static bool ReceivePrefix(BlockEntityCondenser __instance, ItemSlot sourceSlot, ref bool __result)
    {
        if (__instance.Api?.World is not { } world || sourceSlot.Itemstack is not { } input || SpecOf(world, input.Collectible) is not { } spec)
            return true;
        __result = Receive(__instance, world, sourceSlot, input, spec);
        return false;
    }

    /// <summary>One step of the still for a retortable item: false (the boiler tries its next
    /// condenser) with no bucket, or one holding something else or full.</summary>
    public static bool Receive(BlockEntityCondenser condenser, IWorldAccessor world, ItemSlot sourceSlot, ItemStack input, Spec spec)
    {
        var inventory = condenser.Inventory;
        var bucket = inventory[1].Itemstack;
        if (bucket?.Collectible is not BlockLiquidContainerTopOpened container)
        {
            LastDistillateMs(condenser) = -99999L;
            return false;
        }
        var content = container.GetContent(bucket);
        if (content != null && (!content.Equals(world, spec.Mercury, GlobalConstants.IgnoredStackAttributes) || container.IsFull(bucket)))
        {
            LastDistillateMs(condenser) = -99999L;
            return false;
        }
        if (world.Side == EnumAppSide.Server)
        {
            // As the game's condenser: without water half the portions are lost, with it half of them
            // take a portion of the water.
            bool cooled = !inventory[0].Empty;
            if (content == null)
            {
                var first = spec.Mercury.Clone();
                first.StackSize = 1;
                container.SetContent(bucket, first);
            }
            else if (cooled || world.Rand.NextDouble() > 0.5)
            {
                content.StackSize++;
                container.SetContent(bucket, content);
            }
            if (cooled && world.Rand.NextDouble() < 0.5)
                inventory[0].TakeOut(1);
            Advance(world, sourceSlot, input, spec);
            inventory[1].MarkDirty();
            condenser.MarkDirty(true);
        }
        LastDistillate(condenser) = spec.Mercury.Clone();
        LastDistillateMs(condenser) = world.ElapsedMilliseconds;
        return true;
    }

    /// <summary>Counts one portion against the stack being retorted; an item whose mercury is out is
    /// used up and its residue counted, and the residue takes the slot once the stack is gone.</summary>
    public static void Advance(IWorldAccessor world, ItemSlot slot, ItemStack input, Spec spec)
    {
        double? owed = input.Attributes.HasAttribute(OwedKey) ? input.Attributes.GetDouble(OwedKey) : null;
        var (next, done) = MercuryRetort.Portion(owed, spec.Portions);
        if (!done)
        {
            input.Attributes.SetDouble(OwedKey, next);
            slot.MarkDirty();
            return;
        }
        int residue = input.Attributes.GetInt(ResidueKey) + (spec.Residue != null ? 1 : 0);
        input.StackSize--;
        if (input.StackSize <= 0)
        {
            slot.Itemstack = spec.Residue != null && residue > 0 ? new ItemStack(spec.Residue, residue) : null;
            if (slot.Itemstack != null)
                // The sponge comes out as hot as the stack was.
                slot.Itemstack.Collectible.SetTemperature(world, slot.Itemstack, input.Collectible.GetTemperature(world, input));
        }
        else
        {
            input.Attributes.SetDouble(OwedKey, next);
            input.Attributes.SetInt(ResidueKey, residue);
        }
        slot.MarkDirty();
    }

    /// <summary>What a stack being retorted gives back when taken out: the items not yet done (the
    /// one under way whole again) and the residue so far.</summary>
    public static List<ItemStack> TakeBack(IWorldAccessor world, ItemStack stack)
    {
        var back = new List<ItemStack>();
        int residue = stack.Attributes.GetInt(ResidueKey);
        var clean = stack.Clone();
        clean.Attributes.RemoveAttribute(OwedKey);
        clean.Attributes.RemoveAttribute(ResidueKey);
        back.Add(clean);
        if (residue > 0 && SpecOf(world, stack.Collectible)?.Residue is { } sponge)
            back.Add(new ItemStack(sponge, residue));
        return back;
    }

    private static bool IsLiquid(ItemStack stack) => BlockLiquidContainerBase.GetContainableProps(stack) != null;

    private static bool InteractPrefix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is not BlockEntityBoiler boiler)
            return true;
        var hand = byPlayer.InventoryManager.ActiveHotbarSlot;
        var content = boiler.Inventory[0];
        if (!hand.Empty && SpecOf(world, hand.Itemstack.Collectible) != null)
        {
            if (!content.Empty && content.Itemstack.Collectible != hand.Itemstack.Collectible)
                return true;
            if (world.Side == EnumAppSide.Server)
            {
                int room = hand.Itemstack.Collectible.MaxStackSize - (content.Itemstack?.StackSize ?? 0);
                int moved = Math.Min(room, hand.StackSize);
                if (moved > 0)
                {
                    var taken = hand.TakeOut(moved);
                    if (content.Empty)
                        content.Itemstack = taken;
                    else
                        content.Itemstack.StackSize += moved;
                    hand.MarkDirty();
                    content.MarkDirty();
                    boiler.MarkDirty(true);
                }
            }
            (byPlayer as Vintagestory.API.Client.IClientPlayer)?.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            __result = true;
            return false;
        }
        if (hand.Empty && content.Itemstack is { } solid && !IsLiquid(solid))
        {
            if (world.Side == EnumAppSide.Server)
            {
                foreach (var stack in TakeBack(world, solid))
                    if (!byPlayer.InventoryManager.TryGiveItemstack(stack, true))
                        world.SpawnItemEntity(stack, blockSel.Position);
                content.Itemstack = null;
                content.MarkDirty();
                boiler.MarkDirty(true);
            }
            __result = true;
            return false;
        }
        return true;
    }

    private static void InfoPostfix(IWorldAccessor world, BlockPos pos, ref string __result)
    {
        if (world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityBoiler boiler || boiler.InputStack is not { } stack || IsLiquid(stack))
            return;
        var lines = __result.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        int at = lines.IndexOf(Lang.Get("Contents:"));
        var mine = new List<string> { " " + stack.StackSize + "x " + stack.GetName() };
        int residue = stack.Attributes.GetInt(ResidueKey);
        if (residue > 0 && SpecOf(world, stack.Collectible)?.Residue is { } sponge)
            mine.Add(" " + Lang.Get("seraphhorizons:retort-residue", residue, new ItemStack(sponge).GetName()));
        // The game's line under "Contents:" counts the stack as litres of a liquid.
        if (at >= 0 && at + 1 < lines.Count)
            lines.RemoveAt(at + 1);
        else
        {
            lines.Insert(0, Lang.Get("Contents:"));
            at = 0;
        }
        lines.InsertRange(at + 1, mine);
        var sb = new StringBuilder();
        foreach (var line in lines)
            sb.AppendLine(line);
        __result = sb.ToString().TrimEnd('\r', '\n');
    }
}
