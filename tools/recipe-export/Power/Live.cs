using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Func = System.Func<Vintagestory.API.Common.Block, Vintagestory.API.Datastructures.JsonObject?>;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Power;

/// <summary>A figure and where it came from (the export's <c>PowerSource</c>).</summary>
public sealed record Figure(double Value, string What, string From, bool Fallback = false)
{
    public JObject Source()
    {
        var o = new JObject { ["what"] = What, ["from"] = From };
        if (Fallback) o["fallback"] = true;
        return o;
    }
}

/// <summary>
/// Reads the figures the power section needs from the running server. The exporter is built
/// against the game only, so a mod's settings are read by reflection, by type name, from the
/// assemblies the mod loader loaded: a loaded mod system's instance when the type is one,
/// else the type's static members. A member path walks properties and fields, public or not.
/// When a read fails the built-in default stands in, marked <c>fallback</c>, with a warning in
/// the log (the Atlas scenario fails on any fallback, so a mod update that renames a setting is
/// caught).
/// </summary>
public sealed class Live
{
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private readonly ICoreServerAPI _api;
    private readonly Dictionary<string, Type?> _types = new(StringComparer.Ordinal);

    public Live(ICoreServerAPI api) => _api = api;

    public ICoreServerAPI Api => _api;

    public bool ModLoaded(string modId) => _api.ModLoader.IsModEnabled(modId);

    /// <summary>A type by full name, from the loaded mods' assemblies (the game's included).</summary>
    public Type? FindType(string fullName)
    {
        if (_types.TryGetValue(fullName, out var known)) return known;
        Type? found = null;
        foreach (var system in _api.ModLoader.Systems)
        {
            found = system.GetType().Assembly.GetType(fullName, false);
            if (found != null) break;
        }
        found ??= AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => { try { return a.GetType(fullName, false); } catch { return null; } })
            .FirstOrDefault(t => t != null);
        _types[fullName] = found;
        return found;
    }

    /// <summary>The value at <paramref name="path"/> (dot separated members) from the type: its
    /// loaded mod system instance, or its statics. Null when anything along the way is missing.</summary>
    public object? Get(string typeName, string path)
    {
        var type = FindType(typeName);
        if (type == null) return null;
        object? current = _api.ModLoader.Systems.FirstOrDefault(s => s.GetType() == type);
        Type currentType = type;
        foreach (var name in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            object? next;
            var prop = currentType.GetProperty(name, Any);
            if (prop != null && prop.GetIndexParameters().Length == 0)
            {
                var getter = prop.GetGetMethod(true);
                if (getter == null || (!getter.IsStatic && current == null)) return null;
                next = prop.GetValue(getter.IsStatic ? null : current);
            }
            else if (currentType.GetField(name, Any) is { } field)
            {
                if (field.IsLiteral) next = field.GetRawConstantValue();
                else if (!field.IsStatic && current == null) return null;
                else next = field.GetValue(field.IsStatic ? null : current);
            }
            else return null;
            if (next == null) return null;
            current = next;
            currentType = next.GetType();
        }
        return current;
    }

    /// <summary>A number read live, or the default marked as a fallback (and logged).</summary>
    public Figure Number(string what, string typeName, string path, double fallback, string from)
    {
        try
        {
            if (Get(typeName, path) is { } v && v is IConvertible)
            {
                var d = Convert.ToDouble(v);
                if (double.IsFinite(d)) return new Figure(d, what, from);
            }
        }
        catch (Exception e)
        {
            _api.Logger.Warning("[seraphexport] power: reading {0}.{1} threw {2}", typeName, path, e.Message);
        }
        _api.Logger.Warning("[seraphexport] power: cannot read {0} ({1}.{2}); using the default {3}", what, typeName, path, fallback);
        return new Figure(fallback, what, from, true);
    }

    /// <summary>A figure that is a literal inside a method, which reflection cannot read.</summary>
    public static Figure Constant(string what, double value, string className) =>
        new(value, what, $"code constant ({className})");

    /// <summary>A number from a block's JSON (a behavior's properties or its attributes). A
    /// missing key is not a fallback when the game itself uses the same default
    /// (<paramref name="codeDefault"/>), only when the block or the key's parent is missing.</summary>
    public Figure BlockNumber(string what, Block? block, Func select,
        double fallback, string from, string? codeDefault = null)
    {
        if (block != null)
        {
            var json = select(block);
            if (json is { Exists: true }) return new Figure(json.AsDouble(fallback), what, from);
            if (codeDefault != null) return new Figure(fallback, what, $"code default ({codeDefault})");
        }
        _api.Logger.Warning("[seraphexport] power: cannot read {0} from {1}; using the default {2}", what, block?.Code?.ToString() ?? "a missing block", fallback);
        return new Figure(fallback, what, from, true);
    }
}
