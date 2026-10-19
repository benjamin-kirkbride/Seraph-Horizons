using Atlas.XUnit;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Ore.Core;
using SeraphHorizons.Mod.Ore.Processing;
using SeraphHorizons.Mod.Rocker;
using SeraphHorizons.Mod.Rocker.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Rocker (mods-src/seraphhorizons/Rocker/, #716): the rocker in the plain world, worked by
// hand. The player's right-click held with a bucket is played through the block's own interaction calls
// (start, a step each tick, stop), as the game makes them, so the scenarios show it rocking and pouring
// only while held; an item takes four seconds, so they finish the charge through BERocker.Rock, as the
// press brake's finish a plate through Fold. Every scenario builds on a granite floor of its own 40 above
// spawn at x -580 to -620, z -860 (clear of the eidolon body's at z -600), and they share the gear cutter's player, gearcutterhand.
public partial class MillWorldScenarios
{
    private RockerSystem RockerMod => RockerSystem.Of(World.Api);
    private const string RockerItem = "seraphhorizons:rocker-north";

    private async Task<BERocker> PlaceRocker(BlockPos pos, string side = "south")
    {
        World.SetBlock($"seraphhorizons:rocker-{side}", pos);
        await World.Ticks(5);
        return W.BlockAccessor.GetBlockEntity(pos) as BERocker ?? throw new Xunit.Sdk.XunitException($"no rocker at {pos}");
    }

    /// <summary>A wooden bucket holding <paramref name="litres"/> of the game's water.</summary>
    private ItemStack WaterBucket(double litres = 10)
    {
        var bucket = Assert.IsAssignableFrom<BlockLiquidContainerBase>(W.GetBlock(new AssetLocation("game:woodbucket")));
        var water = W.GetItem(new AssetLocation(RockerWater.PlainWater))!;
        var stack = new ItemStack(bucket);
        bucket.SetContent(stack, new ItemStack(water, (int)Math.Round(litres * 100)));
        return stack;
    }

    private static string RockerInfo(BERocker rocker, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        rocker.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    /// <summary>Right-click held on the rocker with <paramref name="held"/> for <paramref name="ticks"/> ticks,
    /// as the game plays it. Returns whether the start was the rocker's and how many steps it went on for.</summary>
    private async Task<(bool Started, int Steps)> RockerHold(IPlayer player, BlockPos at, int ticks, ItemStack? held)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        var block = W.BlockAccessor.GetBlock(at);
        if (!block.OnBlockInteractStart(W, player, sel))
            return (false, 0);
        int steps = 0;
        float seconds = 0;
        for (int i = 0; i < ticks; i++)
        {
            await World.Ticks(1);
            seconds += 0.05f;
            if (!W.BlockAccessor.GetBlock(at).OnBlockInteractStep(seconds, W, player, sel))
                break;
            steps++;
        }
        W.BlockAccessor.GetBlock(at).OnBlockInteractStop(seconds, W, player, sel);
        return (true, steps);
    }

    [AtlasScenario]
    public void Rocker_loads_with_its_block_and_recipe()
    {
        Assert.True(RockerSystem.Applies(World.Api));
        Assert.NotNull(RockerMod.Rig);
        foreach (var side in new[] { "north", "east", "south", "west" })
            Assert.IsType<BlockRocker>(W.GetBlock(new AssetLocation($"seraphhorizons:rocker-{side}")));
        Assert.Equal("Rocker", new ItemStack(W.GetBlock(BlockRocker.ItemCode)).GetName());
        var recipe = Assert.Single(W.GridRecipes, r => r.Output?.Code?.ToString() == RockerItem && r.Enabled);
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:plank-oak");
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.ToString() == "game:cloth-plain");
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.Code?.Path == "metalplate-*" && i.AllowedVariants!.Contains("iron"));
        Assert.Contains(recipe.ResolvedIngredients!, i => i?.IsTool == true && i.Code?.Path == "hammer-*");
        // the figure it works at is ore processing's
        Assert.Equal(0.55, OreProcessingSystem.RecoveryFor(World.Api).Figure(HandStation.Rocker), 6);
    }

    // Placed on solid ground only; no inventory for a hopper or chute; breaking it drops it and its load.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Rocker_stands_on_solid_ground_and_takes_no_automated_transfer()
    {
        var pos = await CutterSite(-580, -860);
        var player = await CutterPlayer();
        var block = W.GetBlock(BlockRocker.ItemCode);
        string failure = "";
        var air = new BlockSelection { Position = pos.UpCopy(2), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0, 0.5) };
        Assert.False(block.CanPlaceBlock(W, player, air, ref failure));
        Assert.Equal("requiresolidground", failure);
        var rocker = await PlaceRocker(pos);
        Assert.False(rocker is IBlockEntityContainer);
        Assert.Contains("Hopper empty", RockerInfo(rocker, player));

        Assert.Equal(2, CutterClick(player, pos, CutterItem("seraphhorizons:crushedore-magnetite-medium", 6))?.StackSize);
        Assert.Equal(4, rocker.State.ChargeCount);
        W.BlockAccessor.BreakBlock(pos, player);
        await World.Ticks(2);
        var near = CutterItemsNear(pos);
        Assert.Equal(1, near.GetValueOrDefault(RockerItem));
        Assert.Equal(4, near.GetValueOrDefault("seraphhorizons:crushedore-magnetite-medium"));
        CutterKillItems(pos);
    }

    // Held with a bucket it rocks and pours; let go it stops. The charge washes into the ore's concentrate
    // at the rocker's 55 % (× 0.85 unriddled), the rest carried, and its tailings fall west of the foot.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Rocker_washes_crushed_ore_into_concentrate_while_rocked_with_water()
    {
        var pos = await CutterSite(-592, -860);
        var player = await CutterPlayer();
        var rocker = await PlaceRocker(pos);
        const string Crushed = "seraphhorizons:crushedore-magnetite-medium";
        Assert.Null(CutterClick(player, pos, CutterItem(Crushed, 4)));
        Assert.Equal(4, rocker.State.ChargeCount);
        // another kind is refused while a charge is in
        Assert.Equal(1, CutterClick(player, pos, CutterItem("seraphhorizons:crushedore-hematite-medium"))?.StackSize);

        // an empty bucket does not rock it
        var (startedDry, _) = await RockerHold(player, pos, 5, WaterBucket(0));
        Assert.False(startedDry);
        // a second of rocking: about a quarter of the first item, half a litre poured
        var (started, steps) = await RockerHold(player, pos, 20, WaterBucket());
        Assert.True(started);
        Assert.True(steps >= 15, $"only {steps} steps");
        Assert.InRange(rocker.State.ItemWork, 0.15, 0.35);
        var bucket = player.InventoryManager.ActiveHotbarSlot.Itemstack!;
        Assert.InRange(RockerWater.Litres(bucket), 9.3, 9.8);
        Assert.Contains("magnetite", RockerInfo(rocker, player), StringComparison.OrdinalIgnoreCase);
        double work = rocker.State.ItemWork;
        await World.Ticks(10);
        Assert.Equal(work, rocker.State.ItemWork, 9);

        // the rest of the charge
        var step = rocker.Rock(player, 100);
        Assert.Equal(4, step.Washed);
        Assert.False(rocker.State.HasCharge);
        // 4 × 5 units × 0.85 unriddled × 0.55 = 9.35: one concentrate, 4.35 carried
        Assert.Equal(1, rocker.State.Riffles["seraphhorizons:concentrate-magnetite"]);
        Assert.Equal(4.35, rocker.State.Carry.HeldFor("seraphhorizons:concentrate-magnetite"), 6);
        Assert.InRange(RockerWater.Litres(player.InventoryManager.ActiveHotbarSlot.Itemstack), 1.9, 2.1);
        // the tailings: layered gravel west of the foot
        var tailings = W.BlockAccessor.GetBlock(rocker.TailingsPos());
        Assert.StartsWith("gravel-", tailings.Code.Path);
        Assert.Equal(pos.AddCopy(-1, 0, 0), rocker.TailingsPos());

        // the concentrate comes out by hand
        var taken = CutterClick(player, pos, null);
        Assert.Equal(("seraphhorizons:concentrate-magnetite", 1), (taken?.Collectible.Code.ToString(), taken?.StackSize));
        Assert.False(rocker.State.HasCatch);
        CutterKillItems(pos);
    }

    // Riddled ore keeps the full 55 %; an untouched charge comes back with Ctrl.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Rocker_gives_riddled_ore_the_full_figure_and_returns_an_untouched_charge()
    {
        var pos = await CutterSite(-604, -860);
        var player = await CutterPlayer();
        var rocker = await PlaceRocker(pos, "east");
        var riddled = CutterItem("seraphhorizons:crushedore-magnetite-medium", 2);
        OreStreamAttributes.AddState(riddled, OreState.Classified);
        Assert.Null(CutterClick(player, pos, riddled));
        Assert.Equal(2, CutterClick(player, pos, null, ctrl: true)?.StackSize);
        Assert.False(rocker.State.HasCharge);

        riddled = CutterItem("seraphhorizons:crushedore-magnetite-medium", 2);
        OreStreamAttributes.AddState(riddled, OreState.Classified);
        Assert.Null(CutterClick(player, pos, riddled));
        player.InventoryManager.ActiveHotbarSlot.Itemstack = WaterBucket();
        rocker.Rock(player, 100);
        // 2 × 5 × 0.55 = 5.5: one concentrate, 0.5 carried
        Assert.Equal(1, rocker.State.Riffles["seraphhorizons:concentrate-magnetite"]);
        Assert.Equal(0.5, rocker.State.Carry.HeldFor("seraphhorizons:concentrate-magnetite"), 6);
        CutterKillItems(pos);
    }

    // Rich gravel gives the gold pan's table at the rocker's rate.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Rocker_washes_rich_gravel_into_the_pans_metals()
    {
        var pos = await CutterSite(-616, -860);
        var player = await CutterPlayer();
        var rocker = await PlaceRocker(pos);
        var drops = RockerSystem.PlacerDrops(W, "game:richgravel-granite");
        Assert.Contains(drops, d => d.Metal && d.ChancePerPan > 0);
        Assert.Null(CutterClick(player, pos, CutterItem("game:richgravel-granite", 4)));
        player.InventoryManager.ActiveHotbarSlot.Itemstack = WaterBucket();
        var step = rocker.Rock(player, 100);
        Assert.Equal(4, step.Washed);
        var expected = RockerSystem.Of(World.Api).YieldOf(CutterItem("game:richgravel-granite"));
        foreach (var y in expected)
        {
            int got = rocker.State.Riffles.GetValueOrDefault(y.Output);
            Assert.Equal(Math.Floor(4 * y.Units + 1e-9), got);
        }
        var pan = drops.Where(d => d.Metal).Sum(d => d.ChancePerPan) * RockerSystem.PansPerBlock;
        Assert.Equal(pan * 0.55 / 0.45, expected.Where(y => y.Output.Contains("nugget-")).Sum(y => y.Units), 6);
        CutterKillItems(pos);
    }
}
