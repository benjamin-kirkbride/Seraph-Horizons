using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.SteelBits;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>A cementation furnace of the game's, built from its own multiblock description on a
/// granite floor, filled by a player sneak-clicking its second half as the game calls it for a
/// right-click, and fired from two coal piles under it. Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal sealed class CoffinSite(IWorldSession world, BlockPos pos, IPlayer player)
{
    public const string Controller = "game:stonecoffinsection-granite-north";
    public const string Partner = "game:stonecoffinsection-granite-south";
    public const string Lid = "game:stonecoffinlid-granite";
    public const string Coke = "game:coke";
    public const string IronIngot = "game:ingot-iron";
    public const string BlisterSteel = "game:ingot-blistersteel";

    private static readonly AccessTools.FieldRef<BlockEntityStoneCoffin, MultiblockStructure> Ms = AccessTools.FieldRefAccess<BlockEntityStoneCoffin, MultiblockStructure>("ms");
    public static readonly AccessTools.FieldRef<BlockEntityStoneCoffin, double> Progress = AccessTools.FieldRefAccess<BlockEntityStoneCoffin, double>("progress");
    public static readonly AccessTools.FieldRef<BlockEntityStoneCoffin, bool> ReceivesHeat = AccessTools.FieldRefAccess<BlockEntityStoneCoffin, bool>("receivesHeat");
    public static readonly AccessTools.FieldRef<BlockEntityStoneCoffin, bool> ProcessComplete = AccessTools.FieldRefAccess<BlockEntityStoneCoffin, bool>("processComplete");

    private IWorldAccessor W => world.Api.World;

    /// <summary>The controller half (north); the other half is a block south of it.</summary>
    public BlockPos Pos { get; } = pos;
    public BlockPos PartnerPos => Pos.AddCopy(0, 0, 1);
    public BlockEntityStoneCoffin Coffin => Assert.IsType<BlockEntityStoneCoffin>(W.BlockAccessor.GetBlockEntity(Pos));
    public ItemSlot Hand => player.InventoryManager.ActiveHotbarSlot;

    public ItemStack Stack(string code, int size) =>
        new(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

    /// <summary>Clears the site and puts up the coffin and every block of its structure.</summary>
    public async Task Build()
    {
        for (int dx = -4; dx <= 4; dx++)
        for (int dz = -3; dz <= 5; dz++)
        {
            world.SetBlock("game:rock-granite", Pos.AddCopy(dx, -3, dz));
            for (int dy = -2; dy <= 6; dy++)
                world.SetBlock("game:air", Pos.AddCopy(dx, dy, dz));
        }
        await world.Ticks(2);
        world.SetBlock(Controller, Pos);
        world.SetBlock(Partner, PartnerPos);
        await world.Ticks(2);
        var ms = Ms(Coffin);
        var codes = ms.BlockNumbers.ToDictionary(kv => kv.Value, kv => kv.Key);
        foreach (var o in ms.TransformedOffsets)
        {
            var at = Pos.AddCopy(o.X, o.Y, o.Z);
            var want = codes[o.W];
            if (WildcardUtil.Match(want, W.BlockAccessor.GetBlock(at).Code))
                continue;
            var block = W.Blocks.FirstOrDefault(b => b?.Code != null && WildcardUtil.Match(want, b.Code))
                        ?? throw new Xunit.Sdk.XunitException($"no block matches {want}");
            world.SetBlock(block.Code.ToString(), at);
        }
        await world.Ticks(2);
        Assert.Equal(0, ms.InCompleteBlockCount(W, Pos));
    }

    /// <summary>Sneak-clicks the coffin's second half holding <paramref name="held"/> (null for an
    /// empty hand), as the game does for a right-click; returns what the click returned.</summary>
    public bool Click(ItemStack? held)
    {
        Hand.Itemstack = held;
        Hand.MarkDirty();
        player.Entity.Controls.ShiftKey = true;
        player.Entity.ServerControls.ShiftKey = true;
        try
        {
            var sel = new BlockSelection { Position = PartnerPos.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.9, 0.5) };
            return W.BlockAccessor.GetBlock(PartnerPos).OnBlockInteractStart(W, player, sel);
        }
        finally
        {
            player.Entity.Controls.ShiftKey = false;
            player.Entity.ServerControls.ShiftKey = false;
        }
    }

    /// <summary>A layer of coal: 8 coke, as the game asks for.</summary>
    public void AddCoal()
    {
        int layers = Coffin.CoalLayerCount;
        Click(Stack(Coke, 8));
        Assert.Equal(layers + 1, Coffin.CoalLayerCount);
    }

    public void PutOnLids()
    {
        world.SetBlock(Lid, Pos.UpCopy());
        world.SetBlock(Lid, PartnerPos.UpCopy());
    }

    /// <summary>Lights a full coal pile of coke under each half.</summary>
    public void Light()
    {
        foreach (var at in Coffin.FuelPositions)
        {
            world.SetBlock("game:coalpile", at);
            var pile = Assert.IsType<BlockEntityCoalPile>(W.BlockAccessor.GetBlockEntity(at));
            pile.inventory[0].Itemstack = Stack(Coke, 16);
            pile.MarkDirty(true);
            pile.TryIgnite();
            Assert.True(pile.IsBurning);
        }
    }

    /// <summary>Whether <paramref name="method"/> carries a patch of steel bits recovery's.</summary>
    public static bool Patched(MethodBase? method) =>
        method != null && Harmony.GetPatchInfo(method) is { } info && info.Owners.Contains(SteelBitsSystem.HarmonyId);

    public static MethodInfo? AddIngotMethod() =>
        AccessTools.DeclaredMethod(typeof(BlockEntityStoneCoffin), "AddIngot", [typeof(ItemSlot)]);
}

/// <summary>
/// mods-src/seraphhorizons, SteelBits: steel bits go back into steel, 20 to an ingot's place in the
/// game's stone coffin, and as scrap in Steelmaking Expanded's Bessemer converter.
/// </summary>
public partial class SharedWorldScenarios
{
    // The fragility guard: a game update that renames or reshapes the coffin's AddIngot fails here
    // instead of silently leaving the bits out of the coffin.
    [AtlasScenario]
    public void The_stone_coffin_patch_target_resolves_and_is_patched()
    {
        var target = CoffinSite.AddIngotMethod();
        Assert.NotNull(target);
        Assert.Equal(typeof(bool), target!.ReturnType);
        Assert.Same(target, SteelBitsSystem.AddIngot);
        Assert.True(CoffinSite.Patched(target), "BlockEntityStoneCoffin.AddIngot is not patched");
        var info = Harmony.GetPatchInfo(target)!;
        Assert.Contains(info.Prefixes, p => p.owner == SteelBitsSystem.HarmonyId && p.PatchMethod.Name == "AddIngotPrefix");
        Assert.Contains(info.Postfixes, p => p.owner == SteelBitsSystem.HarmonyId && p.PatchMethod.Name == "AddIngotPostfix");
        // What the coffin and the patch rely on in the game's data.
        foreach (var field in new[] { "ms", "progress", "receivesHeat", "processComplete" })
            Assert.NotNull(AccessTools.DeclaredField(typeof(BlockEntityStoneCoffin), field));
        var bit = W.GetItem(SteelBitsSystem.SteelBit)!;
        Assert.False(bit.Attributes["carburizableProps"].Exists);
        Assert.Equal(1502, bit.CombustibleProps.MeltingPoint);
        Assert.Equal(SteelBitsRules.BitsPerCharge, bit.CombustibleProps.SmeltedRatio);
        Assert.True(W.GetItem(new AssetLocation(CoffinSite.Coke))!.CombustibleProps.BurnTemperature < bit.CombustibleProps.MeltingPoint);
    }

    [AtlasScenario]
    public void Packed_steel_bits_carburize_to_blister_steel_and_are_made_from_twenty_bits()
    {
        var charge = W.GetItem(SteelBitsSystem.ChargeCode);
        Assert.NotNull(charge);
        var carburized = charge!.Attributes["carburizableProps"]["carburizedOutput"].AsObject<JsonItemStack>(null, "game");
        Assert.True(carburized!.Resolve(W, "test"));
        Assert.Equal(CoffinSite.BlisterSteel, carburized.ResolvedItemstack!.Collectible.Code.ToString());
        Assert.Equal(1, carburized.ResolvedItemstack.StackSize);

        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.Equals(SteelBitsSystem.ChargeCode) == true);
        Assert.True(recipe.Enabled);
        var ingredient = Assert.Single(recipe.ResolvedIngredients!.OfType<CraftingRecipeIngredient>());
        Assert.Equal(SteelBitsSystem.SteelBit, ingredient.Code);
        Assert.Equal(SteelBitsRules.BitsPerCharge, ingredient.Quantity);
        Assert.Equal(1, recipe.Output!.Quantity);
    }

    [AtlasScenario]
    public void The_steel_bits_handbook_page_tells_both_ways_back()
    {
        var sections = W.GetItem(SteelBitsSystem.SteelBit)!.Attributes["handbook"]["extraSections"];
        Assert.True(sections.Exists, "the steel bit has no extra handbook section");
        Assert.Equal("seraphhorizons:steelbits-handbook-title", sections.AsArray()![0]["title"].AsString());
        // The other bits share the item file; the section is the steel bit's alone, and the
        // file's own handbook settings are kept.
        var copper = W.GetItem(new AssetLocation("game:metalbit-copper"))!.Attributes["handbook"];
        Assert.False(copper["extraSections"].Exists);
        Assert.True(copper["groupBy"].Exists);
        var text = Vintagestory.API.Config.Lang.Get("seraphhorizons:steelbits-handbook-text");
        Assert.Contains("stone coffin", text);
        Assert.Contains("Bessemer", text);
    }

    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_stone_coffin_takes_steel_bits_twenty_at_a_time_fires_and_gives_blister_steel()
    {
        var p = await World.JoinPlayer("cementer");
        var pos = World.Spawn.AddCopy(-180, 12, -300);
        // Out of the furnace's heat (it burns whoever stands by it); the click needs no reach.
        await p.TeleportTo(pos.AddCopy(8, 0, 0));
        var site = new CoffinSite(World, pos, p.Player);
        await site.Build();
        var coffin = site.Coffin;

        site.AddCoal();
        // Fewer than 20 bits: refused, nothing taken.
        site.Click(site.Stack(SteelBitsRules.SteelBit, 19));
        Assert.Equal(0, coffin.IngotCount);
        Assert.Equal(19, site.Hand.StackSize);
        // A stack: 20 go in per click, as one packed charge.
        site.Click(site.Stack(SteelBitsRules.SteelBit, 50));
        Assert.Equal(1, coffin.IngotCount);
        Assert.Equal(SteelBitsSystem.ChargeCode, coffin.Inventory[1].Itemstack!.Collectible.Code);
        Assert.Equal(30, site.Hand.StackSize);
        // Iron ingots and bits do not mix: the game's own refusal, the ingot kept.
        site.Click(site.Stack(CoffinSite.IronIngot, 1));
        Assert.Equal(1, coffin.IngotCount);
        Assert.Equal(1, site.Hand.StackSize);
        Assert.Equal(CoffinSite.IronIngot, site.Hand.Itemstack!.Collectible.Code.ToString());
        // A packed charge from the grid goes in the same way.
        site.Click(new ItemStack(W.GetItem(SteelBitsSystem.ChargeCode)!, 1));
        Assert.Equal(2, coffin.IngotCount);
        Assert.True(site.Hand.Empty);

        // The rest: a coal layer between each four places, five layers, sixteen places.
        for (int place = 2; place < SteelBitsRules.CoffinPlaces; place++)
        {
            if (place % 4 == 0)
                site.AddCoal();
            site.Click(site.Stack(SteelBitsRules.SteelBit, 20));
            Assert.Equal(place + 1, coffin.IngotCount);
            Assert.True(site.Hand.Empty);
        }
        site.AddCoal();
        Assert.True(coffin.IsFull);
        Assert.Equal(SteelBitsRules.CoffinPlaces, coffin.Inventory[1].StackSize);
        // Full: the bits are refused.
        site.Click(site.Stack(SteelBitsRules.SteelBit, 20));
        Assert.Equal(20, site.Hand.StackSize);
        site.Hand.Itemstack = null;

        site.PutOnLids();
        site.Light();
        await World.Until(() => CoffinSite.ReceivesHeat(coffin) && coffin.StructureComplete && CoffinSite.Progress(coffin) > 0, 3000);
        // Fired as for iron, it runs 160 hours; skip to the end of it.
        CoffinSite.Progress(coffin) = 0.995;
        await World.Until(() => CoffinSite.ProcessComplete(coffin), 3000);

        var result = coffin.Inventory[1].Itemstack;
        Assert.Equal(CoffinSite.BlisterSteel, result!.Collectible.Code.ToString());
        Assert.Equal(SteelBitsRules.CoffinPlaces, result.StackSize);
        Assert.Equal(32, coffin.Inventory[0].StackSize);
        output.WriteLine($"{SteelBitsRules.BitsPerCharge * SteelBitsRules.CoffinPlaces} steel bits gave {result.StackSize} {result.Collectible.Code}");
    }

    [AtlasScenario]
    public void The_Bessemer_converter_takes_steel_bits_as_scrap_at_five_units_a_bit()
    {
        if (!World.Api.ModLoader.IsModEnabled(SteelBitsSystem.SmexModId))
        {
            Assert.Equal(SmexScrap.Status.Absent, SteelBitsSystem.Of(World.Api).SmexStatus);
            output.WriteLine("Steelmaking Expanded is not installed: nothing to check");
            return;
        }
        Assert.Contains(SteelBitsSystem.Of(World.Api).SmexStatus, new[] { SmexScrap.Status.Listed, SmexScrap.Status.Added });
        Assert.True(SteelBitsRules.ListsSteelBit(SmexScrap.Codes()), $"smex's scrap list is {SmexScrap.Codes()}");

        // The converter's own test of a held stack.
        var isScrap = AccessTools.Method("SteelmakingExpanded.BlockStructures.Converter.BlockEntities.BlockEntityConverterControl:IsScrap");
        Assert.NotNull(isScrap);
        bool Scrap(string code) => (bool)isScrap!.Invoke(null, [new ItemStack(W.GetItem(new AssetLocation(code))!)])!;
        Assert.True(Scrap(SteelBitsRules.SteelBit));
        Assert.False(Scrap("game:ingot-steel"));

        // The value of a bit: 5 units, and a steel ingot 100.
        var values = AccessTools.TypeByName(SmexScrap.ValuesType)!;
        int Value(string name) => (int)values.GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        Assert.Equal(5, Value("MoltenUnitsPerBit"));
        Assert.Equal(100, Value("MoldDefaultUnits"));
        Assert.Equal(SteelBitsRules.BitsPerCharge, Value("MoldDefaultUnits") / Value("MoltenUnitsPerBit"));

        // A server file that leaves the steel bit out gets it back in the live setting.
        var config = AccessTools.Property(AccessTools.Field(values, "_store").GetValue(null)!.GetType(), "Config")
            .GetValue(AccessTools.Field(values, "_store").GetValue(null))!;
        var setting = config.GetType().GetProperty(SmexScrap.Setting)!;
        var shipped = (string)setting.GetValue(config)!;
        try
        {
            setting.SetValue(config, "game:metalbit-iron");
            Assert.False(Scrap(SteelBitsRules.SteelBit));
            Assert.Equal(SmexScrap.Status.Added, SmexScrap.Ensure(World.Api.Logger));
            Assert.Equal("game:metalbit-iron,game:metalbit-steel", SmexScrap.Codes());
            Assert.True(Scrap(SteelBitsRules.SteelBit));
            Assert.Equal(SmexScrap.Status.Listed, SmexScrap.Ensure(World.Api.Logger));
        }
        finally
        {
            setting.SetValue(config, shipped);
        }
    }
}
