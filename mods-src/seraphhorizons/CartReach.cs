using System.Reflection;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace SeraphHorizons.Mod;

/// <summary>
/// Cartwright's Caravan (and any entity matching <see cref="SeraphHorizonsConfig.CartReachEntities"/>):
/// a cart's rear storage slots can be used from behind the cart in survival.
///
/// <c>GameMain.RayTraceForSelection</c> only tests the entities whose origin is within the picking
/// range (4.5 blocks in survival, 100 in creative) of the eye: it takes them from
/// <c>GetEntitiesAround(eye, range, range)</c>, which keeps an entity by the horizontal distance
/// from the eye to its position. That is the only reach limit on an entity's own hitbox; the
/// <c>selectionboxes</c> behavior keeps a slot box's hit only within the ray's length. A basic
/// cart's origin sits near its front, and its rear slots (<c>RightStorage3AP</c>,
/// <c>LeftStorage3AP</c>) end about 4 blocks behind it, so from behind the cart those slots are in
/// reach while the cart is not a candidate at all.
///
/// A postfix on that method looks again at the matching entities the game left out, out to
/// <see cref="EntityReach.SearchPadding"/> past the range, and takes one only if the point its ray
/// hits is within the picking range and nearer than the block or entity the game picked
/// (<see cref="EntityReach"/>). Client only, on the client's own world: the client picks the entity
/// and slot and sends them, and the server takes the slot from the packet (it never loads an
/// entity's selection boxes) after finding the entity within the picking range + 10 of the player.
///
/// It is a postfix, not a change to the method's body: yttenhancedinteractionfiltering transpiles
/// the same method and requires exactly one <c>GetEntitiesAround</c> call in it, and a postfix
/// leaves the body Harmony hands to transpilers untouched, whichever mod patches first.
/// </summary>
public static class CartReach
{
    public const string HarmonyId = "seraphhorizons.cartreach";

    private static GameMain? _client;
    private static System.Func<Entity, bool>? _applies;
    private static ILogger? _logger;
    private static bool _failed;

    /// <summary>Entity types that match the rule. The tweak patches nothing when there are none.</summary>
    public static List<EntityProperties> MatchingTypes(IWorldAccessor world, EntityReach rule) =>
        world.EntityTypes.Where(t => t.Code != null && rule.Applies(t.Code.ToString())).ToList();

    /// <summary>The method patched: <c>GameMain.RayTraceForSelection(IWorldIntersectionSupplier, Ray, ...)</c>,
    /// which every other overload calls.</summary>
    public static MethodInfo? Target => AccessTools.DeclaredMethod(typeof(GameMain), nameof(GameMain.RayTraceForSelection),
        [typeof(IWorldIntersectionSupplier), typeof(Ray), typeof(BlockSelection).MakeByRefType(),
         typeof(EntitySelection).MakeByRefType(), typeof(BlockFilter), typeof(EntityFilter)]);

    public static HarmonyMethod Postfix => new(typeof(CartReach), nameof(RayTraceForSelectionPostfix));

    /// <summary>Postfixes <see cref="Target"/> for the given client world. Returns whether the patch went in.</summary>
    public static bool Patch(Harmony harmony, ICoreClientAPI api, EntityReach rule)
    {
        var target = Target;
        if (target == null || api.World is not GameMain client)
        {
            api.Logger.Warning("[seraphhorizons] GameMain.RayTraceForSelection(IWorldIntersectionSupplier, Ray, ...) "
                               + "not found; the game changed, so a cart's far slots stay out of reach");
            return false;
        }

        _client = client;
        _applies = AppliesCached(rule);
        _logger = api.Logger;
        _failed = false;
        harmony.Patch(target, postfix: Postfix);
        return true;
    }

    /// <summary>Forgets the client world, so a patch left over from it does nothing.</summary>
    public static void Unbind()
    {
        _client = null;
        _applies = null;
        _logger = null;
    }

    // Parameter names match the game's (Harmony binds by name).
    public static void RayTraceForSelectionPostfix(GameMain __instance, IWorldIntersectionSupplier supplier, Ray ray,
        ref BlockSelection? blockSelection, ref EntitySelection? entitySelection, EntityFilter? efilter)
    {
        if (_failed || _applies == null || !ReferenceEquals(__instance, _client))
            return;
        try
        {
            SelectFarEntity(__instance, supplier, ray, ref blockSelection, ref entitySelection, efilter, _applies);
        }
        catch (Exception e)
        {
            // Once: the selection runs every frame.
            _failed = true;
            _logger?.Error("[seraphhorizons] Cart reach failed and is off until the world is joined again: {0}", e);
        }
    }

    /// <summary>
    /// The second look, after <c>RayTraceForSelection</c> has set <paramref name="blockSelection"/>
    /// and <paramref name="entitySelection"/> (at most one of them). Returns whether it replaced
    /// them with a far entity, which then becomes the entity selection and the block selection is
    /// cleared, as the game does when an entity is nearer than the block. Public for the Atlas
    /// scenarios, which call it on the server's world.
    /// </summary>
    public static bool SelectFarEntity(GameMain game, IWorldIntersectionSupplier supplier, Ray ray,
        ref BlockSelection? blockSelection, ref EntitySelection? entitySelection, EntityFilter? efilter,
        System.Func<Entity, bool> applies)
    {
        Vec3d eye = ray.origin;
        float range = (float)ray.Length;
        float rangeSq = range * range;
        float search = range + EntityReach.SearchPadding;

        // The ones the game left out: past the range by the same test it used, and in the search.
        Entity[] far = supplier.GetEntitiesAround(eye, search, search,
            e => applies(e) && !e.InRangeOf(eye, rangeSq, range) && (efilter == null || efilter(e)));
        if (far.Length == 0)
            return false;

        double best = double.PositiveInfinity;
        if (entitySelection?.Entity != null)
            best = entitySelection.Position.AddCopy(entitySelection.HitPosition).SquareDistanceTo(eye);
        else if (blockSelection != null)
        {
            BlockPos pos = blockSelection.Position;
            best = new Vec3d(pos.X, pos.InternalY, pos.Z).Add(blockSelection.HitPosition).SquareDistanceTo(eye);
        }

        // The game's own tester, already loaded with this ray, as the game uses it: IntersectsRay
        // leaves the world position of the hit in hitPosition. A box hit assigns a new vector, a
        // hitbox hit writes into the one there, and a hit from inside the hitbox sets none, so it
        // starts as NaN each time. The game's vector is put back afterwards.
        AABBIntersectionTest tester = game.interesectionTester;
        Vec3d gameHitPosition = tester.hitPosition;
        EntitySelection? picked = null;
        try
        {
            foreach (Entity entity in far)
            {
                int boxIndex = 0;
                tester.hitPosition = new Vec3d(double.NaN, double.NaN, double.NaN);
                if (!entity.IntersectsRay(ray, tester, out _, ref boxIndex) || double.IsNaN(tester.hitPosition.X))
                    continue;
                Vec3d hit = tester.hitPosition.Clone();
                double distanceSq = hit.SquareDistanceTo(eye);
                if (!EntityReach.Replaces(distanceSq, range, best))
                    continue;
                best = distanceSq;
                // As the game fills its own entity selection.
                EntityPos pos = entity.Pos;
                picked = new EntitySelection
                {
                    Entity = entity,
                    SelectionBoxIndex = boxIndex,
                    Face = tester.hitOnBlockFace,
                    HitPosition = hit.SubCopy(pos.X, pos.Y, pos.Z),
                    Position = pos.XYZ,
                };
            }
        }
        finally
        {
            tester.hitPosition = gameHitPosition;
        }
        if (picked == null)
            return false;

        entitySelection = picked;
        blockSelection = null;
        return true;
    }

    /// <summary><see cref="EntityReach.Applies"/> by entity code, remembered per code: the selection
    /// runs every frame.</summary>
    public static System.Func<Entity, bool> AppliesCached(EntityReach rule)
    {
        var known = new Dictionary<AssetLocation, bool>();
        return entity =>
        {
            AssetLocation? code = entity.Code;
            if (code == null)
                return false;
            if (!known.TryGetValue(code, out bool applies))
                known[code] = applies = rule.Applies(code.ToString());
            return applies;
        };
    }
}
