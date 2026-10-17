using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rocker.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeraphHorizons.Mod.Rocker;

/// <summary>
/// Draws the whole rocker (client only; created and disposed by <see cref="BERocker"/>): nothing of it
/// stands still, so its block's chunk mesh is empty and every rig part is drawn here, from
/// <c>shapes/block/rocker.json</c>, each with its rig matrix at the clock's θ (<see cref="RockerClock"/>)
/// turned to the rocker's facing. The cradle always; the water while it is worked; the charge while one is
/// in the hopper and the concentrate while some lies behind the riffles, their texture codes
/// (<c>charge</c>, <c>concentrate</c>) set to the items' own textures (one mesh set per pair). While
/// worked, splashes at the foot (<c>outflow</c>).
/// </summary>
public sealed class RockerRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(RockerSystem.Domain, "shapes/block/rocker.json");
    private const int DrawRange = 48;
    private const float SplashInterval = 0.15f;

    private readonly ICoreClientAPI _capi;
    private readonly BERocker _be;
    private readonly RockerRig _rig;
    private readonly RigParts _parts;
    private readonly Dictionary<string, MultiTextureMeshRef?[]> _meshes = [];
    private readonly RockerClock _clock = new();
    private readonly Matrixf _model = new();
    private Shape? _master;
    private bool _loaded;
    private float _splashTimer;

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public RockerRenderer(ICoreClientAPI capi, BERocker be, RockerRig rig)
    {
        _capi = capi;
        _be = be;
        _rig = rig;
        _parts = rig.MovingParts;
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "rocker");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "rocker");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "rocker");
    }

    private MultiTextureMeshRef?[] Meshes(ItemStack? charge, string? catchCode)
    {
        string key = (charge?.Collectible.Code.ToString() ?? "") + "|" + (catchCode ?? "");
        if (_meshes.TryGetValue(key, out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_parts.Parts.Count];
        _meshes[key] = meshes;
        if (!_loaded)
        {
            _loaded = true;
            _master = Shape.TryGet(_capi, ShapeLoc);
            if (_master == null)
                _capi.Logger.Error("[seraphhorizons] Rocker: {0} is missing; the rocker is not drawn", ShapeLoc);
        }
        if (_master == null)
            return meshes;
        var catchStack = catchCode == null ? null : StackOf(catchCode);
        var tex = new LoadTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), Atlas(charge), Atlas(catchStack));
        var built = MachineMeshes.PartMeshes(_capi, _master, _parts, _ => true, tex, "rocker");
        for (int i = 0; i < meshes.Length; i++)
            if (built[i] is { } mesh)
                meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        return meshes;
    }

    private ItemStack? StackOf(string code)
    {
        var loc = new AssetLocation(code);
        CollectibleObject? c = _capi.World.GetItem(loc) ?? (CollectibleObject?)_capi.World.GetBlock(loc);
        return c == null || c.IsMissing || c.Id == 0 ? null : new ItemStack(c);
    }

    /// <summary>An item's own texture in the block atlas (a crushed ore's, the concentrate's, a gravel's), or null.</summary>
    private TextureAtlasPosition? Atlas(ItemStack? stack)
    {
        if (stack == null)
            return null;
        CompositeTexture? texture = stack.Item != null ? stack.Item.FirstTexture : stack.Block?.Textures?.Values.FirstOrDefault();
        if (texture == null)
            return null;
        try
        {
            return new ContainerTextureSource(_capi, stack, texture)["charge"];
        }
        catch (Exception e)
        {
            _capi.Logger.Warning("[seraphhorizons] Rocker: no texture for {0}: {1}", stack.Collectible.Code, e.Message);
            return null;
        }
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = _be.WorldPoint(new Float3(0.5f, 0.5f, 0.5f));
        bool far = camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange;
        bool worked = _be.Held && _be.State.HasCharge;
        if (stage == EnumRenderStage.Opaque)
        {
            _clock.Advance(deltaTime, worked, _be.RocksPerSecond);
            if (!far && worked && (_splashTimer += deltaTime) >= SplashInterval)
            {
                _splashTimer = 0;
                Splash();
            }
        }
        if (far)
            return;
        var charge = _be.Charge;
        string? catchCode = _be.CatchCode;
        var meshes = Meshes(charge, catchCode);
        var mats = _parts.Matrices(new RigInput(_clock.Theta));
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            prog = rapi.PreparedStandardShader(_be.Pos.X, _be.Pos.Y, _be.Pos.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < meshes.Length; i++)
        {
            bool shown = _parts.Parts[i].Requires switch
            {
                null => true,
                RockerRequires.Water => worked,
                RockerRequires.Charge => charge != null,
                RockerRequires.Concentrate => catchCode != null,
                _ => false,
            };
            if (shown && meshes[i] is { } mesh)
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        }
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private void Splash()
    {
        var at = _be.WorldPoint(_rig.Posed(_rig.Outflow, _clock.Theta));
        int colour = ColorUtil.ToRgba(140, 160, 190, 220);
        _capi.World.SpawnParticles(3, colour, at.AddCopy(-0.05, -0.02, -0.15), at.AddCopy(0.05, 0.02, 0.15),
            new Vec3f(-0.6f, -0.2f, -0.2f), new Vec3f(-0.2f, 0.3f, 0.2f), 0.5f, 1f, 0.2f, EnumParticleModel.Quad, null);
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        foreach (var meshes in _meshes.Values)
            foreach (var mesh in meshes)
                mesh?.Dispose();
    }

    /// <summary>The block's textures with the charge's and the concentrate's in place of their codes.</summary>
    private sealed class LoadTextureSource(ITexPositionSource inner, TextureAtlasPosition? charge, TextureAtlasPosition? concentrate)
        : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode switch
        {
            "charge" when charge != null => charge,
            "concentrate" when concentrate != null => concentrate,
            _ => inner[textureCode]!,
        };
    }
}
