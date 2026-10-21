using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// The shader every machine renderer draws its parts with in the opaque pass (<see cref="Prepare"/>), and
/// the client's <c>.sh machinelight</c>, a diagnostic that switches three fixes for machines drawn darker
/// than the blocks around them, so their effect can be seen one at a time (client only; each takes effect
/// on the next frame on every machine):
/// <list type="bullet">
/// <item><c>shadow</c>: the game's <c>standard</c> shader multiplies its normal shading by the shadow map,
/// where the chunk shader (<c>chunkopaque</c>) takes the smaller of the two with a lower floor, so a part
/// drawn in the shadow passes shadows itself: faces turned from the sun about twice as dark as a block's,
/// and the shadow map's acne flickering on them as they move. On, the parts are drawn with the mod's copy
/// of <c>standard</c> (<c>shaders/seraphhorizons-machine.*</c>) combining them as the chunk shader does.</item>
/// <item><c>boost</c>: the chunk shader brightens by up to about 1.23× in daylight
/// (<c>applyFogAndShadowFromBrightness</c>), <c>standard</c> does not. On, the copy does too.</item>
/// <item><c>cell</c>: on, the shared <see cref="MachineRenderer"/> lights the parts from the cell above the
/// footprint's middle (<see cref="IMachineRig.LightCell"/>); off, from the cell above the controller, as
/// the ore machines' renderer once did, often a cell inside the machine. The rosser's, mill's and
/// furnace's renderers pick their own cell and ignore it.</item>
/// </list>
/// With <c>shadow</c> and <c>boost</c> off (the default) <see cref="Prepare"/> is the game's
/// <see cref="IRenderAPI.PreparedStandardShader"/>, exactly as before. The toggles are not saved.
/// <para>The program is a <see cref="ShaderProgramStandard"/> with this mod's domain, so its uniform
/// setters, and <c>ShaderProgramBase.Use</c> setting the shadow maps and fog for a program including
/// <c>shadowcoords.vsh</c> and <c>fogandlight.fsh</c>, work as on the game's own; it is prepared with what
/// <c>RenderAPIGame.PreparedStandardShader</c> sets (game 1.22.7). It is registered again on every shader
/// reload, which disposes all programs. If it fails to compile the game's shader is used.</para>
/// </summary>
public class MachineShading : ModSystem
{
    public const string PassName = "seraphhorizons-machine";
    public const string SubCommand = "machinelight";

    /// <summary>Fix 1: combine shadow map and normal shading as the chunk shader does.</summary>
    public static bool Shadow { get; private set; }
    /// <summary>Fix 2: the chunk shader's daylight gain.</summary>
    public static bool Boost { get; private set; }
    /// <summary>Fix 3: light the shared renderer's parts from <see cref="IMachineRig.LightCell"/>.</summary>
    public static bool Cell { get; private set; } = true;

    private static ShaderProgramStandard? _program;
    private static bool _failed;
    private ICoreClientAPI? _capi;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        _capi = api;
        Shadow = false;
        Boost = false;
        Cell = true;
        api.Event.ReloadShader += LoadShader;
        LoadShader();

        var parsers = api.ChatCommands.Parsers;
        var root = api.ChatCommands.GetOrCreate("sh");
        if (string.IsNullOrEmpty(root.Description))
            root.WithDescription("Seraph Horizons commands.");
        root.BeginSubCommand(SubCommand)
                .WithDescription("Machine lighting fixes, for testing: .sh machinelight [shadow|boost|cell|all|none] [on|off]")
                .WithArgs(parsers.OptionalWord("shadow|boost|cell|all|none"), parsers.OptionalWord("on|off"))
                .HandleWith(args => Command(args[0] as string, args[1] as string))
            .EndSubCommand();
    }

    private bool LoadShader()
    {
        if (_capi == null)
            return true;
        var prog = new ShaderProgramStandard { AssetDomain = "seraphhorizons" };
        _capi.Shader.RegisterFileShaderProgram(PassName, prog);
        bool ok = prog.Compile() && !prog.LoadError;
        _program = ok ? prog : null;
        _failed = !ok;
        if (!ok)
            _capi.Logger.Error("[seraphhorizons] the machine shader ({0}) did not compile; machines use the game's standard shader", PassName);
        return ok;
    }

    private static TextCommandResult Command(string? fix, string? state)
    {
        bool? to = null;   // null: toggle
        switch (state?.ToLowerInvariant())
        {
            case null:
                break;
            case "on" or "1" or "true":
                to = true;
                break;
            case "off" or "0" or "false":
                to = false;
                break;
            default:
                return TextCommandResult.Error($"'{state}' is not on or off");
        }
        switch (fix?.ToLowerInvariant())
        {
            case null:
                break;
            case "shadow":
                Shadow = to ?? !Shadow;
                break;
            case "boost":
                Boost = to ?? !Boost;
                break;
            case "cell":
                Cell = to ?? !Cell;
                break;
            case "all":
                Shadow = Boost = Cell = true;
                break;
            case "none":
                Shadow = Boost = Cell = false;
                break;
            default:
                return TextCommandResult.Error($"'{fix}' is not shadow, boost, cell, all or none");
        }
        return TextCommandResult.Success(State());
    }

    private static string State()
    {
        static string On(bool b) => b ? "on" : "off";
        string s = $"machine light: shadow {On(Shadow)} (min of shadow map and normal shading, as blocks), "
                 + $"boost {On(Boost)} (daylight gain, as blocks), cell {On(Cell)} (light from above the footprint's middle, off: above the controller)";
        if ((Shadow || Boost) && _failed)
            s += ". The machine shader failed to compile (see client-main.log), so shadow and boost do nothing";
        return s;
    }

    /// <summary>The shader to draw machine parts with in the opaque pass, in use and prepared as
    /// <see cref="IRenderAPI.PreparedStandardShader"/> prepares the game's, lit from <paramref name="light"/>.
    /// The caller sets the view and projection matrices and stops it.</summary>
    public static IStandardShaderProgram Prepare(ICoreClientAPI capi, BlockPos light)
    {
        var rapi = capi.Render;
        var prog = _program;
        if ((!Shadow && !Boost) || prog == null || prog.Disposed)
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
        prog.Uniform("chunkShadow", Shadow ? 1 : 0);
        prog.Uniform("chunkBoost", Boost ? 1 : 0);
        return prog;
    }

    public override void Dispose()
    {
        if (_capi != null)
            _capi.Event.ReloadShader -= LoadShader;
        _program?.Dispose();
        _program = null;
        _failed = false;
    }
}
