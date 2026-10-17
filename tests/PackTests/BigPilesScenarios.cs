using Atlas.XUnit;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// Big piles, big crates and the rusty gear's stack (<see cref="StorageTweaksSystem"/>), with every
/// locked mod loaded: piles hold 64 stacks (vanilla's from their type files, More Piles' from its
/// code), every stacking pile of the pack included, in what a client gets too; a placed pile reads
/// it; crates hold ceil(slots * 32 / 25), a placed one too, and a crate saved with more slots than
/// its type gives keeps them; the rusty gear stacks to 64. The switches off are in
/// <see cref="SwitchesOffScenarios"/>.
/// </summary>
public partial class SharedWorldScenarios
{
    private CollectibleObject Collectible(string code) =>
        W.GetItem(new AssetLocation(code)) is { IsMissing: false } item ? item
        : W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? block
        : throw new Xunit.Sdk.XunitException($"no {code}");

    private static CollectibleBehaviorGroundStorable Pile(CollectibleObject c) =>
        c.GetBehavior<CollectibleBehaviorGroundStorable>() ?? throw new Xunit.Sdk.XunitException($"{c.Code} has no ground storage");

    [AtlasScenario]
    public void Firewood_ingots_and_More_Piles_blocks_pile_64_stacks_a_block()
    {
        // survival/itemtypes/resource/firewood.json: 32 a pile, 2 a model, 0.25 box by 0.125 a
        // layer; firewood stacks to 32, so 2048, a model every 128, 32 a Ctrl click.
        var firewood = Pile(Collectible("game:firewood")).StorageProps;
        Assert.Equal(2048, firewood.StackingCapacity);
        Assert.Equal(128, firewood.ItemsPerModel);
        Assert.Equal(2, firewood.TransferQuantity);
        Assert.Equal(32, firewood.BulkTransferQuantity);
        Assert.Equal(4, BigPileRules.Layers(firewood.CbScaleYByLayer, firewood.StackingCapacity));

        // resource/ingot.json: 64 a pile; an ingot stacks to 16, so 1024.
        var ingot = Pile(Collectible("game:ingot-copper")).StorageProps;
        Assert.Equal(1024, ingot.StackingCapacity);
        Assert.Equal(16, ingot.ItemsPerModel);
        Assert.Equal(16, ingot.BulkTransferQuantity);
        Assert.True(ingot.UpSolid);

        // More Piles' catch-all block pile (64, Ctrl), on cobblestone cut to 16 by SmallerRockStacks: 1024.
        var cobble = Collectible("game:cobblestone-granite");
        var cobblePile = Pile(cobble);
        Assert.Contains("MorePilesProperties", cobblePile.propertiesAtString);
        Assert.Equal(16, cobble.MaxStackSize);
        Assert.Equal(1024, cobblePile.StorageProps.StackingCapacity);
        Assert.Equal(16, cobblePile.StorageProps.BulkTransferQuantity);
        Assert.True(cobblePile.StorageProps.CtrlKey);
        // More Piles' oak slab pile, stacking to 64: 4096, 64 a Ctrl click.
        var slab = Pile(Collectible("game:plankslab-oak-down-free")).StorageProps;
        Assert.Equal(64 * Collectible("game:plankslab-oak-down-free").MaxStackSize, slab.StackingCapacity);

        output.WriteLine($"piles made big: {StorageTweaksSystem.Of(World.Api)!.PilesMadeBig}");
    }

    /// <summary>Every stacking pile of the loaded pack holds 64 of its stacks (or kept more): a mod
    /// that added a pile after the pass would fail this.</summary>
    [AtlasScenario]
    public void Every_stacking_pile_of_the_pack_is_big()
    {
        var small = new List<string>();
        int piles = 0;
        foreach (var c in W.Collectibles)
        {
            if (c?.Code == null || c.CollectibleBehaviors == null) continue;
            foreach (var g in c.CollectibleBehaviors.OfType<CollectibleBehaviorGroundStorable>())
            {
                if (g.StorageProps?.Layout != EnumGroundStorageLayout.Stacking) continue;
                piles++;
                if (g.StorageProps.StackingCapacity < BigPileRules.StacksPerPile * Math.Max(1, c.MaxStackSize))
                    small.Add($"{c.Code} ({g.StorageProps.StackingCapacity} of stack {c.MaxStackSize})");
            }
        }
        output.WriteLine($"{piles} stacking piles");
        Assert.True(piles > 1000, $"only {piles} stacking piles");
        Assert.Empty(small);
    }

    /// <summary>Clutter does not pile (<see cref="ClutterPiles"/>): the game's clutter, banners, the
    /// Jonas lamp, the clutter bookshelves and the elevator rail, which More Piles' catch-all cube
    /// rule piled, have no ground storage and are not in its creative tab, in what a client gets
    /// too, and a client's own More Piles pass is undone; cube blocks keep their pile.</summary>
    [AtlasScenario]
    public void Clutter_does_not_pile()
    {
        string[] codes =
        [
            "game:clutter", "game:banner-banner", "game:jonas-lamp", "game:clutteredbookshelf",
            "game:clutter-elevator-elevatorrail",
        ];
        foreach (var code in codes)
        {
            var block = W.GetBlock(new AssetLocation(code));
            Assert.True(block is { Id: > 0 }, $"no {code}");
            Assert.False(block!.HasBehavior<CollectibleBehaviorGroundStorable>(), $"{code} piles");
            Assert.DoesNotContain(ClutterPiles.GroundStorableTab, block.CreativeInventoryTabs ?? []);
            var packet = BlockTypeNet.GetBlockTypePacket(block, World.Api.ClassRegistry);
            Assert.DoesNotContain(packet.Behaviors.Take(packet.BehaviorsCount), b => b.Code == "GroundStorable");
        }
        // Every block drawn from its attributes: none piles.
        var piled = W.Blocks
            .Where(b => b?.Code != null && b.HasBehavior<CollectibleBehaviorGroundStorable>()
                && (b is BlockShapeFromAttributes || b.BlockEntityBehaviors?.Any(e => e.Name == "ShapeFromAttributes") == true))
            .Select(b => b.Code.ToString())
            .ToList();
        Assert.Empty(piled);

        // A client's More Piles gives the clutter its pile again from its own config; the pass takes it away.
        var clutter = W.GetBlock(new AssetLocation("game:clutter"))!;
        var behaviors = clutter.CollectibleBehaviors;
        try
        {
            var morePiles = new CollectibleBehaviorGroundStorable(clutter);
            morePiles.Initialize(new JsonObject(JToken.Parse(
                "{\"Layout\":\"Stacking\",\"StackingCapacity\":64,\"MorePilesProperties\":{\"MatchesShape\":\"game:block/basic/cube\"}}")));
            clutter.CollectibleBehaviors = [morePiles, .. behaviors];
            Assert.Equal(1, ClutterPiles.Apply(World.Api));
            Assert.False(clutter.HasBehavior<CollectibleBehaviorGroundStorable>());
        }
        finally
        {
            clutter.CollectibleBehaviors = behaviors;
        }

        Assert.NotNull(W.GetBlock(new AssetLocation("game:cobblestone-granite"))!.GetBehavior<CollectibleBehaviorGroundStorable>());
        output.WriteLine($"clutter blocks unpiled: {StorageTweaksSystem.Of(World.Api)!.ClutterPilesTaken}");
        Assert.True(StorageTweaksSystem.Of(World.Api)!.ClutterPilesTaken >= codes.Length);
    }

    /// <summary>What a client gets: the item type packet's behavior text, read back the way a client
    /// reads it, is the big pile, and the client's own pass leaves it alone.</summary>
    [AtlasScenario]
    public void A_client_gets_the_big_pile_and_does_not_enlarge_it_again()
    {
        var item = W.GetItem(new AssetLocation("game:firewood"))!;
        var packet = ItemTypeNet.GetItemTypePacket(item, World.Api.ClassRegistry);
        var sent = packet.Behaviors.Take(packet.BehaviorsCount).Single(b => b.Code == "GroundStorable");
        var client = new CollectibleBehaviorGroundStorable(item);
        client.Initialize(new JsonObject(JToken.Parse(sent.Attributes)));
        Assert.Equal(BigPiles.Numbers(Pile(item).StorageProps), BigPiles.Numbers(client.StorageProps));
        Assert.True(BigPiles.IsBig(client));
        Assert.False(BigPiles.Enlarge(item, client));
        Assert.Equal(2048, client.StorageProps.StackingCapacity);

        // A block pile from More Piles, through the block type packet.
        var cobble = W.GetBlock(new AssetLocation("game:cobblestone-granite"))!;
        var blockPacket = BlockTypeNet.GetBlockTypePacket(cobble, World.Api.ClassRegistry);
        var blockSent = blockPacket.Behaviors.Take(blockPacket.BehaviorsCount).Single(b => b.Code == "GroundStorable");
        var blockClient = new CollectibleBehaviorGroundStorable(cobble);
        blockClient.Initialize(new JsonObject(JToken.Parse(blockSent.Attributes)));
        Assert.Equal(BigPiles.Numbers(Pile(cobble).StorageProps), BigPiles.Numbers(blockClient.StorageProps));
    }

    /// <summary>A placed firewood pile takes 2048 and, full, draws the 16 models and stands the one
    /// block a full pile of 32 did; half full, half of that. At (78, 12, -74).</summary>
    [AtlasScenario]
    public async Task A_placed_firewood_pile_holds_2048_and_looks_full_when_full()
    {
        var pos = World.Spawn.AddCopy(78, 12, -74);
        World.SetBlock("game:rock-granite", pos.DownCopy());
        World.SetBlock("game:groundstorage", pos);
        await World.Ticks(2);
        try
        {
            var be = (BlockEntityGroundStorage)W.BlockAccessor.GetBlockEntity(pos)!;
            var firewood = W.GetItem(new AssetLocation("game:firewood"))!;
            be.Inventory[0].Itemstack = new ItemStack(firewood, 2048);
            be.DetermineStorageProperties(null);
            Assert.Equal(2048, be.Capacity);
            Assert.Equal(16, be.ItemModelCount);
            Assert.Equal(1f, be.GetCollisionBoxes()![0].Y2, 3);

            be.Inventory[0].Itemstack.StackSize = 1024;
            be.DetermineStorageProperties(null);
            Assert.Equal(8, be.ItemModelCount);
            Assert.Equal(0.5f, be.GetCollisionBoxes()![0].Y2, 3);
        }
        finally
        {
            World.SetBlock("game:air", pos);
        }
    }

    private static int CrateSlots(Block crate, string type) =>
        crate.Attributes["properties"][type]["quantitySlots"].AsInt();

    [AtlasScenario]
    public void Crates_hold_32_over_25_of_their_slots()
    {
        // survival/blocktypes/wood/woodtyped/crate.json: aged 16, ebony and purpleheart 25, the rest 20.
        var crate = W.GetBlock(new AssetLocation("game:crate"))!;
        Assert.Equal(21, CrateSlots(crate, "wood-aged"));
        Assert.Equal(32, CrateSlots(crate, "wood-ebony"));
        Assert.Equal(32, CrateSlots(crate, "wood-purpleheart"));
        Assert.Equal(26, CrateSlots(crate, "*"));
        Assert.Equal(32, ((BlockCrate)crate).Props["wood-ebony"].QuantitySlots);
        Assert.Equal(26, ((BlockCrate)crate).Props["wood-oak"].QuantitySlots);
        // Material Needs' dark aged crate (mn-darkaged-crate.json): 16 and 20.
        var dark = W.GetBlock(new AssetLocation("materialneeds:crate"));
        Assert.True(dark is BlockCrate, "Material Needs' dark aged crate is missing");
        var darkTypes = ((JObject)dark!.Attributes["properties"].Token).Properties()
            .Select(p => ((JObject)p.Value)["quantitySlots"]!.Value<int>()).Order().ToList();
        Assert.Equal([21, 26], darkTypes);
        // Chests are not crates.
        var chest = W.Blocks.First(b => b?.Code is { Domain: "game" } c && (c.Path == "chest" || c.Path.StartsWith("chest-")) && b is BlockGenericTypedContainer);
        Assert.Equal(16, chest.Attributes["quantitySlots"]["normal-generic"].AsInt(0));
        output.WriteLine($"crate blocks made big: {StorageTweaksSystem.Of(World.Api)!.CratesMadeBig}");
    }

    /// <summary>A placed aged crate has 21 slots; one saved with 30 (a type that shrank, or the
    /// switch turned off) loads all 30 and what is in them. At (82, 12, -74).</summary>
    [AtlasScenario]
    public async Task A_placed_crate_has_its_new_slots_and_a_saved_one_keeps_more()
    {
        var pos = World.Spawn.AddCopy(82, 12, -74);
        World.SetBlock("game:rock-granite", pos.DownCopy());
        World.SetBlock("game:crate", pos);
        await World.Ticks(2);
        try
        {
            var be = (BlockEntityCrate)W.BlockAccessor.GetBlockEntity(pos)!;
            Assert.Equal(21, be.Inventory.Count);
            Assert.True(BigCrates.Patched);

            var tree = new TreeAttribute();
            be.ToTreeAttributes(tree);
            var inv = (TreeAttribute)tree.GetTreeAttribute("inventory");
            inv.SetInt("qslots", 30);
            var slots = (TreeAttribute)inv.GetTreeAttribute("slots");
            slots.SetItemstack("29", new ItemStack(W.GetItem(new AssetLocation("game:firewood"))!, 7));

            // As the game loads a block entity from a chunk: made, given its behaviors, then read.
            var loaded = (BlockEntityCrate)World.Api.ClassRegistry.CreateBlockEntity("Crate");
            loaded.CreateBehaviors(be.Block, W);
            loaded.FromTreeAttributes(tree, W);
            Assert.Equal(30, loaded.Inventory.Count);
            Assert.Equal(7, loaded.Inventory[29].StackSize);
        }
        finally
        {
            World.SetBlock("game:air", pos);
        }
    }

    [AtlasScenario]
    public void The_rusty_gear_stacks_to_64_and_the_temporal_gear_to_1()
    {
        Assert.Equal(RustyGearStack.Stack, W.GetItem(new AssetLocation(RustyGearStack.Code))!.MaxStackSize);
        Assert.Equal(1, W.GetItem(new AssetLocation("game:gear-temporal"))!.MaxStackSize);
        var packet = ItemTypeNet.GetItemTypePacket(W.GetItem(new AssetLocation(RustyGearStack.Code))!, World.Api.ClassRegistry);
        Assert.Equal(RustyGearStack.Stack, packet.MaxStackSize);

        // No grid or barrel recipe takes more rusty gears in one slot than now stack.
        bool IsGear(AssetLocation? code) => code?.ToString() == RustyGearStack.Code;
        var tooMany = W.GridRecipes.Where(r => r.Ingredients?.Values.Any(i => IsGear(i.Code) && i.Quantity > RustyGearStack.Stack) == true)
            .Select(r => r.Name?.ToString() ?? "?")
            .Concat(World.Api.GetBarrelRecipes()
                .Where(r => r.Ingredients.Any(i => IsGear(i.Code) && Math.Max(i.Quantity, i.ConsumeQuantity ?? 0) > RustyGearStack.Stack))
                .Select(r => r.Code ?? "?"))
            .ToList();
        Assert.Empty(tooMany);
    }

    /// <summary>The handbook's pile line reads the loaded piles: firewood is a pile of 2048 built up,
    /// cobblestone needs Ctrl, coal is an item pile of 16, a saw is set down and not a pile.</summary>
    [AtlasScenario]
    public void The_handbook_pile_line_says_what_piles_and_how()
    {
        var firewood = PileHandbook.Facts(World.Api, Collectible("game:firewood"));
        Assert.Equal(new PileText.Facts(PileText.Kind.Pile, 2048, 2, 32, false, true), firewood);
        var cobble = PileHandbook.Facts(World.Api, Collectible("game:cobblestone-granite"))!.Value;
        Assert.True(cobble.RequiresCtrl);
        Assert.Equal(1024, cobble.Capacity);
        var coal = PileHandbook.Facts(World.Api, Collectible("game:charcoal"));
        Assert.Equal(new PileText.Facts(PileText.Kind.Pile, 16, 2, 2, false, true), coal);
        Assert.Equal(PileText.Kind.Placed, PileHandbook.Facts(World.Api, Collectible("game:saw-copper"))!.Value.Kind);
        Assert.Null(PileHandbook.Facts(World.Api, Collectible("game:flint")));
    }
}
