using BuckingSawmill.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace BuckingSawmill;

/// <summary>
/// Draws the mill's moving parts and the loaded trunk (client only; created and disposed by
/// <see cref="BEBuckingMill"/>). One mesh per rig part from <c>shapes/block/buckingmill.json</c>,
/// built by blanking every other part's elements, as Immersive Woodworking's sawmill renderer
/// does; each is drawn with its rig matrix (<see cref="RigParts.Matrices"/>, from the shaft angle,
/// the saw depth and whether the saws are being raised) turned to the mill's facing. The static frame part is not drawn here: the block's own shape (buckingmill_frame.json,
/// the same elements) draws it in the chunk mesh. The renderer polls <see cref="IMillVisualState"/>
/// every frame; there is no change event.
/// </summary>
public sealed class MillRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(BuckingSawmillSystem.Domain, "shapes/block/buckingmill.json");
    private const int DrawRange = 64;              // blocks from the camera to the controller
    private const float SawdustInterval = 0.1f;    // seconds between sawdust puffs per blade
    private const float LiftingEase = 6f;           // per second: how fast the lifting input follows the phase
    private const int RatchetTeeth = 12;            // teeth on the windlass's ratchet wheel the latch clicks over

    private readonly ICoreClientAPI _capi;
    private readonly BEBuckingMill _be;
    private readonly RigParts _parts;
    private readonly TrunkBed? _bed;
    private readonly MultiTextureMeshRef?[] _meshes;
    private readonly bool[] _drawn;                // false for the static frame part(s)
    private readonly bool[] _isBlade;
    private readonly (Float3 Min, Float3 Max)?[] _bounds;
    private string? _bladeMetal;
    private bool _built;

    private MultiTextureMeshRef? _trunkMesh;
    private AssetLocation? _trunkCode;
    private float[] _trunkMatrix = Mat4.Identity();

    // the client's shaft clock
    private double _theta;
    private double _lastAngle;
    private bool _angleSeeded;
    private int _stroke;
    private float _sawdustTimer;
    private float _lifting;                         // 0..1, eased towards 1 while the saws are raised
    private double _travel;                         // the shaft's travel: total angle turned either way (the rectified gears' input)
    private readonly double _clicksPerDepth;        // latch clicks over a full raise
    private int _click;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public MillRenderer(ICoreClientAPI capi, BEBuckingMill be, Rig rig)
    {
        _capi = capi;
        _be = be;
        _parts = rig.MovingParts;
        _bed = rig.TrunkBed;
        int n = _parts.Parts.Count;
        _meshes = new MultiTextureMeshRef?[n];
        _bounds = new (Float3, Float3)?[n];
        _drawn = new bool[n];
        _isBlade = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = _parts.Parts[i];
            _drawn[i] = p.Requires != null || p.Ride != null || p.Drivers.Count > 0;
            _isBlade[i] = p.Requires is { } r && r.StartsWith("blade", StringComparison.Ordinal);
        }
        // the latch clicks over each of the ratchet wheel's teeth while the windlass winds; the
        // wheel is on the drum shaft, so it turns with the drum
        var drum = _parts.IndexOf("drum");
        var spin = drum < 0 ? null : _parts.Parts[drum].Drivers.FirstOrDefault(d => d.Type == DriverType.Step && d.Rotates);
        _clicksPerDepth = spin == null ? 0 : Math.Abs(spin.Amount) * RatchetTeeth / (2 * Math.PI);
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "buckingmill");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "buckingmill");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "buckingmill");
    }

    // ---- meshes ----

    private void BuildMeshes()
    {
        _built = true;
        var master = Shape.TryGet(_capi, ShapeLoc);
        if (master == null)
        {
            _capi.Logger.Error("[buckingsawmill] {0} is missing; the mill's moving parts are not drawn", ShapeLoc);
            return;
        }
        var blockTex = _capi.Tesselator.GetTextureSource(_be.Block);
        ITexPositionSource bladeTex = new BladeTextureSource(blockTex, BladeMetalTexture(_be.BladeMetal));
        _bladeMetal = _be.BladeMetal;
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i])
                continue;
            _meshes[i]?.Dispose();
            _meshes[i] = null;
            var mesh = PartMesh(master, i, _isBlade[i] ? bladeTex : blockTex);
            if (mesh == null)
                continue;
            _bounds[i] ??= Bounds(mesh);
            _meshes[i] = _capi.Render.UploadMultiTextureMesh(mesh);
        }
    }

    private void RebuildBlades()
    {
        var master = Shape.TryGet(_capi, ShapeLoc);
        if (master == null)
            return;
        var tex = new BladeTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), BladeMetalTexture(_be.BladeMetal));
        _bladeMetal = _be.BladeMetal;
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i] || !_isBlade[i])
                continue;
            _meshes[i]?.Dispose();
            var mesh = PartMesh(master, i, tex);
            _meshes[i] = mesh == null ? null : _capi.Render.UploadMultiTextureMesh(mesh);
        }
    }

    /// <summary>The part's elements alone, in the native frame (blocks).</summary>
    private MeshData? PartMesh(Shape master, int part, ITexPositionSource tex)
    {
        var shape = master.Clone();
        int kept = Blank(shape.Elements, [], part);
        if (kept == 0)
            return null;
        _capi.Tesselator.TesselateShape("buckingmill", shape, out var mesh, tex, null, 0, 0, 0, null, null);
        return mesh.VerticesCount > 0 ? mesh : null;
    }

    private int Blank(ShapeElement[]? elements, List<string> chain, int keep)
    {
        if (elements == null)
            return 0;
        int kept = 0;
        foreach (var el in elements)
        {
            chain.Add(el.Name ?? "");
            if (_parts.PartOf(chain) == keep)
                kept++;
            else
                el.FacesResolved = new ShapeElementFace[6];
            kept += Blank(el.Children, chain, keep);
            chain.RemoveAt(chain.Count - 1);
        }
        return kept;
    }

    private TextureAtlasPosition? BladeMetalTexture(string? metal)
    {
        if (metal == null)
            return null;
        var loc = new AssetLocation("game", "block/metal/ingot/" + metal);
        var pos = _capi.BlockTextureAtlas[loc];
        if (pos == null && _capi.Assets.Exists(loc.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png")))
            _capi.BlockTextureAtlas.GetOrInsertTexture(loc, out _, out pos);
        return pos;
    }

    private static (Float3, Float3) Bounds(MeshData mesh)
    {
        float[] xyz = mesh.xyz;
        var min = new Float3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Float3(float.MinValue, float.MinValue, float.MinValue);
        for (int v = 0; v < mesh.VerticesCount; v++)
        {
            float x = xyz[v * 3], y = xyz[v * 3 + 1], z = xyz[v * 3 + 2];
            min = new Float3(Math.Min(min.X, x), Math.Min(min.Y, y), Math.Min(min.Z, z));
            max = new Float3(Math.Max(max.X, x), Math.Max(max.Y, y), Math.Max(max.Z, z));
        }
        return (min, max);
    }

    private void SyncTrunk()
    {
        var trunk = _be.Trunk;
        var code = trunk?.Block?.Code;
        if (code == null || _bed == null)
        {
            if (_trunkMesh != null)
            {
                _trunkMesh.Dispose();
                _trunkMesh = null;
            }
            _trunkCode = null;
            return;
        }
        if (code.Equals(_trunkCode))
            return;
        _trunkMesh?.Dispose();
        _trunkMesh = null;
        _trunkCode = code;
        // Logging Expanded's own block for this stack: its per-size, per-wood shape and textures,
        // turned by its side variant. Laid on the bed by its bounds, whatever the size's offset.
        _capi.Tesselator.TesselateBlock(trunk!.Block, out var mesh);
        if (mesh == null || mesh.VerticesCount == 0)
            return;
        var (min, max) = Bounds(mesh);
        _trunkMatrix = MillMotion.TrunkPlacement(min, max, _bed);
        _trunkMesh = _capi.Render.UploadMultiTextureMesh(mesh);
    }

    // ---- frame ----

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var pos = _be.Pos;
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        bool far = camPos.SquareDistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) > DrawRange * DrawRange;

        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built)
                BuildMeshes();
            else if (_bladeMetal != _be.BladeMetal)
                RebuildBlades();
            SyncTrunk();
            AdvanceClock(deltaTime, far);
        }
        if (far)
            return;

        var mats = Matrices();
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        bool opaque = stage == EnumRenderStage.Opaque;
        IStandardShaderProgram? prog = null;
        if (opaque)
        {
            var light = Footprint.ToWorld(new Int3(3, 1, 1), _be.Side);
            prog = rapi.PreparedStandardShader(pos.X + light.X, pos.Y + light.Y, pos.Z + light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (_meshes[i] is not { } mesh || !Fitted(i))
                continue;
            Draw(mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, pos);
        }
        if (_trunkMesh != null)
            Draw(_trunkMesh, Mat4.Multiply(facing, _trunkMatrix), prog, camPos, pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private float[][] Matrices() => _parts.Matrices(_theta, _be.ClientSawDepth, _lifting, _travel);

    private bool Fitted(int part) =>
        RigPart.Fitted(_parts.Parts[part].Requires, _be.SashCount, _be.HasCrankshaft, _be.BladeCount, _be.HasLevers);

    private void Draw(MultiTextureMeshRef mesh, float[] native, IStandardShaderProgram? prog, Vec3d cam, BlockPos pos)
    {
        _model.Identity().Translate(pos.X - cam.X, pos.Y - cam.Y, pos.Z - cam.Z).Mul(native);
        if (prog != null)
        {
            prog.ModelMatrix = _model.Values;
            _capi.Render.RenderMultiTextureMesh(mesh, "tex", 0);
            return;
        }
        // Shadow passes: the active shadow shader wants the full model-view-projection.
        var rapi = _capi.Render;
        var mvp = new Matrixf().Set(rapi.CurrentProjectionMatrix).Mul(rapi.CurrentModelviewMatrix).Mul(_model.Values);
        rapi.CurrentActiveShader.UniformMatrix("mvpMatrix", mvp.Values);
        rapi.CurrentActiveShader.Uniform("origin", new Vec3f(0, 0, 0));
        rapi.RenderMultiTextureMesh(mesh, "tex2d", 0);
    }

    // ---- motion, sound, sawdust ----

    /// <summary>Turns the client's shaft clock by the power ghost's angle change (the network
    /// already advances that angle smoothly between server updates) and eases the lifting input
    /// towards the phase, and adds the turn's size to the shaft's travel (the rectified gears turn
    /// with it, the same way whichever way the shaft turns). While cutting it plays a saw stroke per
    /// half turn and puffs sawdust; while the saws are raised the latch clicks over the ratchet's teeth.</summary>
    private void AdvanceClock(float dt, bool far)
    {
        var phase = _be.Phase;
        float target = phase == MillPhase.Raising ? 1 : 0;
        _lifting += (target - _lifting) * Math.Min(1, dt * LiftingEase);
        float speed = _be.ShaftSpeed;
        double angle = MillMotion.NativeShaftAngle(_be.Side, _be.ShaftAngle);
        if (!_angleSeeded || speed <= 0)
        {
            _lastAngle = angle;
            _angleSeeded = true;
            return;
        }
        double before = _theta;
        double delta = MillMotion.WrappedDelta(_lastAngle, angle);
        _theta += delta;
        _travel += Math.Abs(delta);
        _lastAngle = angle;
        // keep the clock small; every shaft ratio in the shipped rig is a whole number
        if (Math.Abs(_theta) > 1000 * Math.PI)
        {
            double wrap = Math.Floor(_theta / (2 * Math.PI)) * 2 * Math.PI;
            _theta -= wrap;
            before -= wrap;
        }
        bool running = _be.Complete && speed >= _be.MinSpeed && !far;
        int click = (int)Math.Floor(_be.ClientSawDepth * _clicksPerDepth);
        if (running && phase == MillPhase.Raising && click != _click)
            PlayClick();
        _click = click;
        if (!running || phase != MillPhase.Cutting || _be.Trunk == null)
            return;
        int strokes = MillMotion.StrokesBetween(before, _theta);
        if (strokes > 0)
            PlayStroke();
        _sawdustTimer += dt;
        if (_sawdustTimer >= SawdustInterval)
        {
            _sawdustTimer = 0;
            SpawnSawdust();
        }
    }

    private void PlayClick()
    {
        // at the latch, on the ratchet wheel east of station 1's east post
        var at = WorldPoint(new Float3(2.65f, 3.65f, 0.7f));
        _capi.World.PlaySoundAt(new AssetLocation("immersivewoodworking", "sounds/saw/metal_click"), at.X, at.Y, at.Z, null, true, 16, 0.35f);
    }

    private void PlayStroke()
    {
        _stroke++;
        char band = MillMotion.SpeedBand(_be.ShaftSpeed);
        char dir = _stroke % 2 == 0 ? 'f' : 'b';
        int variant = 1 + _stroke / 2 % 2;
        var at = WorldPoint(new Float3(3f, 1.5f, 1.5f));
        _capi.World.PlaySoundAt(new AssetLocation("immersivewoodworking", $"sounds/saw/sawing_{band}{dir}{variant}"),
            at.X, at.Y, at.Z, null, true, 24, 0.7f);
    }

    private void SpawnSawdust()
    {
        var mats = Matrices();
        float mid = _bed?.Origin.Z ?? 1.5f;
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_isBlade[i] || _bounds[i] is not { } b || !Fitted(i))
                continue;
            // along the blade's cutting edge, where it is in the trunk
            var edge = Mat4.Apply(mats[i], new Float3((b.Min.X + b.Max.X) / 2, b.Min.Y, mid));
            var lo = WorldPoint(new Float3(edge.X - 0.1f, edge.Y, mid - 0.6f));
            var hi = WorldPoint(new Float3(edge.X + 0.1f, edge.Y + 0.15f, mid + 0.6f));
            _capi.World.SpawnParticles(3, ColorUtil.ToRgba(255, 214, 180, 120),
                new Vec3d(Math.Min(lo.X, hi.X), lo.Y, Math.Min(lo.Z, hi.Z)), new Vec3d(Math.Max(lo.X, hi.X), hi.Y, Math.Max(lo.Z, hi.Z)),
                new Vec3f(-0.4f, 0.1f, -0.4f), new Vec3f(0.4f, 0.6f, 0.4f), 1.2f, 0.6f, 0.4f, EnumParticleModel.Quad, null);
        }
    }

    private Vec3d WorldPoint(Float3 native)
    {
        var w = Footprint.ToWorld(native, _be.Side);
        return new Vec3d(_be.Pos.X + w.X, _be.Pos.Y + w.Y, _be.Pos.Z + w.Z);
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowFar);
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.ShadowNear);
        foreach (var mesh in _meshes)
            mesh?.Dispose();
        _trunkMesh?.Dispose();
    }

    /// <summary>The block's textures, with the blade kit's metal in place of <c>metal</c>.</summary>
    private sealed class BladeTextureSource(ITexPositionSource inner, TextureAtlasPosition? metal) : ITexPositionSource
    {
        public Size2i AtlasSize => inner.AtlasSize!;
        public TextureAtlasPosition this[string textureCode] =>
            textureCode == "metal" && metal != null ? metal : inner[textureCode]!;
    }
}
