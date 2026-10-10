using System.Text;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// The eidolon's body stages on the gantry's spine (#672), a block entity behavior of the gantry's
/// controller (<c>entityBehaviors</c> in <c>blocktypes/eidolongantry/frame.json</c>) plugged in through
/// <see cref="IEidolonGantryExtension"/>. Once the winch and spine are fitted, the six stages of
/// <see cref="BodyBill"/> go on in order (<see cref="BodyParts"/>); within a stage a click fits what the
/// held stack can give toward one ingredient still needed. The last, the mind (a temporal gear), wakes
/// it: the eidolon is spawned where the body hung, owned by the player who fitted the mind, plays
/// <c>activate</c> and walks out of the open front, and the body stages are cleared, so the gantry is an
/// empty dock with its spine again and a second eidolon can be built in it. Breaking the gantry returns
/// everything fitted.
/// </summary>
public class BEBehaviorEidolonBody(BlockEntity blockentity) : BlockEntityBehavior(blockentity), IEidolonGantryExtension
{
    public const string Code = "seraphhorizons.EidolonBody";
    private const string TreeKey = "eidolonBody";

    /// <summary>How far past the open front's middle the woken eidolon walks, blocks.</summary>
    public const double WalkOutBlocks = 3;

    private BodyParts _parts = new();

    public BodyParts Parts => _parts;

    private BEEidolonGantry? Gantry => Blockentity as BEEidolonGantry;

    /// <summary>Never: when the last stage goes in the eidolon leaves and the spine is empty again,
    /// ready for the next.</summary>
    public bool Complete => _parts.Complete;

    // ---- Clicks ----

    public bool OnGantryInteract(BEEidolonGantry gantry, IPlayer byPlayer, ItemSlot? slot)
    {
        var stack = slot?.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        if (stack == null || !IsBodyItem(code))
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (!gantry.WinchComplete)
        {
            gantry.Error(byPlayer, "error-body-needs-spine");
            return true;
        }
        bool consumes = byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative;
        var verdict = _parts.CanFit(code, consumes ? stack.StackSize : int.MaxValue, out var stage, out int take);
        switch (verdict)
        {
            case BodyFitVerdict.OutOfOrder:
                gantry.Error(byPlayer, "error-body-order", StageName(_parts.Next));
                return true;
            case BodyFitVerdict.AlreadyFitted:
                gantry.Error(byPlayer, "error-body-fitted", StageName(_parts.Next));
                return true;
            case BodyFitVerdict.NotAPart:
                return true;
        }
        // The last item of the mind wakes it: spawned first, so nothing is taken if it cannot be.
        bool wakes = stage == BodyBill.Last && _parts.Missing(stage, code!) == take
                     && _parts.StillNeeded(stage).Count == 1;
        if (wakes && !Wake(gantry, byPlayer))
            return true;
        if (!wakes)
            _parts.Fit(code, take);
        if (consumes)
        {
            slot!.TakeOut(take);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(gantry.Block.Sounds.Place, gantry.Pos, -0.25, byPlayer);
        if (!wakes && _parts.StageComplete(stage))
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), gantry.Pos, -0.25, byPlayer);
        gantry.MarkDirty(true);
        return true;
    }

    private static bool IsBodyItem(string? code)
    {
        code = BodyParts.Normalise(code);
        return code != null && BodyBill.Stages.Values.Any(b => b.Any(i => i.Code == code));
    }

    public bool CreativeFitNext(BEEidolonGantry gantry, IPlayer? byPlayer)
    {
        if (!gantry.WinchComplete || _parts.Next is not { } stage)
            return false;
        if (stage == BodyBill.Last)
        {
            if (!Wake(gantry, byPlayer))
                return false;
        }
        else
            _parts.FitNextStage();
        Api.World.PlaySoundAt(gantry.Block.Sounds.Place, gantry.Pos, -0.25, byPlayer);
        gantry.MarkDirty(true);
        return true;
    }

    // ---- Waking ----

    /// <summary>Wakes the eidolon (server side): spawns it where the body hung, facing out of the open
    /// front, owned by <paramref name="byPlayer"/> and charged with the mind's temporal gear, playing
    /// <c>activate</c>; orders it out of the front; and empties the spine. False, with an error and
    /// nothing changed, when it cannot be spawned.</summary>
    public bool Wake(BEEidolonGantry gantry, IPlayer? byPlayer)
    {
        if (EidolonGantrySystem.Of(Api).Rig is not { } rig || EidolonSystem.Of(Api) is not { } eidolons)
            return gantry.Error(byPlayer, "error-body-nowake");
        var at = gantry.WorldPoint(rig.Body);
        var outward = Facing(gantry.WorldSide(rig.ExitSide));
        float yaw = (float)Math.Atan2(outward.X, outward.Z);
        var eidolon = eidolons.Spawn(Api.World, at, yaw, byPlayer, activate: true, gears: 1);
        if (eidolon == null)
            return gantry.Error(byPlayer, "error-body-nowake");
        var exit = gantry.WorldPoint(rig.Exit);
        var target = new Vec3d(Math.Round(exit.X + outward.X * WalkOutBlocks), exit.Y, Math.Round(exit.Z + outward.Z * WalkOutBlocks));
        eidolon.Orders?.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(target, run: false));
        _parts.Clear();
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: an eidolon woke for {1}", gantry.Pos, byPlayer?.PlayerName ?? "nobody");
        return true;
    }

    private static Vec3d Facing(Side side) => side switch
    {
        Side.North => new Vec3d(0, 0, -1),
        Side.South => new Vec3d(0, 0, 1),
        Side.East => new Vec3d(1, 0, 0),
        _ => new Vec3d(-1, 0, 0),
    };

    // ---- Drawing, drops, help ----

    public bool Shows(string requires) => BodyBill.Stages.ContainsKey(requires) && _parts.StageComplete(requires);

    public IEnumerable<ItemStack> Drops(IWorldAccessor world)
    {
        foreach (var drop in _parts.Returns())
            if (Gantry?.Collectible(drop.Code) is { } collectible)
                yield return new ItemStack(collectible, drop.Count);
    }

    public IEnumerable<WorldInteraction> Help(BEEidolonGantry gantry, IPlayer? forPlayer)
    {
        if (!gantry.WinchComplete || _parts.Next is not { } stage)
            yield break;
        var stacks = _parts.StillNeeded(stage)
            .Select(i => gantry.Collectible(i.Code) is { } c ? new ItemStack(c, i.Count) : null)
            .OfType<ItemStack>().ToArray();
        if (stacks.Length > 0)
            yield return new WorldInteraction
            {
                ActionLangCode = stage == BodyBill.Last
                    ? EidolonGantrySystem.Domain + ":blockhelp-eidolongantry-wake"
                    : EidolonGantrySystem.Domain + ":blockhelp-eidolongantry-fitbody",
                MouseButton = EnumMouseButton.Right,
                Itemstacks = stacks,
            };
    }

    /// <summary>A body stage's name as the info and errors show it.</summary>
    public static string StageName(string? stage) =>
        stage == null ? "" : Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-body-" + stage);

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Gantry is not { WinchComplete: true } gantry || _parts.Next is not { } next)
            return;
        string L(string key, params object[] args) => Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-" + key, args);
        var done = BodyBill.Order.Where(_parts.StageComplete).Select(s => StageName(s)).ToList();
        if (done.Count > 0)
            dsc.AppendLine(L("info-body-done", string.Join(", ", done)));
        var needs = _parts.StillNeeded(next).Select(i =>
            L("info-body-item", i.Count, gantry.Collectible(i.Code) is { } c ? new ItemStack(c).GetName() : i.Code));
        dsc.AppendLine(L(next == BodyBill.Last ? "info-body-wake" : "info-body-next", StageName(next), string.Join(", ", needs)));
        var after = BodyBill.Order.SkipWhile(s => s != next).Skip(1).ToList();
        if (after.Count > 0)
            dsc.AppendLine(L("info-then", string.Join(", ", after.Select(s => StageName(s)))));
    }

    // ---- Saving and syncing ----

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        var body = new TreeAttribute();
        foreach (var (key, n) in _parts.Snapshot())
            body.SetInt(key, n);
        tree[TreeKey] = body;
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        var snapshot = new Dictionary<string, int>();
        if (tree.GetTreeAttribute(TreeKey) is { } body)
            foreach (var (key, value) in body)
                if (value is IntAttribute n)
                    snapshot[key] = n.value;
        _parts = BodyParts.Restore(snapshot);
    }
}
