using System.Text;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.EidolonGantry;

/// <summary>
/// The eidolon's body on the gantry's spine (#672, the fittings), a block entity behavior of the gantry's
/// controller (<c>entityBehaviors</c> in <c>blocktypes/eidolongantry/frame.json</c>) plugged in through
/// <see cref="IEidolonGantryExtension"/>. Once the winch and spine are fitted, the seven stages of
/// <see cref="BodyBill"/> go on in order (<see cref="BodyParts"/>): the crafted torso, used on the gantry;
/// the five layers, fitted item by item in any material the layer allows (its first piece fixes it);
/// the mind, a temporal gear. Within a stage a click fits what the held stack can give toward one
/// ingredient still needed. Then a turn of the winch's crank wakes it: the eidolon is spawned where the
/// body hung, wearing its layers (<see cref="EidolonBuild"/> on its attributes), owned by the player who
/// turned the crank, plays <c>activate</c> and walks out of the open front, and the body is cleared, so the
/// gantry is an empty dock with its spine again. Breaking the gantry returns everything fitted.
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

    /// <summary>Every stage fitted, the mind too: only the crank is left (the creative shortcut passes it by).</summary>
    public bool Complete => _parts.Complete;

    /// <summary>The layers the hung body wears so far.</summary>
    public EidolonBuild Build => _parts.Build;

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        // A body saved before fittings may have left parts the new stages do not take: given back at the
        // open front once the chunk is in.
        if (api.Side == EnumAppSide.Server && _parts.Spare.Count > 0)
            Blockentity.RegisterDelayedCallback(_ => GiveBackSpare(), 500);
    }

    private void GiveBackSpare()
    {
        if (Gantry is not { } gantry || EidolonGantrySystem.Of(Api).Rig is not { } rig || _parts.Spare.Count == 0)
            return;
        var at = gantry.WorldPoint(rig.Exit).Add(0, 0.5, 0);
        foreach (var drop in _parts.TakeSpare())
            if (gantry.Collectible(drop.Code) is { } collectible)
                Api.World.SpawnItemEntity(new ItemStack(collectible, drop.Count), at);
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: an old body's parts the new stages do not take were given back", gantry.Pos);
        gantry.MarkDirty(true);
    }

    // ---- Clicks ----

    public bool OnGantryInteract(BEEidolonGantry gantry, IPlayer byPlayer, ItemSlot? slot)
    {
        var stack = slot?.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        // a body item, or a material of the open layer's kind that it does not take (refused with why)
        bool family = BodyBill.FamilyLayer(code) is { } layer && _parts.Next is { } open && BodyBill.LayerOf(open) == layer;
        if (stack == null || !BodyBill.IsBodyItem(code) && !family)
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
                gantry.Error(byPlayer, _parts.Complete ? "error-body-complete" : "error-body-fitted", StageName(_parts.Next));
                return true;
            case BodyFitVerdict.WrongMaterial:
                WrongMaterial(gantry, byPlayer, stage);
                return true;
            case BodyFitVerdict.NotAPart:
                return true;
        }
        _parts.Fit(code, take);
        if (consumes)
        {
            slot!.TakeOut(take);
            slot.MarkDirty();
        }
        Api.World.PlaySoundAt(gantry.Block.Sounds.Place, gantry.Pos, -0.25, byPlayer);
        if (_parts.StageComplete(stage))
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), gantry.Pos, -0.25, byPlayer);
        gantry.MarkDirty(true);
        return true;
    }

    private void WrongMaterial(BEEidolonGantry gantry, IPlayer byPlayer, string stage)
    {
        if (BodyBill.LayerOf(stage) is not { } layer)
            return;
        if (_parts.Material(layer) is { } material)
            gantry.Error(byPlayer, "error-body-samematerial", StageName(stage), MaterialName(layer, material));
        else
            gantry.Error(byPlayer, "error-body-material", StageName(stage), Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-materials-" + stage));
    }

    public bool CreativeFitNext(BEEidolonGantry gantry, IPlayer? byPlayer)
    {
        if (!gantry.WinchComplete || _parts.FitNextStage() == null)
            return false;
        Api.World.PlaySoundAt(gantry.Block.Sounds.Place, gantry.Pos, -0.25, byPlayer);
        gantry.MarkDirty(true);
        return true;
    }

    // ---- Waking ----

    /// <summary>A turn of the crank: wakes a complete body (server side). Nothing on the spine is not
    /// the body's turn; a body still being built is refused, naming its next stage.</summary>
    public bool OnCrank(BEEidolonGantry gantry, IPlayer byPlayer)
    {
        if (!gantry.WinchComplete || !_parts.Started)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (!_parts.Complete)
            gantry.Error(byPlayer, "error-body-notready", StageName(_parts.Next));
        else if (Wake(gantry, byPlayer))
        {
            Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), gantry.Pos, -0.25, byPlayer);
            gantry.MarkDirty(true);
        }
        return true;
    }

    /// <summary>Wakes the eidolon (server side): spawns it where the body hung, facing out of the open
    /// front, wearing the body's layers, owned by <paramref name="byPlayer"/> and charged with the mind's
    /// temporal gear, playing <c>activate</c>; orders it out of the front; and empties the spine. False,
    /// with an error and nothing changed, when it cannot be spawned.</summary>
    public bool Wake(BEEidolonGantry gantry, IPlayer? byPlayer)
    {
        if (EidolonGantrySystem.Of(Api).Rig is not { } rig || EidolonSystem.Of(Api) is not { } eidolons)
            return gantry.Error(byPlayer, "error-body-nowake");
        var at = gantry.WorldPoint(rig.Body);
        var outward = Facing(gantry.WorldSide(rig.ExitSide));
        float yaw = (float)Math.Atan2(outward.X, outward.Z);
        var build = _parts.Build;
        var eidolon = eidolons.Spawn(Api.World, at, yaw, byPlayer, activate: true, gears: 1, build: build);
        if (eidolon == null)
            return gantry.Error(byPlayer, "error-body-nowake");
        var exit = gantry.WorldPoint(rig.Exit);
        var target = new Vec3d(Math.Round(exit.X + outward.X * WalkOutBlocks), exit.Y, Math.Round(exit.Z + outward.Z * WalkOutBlocks));
        eidolon.Orders?.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(target, run: false));
        _parts.Clear();
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: an eidolon ({1}) woke for {2}", gantry.Pos, build,
            byPlayer?.PlayerName ?? "nobody");
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

    public bool Shows(string requires) => BodyBill.Order.Contains(requires) && _parts.StageComplete(requires);

    /// <summary>The hung body's textures over the shape's (by texture code): each complete layer's in its
    /// material (<see cref="EidolonBuild.Textures"/>).</summary>
    public IReadOnlyDictionary<string, string> Textures => _parts.Build.Textures();

    public IEnumerable<ItemStack> Drops(IWorldAccessor world)
    {
        foreach (var drop in _parts.Returns())
            if (Gantry?.Collectible(drop.Code) is { } collectible)
                yield return new ItemStack(collectible, drop.Count);
    }

    public IEnumerable<WorldInteraction> Help(BEEidolonGantry gantry, IPlayer? forPlayer)
    {
        if (!gantry.WinchComplete)
            yield break;
        if (_parts.Next is not { } stage)
        {
            yield return new WorldInteraction
            {
                ActionLangCode = EidolonGantrySystem.Domain + ":blockhelp-eidolongantry-wake",
                MouseButton = EnumMouseButton.Right,
            };
            yield break;
        }
        var stacks = _parts.StillNeeded(stage).SelectMany(i => NeededStacks(gantry, stage, i)).ToArray();
        if (stacks.Length > 0)
            yield return new WorldInteraction
            {
                ActionLangCode = EidolonGantrySystem.Domain + (stage == BodyBill.TorsoStage
                    ? ":blockhelp-eidolongantry-torso"
                    : ":blockhelp-eidolongantry-fitbody"),
                MouseButton = EnumMouseButton.Right,
                Itemstacks = stacks,
            };
    }

    // What the help shows for an ingredient: a material not yet chosen as every material the layer allows.
    private static IEnumerable<ItemStack> NeededStacks(BEEidolonGantry gantry, string stage, BodyIngredient i)
    {
        IEnumerable<string> codes = [i.Code];
        if (i.AnyMaterial && BodyBill.LayerOf(stage) is { } layer)
        {
            int part = EidolonFittings.MaterialCodes(layer, BodyBill.DefaultMaterial(layer)).ToList().IndexOf(i.Code);
            codes = EidolonFittings.Materials(layer).Select(m => EidolonFittings.MaterialCodes(layer, m))
                .Where(c => c.Count > part && part >= 0).Select(c => c[part]);
        }
        foreach (var code in codes)
            if (gantry.Collectible(code) is { } c)
                yield return new ItemStack(c, i.Count);
    }

    /// <summary>A body stage's name as the info and errors show it.</summary>
    public static string StageName(string? stage) =>
        stage == null ? "" : Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-body-" + stage);

    /// <summary>A layer's material as the info shows it: the name of its (first) item.</summary>
    private string MaterialName(FittingLayer layer, string material) =>
        EidolonFittings.MaterialCodes(layer, material) is { Count: > 0 } codes && Gantry?.Collectible(codes[0]) is { } c
            ? new ItemStack(c).GetName()
            : material;

    public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
    {
        base.GetBlockInfo(forPlayer, dsc);
        if (Gantry is not { WinchComplete: true } gantry)
            return;
        string L(string key, params object[] args) => Lang.Get(EidolonGantrySystem.Domain + ":eidolongantry-" + key, args);
        var done = BodyBill.Order.Where(_parts.StageComplete).Select(s => BodyBill.LayerOf(s) is { } layer
            ? L("info-body-layer", StageName(s), MaterialName(layer, _parts.Material(layer)!))
            : StageName(s)).ToList();
        if (done.Count > 0)
            dsc.AppendLine(L("info-body-done", string.Join(", ", done)));
        if (_parts.Next is not { } next)
        {
            dsc.AppendLine(L("info-body-wake"));
            return;
        }
        var needs = _parts.StillNeeded(next).Select(i =>
        {
            string name = gantry.Collectible(i.Code) is { } c ? new ItemStack(c).GetName() : i.Code;
            return i.AnyMaterial
                ? L("info-body-anyitem", i.Count, name, L("materials-" + next))
                : L("info-body-item", i.Count, name);
        });
        dsc.AppendLine(L("info-body-next", StageName(next), string.Join(", ", needs)));
        if (BodyBill.LayerOf(next) is { } open && _parts.Material(open) is { } chosen)
            dsc.AppendLine(L("info-body-material", StageName(next), MaterialName(open, chosen)));
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
        // a live block entity given an old body's state (a schematic, a command): its spare goes back too
        if (Api?.Side == EnumAppSide.Server && _parts.Spare.Count > 0)
            Blockentity.RegisterDelayedCallback(_ => GiveBackSpare(), 500);
    }
}
