using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace SeraphHorizons.Mod.PicklingTub;

/// <summary>
/// An item's model with its textures in the block atlas, to draw inside a block (the gears and bits
/// in the pickling tub): an item's own mesh (<c>TesselateItem</c>) is in the item atlas, which the
/// chunk's mesh cannot use. Built on the main thread, which may add textures to the atlas, and kept
/// per item and session.
/// </summary>
public static class ItemMeshes
{
    // In the client's object cache: one per session, as the atlas is.
    public static MeshData? Get(ICoreClientAPI capi, Item item) =>
        ObjectCacheUtil.GetOrCreate(capi, "seraphhorizons-tubitem-" + item.Code, () => Build(capi, item));

    private static MeshData? Build(ICoreClientAPI capi, Item item)
    {
        if (item.Shape?.Base is not { } basePath)
            return null;
        var shape = Shape.TryGet(capi, basePath.Clone().WithPathPrefixOnce("shapes/").WithPathAppendixOnce(".json"));
        if (shape == null)
            return null;
        capi.Tesselator.TesselateShape("seraphhorizons:tubitem", shape, out var mesh, new Source(capi, item, shape));
        return mesh;
    }

    private sealed class Source(ICoreClientAPI capi, Item item, Shape shape) : ITexPositionSource
    {
        public Size2i AtlasSize => capi.BlockTextureAtlas.Size;

        public TextureAtlasPosition this[string textureCode]
        {
            get
            {
                AssetLocation? location = null;
                if (item.Textures != null && (item.Textures.TryGetValue(textureCode, out var own) || item.Textures.TryGetValue("all", out own)))
                    location = own.Baked?.BakedName ?? own.Base;
                if (location == null && shape.Textures != null && shape.Textures.TryGetValue(textureCode, out var fromShape))
                    location = fromShape;
                if (location == null && item.Textures?.Count > 0)
                    location = item.Textures.Values.First().Base;
                return Get(location);
            }
        }

        private TextureAtlasPosition Get(AssetLocation? location)
        {
            var atlas = capi.BlockTextureAtlas;
            if (location == null)
                return atlas.UnknownTexturePosition;
            if (atlas[location] is { } known)
                return known;
            var asset = capi.Assets.TryGet(location.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (asset != null && atlas.GetOrInsertTexture(location, out _, out var inserted, () => asset.ToBitmap(capi)))
                return inserted;
            return atlas.UnknownTexturePosition;
        }
    }
}
