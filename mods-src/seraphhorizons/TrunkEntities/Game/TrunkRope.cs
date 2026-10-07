using System.Collections;
using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.TrunkEntities.Core;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.TrunkEntities;

/// <summary>
/// A real <c>game:rope</c> tied to a trunk acts at one of its ends. The game pins
/// the rope's point to the entity with an offset that turns with the entity's yaw from the yaw it
/// was tied at (<c>ClothPoint.pinnedToOffset</c>, <c>pinnedToOffsetStartYaw</c>); every tick this
/// moves that offset to the end nearer the rope's far point, with the start yaw set to the
/// trunk's yaw so the game's turn of it is none. On the server, while the rope's pull moves the
/// trunk, the trunk also turns so that end leads, at the drive's turn rate
/// (<see cref="TrunkDrive.Turn"/>). The rope's own pull on the motion is the game's, by the
/// trunk's <c>Properties.Weight</c> (lighter afloat, <see cref="EntityTrunk.LandWeight"/>).
/// </summary>
public static class TrunkRope
{
    // Not public in the game: the cloth's points and a point's tie yaw. Found by name; if either is
    // gone ropes keep the game's own pin (where they were tied) and do not turn the trunk.
    private static readonly MemberInfo? PointsMember =
        (MemberInfo?)AccessTools.Field(typeof(ClothSystem), "Points2d") ?? AccessTools.Property(typeof(ClothSystem), "Points2d");
    private static readonly FieldInfo? PinnedToField = AccessTools.Field(typeof(ClothPoint), "pinnedTo");
    private static readonly FieldInfo? StartYawField = AccessTools.Field(typeof(ClothPoint), "pinnedToOffsetStartYaw");
    private static bool _warned;

    public static void Tick(EntityTrunk trunk, float dt)
    {
        if (PointsMember == null || PinnedToField == null || StartYawField == null)
        {
            if (!_warned)
                trunk.Api.Logger.Warning("[seraphhorizons] Trunk entities: the game's ClothSystem.Points2d, ClothPoint.pinnedTo or ClothPoint.pinnedToOffsetStartYaw is gone, so ropes pull trunks where they were tied and do not turn them");
            _warned = true;
            return;
        }
        var ids = trunk.GetBehavior<EntityBehaviorRopeTieable>()?.ClothIds?.value;
        if (ids == null || ids.Length == 0)
            return;
        var manager = trunk.Api.ModLoader.GetModSystem<ClothManager>();
        if (manager == null)
            return;
        int legacy = trunk.WatchedAttributes.GetInt(EntityTrunk.GrabClothKey);
        foreach (int id in ids)
        {
            if (id == legacy || manager.GetClothSystem(id) is not { } cloth)
                continue;
            Pin(trunk, cloth, dt);
        }
    }

    private static void Pin(EntityTrunk trunk, ClothSystem cloth, float dt)
    {
        var rows = (PointsMember is FieldInfo f ? f.GetValue(cloth) : ((PropertyInfo)PointsMember!).GetValue(cloth)) as IEnumerable;
        if (rows == null)
            return;
        var points = new List<ClothPoint>();
        foreach (var row in rows)
            if (row is IEnumerable ps)
                points.AddRange(ps.OfType<ClothPoint>());
        ClothPoint? own = points.FirstOrDefault(p => PinnedToField!.GetValue(p) == trunk), far = null;
        double farDist = -1;
        if (own == null)
            return;
        foreach (var p in points)
        {
            double d = p.Pos.SquareDistanceTo(own.Pos);
            if (d > farDist)
                (far, farDist) = (p, d);
        }
        if (far == null || far == own)
            return;

        var pos = trunk.Pos;
        double length = TrunkBox.Size(trunk.TypeClass).Length;
        int end = TrunkPull.NearerEnd(pos.X, pos.Z, pos.Yaw, far.Pos.X, far.Pos.Z);

        if (trunk.Api.Side == EnumAppSide.Server)
        {
            double mx = pos.Motion.X, mz = pos.Motion.Z;
            if (mx * mx + mz * mz >= TrunkStep.MinMotion * TrunkStep.MinMotion)
            {
                var (fx, fz) = TrunkPull.EndPos(pos.X, pos.Z, pos.Yaw, length, -end);
                double target = TrunkPull.YawFacing(far.Pos.X - fx, far.Pos.Z - fz, end);
                // Once moving, at the drive's turn rate for the trunk's logs.
                double step = TrunkDrive.Turn(trunk.Logs, trunk.Afloat) * dt;
                pos.Yaw = (float)TrunkPull.StepYaw(pos.Yaw, target, step);
            }
        }

        var (ex, ez) = TrunkPull.EndPos(pos.X, pos.Z, pos.Yaw, length, end);
        float y = TrunkBox.Size(trunk.TypeClass).Height / 2f;
        own.pinnedToOffset = new Vec3f((float)(ex - pos.X), y, (float)(ez - pos.Z));
        StartYawField!.SetValue(own, pos.Yaw);
    }
}
