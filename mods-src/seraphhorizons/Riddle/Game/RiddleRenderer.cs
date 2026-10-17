using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Riddle.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Riddle;

/// <summary>
/// Draws the riddle's moving parts and what it holds (client only; created and disposed by <see cref="BERiddle"/>,
/// and only while its block is the riddle). One mesh per rig part from <c>shapes/block/riddle.json</c>
/// (<see cref="MachineMeshes.PartMesh"/>), each drawn with its rig matrix turned to the station's facing, and only
/// when its <c>requires</c> is shown (<see cref="RiddleClock.Shows"/>). The texture codes <c>ore</c> (the fines) and
/// <c>rock</c> (the oversize) are set to the crushed ore's texture and the oversize's host rock (one mesh set per
/// pair). The static frame is the block's own shape (riddle_frame.json) and is not drawn here.
/// <para>The rig is posed every frame from the view (<see cref="IRiddleView"/>) through <see cref="RiddleClock"/>.
/// Nothing is drawn falling through the mesh (the model's first weak spot): while riddling, dust of the ore falls
/// under the riddle.</para>
/// </summary>
public sealed class RiddleRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(RiddleSystem.Domain, "shapes/block/riddle.json");
    private const int DrawRange = 48;
    private const float DustInterval = 0.15f;

    private readonly ICoreClientAPI _capi;
    private readonly BERiddle _be;
    private readonly IRiddleView _view;
    private readonly RiddleRig _rig;
    private readonly RigParts _parts;
    private readonly bool[] _drawn;
    private readonly Dictionary<(AssetLocation?, AssetLocation?), MultiTextureMeshRef?[]> _meshes = [];
    private Shape? _master;
    private bool _built;

    private readonly RiddleClock _clock = new();
    private float _dustTimer;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public RiddleRenderer(ICoreClientAPI capi, BERiddle be, IRiddleView view, RiddleRig rig)
    {
        _capi = capi;
        _be = be;
        _view = view;
        _rig = rig;
        _parts = rig.MovingParts;
        _drawn = _parts.Parts.Select(p => p.Requires != null || p.Ride != null || p.Drivers.Count > 0).ToArray();
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "riddle");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "riddle");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "riddle");
    }

    private MultiTextureMeshRef?[] Meshes(AssetLocation? ore, AssetLocation? rock)
    {
        if (_meshes.TryGetValue((ore, rock), out var meshes))
            return meshes;
        meshes = new MultiTextureMeshRef?[_drawn.Length];
        _meshes[(ore, rock)] = meshes;
        if (!_built)
        {
            _built = true;
            _master = Shape.TryGet(_capi, ShapeLoc);
            if (_master == null)
                _capi.Logger.Error("[seraphhorizons] Riddle: {0} is missing; its moving parts are not drawn", ShapeLoc);
        }
        if (_master == null)
            return meshes;
        var tex = new ContentsTextureSource(_capi.Tesselator.GetTextureSource(_be.Block),
            MachineMeshes.AtlasTexture(_capi, ore), MachineMeshes.AtlasTexture(_capi, rock));
        for (int i = 0; i < meshes.Length; i++)
            if (_drawn[i] && MachineMeshes.PartMesh(_capi, _master, _parts, i, tex, "riddle") is { } mesh)
                meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        return meshes;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = _be.WorldPoint(new Float3(0.5f, 0.5f, 0.5f));
        bool far = camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange;
        if (stage == EnumRenderStage.Opaque)
            AdvanceClock(deltaTime, far);
        if (far)
            return;

        var meshes = Meshes(_be.OreTexture, _be.RockTexture);
        var mats = _parts.Matrices(_clock.Input());
        var facing = Mat4.Facing(_view.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the riddle, outside the model
            var light = _be.Pos.UpCopy();
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < meshes.Length; i++)
            if (meshes[i] is { } mesh && RiddleClock.Shows(_view, _parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, _be.Pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private void AdvanceClock(float dt, bool far)
    {
        bool on = _view.ChargeOn;
        bool held = _view.Held && on;
        _clock.Advance(dt, on, _view.RiddleWork, held, _view.ShakesPerCharge);
        if (far || !held || _clock.Work >= 1)
            return;
        if ((_dustTimer += dt) >= DustInterval)
        {
            _dustTimer = 0;
            SpawnDust();
        }
    }

    // Fines falling through the mesh into the box.
    private void SpawnDust()
    {
        var at = _be.WorldPoint(_rig.Charge);
        int colour = ColorUtil.ToRgba(200, 105, 95, 90);
        _capi.World.SpawnParticles(3, colour,
            at.AddCopy(-0.25, -0.12, -0.25), at.AddCopy(0.25, -0.1, 0.25),
            new Vec3f(-0.02f, -0.6f, -0.02f), new Vec3f(0.02f, -0.3f, 0.02f), 0.4f, 1f, 0.15f, EnumParticleModel.Cube, null);
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

    /// <summary>The block's textures with the crushed ore's in place of <c>ore</c> and the host rock's in place of <c>rock</c>.</summary>
    private sealed class ContentsTextureSource(ITexPositionSource inner, TextureAtlasPosition? ore, TextureAtlasPosition? rock) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] => textureCode switch
        {
            "ore" when ore != null => ore,
            "rock" when rock != null => rock,
            _ => inner[textureCode]!,
        };
    }
}
