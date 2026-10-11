using SeraphHorizons.Mod.Machines;
using SeraphHorizons.Mod.Machines.Core;
using SeraphHorizons.Mod.Rosser.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Rosser;

/// <summary>
/// Draws the rosser's moving parts and the travelling trunk (client only; created and disposed by
/// <see cref="BERosser"/>, and only while its block is the rosser). One mesh per rig part from
/// <c>shapes/block/rosser.json</c> (<see cref="MachineMeshes.PartMeshes"/>, one tessellation split
/// by part, shared by every rosser through <see cref="MachinePartMeshes"/>), each drawn with its rig
/// matrix turned to the rosser's facing, and only when its <c>requires</c> is fitted; the scraper
/// tips (<c>heads</c>) take the ingot texture of the heads' metal (a cached set per metal). The
/// drip's pipes are a part per metal (<c>pipecopper</c>, <c>pipelead</c>, in the block's pipe
/// textures), and only the fitted metal's is drawn (<see cref="RosserParts.Fitted"/>). The static frame part is not
/// drawn here: the block's own shape (rosser_frame.json, the same elements) draws it in the chunk
/// mesh. Everything is read from the rig at load; no element name or coordinate is known here.
/// <para>The rig is posed every frame from: θ, the shaft angle about the input shaft's native z
/// axis (<see cref="MillMotion.NativeShaftAngle"/>), accumulated; ψ, the total angle it turned
/// either way (the rectified train); T, the block entity's travel, interpolated per frame between
/// client ticks; φ, the feed, grown by T's advance (<see cref="RosserVisuals.FeedAdvance"/>, so the
/// rolls turn exactly with the trunk); p, the trunk's presence, eased per frame; and k, the class,
/// held while p eases out.</para>
/// <para>The trunk is Logging Expanded's own block of its wood, as its class is shown
/// (<see cref="Trunks.ShownBlock"/>, lg or xxl), cut into one-block segments along its length
/// (<see cref="MachineMeshes.Segments"/>), once with bark and once debarked: a segment is drawn
/// debarked once its centre is past the spud heads (<see cref="RosserRig.TipAt"/>: downstream of
/// the ring's plane, further for a thick trunk, where the heads hide the change), and every
/// segment once delivered.</para>
/// <para>Sounds and particles belong to states (design §4.9): while feeding and running with the
/// trunk under the heads, the scraping sound and bark chips at the heads; while feeding and running
/// wet with the trunk under the drip, drips. Silent on purpose: an empty rosser whose ring turns
/// (the network's axles make their own noise); a waiting trunk without power; a stalled trunk; a
/// delivered trunk; and a feeding trunk not yet at the ring or past it (the server plays the
/// sticks' crack, the dog's clunk at load and delivery, and the heads breaking).</para>
/// </summary>
public sealed class RosserRenderer : IRenderer
{
    public static readonly AssetLocation ShapeLoc = new(RosserSystem.Domain, "shapes/block/rosser.json");
    private const int DrawRange = 64;              // blocks from the camera to the middle of the machine
    private const float ChipInterval = 0.1f;       // seconds between bark-chip puffs at the ring
    private const float DripInterval = 0.15f;      // seconds between drips
    private const float ScrapeInterval = 1.2f;     // seconds between scraping sounds (each about that long)
    private const string HeadsRequires = "heads";

    private readonly ICoreClientAPI _capi;
    private readonly BERosser _be;
    private readonly RosserRig _rig;
    private readonly RigParts _parts;
    private readonly TrunkPath _path;
    private readonly MultiTextureMeshRef?[] _meshes;  // the shared sets' (tips from the metal's); never disposed here
    private readonly bool[] _drawn;                // false for the static frame part(s)
    private readonly bool[] _isTip;
    private string? _headMetal;
    private bool _built;

    // the trunk: per segment, with bark and debarked
    private MultiTextureMeshRef?[] _barkSegments = [];
    private MultiTextureMeshRef?[] _bareSegments = [];
    private Float3[] _segmentCentres = [];
    private (Float3 Min, Float3 Max) _trunkBounds;
    private AssetLocation? _trunkCode;

    // the client's clocks
    private double _theta;
    private double _psi;
    private double _phi;
    private double _lastAngle;
    private bool _angleSeeded;
    private double _lastTravel;
    private bool _travelSeeded;
    private float _presence;
    private int _shownClass;
    private double _shownTravel;
    private float _chipTimer;
    private float _dripTimer;
    private float _scrapeTimer;
    private int _scrape;

    private readonly Matrixf _model = new();

    public double RenderOrder => 0.5;
    public int RenderRange => DrawRange;

    public RosserRenderer(ICoreClientAPI capi, BERosser be, RosserRig rig)
    {
        _capi = capi;
        _be = be;
        _rig = rig;
        _parts = rig.MovingParts;
        _path = rig.Path;
        int n = _parts.Parts.Count;
        _meshes = new MultiTextureMeshRef?[n];
        _drawn = new bool[n];
        _isTip = new bool[n];
        for (int i = 0; i < n; i++)
        {
            var p = _parts.Parts[i];
            _drawn[i] = p.Requires != null || p.Ride != null || p.Drivers.Count > 0;
            _isTip[i] = p.Requires == HeadsRequires;
        }
        capi.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "rosser");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowFar, "rosser");
        capi.Event.RegisterRenderer(this, EnumRenderStage.ShadowNear, "rosser");
    }

    // ---- meshes ----

    private void BuildMeshes(bool tipsOnly)
    {
        _built = true;
        _headMetal = _be.HeadMetal;
        string metal = _headMetal ?? "";
        var tips = MachinePartMeshes.Get(_capi, "tips|" + metal, ShapeLoc, _parts, i => _drawn[i] && _isTip[i],
            () => new MachineMeshes.MetalTextureSource(_capi.Tesselator.GetTextureSource(_be.Block), MachineMeshes.MetalTexture(_capi, _headMetal)),
            "rosser");
        var rest = tipsOnly ? null : MachinePartMeshes.Get(_capi, "parts", ShapeLoc, _parts, i => _drawn[i] && !_isTip[i],
            () => _capi.Tesselator.GetTextureSource(_be.Block), "rosser");
        if (tips == null)
        {
            if (!tipsOnly)
                _capi.Logger.Error("[seraphhorizons] Rosser: {0} is missing; the rosser's moving parts are not drawn", ShapeLoc);
            return;
        }
        for (int i = 0; i < _meshes.Length; i++)
        {
            if (!_drawn[i] || (tipsOnly && !_isTip[i]))
                continue;
            _meshes[i] = _isTip[i] ? tips.Meshes[i] : rest?.Meshes[i];
        }
    }

    private void DisposeTrunk()
    {
        foreach (var mesh in _barkSegments.Concat(_bareSegments))
            mesh?.Dispose();
        _barkSegments = _bareSegments = [];
        _segmentCentres = [];
        _trunkCode = null;
    }

    /// <summary>Builds the trunk's segments when the trunk (its wood and class) changed: Logging
    /// Expanded's clean and debarked blocks of the shown size, cut into one-block pieces.</summary>
    private void SyncTrunk()
    {
        var trunk = _be.Trunk;
        if (trunk?.Block is not { } block || Trunks.ShownBlock(_capi.World, trunk) is not { } shown)
        {
            if (_trunkCode != null)
                DisposeTrunk();
            return;
        }
        // the bark and debarked looks of the same wood and shown size
        var bark = _capi.World.GetBlock(shown.CodeWithVariant("branches", "no")) is { Id: > 0 } b ? b : shown;
        var bare = _capi.World.GetBlock(shown.CodeWithVariant("branches", "debarked")) is { Id: > 0 } d ? d : shown;
        if (bark.Code.Equals(_trunkCode))
            return;
        DisposeTrunk();
        _trunkCode = bark.Code;
        int length = (int)Math.Round(_path.LengthOf((int)TrunkBox.ClassOf(block.Variant["size"])));
        _capi.Tesselator.TesselateBlock(bark, out var barkMesh);
        _capi.Tesselator.TesselateBlock(bare, out var bareMesh);
        if (barkMesh == null || barkMesh.VerticesCount == 0 || length <= 0)
            return;
        _trunkBounds = MachineMeshes.Bounds(barkMesh);
        var barkPieces = MachineMeshes.Segments(barkMesh, length);
        var barePieces = bareMesh is { VerticesCount: > 0 } ? MachineMeshes.Segments(bareMesh, length) : null;
        if (barkPieces == null || barePieces == null)
        {
            // Not four-vertex faces: the whole trunk changes at once, as it passes the ring's middle.
            _barkSegments = [_capi.Render.UploadMultiTextureMesh(barkMesh)];
            _bareSegments = [_capi.Render.UploadMultiTextureMesh(bareMesh is { VerticesCount: > 0 } ? bareMesh : barkMesh)];
            var (min, max) = _trunkBounds;
            _segmentCentres = [new Float3((min.X + max.X) / 2, (min.Y + max.Y) / 2, (min.Z + max.Z) / 2)];
            return;
        }
        _barkSegments = barkPieces.Select(m => (MultiTextureMeshRef?)_capi.Render.UploadMultiTextureMesh(m)).ToArray();
        _bareSegments = barePieces.Select(m => (MultiTextureMeshRef?)_capi.Render.UploadMultiTextureMesh(m)).ToArray();
        _segmentCentres = Enumerable.Range(0, length).Select(s => MachineMeshes.SegmentCentre(_trunkBounds, s)).ToArray();
    }

    // ---- frame ----

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        var pos = _be.Pos;
        var camPos = _capi.World.Player?.Entity?.CameraPos;
        if (camPos == null)
            return;
        var middle = WorldPoint(MiddleNative);
        bool far = camPos.SquareDistanceTo(middle.X, middle.Y, middle.Z) > DrawRange * DrawRange;

        if (stage == EnumRenderStage.Opaque)
        {
            if (!_built)
                BuildMeshes(tipsOnly: false);
            else if (_headMetal != _be.HeadMetal)
                BuildMeshes(tipsOnly: true);
            SyncTrunk();
            AdvanceClocks(deltaTime, far);
        }
        if (far)
            return;

        int k = _shownClass;
        var input = new RigInput(_theta, Travel: _psi, Work: _shownTravel, Class: k, Presence: _presence, Feed: _phi);
        var mats = _parts.Matrices(input);
        var facing = Mat4.Facing(_be.Side);
        var rapi = _capi.Render;
        rapi.GlDisableCullFace();
        IStandardShaderProgram? prog = null;
        if (stage == EnumRenderStage.Opaque)
        {
            // light from the cell over the middle of the machine, outside the model
            var light = LightCell();
            prog = rapi.PreparedStandardShader(light.X, light.Y, light.Z);
            prog.ViewMatrix = rapi.CameraMatrixOriginf;
            prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        }
        for (int i = 0; i < _meshes.Length; i++)
            if (_meshes[i] is { Disposed: false } mesh && _be.Fitted(_parts.Parts[i].Requires))
                MachineMeshes.Draw(_capi, _model, mesh, Mat4.Multiply(facing, mats[i]), prog, camPos, pos);
        DrawTrunk(facing, prog, camPos, pos);
        prog?.Stop();
        rapi.GlEnableCullFace();
    }

    private void DrawTrunk(float[] facing, IStandardShaderProgram? prog, Vec3d camPos, BlockPos pos)
    {
        if (_be.Trunk == null || _barkSegments.Length == 0 || _be.TrunkClass == TrunkClass.None)
            return;
        double travel = _be.ClientTravel;
        var place = MillMotion.TrunkOnAxis(_trunkBounds.Min, _trunkBounds.Max, _path, (int)_be.TrunkClass, travel);
        var m = Mat4.Multiply(facing, place);
        bool delivered = _be.State == RosserState.Delivered;
        float tips = _rig.TipAt((int)_be.TrunkClass);
        for (int s = 0; s < _barkSegments.Length; s++)
        {
            var centre = Mat4.Apply(place, _segmentCentres[s]);
            float along = _path.Axis == Axis.Z ? centre.Z : centre.X;
            var mesh = delivered || along > tips ? _bareSegments[s] : _barkSegments[s];
            if (mesh != null)
                MachineMeshes.Draw(_capi, _model, mesh, m, prog, camPos, pos);
        }
    }

    // ---- motion, sound, particles ----

    /// <summary>Turns the client's clocks: θ and ψ by the power ghost's angle change (the network
    /// already advances that angle smoothly between server updates), T from the block entity's
    /// per-frame interpolation, φ by T's advance, p eased toward the trunk being there, k held
    /// while p eases out. Then the states' sounds and particles.</summary>
    private void AdvanceClocks(float dt, bool far)
    {
        float speed = _be.ShaftSpeed;
        double angle = MillMotion.NativeShaftAngle(_be.Side, _be.ShaftAngle, Axis.Z);
        if (!_angleSeeded || speed <= 0)
        {
            _lastAngle = angle;
            _angleSeeded = true;
        }
        else
        {
            double delta = MillMotion.WrappedDelta(_lastAngle, angle);
            _theta += delta;
            _psi += Math.Abs(delta);
            _lastAngle = angle;
            // keep the clocks small; the shipped rig's shaft ratios are fractions, so wrap by many turns
            if (Math.Abs(_theta) > 1e6)
                _theta = 0;
        }

        bool loaded = _be.Trunk != null && _be.TrunkClass != TrunkClass.None;
        double travel = loaded ? _be.ClientTravel : _shownTravel;
        if (_travelSeeded)
            _phi += RosserVisuals.FeedAdvance(_lastTravel, travel, _rig.Feed.BlocksPerRadian);
        _lastTravel = travel;
        _travelSeeded = true;
        _presence = RosserVisuals.EasePresence(_presence, loaded, dt);
        _shownClass = RosserVisuals.ShownClass((int)_be.TrunkClass, _shownClass, _presence);
        // with no trunk the poses ease back from where the last one was
        if (loaded)
            _shownTravel = travel;

        if (far || !loaded || !_be.Running || _be.State != RosserState.Feeding)
            return;
        double nose = _path.Nose(travel), tail = _path.Tail(travel, (int)_be.TrunkClass);
        float tips = _rig.TipAt((int)_be.TrunkClass);
        bool underHeads = tail <= tips && tips <= nose;
        if (underHeads)
        {
            _scrapeTimer -= dt;
            if (_scrapeTimer <= 0)
            {
                _scrapeTimer = ScrapeInterval;
                PlayScrape();
            }
            _chipTimer += dt;
            if (_chipTimer >= ChipInterval)
            {
                _chipTimer = 0;
                SpawnChips();
            }
        }
        else
            _scrapeTimer = 0;
        if (_be.Wet && _path.Stations.TryGetValue(RosserRig.DripStation, out float drip) && tail <= drip && drip <= nose)
        {
            _dripTimer += dt;
            if (_dripTimer >= DripInterval)
            {
                _dripTimer = 0;
                SpawnDrips(drip);
            }
        }
    }

    private void PlayScrape()
    {
        int variant = 1 + _scrape++ % 3;
        float tips = _rig.TipAt((int)_be.TrunkClass);
        var at = WorldPoint(new Float3(_path.Axis == Axis.Z ? _path.Origin.X : tips, _path.Origin.Y, _path.Axis == Axis.Z ? tips : _path.Origin.Z));
        _capi.World.PlaySoundAt(new AssetLocation("immersivewoodworking", $"sounds/debark/debarking{variant}"),
            at.X, at.Y, at.Z, null, true, 24, 0.8f);
    }

    /// <summary>The trunk's half-width as shown (its flats), blocks: the rig's radius, else half a
    /// block per block of width.</summary>
    private float HalfWidth(int k) =>
        _path.Radii[k].Flats > 0 ? _path.Radii[k].Flats / 16f : TrunkBox.Size((TrunkClass)k).Width / 2f;

    private void SpawnChips()
    {
        float r = HalfWidth((int)_be.TrunkClass) + 0.05f;
        var o = _path.Origin;
        float tips = _rig.TipAt((int)_be.TrunkClass);
        // around the trunk where the heads scrape
        Float3 At(float across, float up) => _path.Axis == Axis.Z
            ? new Float3(o.X + across, o.Y + up, tips)
            : new Float3(tips, o.Y + up, o.Z + across);
        var lo = WorldPoint(At(-r, -r));
        var hi = WorldPoint(At(r, r));
        _capi.World.SpawnParticles(4, ColorUtil.ToRgba(255, 70, 92, 120),
            new Vec3d(Math.Min(lo.X, hi.X), lo.Y, Math.Min(lo.Z, hi.Z)), new Vec3d(Math.Max(lo.X, hi.X), hi.Y, Math.Max(lo.Z, hi.Z)),
            new Vec3f(-0.6f, -0.2f, -0.6f), new Vec3f(0.6f, 0.4f, 0.6f), 1.4f, 1f, 0.5f, EnumParticleModel.Cube, null);
    }

    private void SpawnDrips(float drip)
    {
        float r = HalfWidth((int)_be.TrunkClass);
        var o = _path.Origin;
        Float3 At(float across) => _path.Axis == Axis.Z
            ? new Float3(o.X + across, o.Y + r + 0.15f, drip)
            : new Float3(drip, o.Y + r + 0.15f, o.Z + across);
        var lo = WorldPoint(At(-r * 0.6f));
        var hi = WorldPoint(At(r * 0.6f));
        _capi.World.SpawnParticles(1, ColorUtil.ToRgba(180, 190, 160, 70),
            new Vec3d(Math.Min(lo.X, hi.X), lo.Y, Math.Min(lo.Z, hi.Z)), new Vec3d(Math.Max(lo.X, hi.X), hi.Y, Math.Max(lo.Z, hi.Z)),
            new Vec3f(0, -0.2f, 0), new Vec3f(0, -0.1f, 0), 0.6f, 1f, 0.25f, EnumParticleModel.Quad, null);
    }

    // the middle of the machine (native frame): the trunk's path at the ring
    private Float3 MiddleNative => _path.Axis == Axis.Z
        ? new Float3(_path.Origin.X, _path.Origin.Y, _rig.Ring)
        : new Float3(_rig.Ring, _path.Origin.Y, _path.Origin.Z);

    /// <summary>The cell above the highest cell over the ring's station: outside the model, so the
    /// moving parts are lit as the machine around them, not as the inside of a solid cell.</summary>
    private BlockPos LightCell()
    {
        var mid = MiddleNative;
        int x = (int)MathF.Floor(mid.X), z = (int)MathF.Floor(mid.Z);
        int top = _rig.Cells.Where(c => c.Pos.X == x && c.Pos.Z == z).Select(c => c.Pos.Y).DefaultIfEmpty(0).Max();
        return _be.CellPos(new Int3(x, top + 1, z));
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
        // the part meshes are MachinePartMeshes', shared with every other rosser; the trunk's are this one's
        DisposeTrunk();
    }
}
