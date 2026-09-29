using System.Collections;
using System.Globalization;
using System.Reflection;
using SeraphHorizons.IconExport.Core;
using Vintagestory.API.Client;

namespace SeraphHorizons.IconExport.Game;

/// <summary>
/// Reads OpenGL state for the log. The mod references only VintagestoryAPI (so CI can build it
/// against the server), and the API cannot read GL state, so this finds the client's OpenTK
/// GL bindings by reflection. Every read is optional: if anything is missing it reports
/// "unavailable" and the export works the same.
/// </summary>
internal sealed class GlReader
{
    private readonly MethodInfo? _getUniformF;
    private readonly MethodInfo? _getUniformI;
    private readonly MethodInfo? _getInteger;
    private readonly Type? _pname;

    public string? Problem { get; }

    public GlReader()
    {
        try
        {
            Type? gl = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "OpenTK.Graphics")
                .Select(a => a.GetType("OpenTK.Graphics.OpenGL.GL"))
                .FirstOrDefault(t => t != null);
            if (gl == null)
            {
                Problem = "OpenTK.Graphics.OpenGL.GL not loaded";
                return;
            }
            MethodInfo[] methods = gl.GetMethods(BindingFlags.Public | BindingFlags.Static);
            _getUniformF = Find(methods, "GetUniform", typeof(int), typeof(int), typeof(float[]));
            _getUniformI = Find(methods, "GetUniform", typeof(int), typeof(int), typeof(int[]));
            _getInteger = methods.FirstOrDefault(m => m.Name == "GetInteger"
                && m.GetParameters() is { Length: 2 } p && p[0].ParameterType.IsEnum && p[1].ParameterType == typeof(int[]));
            _pname = _getInteger?.GetParameters()[0].ParameterType;
            if (_getUniformF == null || _getUniformI == null || _getInteger == null)
            {
                Problem = "GL.GetUniform or GL.GetInteger overloads not found";
            }
        }
        catch (Exception e)
        {
            Problem = e.GetType().Name + ": " + e.Message;
        }
    }

    private static MethodInfo? Find(MethodInfo[] methods, string name, params Type[] types) =>
        methods.FirstOrDefault(m => m.Name == name && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(types));

    /// <summary>"name=value ..." for every GUI uniform, as the GL program holds them now.</summary>
    public string Uniforms(IShaderProgram? shader)
    {
        if (Problem != null)
        {
            return "unavailable (" + Problem + ")";
        }
        if (shader == null)
        {
            return "unavailable (no GUI shader)";
        }
        try
        {
            if (shader.GetType().GetField("uniformLocations", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(shader) is not IDictionary locations)
            {
                return "unavailable (uniform locations not found)";
            }
            var parts = new List<string>();
            foreach (UniformDefault u in GuiUniforms.Defaults)
            {
                if (!locations.Contains(u.Name) || locations[u.Name] is not int loc || loc < 0)
                {
                    parts.Add(u.Name + "=absent");
                    continue;
                }
                parts.Add(u.Name + "=" + Read(shader.ProgramId, loc, u));
            }
            return string.Join(" ", parts);
        }
        catch (Exception e)
        {
            return "unavailable (" + Unwrap(e) + ")";
        }
    }

    /// <summary>The uniforms whose value differs from the export's defaults, or "".</summary>
    public string NonDefault(IShaderProgram? shader)
    {
        string all = Uniforms(shader);
        if (all.StartsWith("unavailable", StringComparison.Ordinal))
        {
            return all;
        }
        var defaults = GuiUniforms.Defaults.ToDictionary(u => u.Name, Format);
        return string.Join(" ", all.Split(' ').Where(p =>
        {
            string[] kv = p.Split('=', 2);
            return kv.Length == 2 && kv[1] != "absent" && defaults.TryGetValue(kv[0], out string? d) && d != kv[1];
        }));
    }

    private string Read(int program, int location, UniformDefault u)
    {
        int n = u.Type switch { UniformType.Vec2 => 2, UniformType.Vec4 => 4, _ => 1 };
        if (u.Type == UniformType.Int)
        {
            var buf = new int[1];
            _getUniformI!.Invoke(null, new object[] { program, location, buf });
            return buf[0].ToString(CultureInfo.InvariantCulture);
        }
        var f = new float[n];
        _getUniformF!.Invoke(null, new object[] { program, location, f });
        return string.Join(",", f.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    private static string Format(UniformDefault u) => u.Type == UniformType.Int
        ? ((int)u.Value[0]).ToString(CultureInfo.InvariantCulture)
        : string.Join(",", u.Value.Select(x => x.ToString("0.###", CultureInfo.InvariantCulture)));

    /// <summary>A line of GL state: bound program and framebuffers, viewport and capabilities.</summary>
    public string State()
    {
        if (Problem != null)
        {
            return "unavailable (" + Problem + ")";
        }
        try
        {
            return $"program={Get(0x8B8D)} drawFramebuffer={Get(0x8CA6)} readFramebuffer={Get(0x8CAA)} "
                + $"viewport={Get(0x0BA2, 4)} activeTexture=0x{Convert.ToInt32(Get(0x84E0), CultureInfo.InvariantCulture):X} "
                + $"texture2D={Get(0x8069)} sampler={Get(0x8919)} depthTest={Get(0x0B71)} depthMask={Get(0x0B72)} "
                + $"blend={Get(0x0BE2)} cullFace={Get(0x0B44)} scissorTest={Get(0x0C11)} colorMask={Get(0x0C23, 4)}";
        }
        catch (Exception e)
        {
            return "unavailable (" + Unwrap(e) + ")";
        }
    }

    private string Get(int pname, int count = 1)
    {
        var buf = new int[Math.Max(count, 4)];
        _getInteger!.Invoke(null, new[] { Enum.ToObject(_pname!, pname), (object)buf });
        return string.Join(",", buf.Take(count));
    }

    private static string Unwrap(Exception e) =>
        e is TargetInvocationException { InnerException: { } inner } ? inner.GetType().Name + ": " + inner.Message : e.GetType().Name + ": " + e.Message;
}
