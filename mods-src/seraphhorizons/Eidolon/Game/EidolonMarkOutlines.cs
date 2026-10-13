using SeraphHorizons.Mod.Eidolon.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Eidolon;

/// <summary>
/// X-ray outlines of the held command tool's marks (README "Eidolon", the command tool): each mark's
/// box (a first corner, a closed area, the block marked after an area) drawn as its 12 edges through
/// terrain, over the game's translucent fill (<see cref="EidolonCommanderSystem"/>), so a corner marked
/// before digging stays in sight from inside the rock. While an area waits for its opposite corner,
/// a dimmer outline previews the box from the first corner to the block the player is aiming at,
/// red when the server would refuse it (<see cref="EidolonMarking.Preview"/>, the rule the second
/// click goes through). Client side, for the holder only, while the tool is in the active hotbar slot.
///
/// <para>Drawn the way the game draws its selected block outline (<c>SystemSelectedBlockOutline</c>
/// with <c>WireframeCube</c>): one unit cube of lines (<see cref="LineMeshUtil.GetCube"/>), uploaded
/// once and scaled to each box, with the game's wireframe shader at the
/// <see cref="EnumRenderStage.AfterFinalComposition"/> stage. <c>WireframeCube.Render</c> turns the
/// depth test on, so this sets up the same shader itself with the depth test off, which is what lets
/// the lines show through blocks, and leaves the depth state as <c>WireframeCube</c> does.</para>
/// </summary>
public sealed class EidolonMarkOutlines : IRenderer, IDisposable
{
    /// <summary>The marks' lines, a little thicker than the selected block's 1.6.</summary>
    private const float MarkLineWidth = 2.5f;

    private const float PreviewLineWidth = 1.6f;

    /// <summary>The preview's alpha, against the marks' opaque lines.</summary>
    private const float PreviewAlpha = 0.55f;

    private static readonly Vec4f RefusedColor = new(1f, 0.25f, 0.2f, 0.85f);

    private readonly ICoreClientAPI _capi;
    private readonly MeshRef _cube;
    private readonly Matrixf _modelView = new();
    private readonly List<(MarkArea Box, Vec4f Color)> _boxes = [];
    private EidolonCommandMode? _mode;
    private Marks _marks = Marks.None;

    public EidolonMarkOutlines(ICoreClientAPI capi)
    {
        _capi = capi;
        // The game's unit wireframe cube: edges from -1 to 1 halved and moved to 0..1, white, so the
        // shader's colour uniform gives each box its colour. Flag 256 is WireframeCube's: the shader
        // moves the lines a hair toward the camera by it.
        var cube = LineMeshUtil.GetCube(-1);
        cube.Scale(new Vec3f(), 0.5f, 0.5f, 0.5f);
        cube.Translate(0.5f, 0.5f, 0.5f);
        cube.Flags = new int[cube.VerticesCount];
        Array.Fill(cube.Flags, 256);
        _cube = capi.Render.UploadMesh(cube);
        capi.Event.RegisterRenderer(this, EnumRenderStage.AfterFinalComposition, "seraphhorizons-eidolonmarks");
    }

    public double RenderOrder => 0.95;

    public int RenderRange => 999;

    /// <summary>The outline colour for a mark with fill colour <paramref name="fill"/>: its hue at
    /// <paramref name="alpha"/>. The fill goes into the highlight mesh as a raw int, whose low byte
    /// the shader reads as red (so <c>ColorUtil.ToRgba(a, r, g, b)</c>'s <c>b</c> shows as red).</summary>
    public static Vec4f LineColor(int fill, float alpha = 1f) =>
        new((fill & 0xFF) / 255f, ((fill >> 8) & 0xFF) / 255f, ((fill >> 16) & 0xFF) / 255f, alpha);

    /// <summary>Sets what is shown: the held tool's mode and its marks (no mode: nothing), the boxes
    /// with their colours.</summary>
    public void Show(EidolonCommandMode? mode, Marks marks, IEnumerable<(MarkArea Box, Vec4f Color)> boxes)
    {
        _mode = mode;
        _marks = marks;
        _boxes.Clear();
        _boxes.AddRange(boxes);
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_mode is not { } mode || _capi.World?.Player is not { } player
            || player.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Collectible is not ItemEidolonCommander)
            return;
        AreaPreview? preview = null;
        if (player.CurrentBlockSelection?.Position is { } aimed)
            preview = EidolonMarking.Preview(mode.Mark, _marks, new MarkPos(aimed.X, aimed.Y, aimed.Z), mode.MaxAreaSide, mode.MaxAreaBlocks);
        if (_boxes.Count == 0 && preview == null)
            return;

        var render = _capi.Render;
        var camera = player.Entity.CameraPos;
        var shader = _capi.Shader.GetProgram((int)EnumShaderProgram.Wireframe);
        shader.Use();
        render.GLDisableDepthTest();
        render.GLDepthMask(false);
        render.GlToggleBlend(true);
        shader.Uniform("origin", 0f, 0f, 0f);
        shader.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
        if (preview is { } p)
        {
            var color = p.Refused ? RefusedColor : LineColor(EidolonCommanderSystem.AreaFill, PreviewAlpha);
            Draw(shader, camera, p.Area, color, PreviewLineWidth);
        }
        foreach (var (box, color) in _boxes)
            Draw(shader, camera, box, color, MarkLineWidth);
        shader.Stop();
        render.LineWidth = 1.6f;
        if (RuntimeEnv.OS != OS.Mac)
            render.GLEnableDepthTest();
        render.GLDepthMask(true);
    }

    private void Draw(IShaderProgram shader, Vec3d camera, MarkArea box, Vec4f color, float width)
    {
        _modelView.Identity().Set(_capi.Render.CameraMatrixOrigin)
            .Translate(box.Min.X - camera.X, box.Min.Y - camera.Y, box.Min.Z - camera.Z)
            .Scale(box.SizeX, box.SizeY, box.SizeZ);
        _capi.Render.LineWidth = width;
        shader.UniformMatrix("modelViewMatrix", _modelView.Values);
        shader.Uniform("colorIn", color);
        _capi.Render.RenderMesh(_cube);
    }

    public void Dispose()
    {
        _capi.Event.UnregisterRenderer(this, EnumRenderStage.AfterFinalComposition);
        _cube.Dispose();
    }
}
