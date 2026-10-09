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
/// mods-src/seraphhorizons, GearReclamation (#474, #475, #477): the reclaimed stainless gears, the
/// degreasing pot, the lime water barrel, and the neutralized gear's roll in a player's inventory.
/// </summary>
public partial class SharedWorldScenarios
{
    private ItemStack GearStack(string code, int size = 1) =>
        W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
        : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

    private static readonly string[] AllGears =
        [GearCodes.Stainless, GearCodes.Degreased, GearCodes.Pickled, GearCodes.Passivated, GearCodes.Neutralized];

    [AtlasScenario]
    public void The_reclaimed_gears_exist_on_the_rusty_gears_shape()
    {
        var rusty = W.GetItem(new AssetLocation(GearCodes.Rusty))!;
        foreach (var code in AllGears)
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
        Assert.Equal("game:block/metal/ingot/stainlesssteel", (string?)asset["texturesByType"]!["*-stainless"]!["rusty-iron"]!["base"]);
        var large = W.GetItem(new AssetLocation(GearCodes.LargeStainless));
        Assert.NotNull(large);
        Assert.Equal("game:block/machine/jonas/tempgear", large!.Shape.Base.ToString());
        Assert.Equal("Large stainless gear", new ItemStack(large).GetName());
        Assert.Equal("Stainless gear", new ItemStack(W.GetItem(new AssetLocation(GearCodes.Stainless))!).GetName());
        // The oiled and the bare steel gear are gone (#484's stainless rework).
        Assert.Null(W.GetItem(new AssetLocation("seraphhorizons:gear-oiled")));
        Assert.Null(W.GetItem(new AssetLocation("seraphhorizons:gear-steel-bare")));

        // The rusty gear itself is untouched but for its salvage section and its description, which
        // says it is corroded stainless steel.
        Assert.Equal(1, rusty.Attributes["currency"]["value"].AsInt());
        var sections = rusty.Attributes["handbook"]["extraSections"].AsArray();
        Assert.Contains(sections, s => s["text"].AsString() == "seraphhorizons:gearreclamation-rusty-text");
        Assert.Contains("Degrease, pickle, passivate, neutralize. One in ten comes out sound.",
            Lang.Get("seraphhorizons:gearreclamation-rusty-text"));
        Assert.Contains("stainless steel", Lang.Get("game:itemdesc-gear-rusty"));
        Assert.Contains("rusty gears", Lang.Get("seraphhorizons:gearreclamation-text"));
        Assert.DoesNotContain("brine", Lang.Get("seraphhorizons:gearreclamation-text"));
    }

    // No flash rust: stainless stays stainless, so every gear of the line keeps as long as it is left.
    [AtlasScenario]
    public void The_reclaimed_gears_never_rust()
    {
        foreach (var code in AllGears)
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
    public async Task Passivated_gears_neutralize_in_a_sealed_barrel_and_roll_when_taken()
    {
        var p = await World.JoinPlayer("gearreclaimer");
        var pos = World.Spawn.AddCopy(-100, 12, -100);
        await p.TeleportTo(pos.AddCopy(2, 0, 0));
        for (int dx = -1; dx <= 2; dx++)
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = 0; dy < 3; dy++)
                World.SetBlock("game:air", pos.AddCopy(dx, dy, dz));
        await World.Ticks(2);

        // 20 passivated gears in 3 litres of lime water: 2 litres used, 1 left.
        var limed = await SealedBarrel(pos, GearStack(GearCodes.Passivated, 20), GearStack("game:limewaterportion", 300), 2, GearCodes.Neutralized);
        Assert.Equal(20, limed.Inventory[0].StackSize);
        Assert.Equal(100, limed.Inventory[1].StackSize);
        // Pickled gears are not neutralized: they are passivated first. The oil barrel is gone.
        Assert.DoesNotContain(World.Api.GetBarrelRecipes(), r => r.Ingredients.Any(i => i.Code?.ToString() == GearCodes.Pickled));
        Assert.DoesNotContain(World.Api.GetBarrelRecipes(), r => r.Code == "seraphhorizons-gear-oil");

        // Taken out of the barrel into a hand: by the next ticks only stainless gears and bits are left.
        var player = p.Player;
        foreach (var inv in player.InventoryManager.Inventories.Values)
            if (inv.ClassName != GlobalConstants.creativeInvClassName)
                foreach (var s in inv) { s.Itemstack = null; s.MarkDirty(); }
        var hand = player.InventoryManager.ActiveHotbarSlot;
        var op = new ItemStackMoveOperation(W, EnumMouseButton.Left, 0, EnumMergePriority.AutoMerge, 20);
        Assert.Equal(20, limed.Inventory[0].TryPutInto(hand, ref op));
        Assert.True(limed.Inventory[0].Empty);
        await World.Ticks(5);
        var (sound, bits, left) = Count(player);
        Assert.Equal(0, left);
        Assert.Equal(20, sound + bits);
        Assert.NotEqual(GearCodes.Neutralized, hand.Itemstack?.Collectible.Code.ToString());
    }

    private static (int Sound, int Bits, int Neutralized) Count(IPlayer player)
    {
        int sound = 0, bits = 0, neutralized = 0;
        foreach (var inv in player.InventoryManager.Inventories.Values)
        {
            if (inv.ClassName == GlobalConstants.creativeInvClassName)
                continue;
            foreach (var s in inv)
                switch (s.Itemstack?.Collectible.Code.ToString())
                {
                    case GearCodes.Stainless: sound += s.StackSize; break;
                    case GearCodes.StainlessBit: bits += s.StackSize; break;
                    case GearCodes.Neutralized: neutralized += s.StackSize; break;
                }
        }
        return (sound, bits, neutralized);
    }

    [AtlasScenario]
    public async Task Neutralized_gears_come_out_one_in_ten_sound_over_a_large_sample()
    {
        var p = await World.JoinPlayer("gearlottery");
        await p.TeleportTo(World.Spawn.AddCopy(-100, 12, -110));
        var player = p.Player;
        var system = GearReclamationSystem.Of(World.Api);
        var config = system.Config;
        const int rounds = 40, perRound = 64;
        int sound = 0, bits = 0;
        var hotbar = player.InventoryManager.GetHotbarInventory();
        for (int round = 0; round < rounds; round++)
        {
            foreach (var s in hotbar) { s.Itemstack = null; s.MarkDirty(); }
            var slot = hotbar[round % 5];
            slot.Itemstack = GearStack(GearCodes.Neutralized, perRound);
            slot.MarkDirty();
            await World.Ticks(2);
            var (st, bi, left) = Count(player);
            Assert.Equal(0, left);
            Assert.Equal(perRound, st + bi / Math.Max(1, config.BitsPerFailedGear));
            sound += st;
            bits += bi;
        }
        int gears = rounds * perRound;
        var (expected, _) = GearLottery.Expected(gears, config.UsableGearChance, config.BitsPerFailedGear);
        double sd = GearLottery.SoundDeviation(gears, config.UsableGearChance);
        output.WriteLine($"{gears} neutralized gears: {sound} sound (expected {expected:0.#} ± {sd:0.#}), {bits} stainless bits");
        Assert.InRange(sound, expected - 4 * sd, expected + 4 * sd);
        Assert.Equal((gears - sound) * config.BitsPerFailedGear, bits);

        // A stack the slot hook never saw (a direct write, no MarkDirty) is caught by the sweep or on login.
        foreach (var s in hotbar) { s.Itemstack = null; s.MarkDirty(); }
        hotbar[7].Itemstack = GearStack(GearCodes.Neutralized, 10);
        Assert.Equal(1, system.ResolveAll(player));
        Assert.Equal(0, Count(player).Neutralized);
        hotbar[8].Itemstack = GearStack(GearCodes.Neutralized, 10);
        for (int i = 0; i < 60 && Count(player).Neutralized > 0; i++)
            await World.Ticks(5);
        Assert.Equal(0, Count(player).Neutralized);
    }
}
