using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// The rope-less grab, server side: a right-click with an empty hand on a trunk entity (not
/// sneaking, which is Carry On's) takes the trunk by its end nearer the click, and while the
/// player keeps the right button down with an empty hand within
/// <see cref="TrunkEntityConfig.GrabRange"/>, every tick the trunk turns its grabbed end towards
/// the hand and is pushed after it (<see cref="TrunkPull"/>), harder the lighter it is, through its
/// own physics so it still collides. Nothing is drawn and no rope exists: no cloth system, no
/// <c>ropetieable</c>. Letting go, wandering off, dying, leaving the game or the trunk going ends
/// it. One grab per player and one per trunk. A trunk with a real rope tied to it is not grabbed.
/// </summary>
public sealed class TrunkGrab : IDisposable
{
    /// <summary>How often grabs are checked and pulled, milliseconds (about every server tick).</summary>
    public const int TickMs = 20;

    /// <summary>How much of the gap to the pull's motion is closed each tick (0..1).</summary>
    public const double Blend = 0.5;

    private sealed record Hold(IServerPlayer Player, EntityTrunk Trunk, int End);

    private readonly ICoreServerAPI _api;
    private readonly TrunkEntitySystem _system;
    private readonly Dictionary<string, Hold> _byPlayer = [];
    private readonly long _listener;

    public TrunkGrab(ICoreServerAPI api, TrunkEntitySystem system)
    {
        _api = api;
        _system = system;
        _listener = api.Event.RegisterGameTickListener(Tick, TickMs);
        api.Event.PlayerDisconnect += OnLeave;
        api.Event.PlayerDeath += OnDeath;
    }

    /// <summary>Whether a click is a grab: an empty hand, not sneaking (Carry On's), and the trunk
    /// has no rope of the game's own tied to it (an empty hand takes that rope, as the game does).
    /// Side-independent, so the client also keeps a grab click from <c>ropetieable</c>.</summary>
    public static bool Wants(EntityAgent byEntity, ItemSlot? slot, EntityTrunk trunk) =>
        byEntity is EntityPlayer && (slot == null || slot.Empty)
        && !byEntity.Controls.ShiftKey && !byEntity.Controls.Sneak
        && !HasOtherRope(trunk);

    private static bool HasOtherRope(EntityTrunk trunk)
    {
        var ids = trunk.GetBehavior<EntityBehaviorRopeTieable>()?.ClothIds?.value;
        if (ids == null || ids.Length == 0)
            return false;
        // An old save's grab rope (GrabClothKey) is not a real one; ClearStale removes it.
        int legacy = trunk.WatchedAttributes.GetInt(EntityTrunk.GrabClothKey);
        return ids.Any(id => id != legacy);
    }

    /// <summary>Whether this session's grabs hold <paramref name="trunk"/>.</summary>
    public bool Holds(EntityTrunk trunk) => _byPlayer.Values.Any(h => h.Trunk.EntityId == trunk.EntityId);

    /// <summary>The trunk <paramref name="player"/> holds, or null.</summary>
    public EntityTrunk? HeldBy(IPlayer player) => _byPlayer.TryGetValue(player.PlayerUID, out var hold) ? hold.Trunk : null;

    /// <summary>Starts <paramref name="player"/>'s grab on <paramref name="trunk"/> by the end
    /// nearer <paramref name="hit"/> (the clicked point, or the player when null); false, with an
    /// in-game error, when it may not. Calling it again for the trunk already held changes
    /// nothing, so the game's repeated interact while the button is held is harmless.</summary>
    public bool TryStart(IServerPlayer player, EntityTrunk trunk, Vec3d? hit = null)
    {
        var agent = player.Entity;
        if (agent == null || !trunk.Alive)
            return false;
        if (_byPlayer.TryGetValue(player.PlayerUID, out var old))
        {
            if (old.Trunk.EntityId == trunk.EntityId)
                return true;
            Release(player.PlayerUID);
        }
        if (Holds(trunk))
            return Refuse(player, "grabbed");
        if (trunk.Grabbed)
            ClearStale(trunk);
        if (!TrunkWeight.Grabbable(trunk.Properties.Weight, _system.Config))
            return Refuse(player, "too-heavy");
        var hand = HandPoint(agent);
        if (trunk.DistanceTo(hand) > _system.Config.GrabRange)
            return Refuse(player, "too-far");

        // The hit is a world position; should a caller hand over one relative to the trunk, it
        // is far from the trunk, so it is taken as relative.
        var at = hit ?? agent.Pos.XYZ;
        if (hit != null && hit.SquareDistanceTo(trunk.Pos.XYZ) > 64)
            at = trunk.Pos.XYZ.Add(hit);
        int end = TrunkPull.NearerEnd(trunk.Pos.X, trunk.Pos.Z, trunk.Pos.Yaw, at.X, at.Z);

        _byPlayer[player.PlayerUID] = new Hold(player, trunk, end);
        trunk.WatchedAttributes.SetLong(EntityTrunk.GrabbedByKey, agent.EntityId);
        return true;
    }

    private static bool Refuse(IServerPlayer player, string reason)
    {
        player.SendIngameError("trunkentities-" + reason, Lang.Get("seraphhorizons:trunkentities-error-" + reason));
        return false;
    }

    /// <summary>Ends <paramref name="playerUid"/>'s grab, if any.</summary>
    public void Release(string playerUid)
    {
        if (_byPlayer.Remove(playerUid, out var hold))
            ClearMarks(hold.Trunk);
    }

    /// <summary>Clears a grab the trunk was saved with (the session that made it is gone): the
    /// trunk's marks and, from a save made when the grab was a game rope, that rope's cloth id
    /// (and the rope itself, if the game still has it).</summary>
    public static void ClearStale(EntityTrunk trunk)
    {
        int id = trunk.WatchedAttributes.GetInt(EntityTrunk.GrabClothKey);
        var tieable = trunk.GetBehavior<EntityBehaviorRopeTieable>();
        if (id != 0)
        {
            var cloth = trunk.Api.ModLoader.GetModSystem<ClothManager>();
            if (cloth?.GetClothSystem(id) is { } sys)
            {
                tieable?.Detach(sys);
                cloth.UnregisterCloth(id);
            }
            else if (tieable?.ClothIds is { } ids)
            {
                ids.RemoveInt(id);
                if (ids.value.Length == 0)
                    trunk.WatchedAttributes.RemoveAttribute("clothIds");
            }
        }
        ClearMarks(trunk);
    }

    private static void ClearMarks(EntityTrunk trunk)
    {
        trunk.WatchedAttributes.RemoveAttribute(EntityTrunk.GrabbedByKey);
        trunk.WatchedAttributes.RemoveAttribute(EntityTrunk.GrabClothKey);
    }

    private void Tick(float dt)
    {
        if (_byPlayer.Count == 0)
            return;
        foreach (var (uid, hold) in _byPlayer.ToList())
        {
            var agent = hold.Player.Entity;
            bool keep = hold.Player.ConnectionState == EnumClientState.Playing
                        && agent is { Alive: true }
                        && agent.ServerControls.RightMouseDown
                        && hold.Player.InventoryManager.ActiveHotbarSlot is not { Empty: false }
                        && hold.Trunk.Alive
                        && hold.Trunk.DistanceTo(HandPoint(agent)) <= _system.Config.GrabRange;
            if (!keep)
                Release(uid);
            else
                Pull(hold, HandPoint(agent), dt);
        }
    }

    // One tick of the pull: the trunk turns so its grabbed end leads, from the far end towards
    // the hand, then is pushed after the hand.
    private static void Pull(Hold hold, Vec3d hand, float dt)
    {
        var trunk = hold.Trunk;
        var pos = trunk.Pos;
        double length = TrunkBox.Size(trunk.TypeClass).Length;
        float weight = trunk.LandWeight;
        bool afloat = trunk.Afloat;
        var (ex, ez) = TrunkPull.EndPos(pos.X, pos.Z, pos.Yaw, length, hold.End);
        double dist = Math.Sqrt((hand.X - ex) * (hand.X - ex) + (hand.Z - ez) * (hand.Z - ez));
        if (dist <= TrunkPull.Slack)
            return;

        var (fx, fz) = TrunkPull.EndPos(pos.X, pos.Z, pos.Yaw, length, -hold.End);
        double target = TrunkPull.YawFacing(hand.X - fx, hand.Z - fz, hold.End);
        pos.Yaw = (float)TrunkPull.StepYaw(pos.Yaw, target, TrunkPull.TurnStep(dist, weight, dt, afloat));

        (ex, ez) = TrunkPull.EndPos(pos.X, pos.Z, pos.Yaw, length, hold.End);
        double dx = hand.X - ex, dz = hand.Z - ez, d = Math.Sqrt(dx * dx + dz * dz);
        if (d < 1e-6)
            return;
        // Motion is blocks per 1/60 s in the game's physics.
        double speed = TrunkPull.Speed(d, weight, afloat) / 60;
        ApplyPull(trunk, dx / d * speed, dz / d * speed);
    }

    /// <summary>The one place the pull moves a trunk: eases its horizontal motion towards
    /// (<paramref name="mx"/>, <paramref name="mz"/>), blocks per 1/60 s, and leaves the rest to
    /// its <c>passivephysicsmultibox</c>, so it still collides. Stepping up a rise is the trunk's
    /// own (<c>EntityTrunk.StepUp</c>), from the motion left here, so a rope's pull gets it too.</summary>
    private static void ApplyPull(EntityTrunk trunk, double mx, double mz)
    {
        var motion = trunk.Pos.Motion;
        motion.X += (mx - motion.X) * Blend;
        motion.Z += (mz - motion.Z) * Blend;
    }

    private void OnLeave(IServerPlayer player) => Release(player.PlayerUID);

    private void OnDeath(IServerPlayer player, DamageSource? source) => Release(player.PlayerUID);

    // The hand, as the game's rope item pins a rope to it.
    private static Vec3d HandPoint(EntityAgent agent) => agent.Pos.XYZ.Add(0, agent.LocalEyePos.Y - 0.3, 0);

    public void Dispose()
    {
        _api.Event.UnregisterGameTickListener(_listener);
        _api.Event.PlayerDisconnect -= OnLeave;
        _api.Event.PlayerDeath -= OnDeath;
        foreach (var uid in _byPlayer.Keys.ToList())
            Release(uid);
    }
}
