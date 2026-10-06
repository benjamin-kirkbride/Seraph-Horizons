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
/// sneaking, which is Carry On's) pins a game rope, with no rope item, between the player's hand
/// and the trunk, made as the game's rope item makes one (<c>ItemRope</c>) and tied to the trunk
/// through its <c>ropetieable</c> behaviour, so the game's own pull drags it: harder the lighter it
/// is. The grab holds while the player keeps the right button down with an empty hand and stays
/// within <see cref="TrunkEntityConfig.GrabRange"/> of the trunk; letting go, wandering off, dying,
/// leaving the game or the trunk going ends it, and the rope goes with it. One grab per player and
/// one per trunk. A rope tied the ordinary way, with a rope item, is the game's and untouched.
/// </summary>
public sealed class TrunkGrab : IDisposable
{
    /// <summary>How often grabs are checked, milliseconds.</summary>
    public const int CheckMs = 100;

    /// <summary>The rope's shortest length (the game's minimum).</summary>
    public const double MinRope = 1.5;

    private sealed record Hold(IServerPlayer Player, EntityTrunk Trunk, int ClothId);

    private readonly ICoreServerAPI _api;
    private readonly TrunkEntitySystem _system;
    private readonly Dictionary<string, Hold> _byPlayer = [];
    private readonly long _listener;

    public TrunkGrab(ICoreServerAPI api, TrunkEntitySystem system)
    {
        _api = api;
        _system = system;
        _listener = api.Event.RegisterGameTickListener(_ => Check(), CheckMs);
        api.Event.PlayerDisconnect += OnLeave;
        api.Event.PlayerDeath += OnDeath;
    }

    /// <summary>Whether a click is a grab: an empty hand, not sneaking (Carry On's), and the trunk
    /// has no rope of the game's own tied to it (an empty hand takes that rope, as the game does).</summary>
    public static bool Wants(EntityAgent byEntity, ItemSlot? slot, EntityTrunk trunk) =>
        byEntity is EntityPlayer && (slot == null || slot.Empty)
        && !byEntity.Controls.ShiftKey && !byEntity.Controls.Sneak
        && !HasOtherRope(trunk);

    private static bool HasOtherRope(EntityTrunk trunk)
    {
        var ids = trunk.GetBehavior<EntityBehaviorRopeTieable>()?.ClothIds?.value;
        if (ids == null || ids.Length == 0)
            return false;
        int grab = trunk.WatchedAttributes.GetInt(EntityTrunk.GrabClothKey);
        return ids.Any(id => id != grab);
    }

    /// <summary>Whether this session's grabs hold <paramref name="trunk"/>.</summary>
    public bool Holds(EntityTrunk trunk) => _byPlayer.Values.Any(h => h.Trunk.EntityId == trunk.EntityId);

    /// <summary>The trunk <paramref name="player"/> holds, or null.</summary>
    public EntityTrunk? HeldBy(IPlayer player) => _byPlayer.TryGetValue(player.PlayerUID, out var hold) ? hold.Trunk : null;

    /// <summary>Starts <paramref name="player"/>'s grab on <paramref name="trunk"/>; false, with an
    /// in-game error, when it may not.</summary>
    public bool TryStart(IServerPlayer player, EntityTrunk trunk)
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
        var tieable = trunk.GetBehavior<EntityBehaviorRopeTieable>();
        var cloth = _api.ModLoader.GetModSystem<ClothManager>();
        if (tieable == null || cloth == null)
            return false;

        // A rope from the hand towards the trunk's middle, a block shorter than the reach so it
        // pulls before the grab is lost: the game pulls only once the rope is stretched.
        var middle = trunk.Pos.XYZ.Add(0, 0.5, 0);
        double length = Math.Clamp(hand.DistanceTo(middle), MinRope, Math.Max(MinRope, _system.Config.GrabRange - 1));
        var towards = middle.SubCopy(hand);
        double span = towards.Length();
        var end = span < 1e-3 ? hand.AddCopy(0, 0, length) : hand.AddCopy(towards.X / span * length, towards.Y / span * length, towards.Z / span * length);
        var sys = ClothSystem.CreateRope(_api, cloth, hand, end, null);
        sys.FirstPoint.PinTo(agent, HandOffset(agent));
        cloth.RegisterCloth(sys);
        tieable.Attach(sys, sys.LastPoint);
        sys.WalkPoints(p => p.update(0, _api.World));
        sys.setRenderCenterPos();

        _byPlayer[player.PlayerUID] = new Hold(player, trunk, sys.ClothId);
        trunk.WatchedAttributes.SetLong(EntityTrunk.GrabbedByKey, agent.EntityId);
        trunk.WatchedAttributes.SetInt(EntityTrunk.GrabClothKey, sys.ClothId);
        return true;
    }

    private static bool Refuse(IServerPlayer player, string reason)
    {
        player.SendIngameError("trunkentities-" + reason, Lang.Get("seraphhorizons:trunkentities-error-" + reason));
        return false;
    }

    /// <summary>Ends <paramref name="playerUid"/>'s grab, if any: the rope is untied and removed.</summary>
    public void Release(string playerUid)
    {
        if (!_byPlayer.Remove(playerUid, out var hold))
            return;
        var cloth = _api.ModLoader.GetModSystem<ClothManager>();
        var sys = cloth?.GetClothSystem(hold.ClothId);
        var tieable = hold.Trunk.GetBehavior<EntityBehaviorRopeTieable>();
        if (sys != null)
        {
            tieable?.Detach(sys);
            sys.WalkPoints(p =>
            {
                if (p.Pinned)
                    p.UnPin();
            });
            cloth!.UnregisterCloth(sys.ClothId);
        }
        else
            tieable?.ClothIds?.RemoveInt(hold.ClothId);
        ClearMarks(hold.Trunk);
    }

    /// <summary>Clears a grab the trunk was saved with (the session that made it is gone): its
    /// rope, if the game still has it, and the trunk's marks.</summary>
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

    private void Check()
    {
        if (_byPlayer.Count == 0)
            return;
        var cloth = _api.ModLoader.GetModSystem<ClothManager>();
        foreach (var (uid, hold) in _byPlayer.ToList())
        {
            var agent = hold.Player.Entity;
            bool keep = hold.Player.ConnectionState == EnumClientState.Playing
                        && agent is { Alive: true }
                        && agent.ServerControls.RightMouseDown
                        && hold.Player.InventoryManager.ActiveHotbarSlot is not { Empty: false }
                        && hold.Trunk.Alive
                        && cloth?.GetClothSystem(hold.ClothId) != null
                        && hold.Trunk.DistanceTo(HandPoint(agent)) <= _system.Config.GrabRange;
            if (!keep)
                Release(uid);
        }
    }

    private void OnLeave(IServerPlayer player) => Release(player.PlayerUID);

    private void OnDeath(IServerPlayer player, DamageSource? source) => Release(player.PlayerUID);

    // Where the rope's hand end is, as the game's rope item pins it.
    private static Vec3f HandOffset(EntityAgent agent) =>
        new Vec3d(0, agent.LocalEyePos.Y - 0.3, 0).AheadCopy(0.1, agent.Pos.Pitch, agent.Pos.Yaw)
            .AheadCopy(0.4, agent.Pos.Pitch, agent.Pos.Yaw - MathF.PI / 2).ToVec3f();

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
