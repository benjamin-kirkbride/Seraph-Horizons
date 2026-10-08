using SeraphHorizons.Mod.BuckingSawmill.Core;
using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.BuckingSawmill;

/// <summary>
/// Draws the mill's moving parts and the loaded trunk (client only; created and disposed by
/// <see cref="BEBuckingMill"/>). One mesh per rig part from <c>shapes/block/buckingmill.json</c>
/// (<see cref="MachineMeshes.PartMeshes"/>, one tessellation split by part, shared by every mill
/// through <see cref="MachinePartMeshes"/>, the blades in a cached set per blade metal); each is drawn with its rig matrix (<see cref="RigParts.Matrices"/>, from the shaft angle,
/// the saw depth and whether the saws are being raised) turned to the mill's facing. The static frame part is not drawn here: the block's own shape (buckingmill_frame.json,
/// the same elements) draws it in the chunk mesh. The renderer polls <see cref="IMillVisualState"/>
/// every frame; there is no change event.
/// </summary>
public sealed class MillRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(BuckingSawmillSystem.Domain, "shapes/block/buckingmill.json");
    private const int DrawRange = 64;              // blocks from the camera to the controller
    private const float SawdustInterval = 0.1f;    // seconds between sawdust puffs per blade

    private readonly ICoreClientAPI _capi;
    private readonly BEBuckingMill _be;
    private readonly RigParts _parts;
    private readonly TrunkBed? _bed;
    private readonly MultiTextureMeshRef?[] _meshes;  // the shared sets' (blades from the metal's); never disposed here
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
    private float _lifting;                         // 1 while the saws are wound up: the levers' and clutch's trip input
    private double _travel;                         // the shaft's travel: total angle turned either way (the rectified gears' input)

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
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "buckingmill");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "buckingmill");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "buckingmill");
    }

    // ---- meshes ----

    private void BuildMeshes()
    {
        _built = true;
        if (!SetMeshes(bladesOnly: false))
            _capi.Logger.Error("[seraphhorizons] Bucking sawmill: {0} is missing; the mill's moving parts are not drawn", ShapeLoc);
    }

    private void RebuildBlades() => SetMeshes(bladesOnly: true);

    /// <summary>Takes the parts' meshes from the shared sets: the blades' in the blade kit's metal,
    /// the rest in the block's own textures. False when the shape is missing.</summary>
    private bool SetMeshes(bool bladesOnly)
    {
        _bladeMetal = _be.BladeMetal;
        string? metal = _bladeMetal;
        var blades = MachinePartMeshes.Get(_capi, "blades|" + (metal ?? ""), ShapeLoc, _parts, i => _drawn[i] && _isBlade[i],
            () => new MachineMeshes.MetalTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), MachineMeshes.MetalTexture(_capi, metal)),
            "buckingmill");
        if (blades == null)
            return false;
        var rest = bladesOnly ? null : MachinePartMeshes.Get(_capi, "parts", ShapeLoc, _parts, i => _drawn[i] && !_isBlade[i],
            () => _capi.Tesselator.GetTextureSource(_be.Block), "buckingmill");
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i] || (bladesOnly && !_isBlade[i]))
                continue;
            var set = _isBlade[i] ? blades : rest;
            _meshes[i] = set?.Meshes[i];
            _bounds[i] ??= set?.Bounds[i];
        }
        return true;
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
        // Logging Expanded's own block of the trunk's wood, without branches, in the size its class
        // is shown as (Trunks.ShownBlock): its shape and textures, turned by its side variant. Laid
        // on the bed by its bounds, whatever the size's offset.
        if (Trunks.ShownBlock(_capi.World, trunk) is not { } shown)
            return;
        _capi.Tesselator.TesselateBlock(shown, out var mesh);
        if (mesh == null || mesh.VerticesCount == 0)
            return;
        var (min, max) = MachineMeshes.Bounds(mesh);
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
            var c = Middle;
            var light = Footprint.ToWorld(new Int3((int)MathF.Floor(c.X), 1, (int)MathF.Floor(c.Z)), _be.Side);
            prog = rapi.PreparedStandardShader(pos.X + light.X, pos.Y + light.Y, pos.Z + light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (_meshes[i] is not { Disposed: false } mesh || !Fitted(i))
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
        MillRequires.Fitted(_parts.Parts[part].Requires, _be.SashCount, _be.HasCrankshaft, _be.HasBladeKit, _be.HasLevers);

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
    /// already advances that angle smoothly between server updates), adds the turn's size to the
    /// shaft's travel (the rectified gears turn with it, the same way whichever way the shaft
    /// turns), and takes the lifting input from the client's cycle: it changes only at the top and
    /// the bottom of the travel, where the trip steps' two halves agree, so the levers never jump.
    /// While cutting it plays a saw stroke per half turn and puffs sawdust.</summary>
    private void AdvanceClock(float dt, bool far)
    {
        var phase = _be.Phase;
        _lifting = _be.ClientRising ? 1 : 0;
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

    private void PlayStroke()
    {
        _stroke++;
        char band = MillMotion.SpeedBand(_be.ShaftSpeed);
        char dir = _stroke % 2 == 0 ? 'f' : 'b';
        int variant = 1 + _stroke / 2 % 2;
        var at = WorldPoint(new Float3(Middle.X, 1.5f, Middle.Z));
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

    // the middle of the machine (native frame): over the bed's centre, where the trunk lies
    private Float3 Middle => _bed?.Origin ?? new Float3(0.5f, 0.5f, 0.5f);

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
        // the part meshes are MachinePartMeshes', shared with every other mill; the trunk's is this one's
        _trunkMesh?.Dispose();
    }
}
