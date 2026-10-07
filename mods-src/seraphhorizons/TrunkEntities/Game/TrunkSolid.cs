using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// A trunk as good as solid to whoever walks into it: an agent whose collision box overlaps any of
/// the trunk's turned boxes is moved out the shortest way (<see cref="TrunkPush"/>), at most
/// <see cref="TrunkPush.MaxStep"/> a step, and its motion into the trunk is stopped; one whose
/// feet are near the top is lifted onto it and stands there. The trunk itself is never moved by
/// this, and its driver (mounted, standing just beyond its end) is left alone. The game's
/// <c>repulseagents</c> shoves only by the trunk's square middle hitbox, at most 0.1 blocks per
/// 1/60 s of motion; this acts on every box, by position.
/// <para>Server side every game tick for every agent near a trunk (<see cref="EntityTrunk"/>'s
/// tick), and on the client every frame for the local player (<see cref="ClientRenderer"/>, right
/// after the game's player physics), since a player's position is their client's to say: the
/// server's push alone would be overwritten by the client's next report.</para>
/// </summary>
public static class TrunkSolid
{
    /// <summary>Moves <paramref name="agent"/> out of <paramref name="trunk"/>'s boxes by at most
    /// <paramref name="maxStep"/>; true when it was in them.</summary>
    public static bool PushOut(EntityTrunk trunk, Entity agent, double maxStep = TrunkPush.MaxStep)
    {
        if (agent is not EntityAgent { Alive: true } a || a.MountedOn != null || agent == trunk
            || agent.Pos.Dimension != trunk.Pos.Dimension || a.CollisionBox is not { } cb)
            return false;
        var tp = trunk.Pos;
        var ap = agent.Pos;
        double ox = ap.X - tp.X, oy = ap.Y - tp.Y, oz = ap.Z - tp.Z;
        double reach = TrunkBoxes.Radius(trunk.TypeClass) + 2;
        if (ox * ox + oz * oz > reach * reach || oy > 4 || oy < -4)
            return false;
        var box = new Box((float)(ox + cb.X1), (float)(oy + cb.Y1), (float)(oz + cb.Z1),
                          (float)(ox + cb.X2), (float)(oy + cb.Y2), (float)(oz + cb.Z2));
        if (TrunkPush.Out(TrunkBoxes.Turned(trunk.TypeClass, tp.Yaw), box) is not { } exit)
            return false;
        // Onto the top: the whole way, so a player standing there stays put.
        var (dx, dy, dz) = exit.Step(exit.Up ? 0 : maxStep);
        ap.X += dx;
        ap.Y += dy;
        ap.Z += dz;
        var (mx, my, mz) = TrunkPush.Stop(exit, ap.Motion.X, ap.Motion.Y, ap.Motion.Z);
        ap.Motion.Set(mx, my, mz);
        if (exit.Up)
            agent.OnGround = true;
        return true;
    }

    /// <summary>Pushes every agent near <paramref name="trunk"/> out of it. Server side.</summary>
    public static void PushAll(EntityTrunk trunk)
    {
        float reach = TrunkBoxes.Radius(trunk.TypeClass) + 2;
        foreach (var e in trunk.World.GetEntitiesAround(trunk.Pos.XYZ, reach, 4, e => e is EntityAgent && e != trunk))
            PushOut(trunk, e);
    }

    /// <summary>The local player's push, every frame right after the game's player physics
    /// (<c>EntityBehaviorPlayerPhysics</c> renders at order 1 in the <c>Before</c> stage).</summary>
    public sealed class ClientRenderer : IRenderer
    {
        private readonly ICoreClientAPI _capi;

        public ClientRenderer(ICoreClientAPI capi)
        {
            _capi = capi;
            capi.Event.RegisterRenderer(this, EnumRenderStage.Before, "seraphhorizons-trunksolid");
        }

        public double RenderOrder => 1.05;

        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
        {
            if (_capi.IsGamePaused || _capi.World.Player?.Entity is not { MountedOn: null } me)
                return;
            foreach (var e in _capi.World.GetEntitiesAround(me.Pos.XYZ, 6, 4, e => e is EntityTrunk { Alive: true }))
                PushOut((EntityTrunk)e, me);
        }

        public void Dispose() => _capi.Event.UnregisterRenderer(this, EnumRenderStage.Before);
    }
}
