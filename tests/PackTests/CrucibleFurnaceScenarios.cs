using Atlas.XUnit;
using SeraphHorizons.Mod.CrucibleFurnace;
using SeraphHorizons.Mod.CrucibleFurnace.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/CrucibleFurnace (<c>StainlessSteel</c>): the crucible furnace with the
/// default <c>CrucibleFurnaceSettings</c>. A row of melting holes is built with a brick chimney, and a
/// player works a hole by right-clicks through the block's <c>OnBlockInteractStart</c>, as the game
/// calls it. The clock is the world's, which a scenario must not move, so a fire is aged by moving its
/// last tick back (<see cref="BEMeltingHole.Age"/>) and run by <see cref="BEMeltingHole.Update"/>.
/// </summary>
[AtlasWorld]
public class CrucibleFurnaceScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private const string Hole = "seraphhorizons:meltinghole-closed";
    private const string Brick = "game:claybricks-good-fire";

    private sealed class Site(CrucibleFurnaceScenarios s, BlockPos origin, IPlayer player)
    {
        public BlockPos Origin { get; } = origin;
        public IPlayer Player { get; } = player;
        private IWorldAccessor W => s.W;

        public BlockPos HolePos(int i) => Origin.AddCopy(i, 0, 0);
        public BEMeltingHole Hole(int i = 0) => Assert.IsType<BEMeltingHole>(W.BlockAccessor.GetBlockEntity(HolePos(i)));
        public ItemSlot Hand => Player.InventoryManager.ActiveHotbarSlot;
        public ItemSlot OffHand => Player.Entity.LeftHandItemSlot;

        public ItemStack Stack(string code, int size = 1) =>
            W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
            : new ItemStack(W.GetItem(new AssetLocation(code)) ?? throw new Xunit.Sdk.XunitException($"no {code}"), size);

        public bool RightClick(ItemStack? held, int hole = 0, bool sneak = false)
        {
            Hand.Itemstack = held;
            Hand.MarkDirty();
            Player.Entity.Controls.ShiftKey = sneak;
            try
            {
                var sel = new BlockSelection { Position = HolePos(hole), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 1, 0.5) };
                return W.BlockAccessor.GetBlock(HolePos(hole)).OnBlockInteractStart(W, Player, sel);
            }
            finally
            {
                Player.Entity.Controls.ShiftKey = false;
            }
        }

        public void OpenLid(int hole = 0)
        {
            if (Hole(hole).LidClosed)
                RightClick(null, hole, sneak: true);
            Assert.False(Hole(hole).LidClosed);
        }

        public string Info(int hole = 0)
        {
            var sb = new System.Text.StringBuilder();
            Hole(hole).GetBlockInfo(Player, sb);
            return sb.ToString();
        }

        private IEnumerable<ItemSlot> Slots() =>
            Player.InventoryManager.Inventories.Values
                .Where(i => i.ClassName is "hotbar" or "backpack")
                .SelectMany(i => i);

        public int Count(string code) =>
            Slots().Where(slot => slot.Itemstack?.Collectible?.Code?.ToString() == code).Sum(slot => slot.StackSize);

        public void EmptyInventory()
        {
            foreach (var slot in Slots().Append(OffHand))
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
        }

        /// <summary>Puts a fired pot with <paramref name="heats"/> heats in an open hole and charges it.</summary>
        public void Charge(int hole, int heats, params (string Code, int Count)[] items)
        {
            OpenLid(hole);
            var pot = Stack(CrucibleFurnaceSystem.FiredPot);
            PotStack.SetHeats(pot, heats);
            Assert.True(RightClick(pot, hole));
            Assert.True(Hole(hole).HasPot);
            foreach (var (code, count) in items)
            {
                Assert.True(RightClick(Stack(code, count), hole, sneak: code == "game:coke"));
                Assert.Null(Hand.Itemstack);   // all of it went in
            }
        }
    }

    /// <summary>Four holes along +x from <paramref name="dx"/>, a chimney of <paramref name="chimney"/>
    /// clay bricks beyond the last, and a player beside them.</summary>
    private async Task<Site> Build(string player, int dx, int holes = 4, int chimney = 6)
    {
        var p = await World.JoinPlayer(player);
        var origin = World.Spawn.AddCopy(dx, 20, 80);
        await p.TeleportTo(origin.AddCopy(2, 1, 2));
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin) != null, 30000);
        for (int x = -2; x <= 7; x++)
        for (int z = -2; z <= 2; z++)
        {
            World.SetBlock("game:rock-granite", origin.AddCopy(x, -1, z));
            for (int y = 0; y < 9; y++)
                World.SetBlock("game:air", origin.AddCopy(x, y, z));
        }
        for (int i = 0; i < holes; i++)
            World.SetBlock(Hole, origin.AddCopy(i, 0, 0));
        for (int y = 0; y < chimney; y++)
            World.SetBlock(Brick, origin.AddCopy(holes, y, 0));
        await World.Ticks(2);
        var site = new Site(this, origin, p.Player);
        site.EmptyInventory();
        return site;
    }

    [AtlasScenario]
    public void The_furnace_its_pot_ferroalloys_recipes_and_guide_exist()
    {
        foreach (var code in new[] { "seraphhorizons:meltinghole-closed", "seraphhorizons:meltinghole-open", "seraphhorizons:meltingpot-raw",
                     CrucibleFurnaceSystem.FiredPot, CrucibleFurnaceSystem.SmeltedPot })
            Assert.True(W.GetBlock(new AssetLocation(code)) is { Id: > 0 }, $"no {code}");
        Assert.IsType<BlockMeltingPotSmelted>(W.GetBlock(new AssetLocation(CrucibleFurnaceSystem.SmeltedPot)));
        Assert.NotNull(W.GetItem(new AssetLocation(Stainless.Ferrochrome)));
        Assert.NotNull(W.GetItem(new AssetLocation(Stainless.Ferrosilicon)));
        var hole = Assert.Single(W.GridRecipes, r => r.Output.Code?.ToString() == Hole && r.Enabled);
        Assert.Contains(hole.ResolvedIngredients, i => i?.Code?.ToString() == "game:refractorybrick-fired-tier3");
        Assert.Contains(World.Api.GetClayformingRecipes(), r => r.Output?.Code?.ToString() == "seraphhorizons:meltingpot-raw");
        var raw = W.GetBlock(new AssetLocation("seraphhorizons:meltingpot-raw"))!;
        Assert.Equal(CrucibleFurnaceSystem.FiredPot, raw.CombustibleProps.SmeltedStack.Code.ToString());
        // every default pot recipe's items are in the pack (smex's slag among them)
        Assert.Equal(PotRecipes.Default.Recipes.Select(r => r.Code), CrucibleFurnaceSystem.Of(World.Api).Recipes.Recipes.Select(r => r.Code));
        Assert.Empty(CrucibleFurnaceSystem.Of(World.Api).Config.Sanitise());
        Assert.Equal("Making stainless steel", Lang.GetL("en", CrucibleFurnaceSystem.GuideTitleKey));
        Assert.NotNull(World.Api.Assets.TryGet(new AssetLocation("seraphhorizons:config/handbook/cruciblefurnace.json")));
        // the game's stainless ingot and bits are shown in the handbook
        foreach (var code in new[] { Stainless.Ingot, Stainless.Bit })
            Assert.NotEqual(true, W.GetItem(new AssetLocation(code))!.Attributes?["handbook"]?["exclude"]?.AsBool());
        // a hole, its pot and the ferroalloys have their names
        Assert.Equal("Melting hole", new ItemStack(W.GetBlock(new AssetLocation(Hole))).GetName());
        Assert.Equal("Ferrochrome", new ItemStack(W.GetItem(new AssetLocation(Stainless.Ferrochrome))).GetName());
    }

    [AtlasScenario]
    public async Task A_row_needs_its_chimney_and_at_most_four_holes()
    {
        var site = await Build("rowbuilder", 0);
        for (int i = 0; i < 4; i++)
        {
            site.Hole(i).Update();
            Assert.Equal(RowProblem.None, site.Hole(i).Problem);
            Assert.Equal(Draft.Chimney, site.Hole(i).Draft);
        }
        Assert.Contains("chimney (4 holes, stack 6 high)", site.Info(2));
        // the chimney a block short
        World.SetBlock("game:air", site.Origin.AddCopy(4, 5, 0));
        site.Hole(1).Update();
        Assert.Equal(RowProblem.ChimneyTooShort, site.Hole(1).Problem);
        Assert.Equal(Draft.None, site.Hole(1).Draft);
        World.SetBlock(Brick, site.Origin.AddCopy(4, 5, 0));
        // a fifth hole at the other end
        World.SetBlock(Hole, site.Origin.AddCopy(-1, 0, 0));
        site.Hole(0).Update();
        Assert.Equal(RowProblem.TooManyHoles, site.Hole(0).Problem);
        World.SetBlock("game:air", site.Origin.AddCopy(-1, 0, 0));
        site.Hole(0).Update();
        Assert.Equal(RowProblem.None, site.Hole(0).Problem);
    }

    [AtlasScenario]
    public async Task Stainless_melts_pulls_with_tongs_pours_and_freezes()
    {
        var site = await Build("stainlessmaker", 12);
        site.Charge(0, 0, ("game:ingot-iron", 1), ("game:metalbit-iron", 12), (Stainless.Ferrochrome, 8));
        Assert.Contains("Makes stainless steel, melts at 1530 °C", site.Info());
        // coke on the fire, lit, lid shut
        Assert.True(site.RightClick(site.Stack("game:coke", 6)));
        Assert.Equal(6, site.Hole().Coke, 6);
        Assert.True(site.Hole().TryLight(site.Player));
        Assert.True(site.Hole().Lit);
        site.RightClick(null, sneak: true);
        Assert.True(site.Hole().LidClosed);
        // 5 hours on the chimney: about 1020 °C, not melted, the coke topped up
        site.Hole().Age(5);
        site.Hole().Update();
        Assert.InRange(site.Hole().Temperature, 1000, 1040);
        Assert.Null(site.Hole().Melted);
        site.OpenLid();
        Assert.True(site.RightClick(site.Stack("game:coke", 6)));
        site.RightClick(null, sneak: true);
        site.Hole().Age(2.6);
        site.Hole().Update();
        Assert.Equal("stainless", site.Hole().Melted);
        Assert.Equal(200, site.Hole().MeltedUnits);
        Assert.Contains("Molten: 200 units of Stainless steel", site.Info());

        // open, and without tongs the pot stays
        site.OpenLid();
        Assert.True(site.RightClick(null));
        Assert.True(site.Hole().HasPot);
        Assert.Null(site.Hand.Itemstack);
        site.OffHand.Itemstack = site.Stack("game:tongs");
        site.OffHand.MarkDirty();
        Assert.True(site.RightClick(null));
        Assert.False(site.Hole().HasPot);
        var pot = site.Hand.Itemstack!;
        var smelted = Assert.IsType<BlockMeltingPotSmelted>(pot.Collectible);
        var contents = smelted.GetContents(W, pot);
        Assert.Equal(Stainless.Ingot, contents.Key.Collectible.Code.ToString());
        Assert.Equal(200, contents.Value);
        Assert.Equal(1, PotStack.Heats(pot));
        Assert.False(smelted.HasSolidifed(pot, contents.Key, W));
        // the pour window: from the furnace's temperature to solid in 20 real seconds
        var temp = (ITreeAttribute)pot.Attributes["temperature"];
        double hoursPerSecond = W.Calendar.SpeedOfTime * W.Calendar.CalendarSpeedMul / 3600.0;
        double drop = temp.GetFloat("temperature") - 0.9 * 1530;
        Assert.Equal(drop / (20 * hoursPerSecond), temp.GetFloat("cooldownSpeed"), 0);

        // an ingot mold takes it (the game's sink, as from its crucible)
        var moldPos = site.Origin.AddCopy(0, 0, 2);
        World.SetBlock("game:ingotmold-blue-fired", moldPos);
        await World.Ticks(1);
        var sink = Assert.IsAssignableFrom<ILiquidMetalSink>(W.BlockAccessor.GetBlockEntity(moldPos));
        Assert.True(sink.CanReceive(contents.Key));
        // stainless has a melting point (the pack's patch), which the pour window and the molds' hardening need
        Assert.Equal(1530, contents.Key.Collectible.GetMeltingPoint(W, null, new DummySlot(contents.Key)));

        // frozen: a right-click breaks the pot and knocks out 40 bits
        temp.SetFloat("temperature", 1300);
        temp.SetDouble("temperatureLastUpdate", W.Calendar.TotalHours);
        Assert.True(smelted.HasSolidifed(pot, contents.Key, W));
        var handling = EnumHandHandling.NotHandled;
        smelted.OnHeldInteractStart(site.Hand, site.Player.Entity, null!, null!, true, ref handling);
        Assert.Equal(EnumHandHandling.PreventDefault, handling);
        Assert.Equal(0, site.Count(CrucibleFurnaceSystem.SmeltedPot) + site.Count(CrucibleFurnaceSystem.FiredPot));   // the pot is gone
        Assert.Equal(40, site.Count(Stainless.Bit));
    }

    [AtlasScenario]
    public async Task A_wrong_ratio_is_refused_at_charging_and_lighting()
    {
        var site = await Build("ratiochecker", 24);
        site.Charge(0, 0, ("game:ingot-iron", 1));
        // a second ingot would be more iron than a full pot of stainless takes
        Assert.True(site.RightClick(site.Stack("game:ingot-iron")));
        Assert.Equal(1, site.Hand.StackSize);
        // quartz does not go with iron ingots
        Assert.True(site.RightClick(site.Stack("game:crushed-quartz", 4)));
        Assert.Equal(4, site.Hand.StackSize);
        // copper goes in no recipe
        Assert.True(site.RightClick(site.Stack("game:ingot-copper")));
        Assert.Equal(1, site.Hand.StackSize);
        // too little ferrochrome: the charge makes nothing, and the coke will not light
        Assert.True(site.RightClick(site.Stack(Stainless.Ferrochrome, 2)));
        Assert.Null(site.Hand.Itemstack);
        Assert.Contains("Makes nothing: the ratio is off for stainless steel", site.Info());
        Assert.Contains("Ferrochrome: 9 % (needs 18-22 %)", site.Info());
        Assert.True(site.RightClick(site.Stack("game:coke", 2)));
        Assert.False(site.Hole().TryLight(site.Player));
        Assert.False(site.Hole().Lit);
        // made up to the ratio, it lights
        Assert.True(site.RightClick(site.Stack(Stainless.Ferrochrome, 3)));
        Assert.Contains("Makes stainless steel", site.Info());
        Assert.True(site.Hole().TryLight(site.Player));
    }

    [AtlasScenario]
    public async Task Ferroalloys_break_out_of_the_pot_and_the_pot_cracks_after_three_heats()
    {
        var site = await Build("ferromaker", 36);
        site.OffHand.Itemstack = site.Stack("game:tongs");
        site.OffHand.MarkDirty();
        // ferrosilicon, the pot's first heat
        site.Charge(0, 0, ("game:crushed-quartz", 20), ("game:metalbit-iron", 12), ("game:coke", 8));
        site.Hole().SetFire(1390, 3, true);
        site.RightClick(null, sneak: true);
        site.Hole().Age(0.1);
        site.Hole().Update();
        Assert.True(site.Hole().Melted == "ferrosilicon", site.Info());
        site.OpenLid();
        Assert.True(site.RightClick(null));
        Assert.Equal(20, site.Count(Stainless.Ferrosilicon));
        Assert.Equal(CrucibleFurnaceSystem.FiredPot, site.Hand.Itemstack?.Collectible.Code.ToString());
        Assert.Equal(1, PotStack.Heats(site.Hand.Itemstack));

        // ferrochrome in a pot on its last heat: 20 ferrochrome, 8 slag, and the pot cracks
        site.Hand.Itemstack = null;
        site.Charge(1, 2, ("game:crushed-chromite", 20), (Stainless.Ferrosilicon, 12), ("game:lime", 8));
        site.Hole(1).SetFire(1545, 3, true);
        site.RightClick(null, 1, sneak: true);
        site.Hole(1).Age(0.1);
        site.Hole(1).Update();
        Assert.Equal("ferrochrome", site.Hole(1).Melted);
        site.OpenLid(1);
        Assert.True(site.RightClick(null, 1));
        Assert.Equal(20, site.Count(Stainless.Ferrochrome));
        Assert.Equal(8, site.Count("smex:slag"));
        Assert.Equal(0, site.Count(CrucibleFurnaceSystem.FiredPot));   // cracked
        Assert.False(site.Hole(1).HasPot);
    }

    [AtlasScenario]
    public async Task An_unmelted_pot_comes_back_with_its_charge_and_a_broken_hole_drops_it()
    {
        var site = await Build("potkeeper", 48);
        site.Charge(0, 1, ("game:metalbit-stainlesssteel", 20));
        Assert.True(site.RightClick(null));   // cold: no tongs needed
        var pot = site.Hand.Itemstack!;
        Assert.Equal(CrucibleFurnaceSystem.FiredPot, pot.Collectible.Code.ToString());
        Assert.Equal(new ChargeItem(Stainless.Bit, 20), Assert.Single(PotStack.Charge(pot)));
        Assert.Equal(1, PotStack.Heats(pot));
        // back in, and the hole broken: hole, pot with its charge, coke
        Assert.True(site.RightClick(pot));
        Assert.True(site.RightClick(site.Stack("game:coke", 3)));
        var drops = W.BlockAccessor.GetBlock(site.HolePos(0)).GetDrops(W, site.HolePos(0), site.Player);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == Hole);
        Assert.Contains(drops, d => d.Collectible.Code.ToString() == "game:coke" && d.StackSize == 3);
        var dropped = Assert.Single(drops, d => d.Collectible.Code.ToString() == CrucibleFurnaceSystem.FiredPot);
        Assert.Equal(20, PotStack.Charge(dropped).Single().Count);
    }

    [AtlasScenario]
    public async Task A_pipe_next_to_the_row_is_found_as_an_air_source()
    {
        var site = await Build("airpiper", 60);
        var pipe = W.Blocks.FirstOrDefault(b => b?.Code is { Domain: "ppex" } c && c.Path.StartsWith("pipe-straight-ns-"));
        Assert.NotNull(pipe);
        var pos = site.Origin.AddCopy(1, 0, 1);
        World.SetBlock(pipe!.Code.ToString(), pos);
        await World.Ticks(2);
        var be = W.BlockAccessor.GetBlockEntity(pos);
        Assert.True(ForcedAir.IsPipe(be), $"{be?.GetType().FullName} is not an exlib pipe node");
        // an empty pipe gives no air: the hole stays on the chimney's draft
        Assert.Equal(0, ForcedAir.Draw(be, 10));
        site.Hole(1).SetFire(500, 3, true);
        site.Hole(1).Update();
        Assert.Equal(Draft.Chimney, site.Hole(1).Draft);
    }
}
