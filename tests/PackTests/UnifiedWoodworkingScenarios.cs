using System.Reflection;
using Atlas.Api;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using SeraphHorizons.Mod.Woodworking;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>A player at a cleared spot, working blocks with their own interaction methods, the way a
/// client's clicks reach the server: <c>OnBlockInteractStart</c>, then for a hold
/// <c>OnBlockInteractStep</c> and <c>OnBlockInteractStop</c> with the seconds held. What the work
/// makes is read off the ground and out of the player's inventory. Shared with
/// <see cref="SwitchesOffScenarios"/>.</summary>
internal sealed class Woodshop(IWorldSession world, ITestPlayer player, BlockPos at)
{
    public const string Axe = "game:axe-felling-iron";
    public const string Hammer = "game:hammer-iron";
    public const string Saw = "game:saw-iron";
    public const string Maul = "immersivewoodworking:maul-iron";
    public const string Spud = "immersivewoodworking:barkspud-iron";
    public const string Hoop = SplittingBlockRules.HoopCode;
    public const string Nails = SplittingBlockRules.NailCode;
    public const string SplittingBlock = "immersivewoodworking:choppingblock";

    private IWorldAccessor W => world.Api.World;

    public ITestPlayer Player => player;
    public IPlayer P => player.Player;
    public ItemSlot Hand => P.InventoryManager.ActiveHotbarSlot;
    public ItemSlot Offhand => player.Entity.LeftHandItemSlot;

    /// <summary>The cell of the n-th workpiece, on the floor, two blocks apart.</summary>
    public BlockPos Cell(int n) => at.AddCopy(2 * n - 4, 0, 0);

    // One player per server, which takes 16 at most: every shop's.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, ITestPlayer> Players = new();

    /// <summary>Clears a 13 x 4 x 9 room of air on a granite floor and puts the world's woodworker,
    /// empty-handed and in survival, 5 blocks off the row of cells, out of item pickup range (what
    /// is made stays on the ground).</summary>
    public static async Task<Woodshop> Open(IWorldSession world, BlockPos at)
    {
        for (int dx = -6; dx <= 6; dx++)
        for (int dz = -3; dz <= 5; dz++)
        {
            world.SetBlock("game:rock-granite", at.AddCopy(dx, -1, dz));
            for (int dy = 0; dy < 4; dy++)
                world.SetBlock("game:air", at.AddCopy(dx, dy, dz));
        }
        if (!Players.TryGetValue(world.Api, out var player))
            Players.Add(world.Api, player = await world.JoinPlayer("woodworker"));
        await player.TeleportTo(at.AddCopy(0, 0, 5));
        player.Player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        player.Entity.Controls.ShiftKey = false;
        var shop = new Woodshop(world, player, at);
        shop.Holding(null);
        shop.InOffhand(null);
        await shop.Collect();
        return shop;
    }

    public Item Item(string code) => W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no item {code}");

    public Block Block(string code) =>
        W.GetBlock(new AssetLocation(code)) is { Id: not 0 } block ? block : throw new Xunit.Sdk.XunitException($"no block {code}");

    public ItemStack Stack(string code, int size = 1) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item, size) : new ItemStack(Block(code), size);

    /// <summary>A splitting block's item stack of a wood and tier (primitive carries no tier, as
    /// Immersive Woodworking's own stacks).</summary>
    public ItemStack SplittingBlockStack(string wood, SplittingBlockTier tier, string domain = "game")
    {
        var stack = new ItemStack(Block(SplittingBlock));
        stack.Attributes.SetString("wood", wood);
        stack.Attributes.SetString("woodDomain", domain);
        if (tier != SplittingBlockTier.Primitive)
            stack.Attributes.SetString(SplittingBlockTiers.AttributeKey, tier.Name());
        return stack;
    }

    /// <summary>Puts a stack in the main hand (null empties it).</summary>
    public ItemSlot Holding(ItemStack? stack)
    {
        Hand.Itemstack = stack;
        Hand.MarkDirty();
        return Hand;
    }

    public ItemSlot Holding(string code, int size = 1) => Holding(Stack(code, size));

    public void InOffhand(string? code)
    {
        Offhand.Itemstack = code == null ? null : Stack(code);
        Offhand.MarkDirty();
    }

    public static BlockSelection Selection(BlockPos pos) =>
        new() { Position = pos, Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };

    /// <summary>A right click on the block: its <c>OnBlockInteractStart</c>.</summary>
    public bool Click(BlockPos pos) => W.BlockAccessor.GetBlock(pos).OnBlockInteractStart(W, P, Selection(pos));

    /// <summary>A right click held <paramref name="seconds"/> and released: start, one step, stop.
    /// Returns whether the hold started.</summary>
    public bool Hold(BlockPos pos, float seconds)
    {
        var block = W.BlockAccessor.GetBlock(pos);
        if (!block.OnBlockInteractStart(W, P, Selection(pos)))
            return false;
        block.OnBlockInteractStep(seconds, W, P, Selection(pos));
        block.OnBlockInteractStop(seconds, W, P, Selection(pos));
        return true;
    }

    /// <summary>A right click with Ctrl held (the game's sprint key), as a player in
    /// <paramref name="mode"/>; Ctrl is let go and the player is back in survival after.</summary>
    public bool CtrlClick(BlockPos pos, EnumGameMode mode = EnumGameMode.Creative)
    {
        var controls = player.Entity.Controls;
        P.WorldData.CurrentGameMode = mode;
        controls.CtrlKey = true;
        try
        {
            return Click(pos);
        }
        finally
        {
            controls.CtrlKey = false;
            P.WorldData.CurrentGameMode = EnumGameMode.Survival;
        }
    }

    public BlockEntity Entity(BlockPos pos) =>
        W.BlockAccessor.GetBlockEntity(pos) ?? throw new Xunit.Sdk.XunitException($"no block entity at {pos}");

    // --- the splitting block, Immersive Woodworking's chopping block ---

    /// <summary>Places a splitting block from its stack, as a player does (the stack reaches the block
    /// entity's OnBlockPlaced).</summary>
    public async Task<BlockPos> PlaceSplittingBlock(int cell, string wood, SplittingBlockTier tier)
    {
        var pos = Cell(cell);
        var stack = SplittingBlockStack(wood, tier);
        W.BlockAccessor.SetBlock(stack.Block.Id, pos, stack);
        await world.Ticks(2);
        return pos;
    }

    public SplittingBlockTier TierAt(BlockPos pos) =>
        Entity(pos).GetBehavior<BEBehaviorSplittingBlockTier>()?.Tier
        ?? throw new Xunit.Sdk.XunitException($"no splitting block tier at {pos}");

    public string? WoodAt(BlockPos pos) => (string?)AccessTools.Property(Entity(pos).GetType(), "Wood").GetValue(Entity(pos));

    public ItemStack? ContentAt(BlockPos pos) =>
        ((InventoryBase)AccessTools.Field(Entity(pos).GetType(), "inventory").GetValue(Entity(pos))!)[0].Itemstack;

    /// <summary>Lays what is in the hand on the block (Immersive Woodworking's TryPut).</summary>
    public bool Lay(BlockPos pos, ItemStack stack)
    {
        Holding(stack);
        Click(pos);
        return ContentAt(pos) != null;
    }

    /// <summary>Chops what lies on the block with what is in the hand: holds the right button as a
    /// client does, stepping in 50 ms until the workpiece is gone (or a minute has passed), then
    /// lets go. Immersive Woodworking strikes, and chops, when the hold reaches each swing.</summary>
    public void Chop(BlockPos pos)
    {
        var block = W.BlockAccessor.GetBlock(pos);
        var sel = Selection(pos);
        Assert.True(block.OnBlockInteractStart(W, P, sel));
        float t = 0;
        while (ContentAt(pos) != null && t < 60)
        {
            t += 0.05f;
            block.OnBlockInteractStep(t, W, P, sel);
        }
        block.OnBlockInteractStop(t, W, P, sel);
        Assert.Null(ContentAt(pos));
    }

    // --- what the work made ---

    private List<EntityItem> LooseItems() =>
        W.GetEntitiesAround(at.ToVec3d().Add(0.5, 1, 2), 12, 5, e => e is EntityItem && e.Alive).Cast<EntityItem>().ToList();

    /// <summary>Everything made since the last call, on the ground in the room and in the player's
    /// inventory, by code, and takes it all away. Lets it land first.</summary>
    public async Task<Dictionary<string, int>> Collect()
    {
        await world.Ticks(10);
        var made = new Dictionary<string, int>();
        void Add(ItemStack stack) =>
            made[stack.Collectible.Code.ToString()] = made.GetValueOrDefault(stack.Collectible.Code.ToString()) + stack.StackSize;
        foreach (var item in LooseItems())
        {
            Add(item.Itemstack);
            item.Die(EnumDespawnReason.Removed);
        }
        foreach (var name in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in P.InventoryManager.GetOwnInventory(name) ?? (IEnumerable<ItemSlot>)[])
                if (slot != Hand && slot != Offhand && slot.Itemstack != null && slot.GetType().Name != "ItemSlotBackpack")
                {
                    Add(slot.Itemstack);
                    slot.Itemstack = null;
                    slot.MarkDirty();
                }
        return made;
    }

    public int Durability(ItemSlot slot) => slot.Itemstack!.Collectible.GetRemainingDurability(slot.Itemstack);

    // --- Logging Expanded's sawhorses ---

    /// <summary>Places a sawhorse and loads it with <paramref name="logs"/> upright oak logs from the
    /// hand, as a player does.</summary>
    public async Task<BlockPos> PlaceLoadedSawhorse(int cell, string code, int logs)
    {
        var pos = Cell(cell);
        world.SetBlock(code, pos);
        await world.Ticks(2);
        Holding("game:log-placed-oak-ud", logs);
        Assert.True(Click(pos));
        Assert.Equal(logs, LogsOn(pos));
        return pos;
    }

    public int LogsOn(BlockPos pos)
    {
        var inventory = AccessTools.Property(Entity(pos).GetType(), "Inventory").GetValue(Entity(pos))!;
        return (int)AccessTools.Property(inventory.GetType(), "LogCount").GetValue(inventory)!;
    }

    /// <summary>One full hold on a loaded sawhorse (Logging Expanded's 0.75 s). Its hold refuses to
    /// start again for 300 ms after one completes, so this waits that out.</summary>
    public async Task Work(BlockPos pos)
    {
        await world.Until(() => Hold(pos, 0.8f), 200);
    }
}

/// <summary>
/// mods-src/seraphhorizons, UnifiedWoodworking: Immersive Woodworking and Logging Expanded as one
/// woodworking system, run against both real mods. Immersive Woodworking's chopping block is the
/// splitting block, made by an axe on a placed log, upgraded through Logging Expanded's tiers and
/// yielding 6 firewood per log (8 on the advanced tier); the mechanical chopper takes only an
/// advanced one; Logging Expanded's sawhorses also saw support beams and take the bark spud, and
/// debarking drops Immersive Woodworking's bark; the stations either mod had for the same job are
/// retired. Every scenario works the blocks through their own interaction methods, so it is the
/// tweak's Harmony patches on those that are under test.
/// </summary>
[AtlasWorld]
public class UnifiedWoodworkingScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;

    private UnifiedWoodworking Tweak => World.Api.ModLoader.GetModSystem<SeraphHorizonsSystem>().Woodworking;

    private object IwConfig => AccessTools.Property(AccessTools.TypeByName(WoodworkingMods.IwSystemType), "Config")
        .GetValue(World.Api.ModLoader.GetModSystem(WoodworkingMods.IwSystemType))!;

    private T IwSetting<T>(string name) => (T)AccessTools.Field(IwConfig.GetType(), name).GetValue(IwConfig)!;

    private static bool PatchedByUs(MethodBase? method) =>
        method != null && Harmony.GetPatchInfo(method)?.Owners.Contains(UnifiedWoodworking.ServerHarmonyId) == true;

    // Fails when either mod moved something a part binds to: the warning names it.
    [AtlasScenario]
    public void Binds_to_both_mods_with_no_warning()
    {
        Assert.True(World.Api.LoadModConfig("seraphhorizons.json")["UnifiedWoodworking"].AsBool(false));
        Assert.True(Tweak.Active);
        Assert.NotNull(Tweak.Mods);

        var ours = World.BootDiagnostics
            .Where(e => e.Message.Contains("Unified woodworking") || e.Message.Contains("Immersive Woodworking")
                        || e.Message.Contains("Logging Expanded")
                        || WoodworkingMods.IwModId == e.Source && e.Message.Contains("choppingblock")
                        || e.Message.Contains(WoodworkingMods.LeModId + ":") || e.Message.Contains("woodworking"))
            .Select(e => $"[{e.Level}] {e.DescribeSource()}: {e.Message}")
            .ToList();
        Assert.True(ours.Count == 0, "Logged:\n" + string.Join("\n", ours));
        var errors = World.BootDiagnostics
            .Where(e => e.Level is EnumLogType.Error or EnumLogType.Fatal
                        && e.Source is WoodworkingMods.IwModId or WoodworkingMods.LeModId or "seraphhorizons")
            .Select(e => $"[{e.Level}] {e.DescribeSource()}: {e.Message}")
            .ToList();
        Assert.True(errors.Count == 0, "Errors logged:\n" + string.Join("\n", errors));

        // Clients run it because the server says so in the world config it sends them.
        Assert.True(W.Config.GetBool(UnifiedWoodworking.RunningKey));
        Assert.True(UnifiedWoodworking.RunsOnServer(World.Api));

        // The server's patches are in, under the tweak's id.
        var block = AccessTools.TypeByName("ImmersiveWoodworking.BlockChoppingBlock");
        var blockEntity = AccessTools.TypeByName("ImmersiveWoodworking.BlockEntityChoppingBlock");
        Assert.True(PatchedByUs(AccessTools.Method(AccessTools.TypeByName("LoggingMod.BlockBehaviorLogConvert"), "OnBlockInteractStart")));
        Assert.True(PatchedByUs(AccessTools.Method(block, "OnBlockInteractStart")));
        Assert.True(PatchedByUs(AccessTools.Method(block, "GetDrops")));
        Assert.True(PatchedByUs(AccessTools.Method(AccessTools.TypeByName("ImmersiveWoodworking.AutoRestockUtil"), "FindRestockSlot")));
        Assert.True(PatchedByUs(AccessTools.Method(blockEntity, "Chop")));
        Assert.True(PatchedByUs(AccessTools.Method(AccessTools.TypeByName("ImmersiveWoodworking.BlockEntityChopper"), "TryAddPart")));
        Assert.True(PatchedByUs(AccessTools.Method(AccessTools.TypeByName("LoggingMod.BlockSawhorse"), "ProcessWithTool")));
        Assert.True(PatchedByUs(AccessTools.Method(AccessTools.TypeByName("LoggingMod.BlockSawhorseAdvanced"), "ProcessWithTool")));
        Assert.True(PatchedByUs(AccessTools.Method(AccessTools.TypeByName(WoodworkingMods.IwSystemType), RetiredStations.RecipesMethod)));
        foreach (var frame in CreativeUpgrades.Frames)
            Assert.True(PatchedByUs(AccessTools.DeclaredMethod(AccessTools.TypeByName("LoggingMod." + frame.FrameClass), "OnBlockInteractStart")),
                $"{frame.FrameClass} has no creative upgrade");
    }

    [AtlasScenario]
    public void Immersive_Woodworkings_settings_are_set_in_memory()
    {
        Assert.True(IwSetting<bool>("RemovePitSaw"));
        Assert.False(IwSetting<bool>("CraftableSawhorse"));
        Assert.False(IwSetting<bool>("AllowKnifeDebark"));
        Assert.Equal(6, IwSetting<int>("FirewoodPerLog"));
        Assert.InRange(IwSetting<int>("FirewoodDropCount"), 1, 6);
        Assert.Equal(8, IwSetting<int>("ChopperFirewoodPerLog"));
    }

    [AtlasScenario]
    public void It_is_called_a_splitting_block()
    {
        Assert.Equal("Splitting block", Lang.GetL("en", "immersivewoodworking:block-choppingblock"));
        Assert.Equal("Chop with an axe to make a splitting block", Lang.GetL("en", "loggingmod:wi-log-convert"));
        foreach (var language in new[] { "de", "pt-br", "ru", "zh-cn" })
            Assert.False(Lang.AvailableLanguages[language].GetAllEntries().ContainsKey("immersivewoodworking:block-choppingblock"),
                $"{language} still has its own name for the chopping block");
    }

    // --- making and upgrading ---

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_axe_on_an_upright_log_makes_a_primitive_splitting_block_of_its_wood()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(80, 3, 80));
        foreach (var (n, wood) in new[] { (0, "oak"), (1, "birch") })
        {
            var pos = shop.Cell(n);
            World.SetBlock($"game:log-placed-{wood}-ud", pos);
            await World.Ticks(2);
            var axe = shop.Holding(Woodshop.Axe);
            int before = shop.Durability(axe);

            Assert.True(shop.Click(pos));
            await World.Ticks(2);

            Assert.Equal(Woodshop.SplittingBlock, World.BlockAt(pos).Code.ToString());
            Assert.Equal(SplittingBlockTier.Primitive, shop.TierAt(pos));
            Assert.Equal(wood, shop.WoodAt(pos));
            Assert.Equal("game", (string)AccessTools.Property(shop.Entity(pos).GetType(), "WoodDomain").GetValue(shop.Entity(pos))!);
            Assert.Equal(before - 1, shop.Durability(axe));
        }
        // A sideways log is not made into one, as Logging Expanded.
        var side = shop.Cell(2);
        World.SetBlock("game:log-placed-oak-ns", side);
        shop.Holding(Woodshop.Axe);
        shop.Click(side);
        Assert.Equal("game:log-placed-oak-ns", World.BlockAt(side).Code.ToString());
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Each_upgrade_takes_its_cost_and_makes_the_next_tier()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-80, 3, 80));
        var pos = await shop.PlaceSplittingBlock(0, "oak", SplittingBlockTier.Primitive);

        // Debark with the bark spud: a hold of Immersive Woodworking's debark time.
        var spud = shop.Holding(Woodshop.Spud);
        int spudBefore = shop.Durability(spud);
        Assert.True(shop.Hold(pos, 0.2f));
        Assert.Equal(SplittingBlockTier.Primitive, shop.TierAt(pos));
        // Longer than Immersive Woodworking's debark time with any spud.
        const float seconds = 60f;
        Assert.True(shop.Hold(pos, seconds));
        Assert.Equal(SplittingBlockTier.Debarked, shop.TierAt(pos));
        Assert.Equal(spudBefore - 1, shop.Durability(spud));
        // Oak's bark is tan bark, every time with an iron spud; 3 pieces per log.
        Assert.Equal(new Dictionary<string, int> { ["immersivewoodworking:bark-tan-green"] = 3 }, await shop.Collect());

        // Bind: two iron hoops, at once. One is not enough.
        shop.Holding(Woodshop.Hoop, 1);
        shop.Click(pos);
        Assert.Equal(SplittingBlockTier.Debarked, shop.TierAt(pos));
        Assert.Equal(1, shop.Hand.StackSize);
        var hoops = shop.Holding(Woodshop.Hoop, 3);
        // Not with Shift, which Immersive Woodworking uses.
        shop.Player.Entity.Controls.ShiftKey = true;
        shop.Click(pos);
        shop.Player.Entity.Controls.ShiftKey = false;
        Assert.Equal(SplittingBlockTier.Debarked, shop.TierAt(pos));
        Assert.True(shop.Click(pos));
        Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(pos));
        Assert.Equal(1, hoops.StackSize);

        // Nail: eight iron nails, a hammer in the offhand, held 3 s.
        var nails = shop.Holding(Woodshop.Nails, 10);
        shop.Hold(pos, 3f);
        Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(pos));
        shop.InOffhand(Woodshop.Hammer);
        int hammerBefore = shop.Durability(shop.Offhand);
        Assert.True(shop.Hold(pos, 2f));
        Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(pos));
        Assert.Equal(10, nails.StackSize);
        Assert.True(shop.Hold(pos, 3f));
        Assert.Equal(SplittingBlockTier.Advanced, shop.TierAt(pos));
        Assert.Equal(2, nails.StackSize);
        Assert.Equal(hammerBefore - 1, shop.Durability(shop.Offhand));

        // Nothing upgrades an advanced block.
        shop.Holding(Woodshop.Nails, 8);
        shop.Hold(pos, 3f);
        Assert.Equal(8, shop.Hand.StackSize);
        Assert.Equal(SplittingBlockTier.Advanced, shop.TierAt(pos));
        shop.InOffhand(null);

        // A block with a log on it is not upgraded: the spud does nothing on a loaded primitive block.
        var loaded = await shop.PlaceSplittingBlock(1, "oak", SplittingBlockTier.Primitive);
        Assert.True(shop.Lay(loaded, shop.Stack("game:log-placed-oak-ud")));
        shop.Holding(Woodshop.Spud);
        shop.Hold(loaded, seconds);
        Assert.Equal(SplittingBlockTier.Primitive, shop.TierAt(loaded));
        await shop.Collect();
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_axe_with_a_hammer_debarks_and_drops_that_woods_bark()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(80, 3, -80));
        foreach (var (n, wood, kinds) in new[]
                 {
                     (0, "birch", new[] { "immersivewoodworking:bark-birch-green", "immersivewoodworking:bark-generic" }),
                     (1, "walnut", new[] { "immersivewoodworking:bark-generic" }),
                 })
        {
            var pos = await shop.PlaceSplittingBlock(n, wood, SplittingBlockTier.Primitive);
            var axe = shop.Holding(Woodshop.Axe);
            // Without the hammer an axe does nothing on an empty block.
            shop.Hold(pos, 10f);
            Assert.Equal(SplittingBlockTier.Primitive, shop.TierAt(pos));
            shop.InOffhand(Woodshop.Hammer);
            int axeBefore = shop.Durability(axe), hammerBefore = shop.Durability(shop.Offhand);
            Assert.True(shop.Hold(pos, 30f));
            Assert.Equal(SplittingBlockTier.Debarked, shop.TierAt(pos));
            Assert.Equal(axeBefore - 1, shop.Durability(axe));
            Assert.Equal(hammerBefore - 1, shop.Durability(shop.Offhand));
            var bark = Assert.Single(await shop.Collect());
            output.WriteLine($"{wood}: {bark.Value}x {bark.Key}");
            Assert.Contains(bark.Key, kinds);
            Assert.Equal(3, bark.Value);
            shop.InOffhand(null);
        }
    }

    // The water wheel's creative shortcut: Ctrl + right click in creative, nothing in hand.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task In_creative_a_Ctrl_click_upgrades_an_empty_splitting_block_a_tier_for_nothing()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-40, 3, -160));
        var pos = await shop.PlaceSplittingBlock(0, "oak", SplittingBlockTier.Primitive);
        foreach (var tier in new[] { SplittingBlockTier.Debarked, SplittingBlockTier.Bound, SplittingBlockTier.Advanced })
        {
            Assert.True(shop.CtrlClick(pos));
            Assert.Equal(tier, shop.TierAt(pos));
        }
        // Nothing above advanced; no bark from the debark; the hands stay empty.
        shop.CtrlClick(pos);
        Assert.Equal(SplittingBlockTier.Advanced, shop.TierAt(pos));
        Assert.Empty(await shop.Collect());
        Assert.Null(shop.Hand.Itemstack);
        Assert.Null(shop.ContentAt(pos));

        // Whatever is held, and it is neither taken nor laid on the block.
        var held = await shop.PlaceSplittingBlock(1, "oak", SplittingBlockTier.Debarked);
        shop.Holding("game:log-placed-oak-ud", 3);
        Assert.True(shop.CtrlClick(held));
        Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(held));
        Assert.Equal(3, shop.Hand.StackSize);
        Assert.Null(shop.ContentAt(held));

        // Not with Shift too: Immersive Woodworking's.
        shop.Holding(null);
        shop.Player.Entity.Controls.ShiftKey = true;
        shop.CtrlClick(held);
        shop.Player.Entity.Controls.ShiftKey = false;
        Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(held));

        // On a block with a log on it, Ctrl still takes the log back, and nothing is upgraded.
        var loaded = await shop.PlaceSplittingBlock(2, "oak", SplittingBlockTier.Primitive);
        Assert.True(shop.Lay(loaded, shop.Stack("game:log-placed-oak-ud")));
        shop.Holding(null);
        Assert.True(shop.CtrlClick(loaded));
        Assert.Null(shop.ContentAt(loaded));
        Assert.Equal(SplittingBlockTier.Primitive, shop.TierAt(loaded));
        Assert.Equal(1, (await shop.Collect()).GetValueOrDefault("game:log-placed-oak-ud") + (shop.Hand.Itemstack?.StackSize ?? 0));
        shop.Holding(null);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task In_survival_a_Ctrl_click_with_empty_hands_upgrades_nothing()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-40, 3, -200));
        var pos = await shop.PlaceSplittingBlock(0, "oak", SplittingBlockTier.Primitive);
        shop.CtrlClick(pos, EnumGameMode.Survival);
        Assert.Equal(SplittingBlockTier.Primitive, shop.TierAt(pos));

        var frame = shop.Cell(1);
        await PlaceFrame(shop, frame, "loggingmod:sawhorseframe-north");
        shop.CtrlClick(frame, EnumGameMode.Survival);
        Assert.Equal("loggingmod:sawhorseframe-north", World.BlockAt(frame).Code.ToString());
        Assert.Empty(await shop.Collect());
    }

    // Every Logging Expanded frame Logging Expanded turns into something with items, each to the
    // stage its first help line makes, from the frame's wood and facing.
    public static TheoryData<int, string, string> Frames => new()
    {
        { 0, "loggingmod:sawhorseframe-north", "loggingmod:sawhorse-north" },
        { 1, "loggingmod:plankframe-birch-north", "loggingmod:standardsawhorseframe-birch-north" },
        { 2, "loggingmod:standardsawhorseframe-birch-north", "loggingmod:sawhorsestandard-birch-copper-north" },
        { 3, "loggingmod:advancedsawhorseframea-birch-east", "loggingmod:advancedsawhorseframeb-birch-east" },
        { 4, "loggingmod:advancedsawhorseframeb-birch-north", "loggingmod:sawhorseadvanced-birch-north" },
        { 5, "loggingmod:storagerackframe-birch-north", "loggingmod:trunkstorage-birch-empty-north" },
        { 6, "loggingmod:heatingrackframe-fire-north", "loggingmod:resinrack-fire-north" },
        { 7, "loggingmod:stickstorageframe", "loggingmod:stickstorage" },
    };

    // Logging Expanded puts the trunk heating rack a block above its frame, over the fire.
    private static int Rise(string makes) => makes.StartsWith("loggingmod:resinrack-") ? 1 : 0;

    private async Task PlaceFrame(Woodshop shop, BlockPos pos, string code)
    {
        var block = shop.Block(code);
        Assert.True(block.DoPlaceBlock(W, shop.P, Woodshop.Selection(pos), new ItemStack(block)));
        await World.Ticks(2);
        Assert.Equal(code, World.BlockAt(pos).Code.ToString());
    }

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(Frames))]
    public async Task In_creative_a_Ctrl_click_builds_a_frame_into_its_next_stage(int n, string frame, string makes)
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(220, 3, -160 + 20 * n));
        var pos = shop.Cell(0);
        await PlaceFrame(shop, pos, frame);
        shop.Holding(null);
        shop.InOffhand(null);
        Assert.True(shop.CtrlClick(pos));
        await World.Ticks(2);
        Assert.Equal(makes, World.BlockAt(pos.UpCopy(Rise(makes))).Code.ToString());
        // The staged items are gone again: the hands are as they were, and nothing was dropped.
        Assert.Null(shop.Hand.Itemstack);
        Assert.Null(shop.Offhand.Itemstack);
        Assert.Empty(await shop.Collect());

        // Held items stay where they are.
        var again = shop.Cell(2);
        await PlaceFrame(shop, again, frame);
        shop.Holding("game:stick", 5);
        Assert.True(shop.CtrlClick(again));
        await World.Ticks(2);
        Assert.Equal(makes, World.BlockAt(again.UpCopy(Rise(makes))).Code.ToString());
        Assert.Equal(("game:stick", 5), (shop.Hand.Itemstack?.Collectible.Code.ToString(), shop.Hand.StackSize));
        shop.Holding(null);
    }

    // A client reports how long it held; the server believes no more than the time since the hold
    // began there (Immersive Woodworking's HonestHoldSeconds), so a claimed 3 s nail is refused.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task An_upgrade_hold_counts_only_the_time_the_server_saw()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-40, 3, 160));
        var pos = await shop.PlaceSplittingBlock(0, "oak", SplittingBlockTier.Bound);
        var nails = shop.Holding(Woodshop.Nails, 8);
        shop.InOffhand(Woodshop.Hammer);
        var controls = shop.Player.Entity.Controls;
        try
        {
            controls.UsingBeginMS = W.ElapsedMilliseconds;
            Assert.True(shop.Hold(pos, 3f));
            Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(pos));
            Assert.Equal(8, nails.StackSize);

            controls.UsingBeginMS = W.ElapsedMilliseconds - 3500;
            Assert.True(shop.Hold(pos, 3f));
            Assert.Equal(SplittingBlockTier.Advanced, shop.TierAt(pos));
        }
        finally
        {
            controls.UsingBeginMS = 0;
            shop.InOffhand(null);
        }
    }

    // A splitting block of no wood: placed from the creative or handbook stack or /giveblock.
    // Immersive Woodworking drops a plain stack for it, not its picked one; the tier stays.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_splitting_block_with_no_wood_keeps_its_tier_when_broken()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(40, 3, 160));
        var pos = shop.Cell(0);
        var stack = new ItemStack(shop.Block(Woodshop.SplittingBlock));
        stack.Attributes.SetString(SplittingBlockTiers.AttributeKey, SplittingBlockTier.Bound.Name());
        W.BlockAccessor.SetBlock(stack.Block.Id, pos, stack);
        await World.Ticks(2);
        Assert.Null(shop.WoodAt(pos));
        Assert.Equal(SplittingBlockTier.Bound, shop.TierAt(pos));

        var drop = Assert.Single(W.BlockAccessor.GetBlock(pos).GetDrops(W, pos, shop.P));
        Assert.Equal(Woodshop.SplittingBlock, drop.Collectible.Code.ToString());
        Assert.Equal(SplittingBlockTier.Bound, BEBehaviorSplittingBlockTier.Of(drop));
        Assert.Null(drop.Attributes.GetString("wood"));
        Assert.Equal(Lang.Get(SplittingBlockTier.Bound.NameKey()), drop.GetName());
    }

    public static TheoryData<int> Tiers => [.. Enumerable.Range(0, 4)];

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(Tiers))]
    public async Task Breaking_it_gives_it_back_with_its_wood_and_tier_through_a_save(int tierIndex)
    {
        var tier = (SplittingBlockTier)tierIndex;
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-80 - 20 * tierIndex, 3, -80));
        var pos = await shop.PlaceSplittingBlock(0, "birch", tier);
        Assert.Equal(tier, shop.TierAt(pos));

        // Saved and loaded again, as a chunk is.
        var tree = new TreeAttribute();
        shop.Entity(pos).ToTreeAttributes(tree);
        World.SetBlock("game:air", pos);
        World.SetBlock(Woodshop.SplittingBlock, pos);
        await World.Ticks(2);
        shop.Entity(pos).FromTreeAttributes(tree, W);
        Assert.Equal(tier, shop.TierAt(pos));
        Assert.Equal("birch", shop.WoodAt(pos));

        var block = W.BlockAccessor.GetBlock(pos);
        var drop = Assert.Single(block.GetDrops(W, pos, shop.P));
        Assert.Equal(Woodshop.SplittingBlock, drop.Collectible.Code.ToString());
        Assert.Equal("birch", drop.Attributes.GetString("wood"));
        Assert.Equal(tier, BEBehaviorSplittingBlockTier.Of(drop));
        output.WriteLine(drop.GetName());
        Assert.EndsWith(Lang.Get(tier.WoodNameKey(), "").Trim(), drop.GetName());
        Assert.Contains("irch", drop.GetName());

        // Placed again, it is that tier.
        var again = shop.Cell(1);
        W.BlockAccessor.SetBlock(drop.Block.Id, again, drop);
        await World.Ticks(2);
        Assert.Equal(tier, shop.TierAt(again));
    }

    // --- chopping ---

    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(Tiers))]
    public async Task Firewood_per_log_is_six_below_the_advanced_tier_and_eight_on_it(int tierIndex)
    {
        var tier = (SplittingBlockTier)tierIndex;
        int perLog = tier == SplittingBlockTier.Advanced ? 8 : 6;
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(0, 3, 120 + 20 * tierIndex));
        var pos = await shop.PlaceSplittingBlock(0, "oak", tier);

        // Axe: the log into two half-logs, each into half the log's firewood.
        Assert.True(shop.Lay(pos, shop.Stack("game:log-placed-oak-ud")));
        shop.Holding(Woodshop.Axe);
        shop.Chop(pos);
        var halves = await shop.Collect();
        Assert.Equal(2, halves["immersivewoodworking:halflog"]);
        int firewood = 0;
        for (int i = 0; i < 2; i++)
        {
            var half = new ItemStack(shop.Item("immersivewoodworking:halflog"));
            half.Attributes.SetString("wood", "oak");
            half.Attributes.SetString("woodDomain", "game");
            Assert.True(shop.Lay(pos, half));
            shop.Holding(Woodshop.Axe);
            shop.Chop(pos);
            var made = await shop.Collect();
            var wood = Assert.Single(made);
            Assert.EndsWith("firewood", wood.Key);
            Assert.Equal(perLog / 2, wood.Value);
            firewood += wood.Value;
        }
        Assert.Equal(perLog, firewood);

        // Maul: the log into firewood in one pass.
        Assert.True(shop.Lay(pos, shop.Stack("game:log-placed-oak-ud")));
        shop.Holding(Woodshop.Maul);
        shop.Chop(pos);
        var mauled = Assert.Single(await shop.Collect());
        Assert.EndsWith("firewood", mauled.Key);
        Assert.Equal(perLog, mauled.Value);

        // Firewood into sticks, 2 each, whatever the tier.
        Assert.True(shop.Lay(pos, shop.Stack(mauled.Key)));
        shop.Holding(Woodshop.Axe);
        shop.Chop(pos);
        Assert.Equal(new Dictionary<string, int> { ["game:stick"] = 2 }, await shop.Collect());
        // Immersive Woodworking's setting is back where it was after an advanced block's chop.
        Assert.Equal(6, IwSetting<int>("FirewoodPerLog"));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task A_splitting_block_is_never_laid_on_one_nor_restocked_onto_it()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-120, 3, 0));
        var pos = await shop.PlaceSplittingBlock(0, "oak", SplittingBlockTier.Primitive);
        Assert.False(shop.Lay(pos, shop.SplittingBlockStack("oak", SplittingBlockTier.Advanced)));
        Assert.NotNull(shop.Hand.Itemstack);
        Assert.Null(shop.ContentAt(pos));

        // A bound splitting block in the hotbar: Immersive Woodworking's restock after a chop takes
        // logs from there, and must not take it.
        var hotbar = shop.P.InventoryManager.GetOwnInventory(GlobalConstants.hotBarInvClassName);
        var spare = hotbar.First(slot => slot != shop.Hand && slot.Empty);
        spare.Itemstack = shop.SplittingBlockStack("oak", SplittingBlockTier.Bound);
        spare.MarkDirty();
        Assert.True(shop.Lay(pos, shop.Stack("game:log-placed-oak-ud")));
        shop.Holding(Woodshop.Maul);
        shop.Chop(pos);
        await World.Ticks(10);
        Assert.Null(shop.ContentAt(pos));
        Assert.Equal(Woodshop.SplittingBlock, spare.Itemstack?.Collectible.Code.ToString());
        Assert.Equal(SplittingBlockTier.Bound, BEBehaviorSplittingBlockTier.Of(spare.Itemstack));
        spare.Itemstack = null;
        await shop.Collect();
    }

    // --- the mechanical chopper ---

    private static bool HasBed(BlockEntity chopper) => (bool)AccessTools.Field(chopper.GetType(), "hasBed").GetValue(chopper)!;

    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task The_chopper_takes_only_an_advanced_splitting_block_as_its_bed()
    {
        var origin = World.Spawn.AddCopy(120, 12, 0);
        var site = new ChopperSite(World, origin, BlockFacing.NORTH);
        await site.Build(hopper: false);
        var shop = await Woodshop.Open(World, origin.AddCopy(0, 0, 8));
        var frame = W.BlockAccessor.GetBlock(origin);

        foreach (var tier in new[] { SplittingBlockTier.Primitive, SplittingBlockTier.Debarked, SplittingBlockTier.Bound })
        {
            shop.Holding(shop.SplittingBlockStack("birch", tier));
            Assert.True(frame.OnBlockInteractStart(W, shop.P, Woodshop.Selection(origin)));
            Assert.False(HasBed(site.Chopper));
            Assert.NotNull(shop.Hand.Itemstack);
            Assert.True(((ItemSlot)AccessTools.Property(site.Chopper.GetType(), "InputSlot").GetValue(site.Chopper)!).Empty);
        }
        shop.Holding(shop.SplittingBlockStack("birch", SplittingBlockTier.Advanced));
        Assert.True(frame.OnBlockInteractStart(W, shop.P, Woodshop.Selection(origin)));
        Assert.True(HasBed(site.Chopper));
        Assert.Null(shop.Hand.Itemstack);

        // 8 firewood per log, 2 sticks per firewood.
        Assert.Equal(8, site.ChopOneLog());
        var input = (ItemSlot)AccessTools.Property(site.Chopper.GetType(), "InputSlot").GetValue(site.Chopper)!;
        input.Itemstack = new ItemStack(W.GetItem(new AssetLocation("game:firewood")));
        var (sticks, _) = ((ItemStack, int))AccessTools.Method(site.Chopper.GetType(), "GetChopBatch").Invoke(site.Chopper, null)!;
        input.Itemstack = null;
        Assert.Equal("game:stick", sticks.Collectible.Code.ToString());
        Assert.Equal(2, sticks.StackSize);

        // Breaking the frame gives the bed back, advanced and of its wood.
        var bed = Assert.Single(frame.GetDrops(W, origin, shop.P), d => d.Collectible.Code.ToString() == Woodshop.SplittingBlock);
        Assert.Equal(SplittingBlockTier.Advanced, BEBehaviorSplittingBlockTier.Of(bed));
        Assert.Equal("birch", bed.Attributes.GetString("wood"));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_assembled_creative_chopper_has_an_advanced_oak_bed()
    {
        var machine = AssembledMachines.Machines.Single(m => m.FrameCode.StartsWith("chopper"));
        var frame = W.GetBlock(new AssetLocation(AssembledMachines.ModId, machine.FrameCode))!;
        var pos = World.Spawn.AddCopy(140, 3, 40);
        var stack = frame.CreativeInventoryStacks[0].Stacks[0].ResolvedItemstack!.Clone();
        W.BlockAccessor.SetBlock(frame.Id, pos, stack);
        await World.Ticks(2);
        var tree = new TreeAttribute();
        W.BlockAccessor.GetBlockEntity(pos)!.ToTreeAttributes(tree);
        Assert.Equal(AssembledMachines.BedWood, tree.GetString("bedWood"));
        Assert.Equal(AssembledMachines.BedWoodDomain, tree.GetString("bedWoodDomain"));

        var bed = Assert.Single(frame.GetDrops(W, pos, null!), d => d.Collectible.Code.ToString() == Woodshop.SplittingBlock);
        Assert.Equal(SplittingBlockTier.Advanced, BEBehaviorSplittingBlockTier.Of(bed));
        Assert.Equal("oak", bed.Attributes.GetString("wood"));

        // One assembled before its bed had a wood: the bed comes back an advanced oak one, the
        // wood Immersive Woodworking drew it in.
        var chopper = W.BlockAccessor.GetBlockEntity(pos)!;
        tree.RemoveAttribute("bedWood");
        tree.RemoveAttribute("bedWoodDomain");
        chopper.FromTreeAttributes(tree, W);
        Assert.Null(AccessTools.Field(chopper.GetType(), "bedWood").GetValue(chopper));
        var old = Assert.Single(frame.GetDrops(W, pos, null!), d => d.Collectible.Code.ToString() == Woodshop.SplittingBlock);
        Assert.Equal(SplittingBlockTier.Advanced, BEBehaviorSplittingBlockTier.Of(old));
        Assert.Equal(SplittingBlock.DefaultWood, old.Attributes.GetString("wood"));
        Assert.Equal(SplittingBlock.DefaultWoodDomain, old.Attributes.GetString("woodDomain"));
    }

    // --- sawhorses ---

    public static TheoryData<string, int, int> Sawhorses => new()
    {
        { "loggingmod:sawhorse-north", 2, 9 },
        { "loggingmod:sawhorsestandard-oak-copper-north", 2, 12 },
        { "loggingmod:sawhorseadvanced-oak-north", 3, 18 },
    };

    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(Sawhorses))]
    public async Task A_sawhorse_saws_beams_with_Shift_and_boards_without(string sawhorse, int beams, int boards)
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(0, 3, -120 - 10 * boards));
        var pos = await shop.PlaceLoadedSawhorse(0, sawhorse, 4);

        var saw = shop.Holding(Woodshop.Saw);
        int sawBefore = shop.Durability(saw);
        shop.Player.Entity.Controls.ShiftKey = true;
        await shop.Work(pos);
        shop.Player.Entity.Controls.ShiftKey = false;
        Assert.Equal(new Dictionary<string, int> { ["game:supportbeam-oak"] = beams }, await shop.Collect());
        Assert.Equal(3, shop.LogsOn(pos));
        Assert.Equal(sawBefore - 1, shop.Durability(saw));

        // Without Shift, Logging Expanded's boards.
        await shop.Work(pos);
        Assert.Equal(new Dictionary<string, int> { ["game:plank-oak"] = boards }, await shop.Collect());
        Assert.Equal(2, shop.LogsOn(pos));
    }

    [AtlasTheory(TimeoutMs = 180_000), MemberData(nameof(Sawhorses))]
    public async Task A_sawhorse_debarks_with_the_bark_spud_and_drops_the_bark(string sawhorse, int beams, int boards)
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(-160, 3, -120 - 10 * boards));
        var pos = await shop.PlaceLoadedSawhorse(0, sawhorse, 2);
        var spud = shop.Holding(Woodshop.Spud);
        int before = shop.Durability(spud);
        await shop.Work(pos);
        // Loose logs debark 1 for 1 on every tier; an iron spud always gets oak's tan bark.
        Assert.Equal(new Dictionary<string, int>
        {
            ["game:debarkedlog-oak-ud"] = 1,
            ["immersivewoodworking:bark-tan-green"] = 3,
        }, await shop.Collect());
        Assert.Equal(1, shop.LogsOn(pos));
        Assert.Equal(before - 1, shop.Durability(spud));
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task The_advanced_sawhorse_debarks_two_trunk_logs_into_three_with_two_bark_rolls()
    {
        var shop = await Woodshop.Open(World, World.Spawn.AddCopy(160, 3, -160));
        var pos = await shop.PlaceLoadedSawhorse(0, "loggingmod:sawhorseadvanced-oak-north", 4);
        // As a felled trunk loads it: the same logs, not loaded from loose logs.
        var inventory = AccessTools.Property(shop.Entity(pos).GetType(), "Inventory").GetValue(shop.Entity(pos))!;
        AccessTools.Property(inventory.GetType(), "LoadedFromLogs").SetValue(inventory, false);

        shop.Holding(Woodshop.Axe);
        shop.InOffhand(Woodshop.Hammer);
        await shop.Work(pos);
        shop.InOffhand(null);
        var made = await shop.Collect();
        foreach (var (code, count) in made)
            output.WriteLine($"{count}x {code}");
        Assert.Equal(2, shop.LogsOn(pos));
        Assert.Equal(3, made["game:debarkedlog-oak-ud"]);
        // Two rolls of 3 pieces each, tan or generic at the axe's 0.75.
        Assert.Equal(6, made.Where(m => m.Key.StartsWith("immersivewoodworking:bark-")).Sum(m => m.Value));
        Assert.All(made.Keys.Where(k => k.StartsWith("immersivewoodworking:bark-")),
            k => Assert.Contains(k, new[] { "immersivewoodworking:bark-tan-green", "immersivewoodworking:bark-generic" }));
    }

    // --- retired ---

    [AtlasScenario]
    public void Retired_stations_are_hidden_and_nothing_makes_them()
    {
        foreach (var block in W.Blocks.Where(b => b.Code != null
                                                  && (b.Code.Domain == WoodworkingMods.LeModId && b.Code.Path.Contains("splittinglog-")
                                                      || b.Code.Domain == WoodworkingMods.IwModId && b.Code.Path.StartsWith("sawhorse"))))
        {
            Assert.Empty(block.CreativeInventoryTabs ?? []);
            Assert.Empty(block.CreativeInventoryStacks ?? []);
            Assert.True(block.Attributes?["handbook"]["exclude"].AsBool() == true, $"{block.Code} is in the handbook");
        }
        foreach (var code in new[] { "pitsaw", "pitsawblade" })
            foreach (var item in W.Items.Where(i => i.Code?.Domain == WoodworkingMods.IwModId && i.Code.FirstCodePart() == code))
            {
                Assert.Empty(item.CreativeInventoryTabs ?? []);
                Assert.True(item.Attributes?["handbook"]["exclude"].AsBool() == true, $"{item.Code} is in the handbook");
            }
        // Still registered, for worlds that have them.
        Assert.NotNull(W.GetBlock(new AssetLocation("loggingmod:advancedsplittinglog-oak")));

        bool Retired(AssetLocation? code) => code != null
            && (code.Domain == WoodworkingMods.IwModId && (code.Path == "choppingblock" || code.Path.StartsWith("sawhorse")
                                                           || code.FirstCodePart() is "pitsaw" or "pitsawblade")
                || code.Domain == WoodworkingMods.LeModId && code.Path.Contains("splittinglog"));
        // Immersive Woodworking's clay covering coats a blade there already is; it makes none.
        var made = W.GridRecipes.Where(r => Retired(r.Output?.Code)
                                            && r.ResolvedIngredients?.Any(i => i != null && r.Output.ResolvedItemStack is { } output
                                                                                   && i.SatisfiesAsIngredient(output, false)) != true).Select(r => $"grid {r.Name}: {r.Output.Code}")
            .Concat(World.Api.GetSmithingRecipes().Where(r => Retired(r.Output?.Code)).Select(r => $"smithing {r.Name}: {r.Output.Code}"))
            .Concat(World.Api.GetClayformingRecipes().Where(r => Retired(r.Output?.Code)).Select(r => $"clayforming {r.Name}: {r.Output.Code}"))
            .ToList();
        Assert.True(made.Count == 0, "Recipes make retired stations:\n" + string.Join("\n", made));

        // Immersive Woodworking's sawhorse is not built from sticks.
        var stick = W.GetItem(new AssetLocation("game:stick"))!;
        Assert.DoesNotContain(stick.CollectibleBehaviors, b => b.GetType().Name == "BehaviorSawhorseBuild");
    }
}
