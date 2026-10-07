using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using SeraphHorizons.Mod.GearReclamation;
using SeraphHorizons.Mod.GearReclamation.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>The pot's cooking slots, as the firepit hands them to the pot.</summary>
internal sealed class CookingSlots(params ItemStack?[] stacks) : ISlotProvider
{
    public ItemSlot[] Slots { get; } = stacks.Select(s => (ItemSlot)new DummySlot(s)).ToArray();
}

/// <summary>
/// mods-src/seraphhorizons, GearReclamation (#474, #475, #477): the reclaimed gears, the degreasing
/// pot, the lime water and oil barrels, flash rust, and the oiled gear's roll in a player's
/// inventory.
/// </summary>
public partial class SharedWorldScenarios
{
    private ItemStack GearStack(string code, int size = 1) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
        : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

    [AtlasScenario]
    public void The_reclaimed_gears_exist_on_the_rusty_gears_shape()
    {
        var rusty = W.GetItem(new AssetLocation(GearCodes.Rusty))!;
        foreach (var code in new[] { GearCodes.Steel, GearCodes.Degreased, GearCodes.Pickled, GearCodes.Neutralized, GearCodes.Oiled })
        {
            var item = W.GetItem(new AssetLocation(code));
            Assert.True(item != null, $"no {code}");
            Assert.IsType<ItemReclaimedGear>(item);
            Assert.Equal("game:item/gear-rusty", item!.Shape.Base.ToString());
            Assert.NotNull(item.GetBehavior<CollectibleBehaviorGroundStorable>());
            Assert.True(item.Attributes["shelvable"].AsBool(), $"{code} is not shelvable");
            Assert.True(item.Attributes["displaycaseable"].AsBool(), $"{code} is not display-caseable");
            string nameKey = code.Replace("seraphhorizons:", "seraphhorizons:item-");
            Assert.NotEqual(nameKey, Lang.Get(nameKey));
            Assert.Contains("general", item.CreativeInventoryTabs);
        }
        // The server keeps no item textures, so the asset is read: every gear replaces the rusty
        // gear's one texture key, and (under a full install, which ships textures) the files exist.
        var asset = JObject.Parse(World.Api.Assets.Get(new AssetLocation("seraphhorizons:itemtypes/gear.json")).ToText());
        bool Exists(string texture) => World.Api.Assets.Exists(new AssetLocation(texture).WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
        bool hasTextures = Exists("game:block/metal/tarnished/rusty-iron");
        foreach (var (type, textures) in (JObject)asset["texturesByType"]!)
        {
            var texture = (string?)textures!["rusty-iron"]?["base"];
            Assert.True(texture != null, $"{type} keeps the rusty gear's texture key");
            Assert.True(!hasTextures || Exists(texture!), $"{texture} does not exist");
        }
        Assert.Equal("game:block/metal/ingot/steel", (string?)asset["texturesByType"]!["*-steel"]!["rusty-iron"]!["base"]);
        var large = W.GetItem(new AssetLocation(GearCodes.LargeSteel));
        Assert.NotNull(large);
        Assert.Equal("game:block/machine/jonas/tempgear", large!.Shape.Base.ToString());
        Assert.Equal("Large steel gear", Lang.GetMatching("seraphhorizons:item-largegear-steel"));
        Assert.Equal("Steel gear", new ItemStack(W.GetItem(new AssetLocation(GearCodes.Steel))!).GetName());

        // The rusty gear itself is untouched but for its salvage section.
        Assert.Equal(1, rusty.Attributes["currency"]["value"].AsInt());
        var sections = rusty.Attributes["handbook"]["extraSections"].AsArray();
        Assert.Contains(sections, s => s["text"].AsString() == "seraphhorizons:gearreclamation-rusty-text");
        Assert.StartsWith("Boil, pickle, neutralize, oil. One in ten comes out sound.",
            Lang.Get("seraphhorizons:gearreclamation-rusty-text"));
        Assert.Contains("rusty gears", Lang.Get("seraphhorizons:gearreclamation-text"));
    }

    [AtlasScenario]
    public void The_bare_gears_flash_rust_back_into_rusty_gears()
    {
        var hours = FlashRustHours.For(GearReclamationSystem.Of(World.Api).Config.FlashRustHours);
        foreach (var code in new[] { GearCodes.Pickled, GearCodes.Neutralized })
        {
            var item = W.GetItem(new AssetLocation(code))!;
            var perish = Assert.Single(item.TransitionableProps, t => t.Type == EnumTransitionType.Perish);
            Assert.Equal(hours.Fresh, perish.FreshHours.avg, 3);
            Assert.Equal(hours.Transition, perish.TransitionHours.avg, 3);
            Assert.Equal(GearCodes.Rusty, perish.TransitionedStack.ResolvedItemstack.Collectible.Code.ToString());

            // Left bare past fresh and rusting, the stack is a stack of rusty gears.
            var slot = new DummySlot(GearStack(code, 12));
            item.UpdateAndGetTransitionStates(W, slot);
            var state = (ITreeAttribute)slot.Itemstack.Attributes["transitionstate"];
            state.SetDouble("lastUpdatedTotalHours", state.GetDouble("lastUpdatedTotalHours") - hours.Fresh / 2);
            var half = item.UpdateAndGetTransitionStates(W, slot);
            Assert.Equal(code, slot.Itemstack.Collectible.Code.ToString());
            Assert.True(half.Single(s => s.Props.Type == EnumTransitionType.Perish).FreshHoursLeft > 0);
            state = (ITreeAttribute)slot.Itemstack.Attributes["transitionstate"];
            state.SetDouble("lastUpdatedTotalHours", state.GetDouble("lastUpdatedTotalHours") - hours.Total - 1);
            slot.Itemstack.Collectible.UpdateAndGetTransitionStates(W, slot);
            Assert.Equal(GearCodes.Rusty, slot.Itemstack.Collectible.Code.ToString());
            Assert.Equal(12, slot.Itemstack.StackSize);
        }
        // The steel, oiled and degreased gears never rust.
        foreach (var code in new[] { GearCodes.Steel, GearCodes.Degreased, GearCodes.Oiled })
            Assert.Empty(W.GetItem(new AssetLocation(code))!.TransitionableProps ?? []);
    }

    [AtlasScenario]
    public void Rusty_gears_boil_clean_in_lye_a_quarter_litre_a_gear()
    {
        var recipes = World.Api.GetCookingRecipes().Where(r => r.Code.StartsWith("seraphhorizons-gear-degrease")).ToList();
        Assert.Equal(3, recipes.Count);
        // Every alkali of the pack is there: Oils Resoaped's lye, Expanded Matter's caustic and washing soda.
        foreach (var r in recipes)
            Assert.Equal(["oils:lyeportion", "em:causticsodaportion", "em:washingsodaportion"],
                r.Ingredients![1].ValidStacks.Select(s => s.ResolvedItemstack.Collectible.Code.ToString()));

        var pot = (BlockCookingContainer)W.GetBlock(new AssetLocation("game:claypot-blue-fired"))!;
        foreach (var (liquid, slots, perSlot) in new[] { ("oils:lyeportion", 1, 4), ("em:washingsodaportion", 2, 2), ("em:causticsodaportion", 3, 2) })
        {
            int gears = slots * perSlot;
            var stacks = Enumerable.Range(0, slots).Select(_ => (ItemStack?)GearStack(GearCodes.Rusty, perSlot))
                .Append(GearStack(liquid, gears * 25)).ToArray();
            var cooking = new CookingSlots(stacks);
            Assert.Equal($"seraphhorizons-gear-degrease-{slots}",
                pot.GetMatchingCookingRecipe(W, pot.GetCookingStacks(cooking), out int servings)!.Code);
            Assert.Equal(perSlot, servings);

            var input = new DummySlot(new ItemStack(pot));
            pot.DoSmelt(W, cooking, input, new DummySlot());
            Assert.Equal(GearCodes.Degreased, cooking.Slots[0].Itemstack.Collectible.Code.ToString());
            Assert.Equal(gears, cooking.Slots[0].Itemstack.StackSize);
            Assert.All(cooking.Slots.Skip(1), s => Assert.True(s.Empty));
            Assert.Equal("game:dirtyclaypot-blue-cooked", input.Itemstack.Collectible.Code.ToString());
        }

        // The wrong amount of lye is no recipe the pot will cook.
        var wrong = new CookingSlots(GearStack(GearCodes.Rusty, 4), GearStack("oils:lyeportion", 50));
        pot.GetMatchingCookingRecipe(W, pot.GetCookingStacks(wrong), out int none);
        Assert.Equal(-1, none);
    }

    /// <summary>Seals <paramref name="gears"/> in a barrel with <paramref name="liquid"/>, checks it is
    /// not done <paramref name="hours"/> − 0.5 hours in and is at <paramref name="hours"/>; returns the
    /// barrel.</summary>
    private async Task<BlockEntityBarrel> SealedBarrel(Vintagestory.API.MathTools.BlockPos pos, ItemStack gears, ItemStack liquid, double hours, string expect)
    {
        World.SetBlock("game:rock-granite", pos.DownCopy());
        World.SetBlock("game:barrel", pos);
        await World.Ticks(2);
        var barrel = Assert.IsType<BlockEntityBarrel>(W.BlockAccessor.GetBlockEntity(pos));
        barrel.Inventory[0].Itemstack = gears;
        barrel.Inventory[0].MarkDirty();
        barrel.Inventory[1].Itemstack = liquid;
        barrel.Inventory[1].MarkDirty();
        AccessTools.Method(typeof(BlockEntityBarrel), "FindMatchingRecipe", []).Invoke(barrel, []);
        Assert.NotNull(barrel.CurrentRecipe);
        Assert.Equal(hours, barrel.CurrentRecipe!.SealHours);
        barrel.SealBarrel();
        var tick = AccessTools.Method(typeof(BlockEntityBarrel), "OnEvery3Second");
        barrel.SealedSinceTotalHours -= hours - 0.5;
        tick.Invoke(barrel, [3f]);
        Assert.True(barrel.Sealed);
        Assert.Equal(gears.Collectible.Code, barrel.Inventory[0].Itemstack.Collectible.Code);
        barrel.SealedSinceTotalHours -= 0.6;
        tick.Invoke(barrel, [3f]);
        Assert.False(barrel.Sealed);
        Assert.Equal(expect, barrel.Inventory[0].Itemstack.Collectible.Code.ToString());
        return barrel;
    }

    [AtlasScenario]
    public async Task Pickled_gears_neutralize_then_oil_in_sealed_barrels_and_roll_when_taken()
    {
        var p = await World.JoinPlayer("gearreclaimer");
        var pos = World.Spawn.AddCopy(-100, 12, -100);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        for (int dx = -1; dx <= 2; dx++)
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = 0; dy < 3; dy++)
                World.SetBlock("game:air", pos.AddCopy(dx, dy, dz));
        await World.Ticks(2);

        // 20 pickled gears in 3 litres of lime water: 2 litres used, 1 left.
        var limed = await SealedBarrel(pos, GearStack(GearCodes.Pickled, 20), GearStack("game:limewaterportion", 300), 2, GearCodes.Neutralized);
        Assert.Equal(20, limed.Inventory[0].StackSize);
        Assert.Equal(100, limed.Inventory[1].StackSize);

        // The neutralized gears in 3 litres of lard: 2 used.
        var oiled = await SealedBarrel(pos.AddCopy(1, 0, 0), limed.Inventory[0].TakeOutWhole(), GearStack("expandedfoods:lard", 300), 4, GearCodes.Oiled);
        Assert.Equal(20, oiled.Inventory[0].StackSize);
        Assert.Equal(100, oiled.Inventory[1].StackSize);
        // Hardened lard oils as well, a fifth of a litre a gear.
        var hard = await SealedBarrel(pos.AddCopy(2, 0, 0), GearStack(GearCodes.Neutralized, 5), GearStack("expandedfoods:hardlardliquid", 6), 4, GearCodes.Oiled);
        Assert.Equal(1, hard.Inventory[1].StackSize);

        // Taken out of the barrel into a hand: by the next ticks only steel gears and bits are left.
        var player = p.Player;
        foreach (var inv in player.InventoryManager.Inventories.Values)
            if (inv.ClassName != GlobalConstants.creativeInvClassName)
                foreach (var s in inv) { s.Itemstack = null; s.MarkDirty(); }
        var hand = player.InventoryManager.ActiveHotbarSlot;
        var op = new ItemStackMoveOperation(W, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 20);
        Assert.Equal(20, oiled.Inventory[0].TryPutInto(hand, ref op));
        Assert.True(oiled.Inventory[0].Empty);
        await World.Ticks(5);
        var (steel, bits, oiledLeft) = Count(player);
        Assert.Equal(0, oiledLeft);
        Assert.Equal(20, steel + bits);
        Assert.NotEqual(GearCodes.Oiled, hand.Itemstack?.Collectible.Code.ToString());
    }

    private static (int Steel, int Bits, int Oiled) Count(IPlayer player)
    {
        int steel = 0, bits = 0, oiled = 0;
        foreach (var inv in player.InventoryManager.Inventories.Values)
        {
            if (inv.ClassName == GlobalConstants.creativeInvClassName)
                continue;
            foreach (var s in inv)
                switch (s.Itemstack?.Collectible.Code.ToString())
                {
                    case GearCodes.Steel: steel += s.StackSize; break;
                    case GearCodes.SteelBit: bits += s.StackSize; break;
                    case GearCodes.Oiled: oiled += s.StackSize; break;
                }
        }
        return (steel, bits, oiled);
    }

    [AtlasScenario]
    public async Task Oiled_gears_come_out_one_in_ten_sound_over_a_large_sample()
    {
        var p = await World.JoinPlayer("gearlottery");
        await p.TeleportTo(World.Spawn.AddCopy(-100, 12, -110));
        var player = p.Player;
        var system = GearReclamationSystem.Of(World.Api);
        var config = system.Config;
        const int rounds = 40, perRound = 64;
        int steel = 0, bits = 0;
        var hotbar = player.InventoryManager.GetHotbarInventory();
        for (int round = 0; round < rounds; round++)
        {
            foreach (var s in hotbar) { s.Itemstack = null; s.MarkDirty(); }
            var slot = hotbar[round % 5];
            slot.Itemstack = GearStack(GearCodes.Oiled, perRound);
            slot.MarkDirty();
            await World.Ticks(2);
            var (st, bi, left) = Count(player);
            Assert.Equal(0, left);
            Assert.Equal(perRound, st + bi / Math.Max(1, config.BitsPerFailedGear));
            steel += st;
            bits += bi;
        }
        int gears = rounds * perRound;
        var (expected, _) = GearLottery.Expected(gears, config.UsableGearChance, config.BitsPerFailedGear);
        double sd = GearLottery.SteelDeviation(gears, config.UsableGearChance);
        output.WriteLine($"{gears} oiled gears: {steel} sound (expected {expected:0.#} ± {sd:0.#}), {bits} bits");
        Assert.InRange(steel, expected - 4 * sd, expected + 4 * sd);
        Assert.Equal((gears - steel) * config.BitsPerFailedGear, bits);

        // A stack the slot hook never saw (a direct write, no MarkDirty) is caught by the sweep or on login.
        foreach (var s in hotbar) { s.Itemstack = null; s.MarkDirty(); }
        hotbar[7].Itemstack = GearStack(GearCodes.Oiled, 10);
        Assert.Equal(1, system.ResolveAll(player));
        Assert.Equal(0, Count(player).Oiled);
        hotbar[8].Itemstack = GearStack(GearCodes.Oiled, 10);
        for (int i = 0; i < 60 && Count(player).Oiled > 0; i++)
            await World.Ticks(5);
        Assert.Equal(0, Count(player).Oiled);
    }

    /// <summary>Ages a stack of flash-rusting gears past rusty: the transition runs as the game runs
    /// it on the server, through the stack's own <c>UpdateAndGetTransitionStates</c>.</summary>
    private void FlashRustNow(ItemSlot slot)
    {
        var hours = FlashRustHours.For(GearReclamationSystem.Of(World.Api).Config.FlashRustHours);
        slot.Itemstack.Collectible.UpdateAndGetTransitionStates(W, slot);
        var state = (ITreeAttribute)slot.Itemstack.Attributes["transitionstate"];
        state.SetDouble("lastUpdatedTotalHours", state.GetDouble("lastUpdatedTotalHours") - hours.Total - 1);
        slot.Itemstack.Collectible.UpdateAndGetTransitionStates(W, slot);
    }

    private static int CountIn(IEnumerable<ItemSlot> slots, string code) =>
        slots.Where(s => s.Itemstack?.Collectible.Code.ToString() == code).Sum(s => s.StackSize);

    // #477, #482: a bare steel gear that flash-rusts is a rusty gear three times in four
    // (FlashRustLossChance 0.25) and a steel bit otherwise, so dipping steel gears is no free way to
    // currency. In a player's inventory the bits go to the player; in a chest, to its other slots.
    [AtlasScenario]
    public async Task Bare_steel_gears_flash_rust_a_quarter_to_steel_bits()
    {
        var system = GearReclamationSystem.Of(World.Api);
        var config = system.Config;
        Assert.Equal(0.25, config.FlashRustLossChance);
        var bare = W.GetItem(new AssetLocation(GearCodes.SteelBare))!;
        Assert.IsType<ItemReclaimedGear>(bare);
        var hours = FlashRustHours.For(config.FlashRustHours);
        var perish = Assert.Single(bare.TransitionableProps, t => t.Type == EnumTransitionType.Perish);
        Assert.Equal(hours.Fresh, perish.FreshHours.avg, 3);
        Assert.Equal(GearCodes.Rusty, perish.TransitionedStack.ResolvedItemstack.Collectible.Code.ToString());

        var p = await World.JoinPlayer("flashrust");
        await p.TeleportTo(World.Spawn.AddCopy(-100, 12, -120));
        var player = p.Player;
        IEnumerable<ItemSlot> Held() => player.InventoryManager.Inventories.Values
            .Where(inv => inv.ClassName != GlobalConstants.creativeInvClassName).SelectMany(inv => inv);
        var hotbar = player.InventoryManager.GetHotbarInventory();
        const int rounds = 20, perRound = 64;
        int rusty = 0, bits = 0, before = system.FlashRusted;
        for (int round = 0; round < rounds; round++)
        {
            foreach (var s in Held()) { s.Itemstack = null; s.MarkDirty(); }
            var slot = hotbar[round % 5];
            slot.Itemstack = GearStack(GearCodes.SteelBare, perRound);
            FlashRustNow(slot);
            Assert.Equal(GearCodes.Rusty, slot.Itemstack?.Collectible.Code.ToString());
            Assert.Equal(0, CountIn(Held(), GearCodes.SteelBare));
            int r = CountIn(Held(), GearCodes.Rusty), b = CountIn(Held(), GearCodes.SteelBit);
            Assert.Equal(perRound, r + b / Math.Max(1, config.BitsPerFailedGear));
            rusty += r;
            bits += b;
        }
        Assert.Equal(rounds, system.FlashRusted - before);
        int gears = rounds * perRound;
        var (expected, _) = FlashRustLoss.Expected(gears, config.FlashRustLossChance, config.BitsPerFailedGear);
        double sd = GearLottery.SteelDeviation(gears, FlashRustLoss.KeepChance(config.FlashRustLossChance));
        output.WriteLine($"{gears} bare steel gears flash-rusted: {rusty} rusty (expected {expected:0.#} ± {sd:0.#}), {bits} bits");
        Assert.InRange(rusty, expected - 4 * sd, expected + 4 * sd);
        Assert.Equal((gears - rusty) * config.BitsPerFailedGear, bits);

        // In a chest the rusty gears keep the slot and the bits take another.
        var pos = World.Spawn.AddCopy(-104, 12, -120);
        World.SetBlock("game:air", pos.UpCopy());
        World.SetBlock("game:chest-east", pos);
        await World.Ticks(2);
        var chest = W.BlockAccessor.GetBlockEntity(pos) as BlockEntityContainer
                    ?? throw new Xunit.Sdk.XunitException("chest placed without a block entity");
        chest.Inventory[0].Itemstack = GearStack(GearCodes.SteelBare, perRound);
        FlashRustNow(chest.Inventory[0]);
        Assert.Equal(GearCodes.Rusty, chest.Inventory[0].Itemstack?.Collectible.Code.ToString());
        int inChest = CountIn(chest.Inventory, GearCodes.Rusty) + CountIn(chest.Inventory, GearCodes.SteelBit) / Math.Max(1, config.BitsPerFailedGear);
        Assert.Equal(perRound, inChest);
        Assert.True(CountIn(chest.Inventory, GearCodes.SteelBit) > 0, "no bits from 64 gears");
    }
}
