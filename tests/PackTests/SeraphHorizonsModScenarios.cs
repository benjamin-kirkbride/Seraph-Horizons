using System.Reflection;
using Atlas.XUnit;
using HarmonyLib;
using SeraphHorizons.Mod;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons, BoilerLidRelief: ppex's boiler burst (BlockEntityBoiler.Explode) opens
/// the boiler's lid instead, and the English text no longer says a boiler explodes. The scenarios
/// call Explode() directly: ppex's own over-pressure timer is what decides to call it.
/// CreativeSteamSource: the creative steam source fills a ppex pipe placed against it with steam,
/// up to its set pressure. It targets ppex 0.7.1 / exlib 0.8.4, which are not on the ModDB yet:
/// while the pack pins an older ppex the scenarios require the block left out instead.
/// AssembledMachines: Immersive Woodworking's chopper and sawmill frames have a second creative
/// stack that places the machine assembled, with a steel head or blade kit.
/// WellShaftText: Hydrate or Diedrate's Wells page says how a shaft holds water, and wells built to
/// each rule hold what it says.
/// Also its ConfigKit settings (assets/seraphhorizons/config/configlib-patches.json), which thin out
/// Battle Towers' surface towers. CartReach: the server runs the same entity selection code as the
/// client, so the scenarios run it, and cart reach's second look, on the server's world against a
/// Cartwright's cart (the patch itself is applied on the client only, which Atlas does not run).
/// </summary>
[AtlasWorld]
public class SeraphHorizonsModScenarios : AtlasScenarioBase
{
    private const string Boiler = "ppex:boilercornish-north";

    private IWorldAccessor W => World.Api.World;

    private async Task<BlockEntity> PlaceBoiler(BlockPos pos)
    {
        World.SetBlock(Boiler, pos);
        await World.Ticks(2);
        return W.BlockAccessor.GetBlockEntity(pos)
               ?? throw new Xunit.Sdk.XunitException($"{Boiler} placed without a block entity");
    }

    private static bool LidOpen(BlockEntity boiler) =>
        (bool)AccessTools.Property(boiler.GetType(), "LidOpen").GetValue(boiler)!;

    private static void Explode(BlockEntity boiler) =>
        AccessTools.Method(boiler.GetType(), "Explode", Type.EmptyTypes).Invoke(boiler, null);

    [AtlasScenario]
    public async Task Over_pressure_opens_the_lid_and_leaves_the_boiler_standing()
    {
        var pos = World.Spawn.AddCopy(40, 2, 40);
        var boiler = await PlaceBoiler(pos);
        Assert.False(LidOpen(boiler));

        Explode(boiler);
        await World.Ticks(2);

        Assert.Equal(Boiler, World.BlockAt(pos).Code.ToString());
        Assert.Same(boiler, W.BlockAccessor.GetBlockEntity(pos));
        Assert.True(LidOpen(boiler));
    }

    [AtlasScenario]
    public async Task Over_pressure_with_the_lid_open_leaves_it_open()
    {
        var pos = World.Spawn.AddCopy(-40, 2, -40);
        var boiler = await PlaceBoiler(pos);
        AccessTools.Method(boiler.GetType(), "ToggleLid").Invoke(boiler, null);

        Explode(boiler);
        await World.Ticks(2);

        Assert.Equal(Boiler, World.BlockAt(pos).Code.ToString());
        Assert.True(LidOpen(boiler));
    }

    // Fails when ppex's library renames or changes the engine-explosion sound: point
    // BoilerLidRelief.BlowSound at the new one. A sound played from the server leaves no trace to
    // assert on, so this checks the sound itself.
    [AtlasScenario]
    public void The_lid_blows_open_with_the_engine_explosion_sound()
    {
        var engineSound = AccessTools.Field(AccessTools.TypeByName("ExpandedLib.Helpers.ExSounds"), "MediumExplosion");
        Assert.NotNull(engineSound);
        Assert.Equal(BoilerLidRelief.BlowSound, (AssetLocation?)engineSound.GetValue(null));
        Assert.True(World.Api.Assets.Exists(BoilerLidRelief.BlowSound.Clone().WithPathAppendixOnce(".ogg")));
    }

    // Fails when ppex rewords a passage: update BoilerLidRelief.LangEdits to match.
    [AtlasScenario]
    public void Text_says_the_lid_blows_open()
    {
        Assert.All(BoilerLidRelief.LangEdits, edit =>
        {
            var text = Lang.AvailableLanguages[edit.Language].GetAllEntries()[edit.Key];
            Assert.Contains(edit.New, text);
            Assert.DoesNotContain(edit.Old, text);
        });
        Assert.Equal("Over-pressure! 12s until the lid blows open!",
            Lang.GetL("en", "ppex:boiler-info-overpressure", 12));
        Assert.Equal("Превышение давления! Через 12 с давление откинет крышку!",
            Lang.GetL("ru", "ppex:boiler-info-overpressure", 12));
        Assert.Equal("Перевищення тиску! Через 12 с тиск відкине кришку!",
            Lang.GetL("uk", "ppex:boiler-info-overpressure", 12));
    }

    // Fails when ppex ships a new translation: add its LangEdits.
    [AtlasScenario]
    public void Every_ppex_language_is_reworded()
    {
        var shipped = World.Api.Assets.GetMany("lang/", BoilerLidRelief.ModId, loadAsset: false)
            .Select(asset => asset.Location.GetName())
            .Where(name => name.EndsWith(".json") && !name.StartsWith("worldconfig-"))
            .Select(name => name[..^".json".Length])
            .Order();
        var edited = BoilerLidRelief.LangEdits.Select(edit => edit.Language).Distinct().Order();
        Assert.Equal(shipped, edited);
        Assert.Equal(shipped, ChimneyVentText.LangEdits.Select(edit => edit.Language).Distinct().Order());
    }

    // Fails when ppex rewords the Fittings page or the chimney's look-at line: update
    // ChimneyVentText.LangEdits to match.
    [AtlasScenario]
    public void Text_says_where_a_chimney_vents()
    {
        Assert.All(ChimneyVentText.LangEdits, edit =>
        {
            var text = Lang.AvailableLanguages[edit.Language].GetAllEntries()[edit.Key];
            Assert.Contains(edit.New, text);
            Assert.DoesNotContain(edit.Old, text);
            // The page quotes the line ppex shows on a venting chimney, up to its amount.
            var venting = Lang.GetL(edit.Language, "ppex:chimney-info-venting", "").Split(':')[0];
            Assert.Contains(venting, edit.New);
        });
    }

    // ConfigKit writes the settings into Battle Towers' own patch file, by position, before the
    // game applies it. Fails when Battle Towers reorders that file or ConfigKit stops applying:
    // match the paths in configlib-patches.json to the new layout.
    [AtlasScenario]
    public void Surface_battle_towers_are_thinned_out()
    {
        var structures = JsonObject.FromJson(
                World.Api.Assets.Get(new AssetLocation("game", "worldgen/structures.json")).ToText())
            ["structures"].AsArray()!;
        var towers = Assert.Single(structures, s => s["code"].AsString() == "surfacetowers");
        Assert.Equal(0.01f, towers["chance"].AsFloat(), 4);
        Assert.Equal(600, towers["minGroupDistance"].AsInt());
        // The other two keep what Battle Towers ships.
        var hard = Assert.Single(structures, s => s["code"].AsString() == "surfacehardtowers");
        Assert.Equal(0.005f, hard["chance"].AsFloat(), 4);
        Assert.Equal(1000, hard["minGroupDistance"].AsInt());
    }

    private const string SteamSource = "seraphhorizons:" + CreativeSteamSource.BlockCode;

    // A pipe's view of its network (ppex's IPipeNode), read by name as the mod does.
    private static T PipeValue<T>(BlockEntity pipe, string property) =>
        (T)AccessTools.Property(AccessTools.TypeByName(CreativeSteamSource.PipeNodeType), property).GetValue(pipe)!;

    // The first ppex whose pipes the steam source binds to (exlib 0.8 moved them to ExpandedLib.Industry).
    private static readonly Version SteamSourcePpex = new(0, 7, 1);

    /// <summary>Whether the loaded ppex predates <see cref="SteamSourcePpex"/>; if so, requires the
    /// steam source left out of the game, as the mod does when ppex's members are not where it looks.</summary>
    private bool SteamSourceLeftOutForOldPpex()
    {
        var ppex = Version.Parse(World.Api.ModLoader.GetMod("ppex").Info.Version.Split('-')[0]);
        if (ppex >= SteamSourcePpex)
            return false;
        Assert.False(CreativeSteamSource.Bound);
        Assert.Null(W.GetBlock(new AssetLocation(SteamSource)));
        return true;
    }

    [AtlasScenario]
    public void Steam_source_is_in_the_creative_inventory_only()
    {
        if (SteamSourceLeftOutForOldPpex())
            return;
        Assert.True(CreativeSteamSource.Bound);
        var block = W.GetBlock(new AssetLocation(SteamSource));
        Assert.NotNull(block);
        Assert.IsType<BlockCreativeSteamSource>(block);
        Assert.Contains("ppex", block.CreativeInventoryTabs);
        Assert.Empty(block.Drops ?? []);
        Assert.DoesNotContain(W.GridRecipes, r => r.Output?.Code == block.Code);
    }

    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Steam_source_fills_a_connected_pipe_up_to_its_pressure()
    {
        if (SteamSourceLeftOutForOldPpex())
            return;
        // source | pipe (west-east) | rock: the pipe opens only onto the source, so nothing leaks.
        var pos = World.Spawn.AddCopy(0, 2, 60);
        World.SetBlock(SteamSource, pos);
        World.SetBlock("ppex:pipe-straight-we-iron", pos.EastCopy());
        World.SetBlock("game:rock-granite", pos.EastCopy(2));
        await World.Ticks(2);
        var source = Assert.IsType<BlockEntityCreativeSteamSource>(W.BlockAccessor.GetBlockEntity(pos));
        var pipe = W.BlockAccessor.GetBlockEntity(pos.EastCopy())
                   ?? throw new Xunit.Sdk.XunitException("pipe placed without a block entity");

        // At full flow and 2 atm: the network fills to the set pressure and goes no higher.
        source.Configure(pressure: 2, flow: CreativeSteamSource.MaxFlowSetting);
        await World.Until(() => PipeValue<string>(pipe, "Medium") == "Steam"
                                && PipeValue<float>(pipe, "Pressure") > 1.9f, 900);
        await World.Ticks(90);
        Assert.InRange(PipeValue<float>(pipe, "Pressure"), 1.9f, 2.05f);
        Assert.Equal("Steam", PipeValue<string>(pipe, "Medium"));
    }

    private const string BasicCart = "cartwrightscaravan:cart-basiccart-classic-oak";

    // Fails when Cartwright's Caravan renames its entities or drops their selection boxes: match
    // CartReachEntities' default to the new codes.
    [AtlasScenario]
    public void Cart_reach_covers_Cartwrights_carts_sleds_and_market_stalls()
    {
        var rule = new EntityReach(new SeraphHorizonsConfig().CartReachEntities);
        var types = CartReach.MatchingTypes(W, rule);
        var codes = types.Select(t => t.Code.ToString()).ToList();
        Assert.Contains(BasicCart, codes);
        Assert.Contains(codes, c => c.StartsWith("cartwrightscaravan:cart-slimcart-"));
        Assert.Contains(codes, c => c.StartsWith("cartwrightscaravan:sled-"));
        Assert.Contains(codes, c => c.StartsWith("cartwrightscaravan:marketstall-"));
        Assert.All(types, t => Assert.Contains(t.Client.BehaviorsAsJsonObj, b => b["code"].AsString() == "selectionboxes"));
        Assert.DoesNotContain(codes, c => !c.StartsWith("cartwrightscaravan:"));
    }

    /// <summary>A basic cart, standing still, posed and with its selection boxes loaded as the
    /// client has them (the server does neither for a cart), and the world centre of the box of the
    /// given attachment point.</summary>
    private async Task<(Entity Cart, int BoxIndex, Vec3d BoxCentre)> SpawnCartWithBoxes(BlockPos at, string apCode)
    {
        var type = W.GetEntityType(new AssetLocation(BasicCart))
                   ?? throw new Xunit.Sdk.XunitException($"{BasicCart} is not an entity type");
        var cart = W.ClassRegistry.CreateEntity(type);
        cart.Pos.SetPos(at.X + 0.5, at.Y, at.Z + 0.5);
        cart.Pos.Yaw = 0;
        W.SpawnEntity(cart);
        await World.Ticks(20);

        // The server's animator leaves every attachment point's matrix at identity, i.e. at the
        // cart's origin: pose the cart once, at rest, as the client does every frame.
        var animator = cart.AnimManager.Animator;
        animator.CalculateMatrices = true;
        animator.OnFrame(cart.AnimManager.ActiveAnimationsByAnimCode, 0.05f);

        var boxes = cart.GetBehavior<EntityBehaviorSelectionBoxes>()
                    ?? throw new Xunit.Sdk.XunitException($"{BasicCart} has no selectionboxes behavior on the server");
        AccessTools.Method(typeof(EntityBehaviorSelectionBoxes), "loadSelectionBoxes").Invoke(boxes, null);
        int index = Array.FindIndex(boxes.selectionBoxes, b => b.AttachPoint.Code == apCode);
        Assert.True(index >= 0, $"no {apCode} box loaded on the server's cart ({boxes.selectionBoxes.Length} boxes)");

        // As EntityBehaviorSelectionBoxes.getHitIndex places a box: a unit cube under applyBoxTransform.
        var m = new Matrixf().Identity();
        AccessTools.Method(typeof(EntityBehaviorSelectionBoxes), "applyBoxTransform")
            .Invoke(boxes, [m, boxes.selectionBoxes[index]]);
        var centre = m.TransformVector(new Vec4d(0.5, 0.5, 0.5, 1)).XYZ.Add(cart.Pos.XYZ);
        return (cart, index, centre);
    }

    private record Pick(BlockSelection? Block, EntitySelection? Entity, bool CartReachPicked);

    /// <summary>What is picked along a ray from the eye towards the target, as long as the picking
    /// range: the game's own pick (GameMain.RayTraceForSelection, which cart reach does not patch on
    /// the server), then, with <paramref name="cartReach"/>, cart reach's second look as its postfix
    /// runs it. Without <paramref name="blocks"/>, blocks are left out, so terrain does not matter.</summary>
    private static Pick PickAlong(GameMain game, Vec3d eye, Vec3d target, float range, bool cartReach, bool blocks = false)
    {
        var ray = new Ray(eye, target.SubCopy(eye).Normalize().Mul(range));
        BlockSelection? block = null;
        EntitySelection? entity = null;
        BlockFilter? bfilter = blocks ? null : (_, _) => false;
        game.RayTraceForSelection(game, ray, ref block, ref entity, bfilter, null);
        bool picked = cartReach && CartReach.SelectFarEntity(game, game, ray, ref block, ref entity, null,
            CartReach.AppliesCached(new EntityReach(new SeraphHorizonsConfig().CartReachEntities)));
        return new Pick(block, entity, picked);
    }

    // The bug and the fix, on the server's copy of the game's selection code: from behind a basic
    // cart its rear right slot is in survival reach (4.5) but the game does not pick the cart, it
    // does with creative reach (100), and cart reach picks that slot. Fails if the game starts
    // picking the cart itself (the tweak can go) or a Cartwright's update moves the slots.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Cart_reach_picks_a_carts_rear_slot_from_behind()
    {
        const float survival = 4.5f, creative = 100f;
        var game = (GameMain)W;
        var (cart, index, slot) = await SpawnCartWithBoxes(World.Spawn.AddCopy(-60, 3, 60), "RightStorage3AP");
        var origin = cart.Pos.XYZ;

        // Straight out behind the slot, level with it: the slot's box is under 2 blocks away, the
        // cart's origin farther than the picking range.
        var back = new Vec3d(slot.X - origin.X, 0, slot.Z - origin.Z).Normalize();
        var eye = slot.AddCopy(back.X * 2, 0, back.Z * 2);
        Assert.True(eye.HorizontalSquareDistanceTo(origin) > survival * survival,
            $"eye {eye} is within {survival} of the cart at {origin}: the slot is nearer the origin than expected");

        Assert.Null(PickAlong(game, eye, slot, survival, cartReach: false).Entity);
        var creativePick = PickAlong(game, eye, slot, creative, cartReach: false);
        Assert.Same(cart, creativePick.Entity?.Entity);
        Assert.Equal(index + 1, creativePick.Entity!.SelectionBoxIndex);

        var pick = PickAlong(game, eye, slot, survival, cartReach: true);
        Assert.True(pick.CartReachPicked);
        Assert.Null(pick.Block);
        Assert.Same(cart, pick.Entity?.Entity);
        Assert.Equal(index + 1, pick.Entity!.SelectionBoxIndex);
        Assert.True(pick.Entity.Position.AddCopy(pick.Entity.HitPosition).DistanceTo(eye) <= survival);

        // From 6 blocks behind the slot it is out of reach, and cart reach leaves it so.
        var far = slot.AddCopy(back.X * 6, 0, back.Z * 6);
        var farPick = PickAlong(game, far, slot, survival, cartReach: true);
        Assert.False(farPick.CartReachPicked);
        Assert.Null(farPick.Entity);
        Assert.NotNull(PickAlong(game, far, slot, creative, cartReach: false).Entity);

        // A block between the eye and the slot stays picked.
        var wall = new BlockPos((int)Math.Floor(eye.X - back.X), (int)Math.Floor(eye.Y), (int)Math.Floor(eye.Z - back.Z));
        World.SetBlock("game:rock-granite", wall);
        var blocked = PickAlong(game, eye, slot, survival, cartReach: true, blocks: true);
        World.SetBlock("game:air", wall);
        Assert.False(blocked.CartReachPicked);
        Assert.Null(blocked.Entity);
        Assert.NotNull(blocked.Block);

        cart.Die(EnumDespawnReason.Removed);
    }

    // yttenhancedinteractionfiltering (client only, so not loaded here; its assembly is) transpiles
    // the method cart reach postfixes. Both go on here, in both orders: its transpiler must still
    // find its one GetEntitiesAround call and swap in its own (it throws on its unset system if
    // not), and the method must still run. Fails when that mod changes how it patches.
    [AtlasScenario]
    public void Cart_reach_and_yttenhancedinteractionfiltering_patch_the_selection_together()
    {
        var target = CartReach.Target ?? throw new Xunit.Sdk.XunitException("GameMain.RayTraceForSelection not found");
        var ytt = AccessTools.TypeByName("YttEnhancedInteractionFiltering.YttEnhancedInteractionFilteringSystem")
                  ?? throw new Xunit.Sdk.XunitException("yttenhancedinteractionfiltering is not loaded");
        var transpiler = new HarmonyMethod(AccessTools.Method(AccessTools.Inner(ytt, "RayTraceForSelectionPatch"), "Transpiler"));
        var yttHarmony = new Harmony("seraphhorizons.tests.ytt");
        var cartReach = new Harmony(CartReach.HarmonyId);
        var game = (GameMain)W;

        foreach (bool yttFirst in new[] { true, false })
        {
            try
            {
                if (yttFirst)
                    yttHarmony.Patch(target, transpiler: transpiler);
                cartReach.Patch(target, postfix: CartReach.Postfix);
                if (!yttFirst)
                    yttHarmony.Patch(target, transpiler: transpiler);

                var info = Harmony.GetPatchInfo(target);
                Assert.Contains(info.Transpilers, p => p.owner == yttHarmony.Id);
                Assert.Contains(info.Postfixes, p => p.owner == CartReach.HarmonyId);
                var body = PatchProcessor.GetCurrentInstructions(target);
                Assert.Contains(body, i => i.operand is MethodInfo m && m.Name == "GetEntitiesAroundForSelection");
                Assert.DoesNotContain(body, i => i.operand is MethodInfo m && m.Name == "GetEntitiesAround");

                var ray = new Ray(World.Spawn.ToVec3d().Add(0.5, 3, 0.5), new Vec3d(0, 0, 4.5));
                BlockSelection? block = null;
                EntitySelection? entity = null;
                game.RayTraceForSelection(game, ray, ref block, ref entity, null, null);
            }
            finally
            {
                yttHarmony.UnpatchAll(yttHarmony.Id);
                cartReach.UnpatchAll(CartReach.HarmonyId);
            }
        }
    }

    public static TheoryData<int> AssembledMachineIndexes => [.. Enumerable.Range(0, AssembledMachines.Machines.Length)];

    private Block Frame(AssembledMachines.Machine machine) =>
        W.GetBlock(new AssetLocation(AssembledMachines.ModId, machine.FrameCode))
        ?? throw new Xunit.Sdk.XunitException($"{machine.FrameCode} is not loaded");

    [AtlasTheory, MemberData(nameof(AssembledMachineIndexes))]
    public void Frame_has_an_assembled_creative_stack_next_to_the_plain_one(int index)
    {
        var machine = AssembledMachines.Machines[index];
        var frame = Frame(machine);
        Assert.NotEmpty(frame.CreativeInventoryTabs);
        var list = Assert.Single(frame.CreativeInventoryStacks);
        Assert.Equal(frame.CreativeInventoryTabs, list.Tabs);
        var stack = Assert.Single(list.Stacks).ResolvedItemstack!;
        Assert.Same(frame, stack.Block);
        Assert.Equal(AssembledMachines.DefaultMetal, stack.Attributes.GetString(AssembledMachines.MetalAttribute));

        // Named after the finished machine; the plain frame keeps its name and its handbook page.
        Assert.Equal(Lang.Get(machine.NameKey), stack.GetName());
        Assert.NotEqual(Lang.Get(machine.NameKey), new ItemStack(frame).GetName());
        Assert.True(frame.Attributes!["handbook"]["ignoreCreativeInvStacks"].AsBool());
        // The other orientations stay out of the creative inventory.
        var south = W.GetBlock(frame.CodeWithVariant("side", "south"))!;
        Assert.Empty(south.CreativeInventoryStacks ?? []);
    }

    [AtlasTheory(TimeoutMs = 120_000), MemberData(nameof(AssembledMachineIndexes))]
    public async Task Assembled_stack_places_the_machine_complete_with_a_steel_tool(int index)
    {
        var machine = AssembledMachines.Machines[index];
        var frame = Frame(machine);
        bool Complete(BlockEntity be) => (bool)AccessTools.Property(be.GetType(), "IsComplete").GetValue(be)!;

        // The machines cover several cells; each placement gets room of its own.
        var plainPos = World.Spawn.AddCopy(-40 - 20 * index, 3, 60);
        W.BlockAccessor.SetBlock(frame.Id, plainPos, new ItemStack(frame));
        var assembledPos = plainPos.AddCopy(0, 0, 10);
        var stack = frame.CreativeInventoryStacks[0].Stacks[0].ResolvedItemstack!.Clone();
        W.BlockAccessor.SetBlock(frame.Id, assembledPos, stack);
        await World.Ticks(2);

        var plain = W.BlockAccessor.GetBlockEntity(plainPos) ?? throw new Xunit.Sdk.XunitException("frame placed without a block entity");
        Assert.False(Complete(plain));

        var assembled = W.BlockAccessor.GetBlockEntity(assembledPos) ?? throw new Xunit.Sdk.XunitException("frame placed without a block entity");
        Assert.True(Complete(assembled));
        var tree = new TreeAttribute();
        assembled.ToTreeAttributes(tree);
        Assert.All(machine.Flags, flag => Assert.True(tree.GetBool(flag)));
        var tool = tree.GetItemstack(machine.ToolKey);
        tool.ResolveBlockOrItem(W);
        Assert.Equal(machine.ToolCode.Replace("{metal}", "steel"), tool.Collectible.Code.ToString());
        Assert.Equal(Lang.Get(machine.NameKey), frame.GetPlacedBlockName(W, assembledPos));
        // Breaking it gives back the frame and every part, as for one built by hand.
        Assert.True(frame.GetDrops(W, assembledPos, null!).Length > machine.Flags.Length);
    }

    // Fails when Hydrate or Diedrate rewords the Wells page, its look-at line or its default
    // depths: update WellShaftText.LangEdits to match.
    [AtlasScenario]
    public void Text_says_how_a_well_shaft_holds_water()
    {
        Assert.All(WellShaftText.LangEdits, edit =>
        {
            var text = Lang.AvailableLanguages[edit.Language].GetAllEntries()[edit.Key];
            Assert.Contains(edit.New, text);
            Assert.DoesNotContain(edit.Old, text);
        });
        var page = Lang.GetL("en", "hydrateordiedrate:wellinfo-text");
        // The page quotes the line a spring under a shaft that holds nothing shows.
        Assert.Contains(Lang.GetL("en", "hydrateordiedrate:well.retentionVolume", 0), page);
        // And gives the depths Hydrate or Diedrate ships, with the liters they hold.
        var settings = AccessTools.Property(AccessTools.TypeByName("HydrateOrDiedrate.Config.ModConfig"), "Instance")
            .GetValue(null)!;
        var groundWater = AccessTools.Property(settings.GetType(), "GroundWater").GetValue(settings)!;
        var perBlock = (float)AccessTools.Field(WellSpringType, "LitersPerFullBlock").GetValue(null)!;
        foreach (var (setting, label) in new[]
                 {
                     ("WellwaterDepthMaxBase", "any other block)"), ("WellwaterDepthMaxClay", "large or small)"),
                     ("WellwaterDepthMaxStone", "ashlar blocks"),
                 })
        {
            var depth = (int)AccessTools.Property(groundWater.GetType(), setting).GetValue(groundWater)!;
            Assert.Contains($"{label}: {depth}, or {depth * perBlock:0} liters", page);
        }
    }

    private const string Rock = "game:rock-granite";
    private const string Ashlar = "game:stonebricks-granite";
    private const string WellSpring = "hydrateordiedrate:wellspring";

    private static Type WellSpringType =>
        AccessTools.TypeByName("HydrateOrDiedrate.Wells.WellWater.BlockEntityWellSpring");

    /// <summary>Stands a well in the air: a spring on rock, ringed by rock, under a one-block
    /// shaft whose four walls at each level (1 is the cell above the spring) are what
    /// <paramref name="wall"/> names. Returns the spring's position.</summary>
    private BlockPos BuildWell(BlockPos spring, int levels, System.Func<int, BlockFacing, string> wall)
    {
        World.SetBlock(Rock, spring.DownCopy());
        for (var level = 0; level <= levels; level++)
            foreach (var facing in BlockFacing.HORIZONTALS)
                World.SetBlock(level == 0 ? Rock : wall(level, facing), spring.UpCopy(level).AddCopy(facing));
        World.SetBlock(WellSpring, spring);
        return spring;
    }

    /// <summary>The levels of shaft the spring at <paramref name="pos"/> counts, after its own
    /// shaft check (which it otherwise runs every 30 s), and the liters it says it can hold.</summary>
    private (int Levels, float Liters) WellShaft(BlockPos pos)
    {
        var spring = W.BlockAccessor.GetBlockEntity(pos);
        Assert.IsType(WellSpringType, spring);
        Assert.True(World.Api.World.IsFullyLoadedChunk(pos));
        Assert.False((bool)AccessTools.Property(WellSpringType, "IsShallow").GetValue(spring)!);
        AccessTools.Method(WellSpringType, "OnPeriodicShaftCheck").Invoke(spring, [0f]);
        return ((int)AccessTools.Property(WellSpringType, "WellShaftHeight").GetValue(spring)!,
            (float)AccessTools.Property(WellSpringType, "CapacityLitres").GetValue(spring)!);
    }

    // What WellShaftText's page says, on real wells. Fails when Hydrate or Diedrate changes how a
    // spring counts its shaft: reword WellShaftText.LangEdits (and the README) to the new rules.
    [AtlasScenario]
    public async Task A_well_holds_what_the_handbook_page_says()
    {
        // Close to spawn: a spring only checks its shaft where the chunks around it are loaded.
        var origin = World.Spawn.AddCopy(-8, 30, -8);
        BlockPos At(int index) => origin.AddCopy(index % 3 * 6, 0, index / 3 * 6);
        const int levels = 12;

        var rock = BuildWell(At(0), levels, (_, _) => Rock);
        var ashlar = BuildWell(At(1), levels, (_, _) => Ashlar);
        var bricks = BuildWell(At(2), levels, (_, _) => "game:brickcourse-four-running-red");
        var fireclay = BuildWell(At(3), levels, (_, _) => "game:claybricks-good-fire");
        var uneven = BuildWell(At(4), levels, (_, _) => "game:claybricks-uneven-four-running-red");
        var aged = BuildWell(At(5), levels, (_, _) => "game:agedstonebricks-granite");
        // One raw rock block in an ashlar shaft, low and high.
        var rockAtThree = BuildWell(At(6), levels,
            (level, facing) => level == 3 && facing == BlockFacing.NORTH ? Rock : Ashlar);
        var rockAtEight = BuildWell(At(7), levels,
            (level, facing) => level == 8 && facing == BlockFacing.EAST ? Rock : Ashlar);
        // A 2x2 shaft in rock: four springs, each cell open to two others.
        var wide = At(8);
        var cells = new[] { wide, wide.AddCopy(1, 0, 0), wide.AddCopy(0, 0, 1), wide.AddCopy(1, 0, 1) };
        for (var level = -1; level <= levels; level++)
            for (var dx = -1; dx <= 2; dx++)
                for (var dz = -1; dz <= 2; dz++)
                    if (level == -1 || dx is -1 or 2 || dz is -1 or 2)
                        World.SetBlock(Rock, wide.AddCopy(dx, level, dz));
        foreach (var cell in cells)
            World.SetBlock(WellSpring, cell);
        await World.Ticks(5);

        Assert.Equal((5, 350f), WellShaft(rock));
        Assert.Equal((10, 700f), WellShaft(ashlar));
        Assert.Equal((7, 490f), WellShaft(bricks));
        Assert.Equal((5, 350f), WellShaft(fireclay));
        Assert.Equal((5, 350f), WellShaft(uneven));
        Assert.Equal((5, 350f), WellShaft(aged));
        Assert.Equal((5, 350f), WellShaft(rockAtThree));
        Assert.Equal((7, 490f), WellShaft(rockAtEight));
        Assert.All(cells, cell => Assert.Equal((0, 0f), WellShaft(cell)));
    }
}
