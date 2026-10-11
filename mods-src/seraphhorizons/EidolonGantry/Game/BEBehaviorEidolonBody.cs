using System.Text;
using SeraphHorizons.Mod.Eidolon;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry.Core;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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
///
/// <para>The dock (Eidolon/README.md, "Docking"): a woken eidolon sent here by the command tool's dock order
/// backs onto the free spine and is clamped (<see cref="TakeHold"/>): the gantry keeps its entity id
/// (<see cref="DockedId"/>), the entity its gantry (<c>EntityBehaviorEidolonDock</c>), and the body's
/// stages are the eidolon's build (<see cref="BodyParts.Docked"/>), so a stripped layer is fitted again
/// item by item exactly as in the build, each completed layer written to the eidolon at once. What the
/// gantry saves of them is only a copy, made again from the eidolon's build once it is loaded
/// (<see cref="BodyParts.Reconciled"/>), so a bill changed since never strips it or drops its layers. The entity
/// is drawn hung, never the gantry's body meshes; Ctrl+right-click strips its outermost layer
/// (<see cref="Strip"/>); the crank wakes it once all five layers are on; breaking the gantry lets it go,
/// layers and all, and gives back only the pieces of a layer part fitted.</para>
/// </summary>
public class BEBehaviorEidolonBody(BlockEntity blockentity) : BlockEntityBehavior(blockentity), IEidolonGantryExtension
{
    public const string Code = "seraphhorizons.EidolonBody";
    private const string TreeKey = "eidolonBody";
    private const string DockedKey = "eidolonDocked";

    /// <summary>How often the server checks a docked eidolon is still there, ms, and how many checks in a
    /// row it may be missing from a loaded chunk before the gantry lets it go (it was removed).</summary>
    private const int DockCheckMs = 2000;
    private const int DockMissesAllowed = 3;

    /// <summary>How far past the open front's middle the woken eidolon walks, blocks.</summary>
    public const double WalkOutBlocks = 3;

    private BodyParts _parts = new();
    private long _dockedId;
    private int _dockMisses;
    // the docked body has been made again from the loaded eidolon since it was loaded (Reconcile)
    private bool _reconciled;

    public BodyParts Parts => _parts;

    private BEEidolonGantry? Gantry => Blockentity as BEEidolonGantry;

    /// <summary>Every stage fitted, the mind too: only the crank is left (the creative shortcut passes it by).</summary>
    public bool Complete => _parts.Complete;

    /// <summary>The layers the hung body wears so far.</summary>
    public EidolonBuild Build => _parts.Build;

    /// <summary>The docked eidolon's entity id, 0 with none.</summary>
    public long DockedId => _dockedId;

    /// <summary>An eidolon is docked here (loaded or not).</summary>
    public bool HoldsEidolon => _dockedId != 0;

    /// <summary>The docked eidolon, while it is loaded.</summary>
    public EntityLaborEidolon? DockedEidolon =>
        _dockedId != 0 ? Api?.World.GetEntityById(_dockedId) as EntityLaborEidolon : null;

    /// <summary>Whether <paramref name="eidolon"/> may dock here: the spine is built and bare (no body
    /// being built on it, no other eidolon docked), or it is already this eidolon's.</summary>
    public bool SpineFreeFor(Entity eidolon) =>
        Gantry is { WinchComplete: true } && (_dockedId == eidolon.EntityId || _dockedId == 0 && !_parts.Started);

    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        // A body saved before fittings may have left parts the new stages do not take: given back at the
        // open front once the chunk is in.
        if (api.Side == EnumAppSide.Server && _parts.Spare.Count > 0 && !HoldsEidolon)
            Blockentity.RegisterDelayedCallback(_ => GiveBackSpare(), 500);
        if (api.Side == EnumAppSide.Server)
            Blockentity.RegisterGameTickListener(_ => CheckDocked(), DockCheckMs);
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
        // Oil, on any cell: the docked eidolon's (the cells round it can take the click before it).
        if (DockedEidolon?.Oil is { } oil && oil.PourFrom(byPlayer))
            return true;
        var stack = slot?.Itemstack;
        string? code = stack?.Collectible?.Code?.ToString();
        // a body item, or a material of the open layer's kind that it does not take (refused with why)
        bool family = BodyBill.FamilyLayer(code) is { } layer && _parts.Next is { } open && BodyBill.LayerOf(open) == layer;
        if (stack == null || !BodyBill.IsBodyItem(code) && !family)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        Reconcile();
        if (!gantry.WinchComplete)
        {
            gantry.Error(byPlayer, "error-body-needs-spine");
            return true;
        }
        if (HoldsEidolon && DockedEidolon == null)
        {
            gantry.Error(byPlayer, "error-dock-away");
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
        DressDocked();
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
        Reconcile();
        if (!gantry.WinchComplete || HoldsEidolon && DockedEidolon == null || _parts.FitNextStage(Api.World.Rand) == null)
            return false;
        Api.World.PlaySoundAt(gantry.Block.Sounds.Place, gantry.Pos, -0.25, byPlayer);
        DressDocked();
        gantry.MarkDirty(true);
        return true;
    }

    // ---- Waking ----

    /// <summary>A turn of the crank: wakes a complete body (server side). Nothing on the spine is not
    /// the body's turn; a body still being built is refused, naming its next stage.</summary>
    public bool OnCrank(BEEidolonGantry gantry, IPlayer byPlayer)
    {
        if (!gantry.WinchComplete || !_parts.Started && !HoldsEidolon)
            return false;
        if (Api.Side != EnumAppSide.Server)
            return true;
        if (HoldsEidolon)
            WakeDocked(gantry, byPlayer);
        else if (!_parts.Complete)
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
        if (gantry.BodyPoint is not { } at || gantry.WalkOutTarget(WalkOutBlocks) is not { } target
            || EidolonSystem.Of(Api) is not { } eidolons)
            return gantry.Error(byPlayer, "error-body-nowake");
        var build = _parts.Build;
        var eidolon = eidolons.Spawn(Api.World, at, gantry.OutwardYaw, byPlayer, activate: true, gears: 1, build: build);
        if (eidolon == null)
            return gantry.Error(byPlayer, "error-body-nowake");
        eidolon.Orders?.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(target, run: false));
        _parts.Clear();
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: an eidolon ({1}) woke for {2}", gantry.Pos, build,
            byPlayer?.PlayerName ?? "nobody");
        return true;
    }

    // ---- The dock ----

    /// <summary>Clamps <paramref name="eidolon"/> onto the spine (server side; its <c>dock</c> animation's
    /// clamp frame): the gantry remembers it, and the body's stages become its build. False when the
    /// spine is not free for it.</summary>
    public bool TakeHold(EntityLaborEidolon eidolon)
    {
        if (Gantry is not { } gantry || !SpineFreeFor(eidolon))
            return false;
        if (_dockedId != eidolon.EntityId)
            _parts = BodyParts.Docked(eidolon.Build);
        _dockedId = eidolon.EntityId;
        _dockMisses = 0;
        _reconciled = false;
        Reconcile();
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), gantry.Pos, -0.25, null);
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: an eidolon ({1}) docked", gantry.Pos, eidolon.Build);
        gantry.MarkDirty(true);
        return true;
    }

    /// <summary>Whether <paramref name="eidolon"/> is the one docked here.</summary>
    public bool Holds(Entity eidolon) => _dockedId != 0 && _dockedId == eidolon.EntityId;

    /// <summary>Strips the docked eidolon (server side; Ctrl+right-click on it): pieces of a layer part
    /// fitted come back first, then, at full health only, its outermost layer whole, the layer's bill
    /// (<see cref="EidolonFittings.Bill"/>: its material and Jonas parts) given to
    /// <paramref name="byPlayer"/> or dropped at their feet. <paramref name="layer"/>: the layer taken off,
    /// null for loose pieces.</summary>
    public StripVerdict Strip(EntityLaborEidolon eidolon, IPlayer? byPlayer, out FittingLayer? layer)
    {
        layer = null;
        if (Gantry is not { } gantry || !Holds(eidolon))
            return StripVerdict.NotDocked;
        Reconcile();
        var build = eidolon.Build;
        if (_parts.Loose(build).Count > 0)
        {
            Give(gantry, byPlayer, eidolon, _parts.TakeLoose(build));
            gantry.MarkDirty(true);
            return StripVerdict.Strips;
        }
        var health = eidolon.GetBehavior<EntityBehaviorHealth>();
        var verdict = EidolonDocking.CanStrip(build, true, health?.Health ?? 0, health?.MaxHealth ?? 1);
        if (verdict != StripVerdict.Strips || build.NextToStrip is not { } off || build.Stripped() is not { } stripped)
            return verdict;
        string material = build[off]!;
        _parts.Strip(off);
        eidolon.Build = stripped;
        Give(gantry, byPlayer, eidolon, EidolonFittings.Bill(off, material).Select(i => new GantryDrop(i.Code, i.Count)));
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), gantry.Pos, -0.25, byPlayer);
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: an eidolon's {1} ({2}) stripped by {3}", gantry.Pos,
            EidolonFittings.TextureCode(off), material, byPlayer?.PlayerName ?? "nobody");
        gantry.MarkDirty(true);
        layer = off;
        return verdict;
    }

    // Gives the player each stack, dropping at their feet (or the eidolon's) what does not fit.
    private void Give(BEEidolonGantry gantry, IPlayer? byPlayer, Entity eidolon, IEnumerable<GantryDrop> drops)
    {
        var at = byPlayer?.Entity?.Pos.XYZ ?? eidolon.Pos.XYZ;
        foreach (var drop in drops)
        {
            if (gantry.Collectible(drop.Code) is not { } collectible)
                continue;
            var stack = new ItemStack(collectible, drop.Count);
            byPlayer?.Entity?.TryGiveItemStack(stack);
            if (stack.StackSize > 0)
                Api.World.SpawnItemEntity(stack, at);
        }
    }

    // A layer completed on a docked eidolon is on it at once: its look and figures follow. Only ever a
    // layer added: a body short of what the eidolon wears never takes its layers off (BodyParts.Dressing).
    private void DressDocked()
    {
        if (DockedEidolon is { } eidolon && _parts.Dressing(eidolon.Build) is { } dressed)
            eidolon.Build = dressed;
    }

    /// <summary>What the docked eidolon wears, the measure of the loose pieces: its own build while it is
    /// loaded, else the most it can be wearing (<see cref="BodyParts.MostWorn"/>), never the saved body's
    /// <see cref="BodyParts.Build"/>, which a bill changed since leaves short of it.</summary>
    private EidolonBuild DockedBuild => DockedEidolon?.Build ?? _parts.MostWorn;

    // While an eidolon is docked the body is a saved copy of what it is made of, stale once a bill has
    // changed (a mod update): the first time the eidolon is found loaded, on the server, the body is made
    // again from its build, keeping only the pieces fitted beyond it (BodyParts.Reconciled).
    private void Reconcile()
    {
        if (_reconciled || Api?.Side != EnumAppSide.Server || DockedEidolon is not { } eidolon)
            return;
        _reconciled = true;
        var before = _parts.Snapshot();
        _parts = _parts.Reconciled(eidolon.Build);
        var after = _parts.Snapshot();
        if (before.Count == after.Count && before.All(kv => after.TryGetValue(kv.Key, out int n) && n == kv.Value))
            return;
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: the docked eidolon's body was made again from its build ({1})",
            Blockentity.Pos, eidolon.Build);
        Gantry?.MarkDirty(true);
    }

    /// <summary>A turn of the crank with an eidolon docked: wakes it when all five layers are on (it plays
    /// <c>activate</c>, steps off and walks out of the front, then goes back to the order it docked from),
    /// else says which layer is missing.</summary>
    private void WakeDocked(BEEidolonGantry gantry, IPlayer byPlayer)
    {
        if (DockedEidolon is not { } eidolon || eidolon.Dock is not { } dock)
        {
            gantry.Error(byPlayer, "error-dock-away");
            return;
        }
        Reconcile();
        if (EidolonDocking.CanWake(eidolon.Build) != DockWakeVerdict.Wakes)
        {
            gantry.Error(byPlayer, "error-dock-notready", StageName(_parts.Next));
            return;
        }
        dock.Wake(gantry.WalkOutTarget(WalkOutBlocks));
        Unhold(gantry);
        Api.World.PlaySoundAt(new AssetLocation("game:sounds/effect/latch"), gantry.Pos, -0.25, byPlayer);
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: a docked eidolon ({1}) woke for {2}", gantry.Pos,
            eidolon.Build, byPlayer.PlayerName);
    }

    // The spine is bare again; any pieces of a layer part fitted are given back at the open front (never
    // a layer the eidolon wears: it leaves in them).
    private void Unhold(BEEidolonGantry gantry)
    {
        var loose = _parts.Loose(DockedBuild);
        if (loose.Count > 0 && EidolonGantrySystem.Of(Api).Rig is { } rig)
        {
            var at = gantry.WorldPoint(rig.Exit).Add(0, 0.5, 0);
            foreach (var drop in loose)
                if (gantry.Collectible(drop.Code) is { } collectible)
                    Api.World.SpawnItemEntity(new ItemStack(collectible, drop.Count), at);
        }
        _dockedId = 0;
        _dockMisses = 0;
        _reconciled = false;
        _parts.Clear();
        _parts.TakeSpare();
        gantry.MarkDirty(true);
    }

    /// <summary>Lets the docked eidolon go (server side; the gantry broken): it drops onto its feet in the
    /// layers it wears.</summary>
    public void ReleaseDocked()
    {
        if (!HoldsEidolon)
            return;
        DockedEidolon?.Dock?.Release();
        _dockedId = 0;
    }

    // A docked eidolon that is gone (removed by a command, or let go while the gantry was unloaded) frees
    // the spine: missing from a loaded chunk a few checks running, or loaded and no longer docked here.
    private void CheckDocked()
    {
        if (!HoldsEidolon || Gantry is not { } gantry)
            return;
        if (DockedEidolon is { } eidolon)
        {
            _dockMisses = 0;
            if (eidolon.Dock?.HeldAt(gantry.Pos) != true)
                Lost(gantry);
            else
                Reconcile();
            return;
        }
        if (gantry.BodyPoint is { } body && Api.World.BlockAccessor.GetChunkAtBlockPos(body.AsBlockPos) != null
            && ++_dockMisses >= DockMissesAllowed)
            Lost(gantry);
    }

    private void Lost(BEEidolonGantry gantry)
    {
        Api.Logger.Notification("[seraphhorizons] Eidolon gantry at {0}: the docked eidolon {1} is gone; the spine is free", gantry.Pos, _dockedId);
        Unhold(gantry);
    }

    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (Api?.Side == EnumAppSide.Server)
            ReleaseDocked();
    }

    // ---- Drawing, drops, help ----

    /// <summary>Whether a body stage's meshes are drawn: never while an eidolon is docked (it is drawn
    /// itself, hung on the spine).</summary>
    public bool Shows(string requires) => !HoldsEidolon && BodyBill.Order.Contains(requires) && _parts.StageComplete(requires);

    /// <summary>The hung body's textures over the shape's (by texture code): each complete layer's in its
    /// material (<see cref="EidolonBuild.Textures"/>).</summary>
    public IReadOnlyDictionary<string, string> Textures => _parts.Build.Textures();

    /// <summary>Everything fitted; with an eidolon docked, only the pieces of a layer part fitted (the
    /// rest is the eidolon's, let go whole).</summary>
    public IEnumerable<ItemStack> Drops(IWorldAccessor world)
    {
        foreach (var drop in HoldsEidolon ? _parts.Loose(DockedBuild) : _parts.Returns())
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
                ActionLangCode = EidolonGantrySystem.Domain + (HoldsEidolon ? ":blockhelp-eidolongantry-wakedocked" : ":blockhelp-eidolongantry-wake"),
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
        if (HoldsEidolon)
        {
            dsc.AppendLine(L("info-docked"));
            if (_parts.Next is { } missing)
                dsc.AppendLine(L("info-docked-missing", StageName(missing)));
            else
            {
                dsc.AppendLine(L("info-docked-wake"));
                return;
            }
        }
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
        // (a docked eidolon's mind is in already)
        var after = BodyBill.Order.SkipWhile(s => s != next).Skip(1).Where(s => !_parts.StageComplete(s)).ToList();
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
        if (_dockedId != 0)
            tree.SetLong(DockedKey, _dockedId);
        else
            tree.RemoveAttribute(DockedKey);
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
        _dockedId = tree.GetLong(DockedKey);
        _reconciled = false;
        // a live block entity given an old body's state (a schematic, a command): its spare goes back too
        if (Api?.Side == EnumAppSide.Server && _parts.Spare.Count > 0 && !HoldsEidolon)
            Blockentity.RegisterDelayedCallback(_ => GiveBackSpare(), 500);
    }
}
