using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SeraphHorizons.Mod.Machines;

/// <summary>A machine's uploaded part meshes for one texture variant, by part index (null for a
/// part not built or with no faces), each with its mesh's bounding box in the native frame.
/// Owned by <see cref="MachinePartMeshes"/>; a renderer only draws them.</summary>
public sealed class PartMeshSet(MultiTextureMeshRef?[] meshes, (Float3 Min, Float3 Max)?[] bounds)
{
    public MultiTextureMeshRef?[] Meshes { get; } = meshes;
    public (Float3 Min, Float3 Max)?[] Bounds { get; } = bounds;
}

/// <summary>
/// The client's cache of the machines' uploaded part meshes (<see cref="MachineMeshes.PartMeshes"/>):
/// built the first time a renderer asks for a key, then shared by every block entity of that
/// machine, so a second machine, or one whose chunk reloads, costs no tessellation. A key is the
/// shape plus the texture variant and the parts it holds (the renderer's choice, e.g. the rosser's
/// tips in one head metal); the block's own textures are not part of it, which holds because a
/// machine's blocktype has one <c>textures</c> for every side. The meshes are disposed here, once,
/// when the client leaves the world or the mod is disposed (the atlas they point into is rebuilt
/// on the next join), never by a renderer.
/// </summary>
public sealed class MachinePartMeshes : ModSystem
{
    private readonly Dictionary<string, PartMeshSet> _sets = [];
    private ICoreClientAPI? _capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        api.Event.LeaveWorld += Clear;
    }

    /// <summary>The set for <paramref name="key"/> (shape and variant), built on first use from
    /// <paramref name="shapeLoc"/> with the texture source <paramref name="tex"/> gives, holding the
    /// parts <paramref name="wanted"/> accepts. Null when the shape is missing (not cached, so the
    /// caller's error is as before).</summary>
    public static PartMeshSet? Get(ICoreClientAPI capi, string key, AssetLocation shapeLoc, RigParts parts,
                                   System.Func<int, bool> wanted, System.Func<ITexPositionSource> tex, string name)
    {
        var self = capi.ModLoader.GetModSystem<MachinePartMeshes>();
        string full = shapeLoc + "|" + key;
        if (self._sets.TryGetValue(full, out var set))
            return set;
        var master = Shape.TryGet(capi, shapeLoc);
        if (master == null)
            return null;
        var meshes = MachineMeshes.PartMeshes(capi, master, parts, wanted, tex(), name);
        var refs = new MultiTextureMeshRef?[meshes.Length];
        var bounds = new (Float3, Float3)?[meshes.Length];
        for (int i = 0; i < meshes.Length; i++)
        {
            if (meshes[i] is not { } mesh)
                continue;
            bounds[i] = MachineMeshes.Bounds(mesh);
            refs[i] = capi.Render.UploadMultiTextureMesh(mesh);
        }
        set = new PartMeshSet(refs, bounds);
        self._sets[full] = set;
        return set;
    }

    private void Clear()
    {
        foreach (var set in _sets.Values)
            foreach (var mesh in set.Meshes)
                mesh?.Dispose();
        _sets.Clear();
    }

    public override void Dispose()
    {
        if (_capi != null)
            _capi.Event.LeaveWorld -= Clear;
        Clear();
    }
}
