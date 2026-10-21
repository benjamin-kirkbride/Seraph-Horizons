using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// The shader every machine renderer draws its parts with in the opaque pass (<see cref="Prepare"/>, client
/// only): the mod's copy of the game's <c>standard</c> shader (<c>shaders/seraphhorizons-machine.*</c>) that
/// shades as the chunk shader (<c>chunkopaque</c>) shades placed blocks, so the parts match the blocks around
/// them:
/// <list type="bullet">
/// <item>Shadow: <c>standard</c> multiplies its normal shading by the shadow map, where the chunk shader
/// takes the smaller of the two with a lower floor, so a part drawn in the shadow passes shadows itself:
/// faces turned from the sun about twice as dark as a block's, and the shadow map's acne flickering on them
/// as they move. The copy combines them as the chunk shader does.</item>
/// <item>Daylight gain: the chunk shader brightens by up to about 1.23× in daylight
/// (<c>applyFogAndShadowFromBrightness</c>), <c>standard</c> does not. The copy does too.</item>
/// </list>
/// Tested in game (#1412), the shadow combine made the big difference and the gain a slight one; both are
/// kept, as both are what the chunk shader does.
/// <para>The program is a <see cref="ShaderProgramStandard"/> with this mod's domain, so its uniform
/// setters, and <c>ShaderProgramBase.Use</c> setting the shadow maps and fog for a program including
/// <c>shadowcoords.vsh</c> and <c>fogandlight.fsh</c>, work as on the game's own; it is prepared with what
/// <c>RenderAPIGame.PreparedStandardShader</c> sets (game 1.22.7). It is registered again on every shader
/// reload, which disposes all programs. If it fails to compile the game's
/// <see cref="IRenderAPI.PreparedStandardShader"/> is used, with an error in the log.</para>
/// </summary>
public class MachineShading : ModSystem
{
    public const string PassName = "seraphhorizons-machine";

    private static ShaderProgramStandard? _program;
    private ICoreClientAPI? _capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        api.Event.ReloadShader += LoadShader;
        LoadShader();
    }

    private bool LoadShader()
    {
        if (_capi == null)
            return true;
        var prog = new ShaderProgramStandard { AssetDomain = "seraphhorizons" };
        _capi.Shader.RegisterFileShaderProgram(PassName, prog);
        bool ok = prog.Compile() && !prog.LoadError;
        _program = ok ? prog : null;
        if (!ok)
            _capi.Logger.Error("[seraphhorizons] the machine shader ({0}) did not compile; machines use the game's standard shader", PassName);
        return ok;
    }

    /// <summary>The shader to draw machine parts with in the opaque pass, in use and prepared as
    /// <see cref="IRenderAPI.PreparedStandardShader"/> prepares the game's, lit from <paramref name="light"/>.
    /// The caller sets the view and projection matrices and stops it.</summary>
    public static IStandardShaderProgram Prepare(ICoreClientAPI capi, BlockPos light)
    {
        var rapi = capi.Render;
        var prog = _program;
        if (prog == null || prog.Disposed)
            return rapi.PreparedStandardShader(light.X, light.Y, light.Z);
        prog.Use();
        prog.RgbaTint = ColorUtil.WhiteArgbVec;
        prog.RgbaAmbientIn = rapi.AmbientColor;
        prog.RgbaLightIn = capi.World.BlockAccessor.GetLightRGBs(light.X, light.Y, light.Z);   // the world map's, as the game's
        prog.RgbaFogIn = rapi.FogColor;
        prog.NormalShaded = 1;
        prog.ExtraGlow = 0;
        prog.FogMinIn = rapi.FogMin;
        prog.FogDensityIn = rapi.FogDensity;
        prog.DontWarpVertices = 0;
        prog.AddRenderFlags = 0;
        prog.ExtraZOffset = 0f;
        prog.OverlayOpacity = 0f;
        prog.DamageEffect = 0f;
        prog.ExtraGodray = 0f;
        prog.ProjectionMatrix = rapi.CurrentProjectionMatrix;
        return prog;
    }

    public override void Dispose()
    {
        if (_capi != null)
            _capi.Event.ReloadShader -= LoadShader;
        _program?.Dispose();
        _program = null;
    }
}
