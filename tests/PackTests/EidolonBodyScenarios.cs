using Atlas.XUnit;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.EidolonGantry;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.PackTests;

// seraphhorizons, Eidolon (#672, mods-src/seraphhorizons/EidolonGantry/Game/BEBehaviorEidolonBody.cs):
// the eidolon's body built on the gantry's spine, and waking it, in the plain world. Each scenario
// builds on a floor of its own like the gantry's (EidolonGantryScenarios.cs), at x -560 to -600,
// z -600, and uses the gear cutter's player, gearcutterhand, who also stands by so the woken eidolon's
// AI runs (the game runs a creature's AI only with a player in range).
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

    // Every body stage with real items, in survival: a later stage's item and the next stage's before
    // its time are refused with nothing taken; within a stage a click takes what the stack can give to
    // one item still needed and leaves the rest; the info line lists what is still needed. The temporal
    // gear wakes it: one eidolon, owned by the player who fitted the mind, charged, walks out of the open
    // front, and the gantry is left with its spine and no body, ready for another.
    [AtlasScenario(TimeoutMs = 180_000)]
    public async Task Eidolon_body_is_built_stage_by_stage_and_the_mind_wakes_it_out_of_the_gantry()
    {
        var (player, gantry) = await BuiltGantry(-560, -600);
        var body = BodyOf(gantry);
        var ghost = gantry.CellPos(new Int3(1, 0, 0));
        Assert.Contains("Body, next stage: torso, still needs", GantryInfo(gantry, player));

        // out of order: the pelvis's gearbox and the mind's gear before the torso
        Assert.Equal(1, CutterClick(player, gantry.Pos, CutterItem("game:jonasframes-gearbox02"))?.StackSize);
        Assert.True(_cutterHandled);
        Assert.Equal(1, CutterClick(player, ghost, CutterItem(BodyBill.TemporalGear))?.StackSize);
        Assert.False(body.Parts.Started);
        Assert.Empty(EidolonsNear(gantry.Pos));

        int i = 0;
        foreach (var stage in BodyBill.Order)
        {
            Assert.Equal(stage, body.Parts.Next);
            foreach (var ing in BodyBill.Of(stage))
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
                // the mind's gear wakes it and clears the body, so nothing of it is left to count
                if (stage != BodyBill.Last)
                    Assert.Equal(0, body.Parts.Missing(stage, ing.Code));
            }
            if (stage == BodyBill.Last)
                break;
            Assert.True(body.Parts.StageComplete(stage), $"{stage} is not complete");
            Assert.True(body.Shows(stage));
            // nothing more of the stage's first item goes in now
            Assert.Equal(1, CutterClick(player, gantry.Pos, CutterItem(BodyBill.Of(stage)[0].Code))?.StackSize);
        }

        // woken: one eidolon, the player's, charged with the gear; the body cleared, the spine kept
        var woken = Assert.Single(EidolonsNear(gantry.Pos));
        Assert.Equal(player.PlayerUID, woken.OwnerUid);
        Assert.True(woken.GetBehavior<EntityBehaviorEidolonCharge>()!.ChargeDays > 0);
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

        woken.Die(EnumDespawnReason.Removed);
        CutterKillItems(gantry.Pos, 10);
        W.BlockAccessor.GetBlock(gantry.Pos).OnBlockBroken(W, gantry.Pos, player);
        await World.Ticks(2);
        CutterKillItems(gantry.Pos, 10);
    }

    // Creative Ctrl fits the next body stage whole, nothing taken; part of the next by hand; the dock
    // knows where the body hangs; breaking the gantry returns the frame, the winch and every body item
    // fitted, and no eidolon is made.
    [AtlasScenario(TimeoutMs = 120_000)]
    public async Task Eidolon_body_partly_built_and_broken_returns_everything()
    {
        var (player, gantry) = await BuiltGantry(-600, -600, "birch");
        var body = BodyOf(gantry);
        Assert.Null(CutterClick(player, gantry.Pos, null, ctrl: true, creative: true));
        Assert.True(body.Parts.StageComplete("torso"));
        Assert.False(body.Parts.StageComplete("pelvis"));
        // the pelvis: its gearbox and three of eight nails and strips
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem("game:jonasframes-gearbox02")));
        Assert.Null(CutterClick(player, gantry.Pos, CutterItem(BodyBill.SteelNails, 3)));
        Assert.Equal(5, body.Parts.Missing("pelvis", BodyBill.SteelNails));

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
        foreach (var ing in BodyBill.Of("torso"))
            Add(ing.Code, ing.Count);
        Add("game:jonasframes-gearbox02", 1);
        Add(BodyBill.SteelNails, 3);
        Assert.Equal(expected.OrderBy(kv => kv.Key), dropped.OrderBy(kv => kv.Key));
        Assert.Empty(EidolonsNear(gantry.Pos));
        CutterKillItems(gantry.Pos, 10);
    }
}
