using System.Text;
using SeraphHorizons.Mod.Eidolon.Core;
using SeraphHorizons.Mod.EidolonGantry;
using SeraphHorizons.Mod.EidolonGantry.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// The eidolon docked on a gantry's spine (Eidolon/README.md, "Docking"): the same entity, its owner,
/// charge, oil and the order it docked from kept, held hung where the body hangs. Its dock order
/// (<see cref="DockOrder"/>) walks it to the gantry's body point and calls <see cref="Begin"/>: it faces
/// out of the open front and plays <c>dock</c> (<see cref="DockPhase.Docking"/>); on the clamp frame
/// (<see cref="EidolonDocking.ClampSeconds"/>) the gantry takes hold (<see cref="BEBehaviorEidolonBody.TakeHold"/>)
/// and it is <see cref="DockPhase.Docked"/>, then <c>hung</c> holds the pose. While docking or docked it does
/// not work or walk (<see cref="EntityLaborEidolon.CanWork"/>), its upkeeps do not run down, and it is put
/// back on its point every tick, so nothing pushes it off.
///
/// <para>Kept in its watched attributes under <see cref="DockKey"/> (the gantry's position, the point,
/// the yaw, the phase and the order to give back), so it is still docked after a save or with its
/// gantry's chunk unloaded; the gantry keeps its entity id. Each checks the other: a docked eidolon whose
/// gantry is gone, or holds someone else, lets go (<see cref="Release"/>), and the gantry frees its
/// spine of an eidolon that is gone.</para>
///
/// <para>Docked, Ctrl+right-click strips its outermost layer (<see cref="BEBehaviorEidolonBody.Strip"/>),
/// and a right-click with a layer's item fits it as on the gantry. On the client, the elements of every
/// layer it does not wear are left out of its shape (<see cref="EidolonLayerElements"/>), docked or not.</para>
/// </summary>
public class EntityBehaviorEidolonDock(Entity entity) : EntityBehavior(entity)
{
    public const string Code = "seraphhorizons.eidolonDock";
    public const string DockKey = "seraphhorizons:dock";
    public static readonly AssetLocation StagesAsset = new("seraphhorizons", "config/eidolon-stages.json");

    private const double CheckSeconds = 2;

    private double _startedAt = double.NaN;
    private double _checkAt;
    private bool _hungStarted;

    public override string PropertyName() => Code;

    private EntityLaborEidolon Eidolon => (EntityLaborEidolon)entity;

    private ITreeAttribute? Tree => entity.WatchedAttributes.GetTreeAttribute(DockKey);

    /// <summary>Where it is in docking (synced).</summary>
    public DockPhase Phase => Tree?.GetString("phase") switch
    {
        "docking" => DockPhase.Docking,
        "docked" => DockPhase.Docked,
        _ => DockPhase.None,
    };

    /// <summary>Docking or docked: it neither works nor walks.</summary>
    public bool Holds => Phase != DockPhase.None;

    /// <summary>The gantry it docks on (its controller), or null.</summary>
    public BlockPos? GantryPos => Tree is { } t ? new BlockPos(t.GetInt("gx"), t.GetInt("gy"), t.GetInt("gz")) : null;

    /// <summary>Whether it is docked (clamped) on the gantry at <paramref name="gantry"/>.</summary>
    public bool HeldAt(BlockPos gantry) => Phase == DockPhase.Docked && gantry.Equals(GantryPos);

    private Vec3d? Point => Tree is { } t ? new Vec3d(t.GetDouble("px"), t.GetDouble("py"), t.GetDouble("pz")) : null;

    private BEEidolonGantry? Gantry => GantryPos is { } pos ? BEEidolonGantry.At(entity.World.BlockAccessor, pos) : null;

    private BEBehaviorEidolonBody? Body => Gantry?.GetBehavior<BEBehaviorEidolonBody>();

    // ---- Docking (server side) ----

    /// <summary>Backs it onto <paramref name="gantry"/>'s spine (server side; its dock order, arrived at the
    /// body point): set on the point facing out, its work stopped, <c>dock</c> playing. The gantry takes
    /// hold on the clamp frame. <paramref name="then"/>: the order to give back when the crank wakes it.
    /// False when the spine is not free for it.</summary>
    public bool Begin(BEEidolonGantry gantry, ITreeAttribute? then)
    {
        if (entity.Api.Side != EnumAppSide.Server || gantry.BodyPoint is not { } point
            || gantry.GetBehavior<BEBehaviorEidolonBody>() is not { } body || !body.SpineFreeFor(entity))
            return false;
        var tree = new TreeAttribute();
        tree.SetInt("gx", gantry.Pos.X);
        tree.SetInt("gy", gantry.Pos.Y);
        tree.SetInt("gz", gantry.Pos.Z);
        tree.SetDouble("px", point.X);
        tree.SetDouble("py", point.Y);
        tree.SetDouble("pz", point.Z);
        tree.SetFloat("yaw", gantry.OutwardYaw);
        tree.SetString("phase", "docking");
        if (then != null)
            tree["then"] = then.Clone();
        entity.WatchedAttributes[DockKey] = tree;
        entity.WatchedAttributes.MarkPathDirty(DockKey);
        // its tasks stop at its next check (it can no longer work); not here, inside its order's own tick
        Eidolon.TaskAi?.PathTraverser?.Stop();
        Pin();
        _startedAt = Now;
        _hungStarted = false;
        entity.AnimManager.StopAnimation("idle");
        entity.AnimManager.StartAnimation("dock");
        return true;
    }

    /// <summary>Wakes it on the crank (server side): the gantry lets go, it plays <c>activate</c> and walks
    /// to <paramref name="walkTo"/>, out of the open front, then takes up the order it docked from.</summary>
    public void Wake(Vec3d? walkTo)
    {
        var then = Tree?.GetTreeAttribute("then");
        Clear();
        Eidolon.Activate();
        if (walkTo != null)
            Eidolon.Orders?.SetOrder(GoToOrder.OrderCode, GoToOrder.Args(walkTo, run: false, then: then));
        else if (then?.GetString("code") is { } code)
            Eidolon.Orders?.SetOrder(code, then);
    }

    /// <summary>Lets it go (server side; its gantry broken or gone): it drops onto its feet in the layers
    /// it wears, with no order.</summary>
    public void Release()
    {
        if (!Holds)
            return;
        bool hung = Phase == DockPhase.Docked;
        Clear();
        entity.AnimManager.StopAnimation("dock");
        if (hung)
            Eidolon.Activate();
        entity.Api.Logger.Notification("[seraphhorizons] Eidolon {0}: let go by its gantry", entity.EntityId);
    }

    private void Clear()
    {
        entity.WatchedAttributes.RemoveAttribute(DockKey);
        _startedAt = double.NaN;
        _hungStarted = false;
        entity.AnimManager.StopAnimation("hung");
    }

    private double Now => entity.World.ElapsedMilliseconds / 1000.0;

    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server || !Holds)
            return;
        Pin();
        double now = Now;
        // loaded part way through docking: on at the clamp at once
        if (double.IsNaN(_startedAt))
            _startedAt = Phase == DockPhase.Docking ? now - EidolonDocking.ClampSeconds : now - EidolonDocking.DockSeconds;
        double since = now - _startedAt;
        if (Phase == DockPhase.Docking && since >= EidolonDocking.ClampSeconds)
            Clamp();
        if (Phase == DockPhase.Docked)
        {
            if (since >= EidolonDocking.DockSeconds && !_hungStarted)
            {
                _hungStarted = true;
                entity.AnimManager.StartAnimation("hung");
                entity.AnimManager.StopAnimation("dock");
            }
            if (now >= _checkAt)
            {
                _checkAt = now + CheckSeconds;
                CheckGantry();
            }
        }
    }

    // The clamps close: the gantry takes hold, or, when it is gone or taken, it gives up and stands.
    private void Clamp()
    {
        var then = Tree?.GetTreeAttribute("then");
        if (Body is { } body && body.TakeHold(Eidolon))
        {
            Tree!.SetString("phase", "docked");
            entity.WatchedAttributes.MarkPathDirty(DockKey);
            return;
        }
        Clear();
        entity.AnimManager.StopAnimation("dock");
        if (then?.GetString("code") is { } code)
            Eidolon.Orders?.SetOrder(code, then);
    }

    // Docked with its gantry's chunk in: let go if the gantry is gone or no longer holds it.
    private void CheckGantry()
    {
        if (GantryPos is not { } pos || entity.World.BlockAccessor.GetChunkAtBlockPos(pos) == null)
            return;
        if (Body is not { } body || !body.Holds(entity))
            Release();
    }

    // Held where it hangs, facing out: nothing walks or pushes it off.
    private void Pin()
    {
        if (Point is not { } point || Tree is not { } tree)
            return;
        float yaw = tree.GetFloat("yaw");
        if (entity.Pos.SquareDistanceTo(point) > 1e-6)
            entity.Pos.SetPos(point);
        entity.Pos.Motion.Set(0, 0, 0);
        entity.Pos.Yaw = yaw;
        if (entity is EntityAgent agent)
        {
            agent.BodyYaw = yaw;
            agent.Controls.StopAllMovement();
            agent.ServerControls.StopAllMovement();
        }
    }

    // ---- Stripping and fitting by hand ----

    public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
    {
        if (mode != EnumInteractMode.Interact || Phase != DockPhase.Docked || byEntity is not EntityPlayer { Player: { } player })
            return;
        var controls = byEntity.Controls;
        bool strip = controls.CtrlKey && !controls.ShiftKey;
        string? code = itemslot.Itemstack?.Collectible?.Code?.ToString();
        // a repair item on a damaged eidolon is the repair's (EntityBehaviorEidolonRepair)
        bool repair = EntityBehaviorEidolonRepair.Repairs(itemslot.Itemstack)
                      && entity.GetBehavior<EntityBehaviorHealth>() is { } h && h.Health < h.MaxHealth;
        bool fits = !strip && !repair && code != null && Body?.Parts is { } parts
                    && parts.CanFit(code, int.MaxValue, out _, out _) is BodyFitVerdict.Fits or BodyFitVerdict.WrongMaterial;
        if (!strip && !fits)
            return;
        handled = EnumHandling.PreventSubsequent;
        if (entity.Api.Side != EnumAppSide.Server || player is not IServerPlayer sp)
            return;
        if (Gantry is not { } gantry || Body is not { } body)
        {
            Error(sp, "eidolon-dock-nogantry");
            return;
        }
        if (fits)
        {
            body.OnGantryInteract(gantry, player, itemslot);
            return;
        }
        if (!Eidolon.RefuseUnlessCommander(player))
            return;
        switch (body.Strip(Eidolon, player, out var layer))
        {
            case StripVerdict.Damaged:
                Error(sp, "eidolon-strip-damaged");
                break;
            case StripVerdict.Bare:
                Error(sp, "eidolon-strip-bare");
                break;
            case StripVerdict.NotDocked:
                Error(sp, "eidolon-strip-notdocked");
                break;
            case StripVerdict.Strips when layer is { } off:
                sp.SendMessage(GlobalConstants.GeneralChatGroup, Lang.GetL(sp.LanguageCode, "seraphhorizons:eidolon-stripped",
                    Lang.GetL(sp.LanguageCode, EidolonFittingText.LayerKey(off))), EnumChatType.Notification);
                break;
        }
    }

    private static void Error(IServerPlayer player, string key) =>
        player.SendIngameError("seraphhorizons-" + key, Lang.GetL(player.LanguageCode, "seraphhorizons:" + key));

    public override WorldInteraction[]? GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
    {
        if (Phase != DockPhase.Docked)
            return null;
        var help = new List<WorldInteraction>();
        if (EntityLaborEidolon.ReadBuild(entity.WatchedAttributes).NextToStrip is { } outer)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Lang.Get("seraphhorizons:eidolon-help-strip", Lang.Get(EidolonFittingText.LayerKey(outer))),
                MouseButton = EnumMouseButton.Right,
                HotKeyCode = "ctrl",
            });
        if (Body?.Parts.Next is { } next && BodyBill.LayerOf(next) != null)
            help.Add(new WorldInteraction
            {
                ActionLangCode = Lang.Get("seraphhorizons:eidolon-help-fitlayer", BEBehaviorEidolonBody.StageName(next)),
                MouseButton = EnumMouseButton.Right,
            });
        return help.ToArray();
    }

    public override void GetInfoText(StringBuilder infotext)
    {
        switch (Phase)
        {
            case DockPhase.Docking:
                infotext.AppendLine(Lang.Get("seraphhorizons:eidolon-info-docking"));
                break;
            case DockPhase.Docked:
                infotext.AppendLine(Lang.Get("seraphhorizons:eidolon-info-docked"));
                break;
        }
    }

    // ---- Its look (client side) ----

    private static EidolonLayerElements? _elements;
    private static readonly object ElementsLock = new();

    /// <summary>The layers' elements from the stages file (read once).</summary>
    public static EidolonLayerElements? Elements(ICoreAPI api)
    {
        lock (ElementsLock)
        {
            if (_elements == null && api.Assets.TryGet(StagesAsset) is { } asset)
            {
                try
                {
                    _elements = EidolonLayerElements.Parse(asset.ToText());
                }
                catch (Exception e)
                {
                    api.Logger.Error("[seraphhorizons] Eidolon: {0} cannot be read, so stripped layers are drawn: {1}", StagesAsset, e.Message);
                }
            }
            return _elements;
        }
    }

    // A layer that is off is not drawn: its elements are left out of the shape it is tessellated from.
    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
    {
        if (entity.Api.Side != EnumAppSide.Client || Elements(entity.Api) is not { } elements)
            return;
        var hidden = elements.Hidden(EntityLaborEidolon.ReadBuild(entity.WatchedAttributes));
        if (hidden.Count > 0)
            willDeleteElements = [.. willDeleteElements ?? [], .. hidden];
    }
}
