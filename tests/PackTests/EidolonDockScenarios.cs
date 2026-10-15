using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry;
using SeraphHorizons.Mod.EidolonGantry.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Xunit.Abstractions;

namespace SeraphHorizons.PackTests;

/// <summary>
/// mods-src/seraphhorizons/Eidolon, docking (Eidolon/README.md, "Docking"): a woken eidolon sent to a
/// gantry by the command tool's dock mode walks in and backs onto the spine, where the gantry holds it
/// hung (the same entity, its order kept for after); stripping is refused away from a gantry and below
/// full health and takes the outermost layer first, its whole bill to the player; a stripped layer is
/// fitted again in another material on the eidolon or the gantry, and its look and figures follow; the
/// crank wakes it only with all five layers on, and it walks out in its new build and back to its order.
/// Docked, it survives its chunk being unloaded and loaded again, both sides still knowing each other;
/// breaking the gantry lets it go whole, giving back only a layer's loose pieces. Each scenario builds on
/// a granite floor of its own high over the spawn, with a player of its own standing by (the game runs a
/// creature's AI only with a player in range).
/// </summary>
[AtlasWorld]
public class EidolonDockScenarios(ITestOutputHelper output) : AtlasScenarioBase
{
    private IWorldAccessor W => World.Api.World;
    private ICoreServerAPI Sapi => (ICoreServerAPI)World.Api;
    private EidolonSystem Mod => EidolonSystem.Of(World.Api)!;
    private EidolonConfig Settings => Mod.Config;
    private GantryRig Rig => EidolonGantrySystem.Of(World.Api).Rig ?? throw new Xunit.Sdk.XunitException("the gantry's rig did not load");

    private void Log(string what)
    {
        W.Logger.Notification("[eidolon-dock-test] " + what);
        output.WriteLine(what);
    }

    private static int Mod32(int v) => ((v % 32) + 32) % 32;

    /// <summary>A clear granite floor 40 above spawn at (<paramref name="dx"/>, <paramref name="dz"/>), 12
    /// each way with 8 blocks of air over it, its chunks loaded; <paramref name="who"/> joins in survival
    /// and stands at a corner.</summary>
    private async Task<(BlockPos Origin, IServerPlayer Player)> Floor(int dx, int dz, string who)
    {
        const int reach = 12;
        var origin = World.Spawn.AddCopy(dx, 40, dz);
        int size = GlobalConstants.ChunkSize;
        for (int cx = (origin.X - reach) / size; cx <= (origin.X + reach) / size; cx++)
            for (int cz = (origin.Z - reach) / size; cz <= (origin.Z + reach) / size; cz++)
                Sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
        await World.Until(() => W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(-reach, 0, -reach)) != null
                                && W.BlockAccessor.GetChunkAtBlockPos(origin.AddCopy(reach, 0, reach)) != null, 1200);
        int granite = W.GetBlock(new AssetLocation("game:rock-granite"))!.Id;
        for (int x = -reach; x <= reach; x++)
        for (int z = -reach; z <= reach; z++)
        {
            W.BlockAccessor.SetBlock(granite, origin.AddCopy(x, -1, z));
            for (int y = 0; y <= 8; y++)
                W.BlockAccessor.SetBlock(0, origin.AddCopy(x, y, z));
        }
        await World.Ticks(5);
        var p = await World.JoinPlayer(who);
        var player = (IServerPlayer)p.Player;
        player.WorldData.CurrentGameMode = EnumGameMode.Survival;
        await p.TeleportTo(origin.AddCopy(-6, 0, -6));
        return (origin, player);
    }

    /// <summary>A gantry facing south (its open front west) with its winch and spine built.</summary>
    private async Task<BEEidolonGantry> Gantry(BlockPos pos)
    {
        World.SetBlock("seraphhorizons:eidolongantry-oak-south", pos);
        await World.Ticks(5);
        var gantry = W.BlockAccessor.GetBlockEntity(pos) as BEEidolonGantry ?? throw new Xunit.Sdk.XunitException($"no eidolon gantry at {pos}");
        gantry.PlaceGhosts();
        gantry.FitAll();
        Assert.True(gantry.WinchComplete);
        return gantry;
    }

    private static BEBehaviorEidolonBody BodyOf(BEEidolonGantry gantry) => gantry.GetBehavior<BEBehaviorEidolonBody>()!;

    /// <summary>A woken eidolon owned by <paramref name="owner"/>, standing <paramref name="west"/> blocks out
    /// of the gantry's front, free to work.</summary>
    private async Task<EntityLaborEidolon> Woken(BEEidolonGantry gantry, IPlayer owner, EidolonBuild build, double west = 6)
    {
        var at = gantry.WalkOutTarget(west)!;
        var e = Mod.Spawn(W, at, 0, owner, activate: true, build: build);
        Assert.NotNull(e);
        await World.Until(() => e!.CanWork, 400);
        return e!;
    }

    private static EntityBehaviorHealth Health(Entity e) => e.GetBehavior<EntityBehaviorHealth>()!;

    private ItemStack Item(string code, int size = 1) =>
        W.GetItem(new AssetLocation(code)) is { } item ? new ItemStack(item, size)
        : W.GetBlock(new AssetLocation(code)) is { Id: > 0 } block ? new ItemStack(block, size)
        : throw new Xunit.Sdk.XunitException($"no item {code}");

    private static void Empty(IPlayer player)
    {
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
            {
                slot.Itemstack = null;
                slot.MarkDirty();
            }
    }

    /// <summary>Every item stack in the player's hotbar and backpack, by code.</summary>
    private static Dictionary<string, int> Carried(IPlayer player)
    {
        var all = new Dictionary<string, int>();
        foreach (var inv in new[] { GlobalConstants.hotBarInvClassName, GlobalConstants.backpackInvClassName })
            foreach (var slot in player.InventoryManager.GetOwnInventory(inv) ?? Enumerable.Empty<ItemSlot>())
                if (slot.Itemstack is { } s)
                    all[s.Collectible.Code.ToString()] = all.GetValueOrDefault(s.Collectible.Code.ToString()) + s.StackSize;
        return all;
    }

    /// <summary>A right-click on <paramref name="e"/> holding <paramref name="held"/> (Ctrl down with
    /// <paramref name="ctrl"/>); returns what is left in the hand.</summary>
    private static ItemStack? ClickEntity(IPlayer player, Entity e, ItemStack? held, bool ctrl = false)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        player.Entity.Controls.CtrlKey = ctrl;
        try
        {
            e.OnInteract(player.Entity, slot, e.Pos.XYZ.AddCopy(0, 2, 0), EnumInteractMode.Interact);
        }
        finally
        {
            player.Entity.Controls.CtrlKey = false;
        }
        return slot.Itemstack;
    }

    /// <summary>A right-click on the gantry's cell <paramref name="at"/>; returns what is left in the hand.</summary>
    private ItemStack? ClickGantry(IPlayer player, BlockPos at, ItemStack? held)
    {
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = held;
        slot.MarkDirty();
        var sel = new BlockSelection { Position = at.Copy(), Face = BlockFacing.UP, HitPosition = new Vec3d(0.5, 0.5, 0.5) };
        W.BlockAccessor.GetBlock(at).OnBlockInteractStart(W, player, sel);
        return slot.Itemstack;
    }

    private BlockPos Crank(BEEidolonGantry gantry) => gantry.CellPos(Rig.CrankCell);

    /// <summary>The player's active slot holding a command tool bound to <paramref name="e"/>, in
    /// <paramref name="mode"/>.</summary>
    private ItemSlot Tool(IServerPlayer player, EntityLaborEidolon e, string mode)
    {
        var item = (ItemEidolonCommander)W.GetItem(new AssetLocation(EidolonCommanderSystem.CommanderCode))!;
        var slot = player.InventoryManager.ActiveHotbarSlot;
        slot.Itemstack = new ItemStack(item);
        slot.MarkDirty();
        Assert.True(item.Bind(player, slot, e));
        item.SetToolMode(slot, player, null!, EidolonCommandModes.IndexOf(mode));
        Assert.Equal(mode, ItemEidolonCommander.ModeOf(slot.Itemstack!)!.Code);
        return slot;
    }

    private async Task Docked(EntityLaborEidolon e, int ticks = 2400)
    {
        await World.Until(() => e.Dock!.Phase == DockPhase.Docked, ticks);
        await World.Ticks(50); // dock plays out into hung
    }

    private void AssertHung(EntityLaborEidolon e, BEEidolonGantry gantry)
    {
        var body = BodyOf(gantry);
        Assert.Equal(DockPhase.Docked, e.Dock!.Phase);
        Assert.Equal(e.EntityId, body.DockedId);
        Assert.True(e.Dock.HeldAt(gantry.Pos));
        Assert.True(e.Pos.XYZ.DistanceTo(gantry.BodyPoint!) < 0.01, $"at {e.Pos.XYZ}, the body point is {gantry.BodyPoint}");
        Assert.Equal(gantry.OutwardYaw, e.Pos.Yaw, 3);
        Assert.True(e.AnimManager.IsAnimationActive("hung"), "not hung");
        Assert.False(e.CanWork);
        // the entity is drawn, not the gantry's body
        foreach (var stage in BodyBill.Order)
            Assert.False(body.Shows(stage), $"the gantry draws the {stage}");
    }

    // The command tool's dock mode with nothing marked sends it to the nearest free gantry, it walks in and
    // is held hung; a push does not move it. Strip is refused before docking and below full health, then
    // takes the skirt, sash and plates in that order, each bill to the player. The crank refuses while a
    // layer is off. Brass plates go back on the eidolon itself, a blue cloth sash on the gantry, a stainless
    // skirt on the eidolon: its build, textures and figures follow at once. The crank wakes it: activate,
    // out of the front, and back to the stay order it docked from, in its new build; the spine is free.
    [AtlasScenario(TimeoutMs = 150_000)]
    public async Task Docked_by_order_it_is_stripped_refitted_in_new_materials_and_woken()
    {
        var (origin, player) = await Floor(-700, 600, "dockhand");
        var gantry = await Gantry(origin);
        var body = BodyOf(gantry);
        var e = await Woken(gantry, player, EidolonBuild.OldBuild);
        using var clock = VirtualClock.Start(World);
        var stayAt = e.Pos.XYZ.Clone();
        e.Orders!.SetOrder(StayOrder.OrderCode, StayOrder.Args(stayAt));
        Assert.Equal(300, Health(e).MaxHealth, 1);

        // not docked: Ctrl+right-click strips nothing
        Assert.Null(ClickEntity(player, e, null, ctrl: true));
        Assert.Equal(EidolonBuild.OldBuild, e.Build);

        // the dock mode, pointed at the air: the nearest gantry
        var slot = Tool(player, e, DockOrder.OrderCode);
        Assert.Equal(1, ((ItemEidolonCommander)slot.Itemstack!.Collectible).Use(player, slot, null));
        Assert.Equal(DockOrder.OrderCode, e.Orders.OrderCode);
        Assert.Equal(gantry.Pos, DockOrder.From(e.Orders.OrderArgs!).Gantry);
        await Docked(e);
        AssertHung(e, gantry);
        Assert.Null(e.Orders.OrderCode);
        Assert.True(body.Parts.Complete);
        Assert.Contains("docked", Info(gantry, player));
        // a second order to dock there is refused: it is taken
        Assert.Equal(0, ((ItemEidolonCommander)slot.Itemstack!.Collectible).Use(player, slot, gantry.Pos));
        // pushed, it stays
        e.Pos.Motion.Set(0.5, 0, 0.5);
        await World.Ticks(20);
        AssertHung(e, gantry);

        // below full health: no strip
        Empty(player);
        e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Injury }, 10);
        Assert.True(Health(e).Health < Health(e).MaxHealth);
        ClickEntity(player, e, null, ctrl: true);
        Assert.Equal(EidolonBuild.OldBuild, e.Build);
        Assert.Empty(Carried(player));
        // repaired in the dock, double: a whole one strips
        Health(e).Health = Health(e).MaxHealth;

        var expectBills = new[] { FittingLayer.Skirt, FittingLayer.Sash, FittingLayer.Plates };
        var build = EidolonBuild.OldBuild;
        foreach (var layer in expectBills)
        {
            Empty(player);
            Assert.Equal(layer, build.NextToStrip);
            var bill = EidolonFittings.Bill(layer, build[layer]);
            ClickEntity(player, e, null, ctrl: true);
            build = build.Stripped()!;
            Assert.Equal(build, e.Build);
            Assert.Equal(bill.ToDictionary(i => i.Code, i => i.Count).OrderBy(kv => kv.Key), Carried(player).OrderBy(kv => kv.Key));
            Assert.Equal(EidolonStats.Of(build, Settings).MaxHealth, Health(e).MaxHealth, 1);
            Assert.Equal(Health(e).MaxHealth, Health(e).Health, 1);
            Log($"stripped the {layer}: {e.Build}, {Health(e).MaxHealth} HP");
        }
        Assert.Equal("plates", body.Parts.Next);
        AssertHung(e, gantry);

        // the crank refuses: layers off
        Assert.Null(ClickGantry(player, Crank(gantry), null));
        Assert.Equal(DockPhase.Docked, e.Dock!.Phase);

        // brass plates on the eidolon itself; a steel one after the first brass is refused
        Assert.Null(ClickEntity(player, e, Item("game:metalplate-brass", 1)));
        Assert.Equal(EidolonFittings.Brass, body.Parts.Material(FittingLayer.Plates));
        Assert.Equal(1, ClickEntity(player, e, Item("game:metalplate-steel", 1))?.StackSize);
        Assert.Equal(1, ClickEntity(player, e, Item("game:metalplate-brass", 17))?.StackSize);
        Assert.Equal(EidolonFittings.Brass, e.Build[FittingLayer.Plates]);
        // the sash on the gantry, the skirt on the eidolon
        Assert.Null(ClickGantry(player, gantry.Pos, Item("game:cloth-blue", 4)));
        Assert.Null(ClickEntity(player, e, Item("game:metalchain-stainlesssteel", 4)));
        var refit = EidolonBuild.Of("oak", EidolonFittings.Steel, EidolonFittings.Brass, "cloth-blue", EidolonFittings.Stainless);
        Assert.Equal(refit, e.Build);
        Assert.Equal(refit.Textures(), e.Build.Textures());
        Assert.Equal("game:block/metal/plate/brass", e.Build.Textures()["plates"]);
        var stats = EidolonStats.Of(refit, Settings);
        Assert.Equal(stats.MaxHealth, Health(e).MaxHealth, 1);
        Assert.Equal(stats, e.FittedStats);
        Assert.NotEqual(300, Health(e).MaxHealth, 1);
        Assert.True(body.Parts.Complete);

        // the crank wakes it: off the spine, out of the front, back to its stay order
        Assert.Null(ClickGantry(player, Crank(gantry), null));
        Assert.Equal(DockPhase.None, e.Dock!.Phase);
        Assert.False(body.HoldsEidolon);
        Assert.False(body.Parts.Started);
        Assert.True(e.AnimManager.IsAnimationActive("activate"));
        Assert.Equal(GoToOrder.OrderCode, e.Orders.OrderCode);
        await World.Until(() => e.Orders.OrderCode == StayOrder.OrderCode, 2400);
        Assert.Null(BEEidolonGantry.DockAt(W.BlockAccessor, e.Pos.XYZ));
        Assert.Equal(refit, e.Build);
        Assert.Equal(player.PlayerUID, e.OwnerUid);
        Assert.True(e.CanWork);
        Log($"woken and out at {e.Pos.XYZ}, ordered {e.Orders.OrderCode}");

        e.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.GetBlock(gantry.Pos).OnBlockBroken(W, gantry.Pos, player);
    }

    private static string Info(BEEidolonGantry gantry, IPlayer player)
    {
        var sb = new System.Text.StringBuilder();
        gantry.GetBlockInfo(player, sb);
        return sb.ToString();
    }

    // Docked, with the gantry and the eidolon in one chunk column: unloaded (saved) and loaded again, the
    // eidolon (the same entity id) is still docked, hung on its point, and the gantry holds it. An order
    // given while docked waits; it strips and wakes as before, then takes up that order.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task Docked_it_stays_docked_through_unloading_and_loading()
    {
        int dx = 16 - Mod32(World.Spawn.X - 760) - 760, dz = 16 - Mod32(World.Spawn.Z + 600) + 600;
        var (origin, player) = await Floor(dx, dz, "dockkeeper");
        var gantry = await Gantry(origin);
        var e = await Woken(gantry, player, EidolonBuild.OldBuild, west: 1.5);
        Assert.True(e.Dock!.Begin(gantry, null));
        await Docked(e, 400);
        AssertHung(e, gantry);
        long id = e.EntityId;
        int cx = origin.X / GlobalConstants.ChunkSize, cz = origin.Z / GlobalConstants.ChunkSize;
        foreach (var c in gantry.GhostCells().Append(gantry.BodyPoint!.AsBlockPos))
            Assert.Equal((cx, cz), (c.X / GlobalConstants.ChunkSize, c.Z / GlobalConstants.ChunkSize));

        // nobody near: the column is saved and unloaded, then loaded again
        player.Entity.TeleportTo(World.Spawn.ToVec3d());
        await World.Ticks(5);
        var saved = await World.ExecuteCommand("/autosavenow");
        Log($"/autosavenow: {saved.Ok}");
        await World.Ticks(5);
        Sapi.WorldManager.UnloadChunkColumn(cx, cz);
        for (int i = 0; i < 200 && W.GetEntityById(id) != null; i++)
            await World.Ticks(1);
        Assert.Null(W.GetEntityById(id));
        for (int i = 0; i < 60 && W.GetEntityById(id) == null; i++)
        {
            Sapi.WorldManager.LoadChunkColumnPriority(cx, cz);
            await World.Ticks(10);
        }
        player.Entity.TeleportTo(origin.AddCopy(-6, 0, -6).ToVec3d());
        var again = W.GetEntityById(id) as EntityLaborEidolon;
        Assert.NotNull(again);
        Assert.NotSame(e, again);
        var gantryAgain = W.BlockAccessor.GetBlockEntity(origin) as BEEidolonGantry;
        Assert.NotNull(gantryAgain);
        Assert.NotSame(gantry, gantryAgain);
        // Loaded again: the virtual clock from here on (the save, unloading and loading above run on
        // the chunk threads, on wall time, so stay outside it).
        using var clock = VirtualClock.Start(World);
        await World.Ticks(100);
        AssertHung(again!, gantryAgain!);
        Assert.Equal(EidolonBuild.OldBuild, again!.Build);

        // an order given while docked waits: it stays hung
        again.Orders!.SetOrder(FollowOrder.OrderCode, FollowOrder.Args(player));
        await World.Ticks(40);
        AssertHung(again, gantryAgain!);

        // it strips and wakes as before, then follows
        Empty(player);
        ClickEntity(player, again, null, ctrl: true);
        Assert.Equal(EidolonBuild.OldBuild.Stripped(), again.Build);
        Assert.Null(ClickEntity(player, again, Item("game:metalchain-steel", 4)));
        Assert.Null(ClickGantry(player, Crank(gantryAgain!), null));
        Assert.Equal(DockPhase.None, again.Dock!.Phase);
        Assert.False(BodyOf(gantryAgain!).HoldsEidolon);
        Assert.Equal(GoToOrder.OrderCode, again.Orders.OrderCode);
        await World.Until(() => again.Orders.OrderCode == FollowOrder.OrderCode, 2400);

        again.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.GetBlock(origin).OnBlockBroken(W, origin, player);
    }

    // Breaking a gantry with an eidolon docked lets it go, layers and all, and gives back the frame, the
    // winch and the one loose chain of a skirt part fitted, nothing of the eidolon's. It stands up and can
    // work. An eidolon removed while docked frees the spine.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Breaking_the_gantry_lets_the_docked_eidolon_go_whole()
    {
        var (origin, player) = await Floor(-760, 680, "dockbreaker");
        var gantry = await Gantry(origin);
        var e = await Woken(gantry, player, EidolonBuild.OldBuild, west: 1.5);
        using var clock = VirtualClock.Start(World);
        Assert.True(e.Dock!.Begin(gantry, null));
        await Docked(e, 400);
        Empty(player);

        // Oil docked: on a cell of the gantry (which can take the click before the eidolon) it goes to
        // the eidolon, as it does on the eidolon itself; two lumps of rendered fat, 50 points each.
        e.Oil!.SetPoints(0);
        Assert.Null(ClickGantry(player, origin, Item("game:fat-rendered", 2)));
        Assert.Equal(100, e.Oil.Tank!.Value.Points, 3);
        Assert.Null(ClickEntity(player, e, Item("game:fat-rendered", 2)));
        Assert.Equal(200, e.Oil.Tank!.Value.Points, 3);

        ClickEntity(player, e, null, ctrl: true);
        var stripped = EidolonBuild.OldBuild.Stripped()!;
        Assert.Equal(stripped, e.Build);
        Assert.Null(ClickEntity(player, e, Item("game:metalchain-iron", 1)));
        Assert.Equal(stripped, e.Build);

        foreach (var drop in W.GetEntitiesAround(origin.ToVec3d(), 12, 8, x => x is EntityItem))
            drop.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.GetBlock(origin).OnBlockBroken(W, origin, player);
        await World.Ticks(5);
        Assert.Equal(DockPhase.None, e.Dock!.Phase);
        Assert.True(e.Alive);
        Assert.Equal(stripped, e.Build);
        var dropped = new Dictionary<string, int>();
        foreach (var item in W.GetEntitiesAround(origin.ToVec3d(), 12, 8, x => x is EntityItem).OfType<EntityItem>())
            dropped[item.Itemstack.Collectible.Code.ToString()] = dropped.GetValueOrDefault(item.Itemstack.Collectible.Code.ToString()) + item.Itemstack.StackSize;
        Log("dropped: " + string.Join(", ", dropped.Select(kv => $"{kv.Value} {kv.Key}")));
        // the winch's chain and the skirt's one loose chain
        Assert.Equal(GantryParts.Needed(GantryStage.Chain) + 1, dropped.GetValueOrDefault("game:metalchain-iron"));
        Assert.DoesNotContain("game:metalplate-steel", dropped.Keys);
        Assert.DoesNotContain(BodyBill.Torso, dropped.Keys);
        Assert.Contains("seraphhorizons:eidolongantry-oak-north", dropped.Keys);
        await World.Until(() => e.CanWork, 400);
        foreach (var drop in W.GetEntitiesAround(origin.ToVec3d(), 12, 8, x => x is EntityItem))
            drop.Die(EnumDespawnReason.Removed);

        // an eidolon removed while docked frees the spine after a few checks
        var gantry2 = await Gantry(origin);
        Assert.True(e.Dock.Begin(gantry2, null));
        await Docked(e, 400);
        Assert.True(BodyOf(gantry2).HoldsEidolon);
        e.Die(EnumDespawnReason.Removed);
        await World.Until(() => !BodyOf(gantry2).HoldsEidolon, 400);
        Assert.False(BodyOf(gantry2).Parts.Started);
        W.BlockAccessor.GetBlock(origin).OnBlockBroken(W, origin, player);
    }

    // A docked body saved when the bones took 10 support beams (before #831 raised them to 30) loads short
    // of the eidolon it holds: its bones part fitted. The crank, before the gantry's own check has seen
    // the eidolon, wakes it in its whole build and gives back nothing; the gantry's body is not stripped.
    [AtlasScenario(TimeoutMs = 240_000)]
    public async Task A_docked_body_saved_under_an_older_bill_wakes_without_dropping_its_layers()
    {
        var (origin, player) = await Floor(-640, 760, "dockrestorer");
        var gantry = await Gantry(origin);
        var e = await Woken(gantry, player, EidolonBuild.OldBuild, west: 1.5);
        Assert.True(e.Dock!.Begin(gantry, null));
        await Docked(e, 400);
        AssertHung(e, gantry);

        var tree = new TreeAttribute();
        gantry.ToTreeAttributes(tree);
        ((ITreeAttribute)tree["eidolonBody"]).SetInt("bones/game:supportbeam-oak", 10);
        gantry.FromTreeAttributes(tree, W);
        Assert.Equal("bones", BodyOf(gantry).Parts.Next); // short
        Assert.Equal(EidolonBuild.OldBuild, e.Build);

        foreach (var drop in W.GetEntitiesAround(origin.ToVec3d(), 12, 8, x => x is EntityItem))
            drop.Die(EnumDespawnReason.Removed);
        Empty(player);
        Assert.Null(ClickGantry(player, Crank(gantry), null));
        await World.Ticks(5);
        Assert.Equal(DockPhase.None, e.Dock!.Phase);
        Assert.False(BodyOf(gantry).HoldsEidolon);
        Assert.Equal(EidolonBuild.OldBuild, e.Build);
        var dropped = W.GetEntitiesAround(origin.ToVec3d(), 12, 8, x => x is EntityItem).OfType<EntityItem>()
            .Select(i => $"{i.Itemstack.StackSize} {i.Itemstack.Collectible.Code}").ToList();
        Assert.True(dropped.Count == 0, "dropped: " + string.Join(", ", dropped));

        e.Die(EnumDespawnReason.Removed);
        W.BlockAccessor.GetBlock(origin).OnBlockBroken(W, origin, player);
    }
}
