using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SeraphHorizons.Mod.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Woodworking;

/// <summary>
/// The splitting block's look, client side: Logging Expanded's splitting log model for the tier
/// (<see cref="ShapePaths"/>), in the block's own wood, in place of Immersive Woodworking's
/// chopping block, placed and held.
///
/// Textures come from the wood the way Immersive Woodworking finds them for any wood it knows,
/// not from Logging Expanded's per-wood tables (which name Wildcraft Trees textures the pack does
/// not have): <c>block/wood/bark/&lt;wood&gt;</c> on the primitive tier's sides,
/// <c>block/wood/debarked/&lt;wood&gt;</c> on the others', <c>block/wood/treetrunk/&lt;wood&gt;</c> on
/// the primitive's top, each from the wood's domain and else the game's, and iron for the hoops
/// and nails (the shapes call them <c>steel</c> and <c>iron</c>).
///
/// The models are 15/16 tall, Immersive Woodworking's 11/16, and Immersive Woodworking places what
/// lies on the block (log, half-log, firewood) and an axe stuck in it at its own height. A prefix
/// on its block entity's <c>OnTesselation</c> draws the model and those meshes lifted by
/// <see cref="Lift"/>; Immersive Woodworking's static mesh builders, which its chopper shares,
/// are left alone.
///
/// The chopper's bed (<see cref="ChopperBed"/>) is the advanced model too, <see cref="BuildBed"/>.
/// Immersive Woodworking's bed is 11/16 tall, and the chopper's log and the maul's strike are
/// placed on that, so the model is squashed to 11/15 in height rather than sunk 4/16 into the
/// block below: both hoops and every nail stay in sight (sunk, the lower hoop would sit on the
/// floor line), and nothing pokes into a hopper or block under the frame. The chopper caches each
/// bed's mesh itself (by wood and rotation).
///
/// <c>OnTesselation</c> runs on the tesselation thread, where textures may not be added to the
/// atlas, so a model is built on the main thread the first time it is wanted; until then the block
/// draws as Immersive Woodworking has it, and it is redrawn once the model is there.
/// </summary>
public static class SplittingBlockLook
{
    /// <summary>The models' top (15/16) over Immersive Woodworking's (11/16).</summary>
    public const float Lift = 4 / 16f;

    /// <summary>Logging Expanded's models, by tier.</summary>
    public static readonly AssetLocation[] ShapePaths =
    [
        new(WoodworkingMods.LeModId, "shapes/splittinglog.json"),
        new(WoodworkingMods.LeModId, "shapes/debarkedsplittinglog.json"),
        new(WoodworkingMods.LeModId, "shapes/boundsplittinglog.json"),
        new(WoodworkingMods.LeModId, "shapes/advancedsplittingblock.json"),
    ];

    /// <summary>Immersive Woodworking's chopper bed height over the advanced model's, in sixteenths.</summary>
    public const float BedHeightScale = 11f / 15f;

    public static readonly AssetLocation IronTexture = new("game", "block/metal/ingot/iron");

    private static PropertyInfo? _wood;
    private static PropertyInfo? _woodDomain;
    private static FieldInfo? _contentMesh;
    private static FieldInfo? _toolMesh;

    private static readonly Shape?[] Shapes = new Shape?[ShapePaths.Length];
    // Placed models by wood and tier (null: it cannot be built), and the blocks to redraw when one
    // being built is done. Written on the main thread, read on the tesselation thread.
    private static readonly ConcurrentDictionary<string, MeshData?> Stands = new();
    private static readonly Dictionary<string, List<BlockPos>> Pending = [];
    // What lies on a block and its stuck axe, lifted, by Immersive Woodworking's mesh; it makes a
    // new mesh whenever those change, and the old ones' copies go with them.
    private static readonly ConditionalWeakTable<MeshData, MeshData> Lifted = new();
    // Held and inventory models; main thread only.
    private static readonly Dictionary<string, MultiTextureMeshRef?> Held = [];
    private static readonly ConcurrentDictionary<string, byte> Warned = new();

    /// <summary>Immersive Woodworking's block entity members the look reads. Called from
    /// <see cref="SplittingBlock.Bind"/>; returns false when any is missing.</summary>
    public static bool Bind(Type blockEntity)
    {
        _wood = AccessTools.DeclaredProperty(blockEntity, "Wood");
        _woodDomain = AccessTools.DeclaredProperty(blockEntity, "WoodDomain");
        _contentMesh = AccessTools.DeclaredField(blockEntity, "contentMesh");
        _toolMesh = AccessTools.DeclaredField(blockEntity, "toolMesh");
        return _wood?.PropertyType == typeof(string) && _woodDomain?.PropertyType == typeof(string)
               && _contentMesh?.FieldType == typeof(MeshData) && _toolMesh?.FieldType == typeof(MeshData);
    }

    /// <summary>Forgets every model (and frees the held ones). Client, main thread.</summary>
    public static void Clear()
    {
        foreach (var meshRef in Held.Values)
            meshRef?.Dispose();
        Held.Clear();
        Stands.Clear();
        lock (Pending)
            Pending.Clear();
        Array.Clear(Shapes);
    }

    /// <summary>The model for a wood and tier, unrotated, block-sized; null when Logging Expanded's
    /// shape is missing. Main thread: it may add textures to the block atlas.</summary>
    public static MeshData? Build(ICoreClientAPI capi, string wood, string woodDomain, SplittingBlockTier tier)
    {
        var shape = Shapes[(int)tier] ??= Shape.TryGet(capi, ShapePaths[(int)tier]);
        if (shape == null)
            return null;
        capi.Tesselator.TesselateShape("seraphhorizons:splittingblock", shape, out var mesh,
            new TexSource(capi, wood, woodDomain, tier), null, 0, 0, 0);
        return mesh;
    }

    /// <summary>The chopper's bed for a wood: the advanced model, squashed to Immersive Woodworking's
    /// bed height and turned by the frame's <paramref name="rotY"/> degrees as Immersive Woodworking
    /// turns its own; null when the shape is missing. Main thread.</summary>
    public static MeshData? BuildBed(ICoreClientAPI capi, string wood, string woodDomain, int rotY)
    {
        if (Build(capi, wood, woodDomain, SplittingBlockTier.Advanced) is not { } mesh)
            return null;
        mesh.Scale(new Vec3f(0.5f, 0f, 0.5f), 1f, BedHeightScale, 1f);
        if (rotY != 0)
            mesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, rotY * GameMath.DEG2RAD, 0f);
        return mesh;
    }

    private static string Key(string wood, string woodDomain, SplittingBlockTier tier) => $"{woodDomain}:{wood}:{tier.Name()}";

    /// <summary>The placed model, or null while it is being built (or cannot be): then the block at
    /// <paramref name="pos"/> is redrawn once it is there. Any thread.</summary>
    private static MeshData? Placed(ICoreClientAPI capi, string wood, string woodDomain, SplittingBlockTier tier, BlockPos pos)
    {
        string key = Key(wood, woodDomain, tier);
        if (Stands.TryGetValue(key, out var stand))
            return stand;
        lock (Pending)
        {
            if (Pending.TryGetValue(key, out var waiting))
            {
                waiting.Add(pos.Copy());
                return null;
            }
            Pending[key] = [pos.Copy()];
        }
        capi.Event.EnqueueMainThreadTask(() =>
        {
            Stands[key] = Build(capi, wood, woodDomain, tier);
            List<BlockPos> redraw;
            lock (Pending)
            {
                if (!Pending.Remove(key, out redraw!))
                    return;
            }
            foreach (var at in redraw)
                capi.World.BlockAccessor.MarkBlockDirty(at);
        }, "seraphhorizons-splittingblock");
        return null;
    }

    /// <summary>Prefix on <c>BlockEntityChoppingBlock.OnTesselation</c> (client): the tier's model,
    /// and what lies on the block and a stuck axe lifted to its top. A block without a wood is
    /// <see cref="SplittingBlock.DefaultWood"/>. A block whose model is not built yet draws as
    /// Immersive Woodworking has it.</summary>
    public static bool OnTesselationPrefix(BlockEntity __instance, ITerrainMeshPool __0, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal || _wood == null || __instance.Api is not ICoreClientAPI capi
            || __instance.GetBehavior<BEBehaviorSplittingBlockTier>() is not { } behavior)
            return __runOriginal;
        var (wood, woodDomain) = _wood.GetValue(__instance) is string own
            ? (own, _woodDomain!.GetValue(__instance) as string ?? SplittingBlock.DefaultWoodDomain)
            : (SplittingBlock.DefaultWood, SplittingBlock.DefaultWoodDomain);
        var stand = Placed(capi, wood, woodDomain, behavior.Tier, __instance.Pos);
        if (stand == null)
            return true;
        foreach (var field in new[] { _contentMesh!, _toolMesh! })
            if (field.GetValue(__instance) is MeshData mesh)
                __0.AddMeshData(Lifted.GetValue(mesh, original => original.Clone().Translate(0, Lift, 0)));
        __0.AddMeshData(stand);
        __result = true;
        return false;
    }

    /// <summary>Postfix on <c>BlockChoppingBlock.OnBeforeRender</c> (client): a stack renders as its
    /// tier's model in its wood, <see cref="SplittingBlock.DefaultWood"/> for a stack without one
    /// (the creative and handbook stack). Immersive Woodworking has already set its own; this
    /// replaces it.</summary>
    public static void OnBeforeRenderPostfix(ICoreClientAPI __0, ItemStack __1, ref ItemRenderInfo __3)
    {
        var (wood, woodDomain) = __1.Attributes.GetString("wood") is { } own
            ? (own, __1.Attributes.GetString("woodDomain", "game"))
            : (SplittingBlock.DefaultWood, SplittingBlock.DefaultWoodDomain);
        var tier = BEBehaviorSplittingBlockTier.Of(__1);
        string key = Key(wood, woodDomain, tier);
        if (!Held.TryGetValue(key, out var meshRef))
            Held[key] = meshRef = Build(__0, wood, woodDomain, tier) is { } mesh ? __0.Render.UploadMultiTextureMesh(mesh) : null;
        if (meshRef != null)
            __3.ModelRef = meshRef;
    }

    /// <summary>The model's textures by the shape's texture codes: <c>oak</c> the sides (bark on the
    /// primitive tier, debarked wood on the others), <c>oak2</c> the primitive's top (end grain),
    /// <c>steel</c> and <c>iron</c> the hoops and nails. Each texture is looked up in the wood's
    /// domain, then the game's, and a debarked one falls back to the bark.</summary>
    private sealed class TexSource : ITexPositionSource
    {
        private readonly ICoreClientAPI _capi;
        private readonly AssetLocation[] _side;
        private readonly AssetLocation[] _top;
        private readonly string _forLogging;

        public TexSource(ICoreClientAPI capi, string wood, string woodDomain, SplittingBlockTier tier)
        {
            _capi = capi;
            AssetLocation[] InDomains(string path) =>
                woodDomain == "game" ? [new("game", path + wood)] : [new(woodDomain, path + wood), new("game", path + wood)];
            var bark = InDomains("block/wood/bark/");
            _side = tier == SplittingBlockTier.Primitive ? bark : [.. InDomains("block/wood/debarked/"), .. bark];
            _top = [.. InDomains("block/wood/treetrunk/"), .. _side];
            _forLogging = $"{woodDomain}:{wood} {tier.Name()}";
        }

        public Size2i AtlasSize => _capi.BlockTextureAtlas.Size;

        public TextureAtlasPosition this[string textureCode] => textureCode switch
        {
            "oak2" => Get(_top),
            "steel" or "iron" => Get([IronTexture]),
            _ => Get(_side),
        };

        private TextureAtlasPosition Get(AssetLocation[] candidates)
        {
            var atlas = _capi.BlockTextureAtlas;
            foreach (var location in candidates)
            {
                if (atlas[location] is { } known)
                    return known;
                var asset = _capi.Assets.TryGet(location.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
                if (asset != null && atlas.GetOrInsertTexture(location, out _, out var inserted, () => asset.ToBitmap(_capi)))
                    return inserted;
            }
            if (Warned.TryAdd(candidates[0].ToString(), 0))
                _capi.Logger.Warning($"[seraphhorizons] No texture {candidates[0]} for the {_forLogging} splitting block; "
                                     + "it shows the unknown texture");
            return atlas.UnknownTexturePosition;
        }
    }
}
