using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Eidolon (#672 and the fittings, mods-src/seraphhorizons/EidolonGantry/Game/BEBehaviorEidolonBody.cs):
// the eidolon's body built on the gantry's spine in layers of material, and waking it with the winch's
// crank, in the plain world. Each scenario builds on a floor of its own like the gantry's
// (EidolonGantryScenarios.cs), at x -560 to -640, z -600, and uses the gear cutter's player,
// gearcutterhand, who also stands by so the woken eidolon's AI runs (the game runs a creature's AI only
// with a player in range).
public partial class SharedWorldScenarios
{
    private BEBehaviorEidolonBody BodyOf(BEEidolonGantry gantry) =>
        gantry.GetBehavior<BEBehaviorEidolonBody>() ?? throw new Xunit.Sdk.XunitException("the gantry has no body behavior");

    private List<EntityLaborEidolon> EidolonsNear(BlockPos pos, double range = 16) =>
        W.GetEntitiesAround(new Vec3d(pos.X, pos.Y, pos.Z), (float)range, 8, e => e is EntityLaborEidolon && e.Alive)
            .OfType<EntityLaborEidolon>().ToList();

    /// <summary>A gantry with its winch built and spine hung, on a floor of its own.</summary>
    private async Task<(IPlayer Player, BEEidolonGantry Gantry)> BuiltGantry(int dx, int dz, string wood = "oak")
    {
        var player = await CutterPlayer();
        var pos = await GantrySite(dx, dz);
        player.Entity.TeleportTo(pos.AddCopy(-6, 0, -6));
        await World.Ticks(2);
        foreach (var e in EidolonsNear(pos))
            e.Die(EnumDespawnReason.Removed);
        var gantry = await PlaceGantry(pos, wood);
        gantry.FitAll();
        Assert.True(gantry.WinchComplete);
        return (player, gantry);
    }

    /// <summary>The winch crank's cell, where a click turns it.</summary>
    private BlockPos CrankOf(BEEidolonGantry gantry) => gantry.CellPos(GantryRigOf.CrankCell);

    /// <summary>The build an entity carries (its watched <c>eidolonFittings</c>), or null with none.</summary>
    private static EidolonBuild? BuildOf(Vintagestory.API.Common.Entities.Entity entity) =>
        entity.WatchedAttributes.GetTreeAttribute(EidolonBuild.AttributeKey) is { } tree
            ? EidolonBuild.FromAttributes(tree.ToDictionary(kv => kv.Key, kv => (kv.Value as StringAttribute)?.value))
            : null;

    // The whole body with real items in survival, each layer in a material of its own: the torso first
    // (nothing of a layer before it), a layer's first piece fixing its material and a piece of another
    // refused with nothing taken, a stack giving what the stage still needs and keeping the rest, the
    // info line naming what is next. The crank does nothing before the mind; with it in, a turn of the
    // crank wakes it: one eidolon, owned by the player who turned it, charged, wearing the layers it was
    // built in (eidolonFittings), walks out of the open front, and the gantry is left with its spine.
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Eidolon_body_is_built_in_layers_of_material_and_the_crank_wakes_it()
    {
        var (player, gantry) = await BuiltGantry(-560, -600);
        var body = BodyOf(gantry);
        var ghost = gantry.CellPos(new Int3(1, 0, 0));
        var crank = CrankOf(gantry);
        Assert.Contains("Body, next stage: torso, still needs", GantryInfo(gantry, player));
        // the crank is selectable once fitted, though its cell is hollow
        Assert.NotEmpty(W.BlockAccessor.GetBlock(crank).GetSelectionBoxes(W.BlockAccessor, crank));
        Assert.Empty(W.BlockAccessor.GetBlock(crank).GetCollisionBoxes(W.BlockAccessor, crank));
        // a turn of the crank with nothing on the spine wakes nothing
        Assert.Null(CutterClick(player, crank, null));
        Assert.True(_cutterHandled);
        Assert.Empty(EidolonsNear(gantry.Pos));

        // out of order: the bones' gearbox, a plate and the mind's gear before the torso
        Assert.Equal(1, CutterClick(player, gantry.Pos, CutterItem("game:jonasframes-gearbox02"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem("game:metalplate-brass"))?.StackSize);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem(BodyBill.TemporalGear))?.StackSize);
        Assert.False(body.Parts.Started);
        Assert.Null(CutterClick(player, ghost, CutterItem(BodyBill.Torso)));
        Assert.True(body.Parts.HasTorso);
        Assert.True(body.Shows("torso"));

        var build = EidolonBuild.Of("birch", EidolonFittings.Iron, EidolonFittings.Brass, "cloth-blue", EidolonFittings.Stainless);
        // a piece of each layer's material in another (allowed) one, refused once the first fixes it
        var other = new Dictionary<FittingLayer, string>
        {
            [FittingLayer.Bones] = "game:supportbeam-oak",
            [FittingLayer.Joints] = "game:rod-steel",
            [FittingLayer.Plates] = "game:metalplate-steel",
            [FittingLayer.Sash] = "game:leather-normal-blue",
            [FittingLayer.Skirt] = "game:metalchain-iron",
        };
        int i = 0;
        foreach (var layer in EidolonFittings.Layers)
        {
            string stage = BodyBill.StageOf(layer);
            Assert.Equal(stage, body.Parts.Next);
            Assert.False(body.Shows(stage));
            bool first = true;
            foreach (var ing in BodyBill.Of(stage, build[layer]))
            {
                var at = i++ % 2 == 0 ? gantry.Pos : ghost;
                if (ing.Count > 1)
                {
                    // part of it, then the rest from a stack with one over, which stays in hand
                    Assert.Null(CutterClick(player, at, CutterItem(ing.Code, ing.Count - 1)));
                    Assert.Equal(1, body.Parts.Missing(stage, ing.Code));
                    Assert.Equal(1, CutterClick(player, at, CutterItem(ing.Code, 2))?.StackSize);
                }
                else
                    Assert.Null(CutterClick(player, at, CutterItem(ing.Code)));
                Assert.Equal(0, body.Parts.Missing(stage, ing.Code));
                if (first)
                {
                    Assert.Equal(build[layer], body.Parts.Material(layer));
                    Assert.Equal(2, CutterClick(player, at, CutterItem(other[layer], 2))?.StackSize);
                    Assert.True(_cutterHandled);
                    first = false;
                }
            }
            Assert.True(body.Parts.StageComplete(stage), $"{stage} is not complete");
            Assert.True(body.Shows(stage));
            Assert.Equal(build[layer], body.Build[layer]);
        }
        Assert.Equal(build, body.Build);
        Assert.Equal("mind", body.Parts.Next);

        // the crank before the mind is refused, nothing woken
        Assert.Null(CutterClick(player, crank, null));
        Assert.Empty(EidolonsNear(gantry.Pos));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem(BodyBill.TemporalGear)));
        Assert.True(body.Parts.Complete);
        Assert.True(body.Shows("mind"));
        Assert.Contains("turn the winch's crank", GantryInfo(gantry, player));
        Assert.Empty(EidolonsNear(gantry.Pos));
        // nothing more goes in, even the gear again
        Assert.Equal(1, CutterClick(player, gantry.Pos, CutterItem(BodyBill.TemporalGear))?.StackSize);

        // the crank wakes it: one eidolon, the player's, charged with the gear, in its layers
        Assert.Null(CutterClick(player, crank, null));
        var woken = Assert.Single(EidolonsNear(gantry.Pos));
        Assert.Equal(player.PlayerUID, woken.OwnerUid);
        Assert.True(woken.GetBehavior<EntityBehaviorEidolonCharge>()!.ChargeDays > 0);
        Assert.Equal(build, BuildOf(woken));
        Assert.False(body.Parts.Started);
        Assert.Equal("torso", body.Parts.Next);
        Assert.False(body.Shows("torso"));
        Assert.True(gantry.WinchComplete);
        Assert.Contains("Body, next stage: torso", GantryInfo(gantry, player));
        // facing out of the open front (world west for a gantry facing south)
        Assert.Equal(Side.West, gantry.WorldSide(GantryRigOf.ExitSide));
        Assert.InRange(Math.Sin(woken.Pos.Yaw), -1.0, -0.99);

        // saved and loaded as the world does: an empty body stays empty
        var tree = new TreeAttribute();
        gantry.ToTreeAttributes(tree);
        gantry.FromTreeAttributes(tree, W);
        Assert.False(BodyOf(gantry).Parts.Started);

        // it walks out of the open front once activate has played, and stands outside the gantry
        Assert.Equal(GoToOrder.OrderCode, woken.Orders!.OrderCode);
        await World.Until(() => woken.Orders!.OrderCode == null, 2400);
        Assert.True(woken.Pos.X < gantry.Pos.X - 1, $"still in the gantry at {woken.Pos.XYZ} (front at x {gantry.Pos.X})");
        Assert.Null(BEEidolonGantry.DockAt(W.BlockAccessor, woken.Pos.XYZ));
        Assert.Single(EidolonsNear(gantry.Pos));
        Assert.Equal(build, BuildOf(woken));

        woken.Die(EnumDespawnReason.Removed);
        CutterKillItems(gantry.Pos, 10);
        W.BlockAccessor.GetBlock(gantry.Pos).OnBlockBroken(W, gantry.Pos, player);
        await World.Ticks(2);
        CutterKillItems(gantry.Pos, 10);
    }

    // Creative Ctrl fits the next body stage whole, in the default build's material, nothing taken; part
    // of the next by hand; partial progress survives saving; the dock knows where the body hangs;
    // breaking the gantry returns the frame, the winch and every body item fitted, and no eidolon is made.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Eidolon_body_partly_built_and_broken_returns_everything()
    {
        var (player, gantry) = await BuiltGantry(-600, -600, "birch");
        var body = BodyOf(gantry);
        Assert.Null(CutterClick(player, gantry.Pos, null, ctrl: true, creative: true));
        Assert.True(body.Parts.StageComplete("torso"));
        Assert.Null(CutterClick(player, gantry.Pos, null, ctrl: true, creative: true));
        Assert.True(body.Parts.StageComplete("bones"));
        // the shortcut draws the bones' wood at random
        string wood = Assert.IsType<string>(body.Parts.Material(FittingLayer.Bones));
        Assert.True(EidolonFittings.Allows(FittingLayer.Bones, wood), wood);
        // the joints: a gearbox's spring and three of twenty stainless nails and strips
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:jonasframes-spring01")));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:metalnailsandstrips-stainlesssteel", 3)));
        Assert.Equal(17, body.Parts.Missing("joints", "game:metalnailsandstrips-stainlesssteel"));
        Assert.Contains("stainless", GantryInfo(gantry, player), StringComparison.OrdinalIgnoreCase);

        // saved and loaded: the progress and the joints' material kept
        var tree = new TreeAttribute();
        gantry.ToTreeAttributes(tree);
        gantry.FromTreeAttributes(tree, W);
        body = BodyOf(gantry);
        Assert.Equal(EidolonFittings.Stainless, body.Parts.Material(FittingLayer.Joints));
        Assert.Equal(3, body.Parts.Fitted("joints", "game:metalnailsandstrips-stainlesssteel"));

        var rig = GantryRigOf;
        Assert.Same(gantry, BEEidolonGantry.DockAt(W.BlockAccessor, gantry.WorldPoint(rig.Body)));
        Assert.Null(BEEidolonGantry.DockAt(W.BlockAccessor, gantry.WorldPoint(rig.Exit).AddCopy(-2, 0, 0)));

        CutterKillItems(gantry.Pos, 10);
        W.BlockAccessor.GetBlock(gantry.Pos).OnBlockBroken(W, gantry.Pos, player);
        await World.Ticks(2);
        var dropped = CutterItemsNear(gantry.Pos, 10);
        var expected = new Dictionary<string, int> { ["seraphhorizons:eidolongantry-birch-north"] = 1 };
        void Add(string code, int n) => expected[code] = expected.GetValueOrDefault(code) + n;
        foreach (var stage in GantryRequires.Stages)
            Add(GantryParts.CodesFor(stage, "birch")[0], GantryParts.Needed(stage));
        foreach (var ing in BodyBill.Of("torso").Concat(BodyBill.Of("bones", wood)))
            Add(ing.Code, ing.Count);
        Add("game:jonasframes-spring01", 1);
        Add("game:metalnailsandstrips-stainlesssteel", 3);
        Assert.Equal(expected.OrderBy(kv => kv.Key), dropped.OrderBy(kv => kv.Key));
        Assert.Empty(EidolonsNear(gantry.Pos));
        CutterKillItems(gantry.Pos, 10);
    }

    // A body saved limb by limb before the fittings (torso, pelvis, legs, arms, head, all steel) loads on
    // the new stages: the old torso is the crafted torso, the Jonas parts and steel go to their layers,
    // and what the new stages do not take (nails and strips and rods past the joints' 20 and 4) is given
    // back at the open front. With oak beams and the rest it is finished and woken all steel.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Eidolon_body_saved_limb_by_limb_loads_on_the_layers_in_steel()
    {
        var (player, gantry) = await BuiltGantry(-640, -600);
        CutterKillItems(gantry.Pos, 10);
        var old = new (string Stage, string Code, int Count)[]
        {
            ("torso", "game:jonasparts-tank01", 1), ("torso", "game:jonasparts-tank02", 1), ("torso", "game:jonasparts-pumphead", 1),
            ("torso", "game:metalplate-steel", 4), ("torso", "game:metalnailsandstrips-steel", 8), ("torso", "game:metal-parts", 4),
            ("torso", EidolonFittings.StainlessGear, 3),
            ("pelvis", "game:jonasframes-gearbox02", 1), ("pelvis", "game:metalplate-steel", 3), ("pelvis", "game:metalnailsandstrips-steel", 8),
            ("pelvis", "game:metal-parts", 3), ("pelvis", EidolonFittings.StainlessGear, 2),
            ("legs", "game:jonasframes-joint01", 2), ("legs", "game:jonasframes-spring01", 2), ("legs", "game:rod-steel", 4),
            ("legs", "game:metalplate-steel", 3), ("legs", "game:metalnailsandstrips-steel", 8), ("legs", "game:metal-parts", 3),
            ("legs", EidolonFittings.StainlessGear, 2),
            ("arms", "game:jonasframes-gears01", 1), ("arms", "game:jonasframes-gears02", 1), ("arms", "game:jonasparts-cylinder01", 1),
            ("arms", "game:jonasparts-valve01", 1), ("arms", "game:rod-steel", 4), ("arms", "game:metalplate-steel", 2),
            ("arms", "game:metalnailsandstrips-steel", 8), ("arms", "game:metal-parts", 4), ("arms", EidolonFittings.StainlessGear, 3),
            ("head", EidolonFittings.Vessel, 1), ("head", "game:jonasframes-oscillator01", 1), ("head", "game:jonasframes-gearbox01", 1),
            ("head", "game:jonasparts-cylinder02", 1), ("head", "game:jonasparts-connector01", 1), ("head", "game:metalplate-steel", 1),
            ("head", "game:metalnailsandstrips-steel", 4), ("head", "game:metal-parts", 2), ("head", EidolonFittings.StainlessGear, 2),
        };
        var tree = new TreeAttribute();
        gantry.ToTreeAttributes(tree);
        var saved = new TreeAttribute();
        foreach (var (stage, code, n) in old)
            saved.SetInt(stage + "/" + code, n);
        tree["eidolonBody"] = saved;
        gantry.FromTreeAttributes(tree, W);
        var body = BodyOf(gantry);
        Assert.True(body.Parts.HasTorso);
        Assert.Equal("bones", body.Parts.Next);
        Assert.True(body.Parts.StageComplete("joints"));
        Assert.Equal(13, body.Parts.Fitted("plates", "game:metalplate-steel"));

        // the spare is given back at the open front
        await World.Until(() => body.Parts.Spare.Count == 0, 200);
        await World.Ticks(5);
        var given = CutterItemsNear(gantry.Pos, 10);
        Assert.Equal(16, given.GetValueOrDefault("game:metalnailsandstrips-steel"));
        Assert.Equal(4, given.GetValueOrDefault("game:rod-steel"));
        CutterKillItems(gantry.Pos, 10);

        // finished: the bones' beams, the plates' last four, a plain leather sash, a steel skirt, the mind
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:supportbeam-oak", 30)));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:metalplate-steel", 4)));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:leather-normal-plain", 4)));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:metalchain-steel", 4)));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem(BodyBill.TemporalGear)));
        Assert.True(body.Parts.Complete);
        Assert.Null(CutterClick(player, CrankOf(gantry), null));
        var woken = Assert.Single(EidolonsNear(gantry.Pos));
        Assert.Equal(EidolonBuild.OldBuild, BuildOf(woken));

        woken.Die(EnumDespawnReason.Removed);
        CutterKillItems(gantry.Pos, 10);
        W.BlockAccessor.GetBlock(gantry.Pos).OnBlockBroken(W, gantry.Pos, player);
        await World.Ticks(2);
        CutterKillItems(gantry.Pos, 10);
    }
}
