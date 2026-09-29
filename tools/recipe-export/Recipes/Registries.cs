using System.Collections;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One recipe registry as the engine holds it.</summary>
public sealed class RegistryInfo
{
    public required string Code;
    public required RecipeRegistryBase Registry;

    /// <summary>Id of the mod that registered it.</summary>
    public required string Mod;
}

public static class Registries
{
    public static readonly HashSet<string> BaseGameMods = new() { "game", "survival", "creative" };

    /// <summary>
    /// Every registry registered through ICoreAPICommon.RegisterRecipeRegistry. The engine
    /// keeps them in a Dictionary&lt;string, RecipeRegistryBase&gt; on the world (GameMain.
    /// recipeRegistries, not exposed by the API); it is found by type, not by field name.
    /// </summary>
    public static List<RegistryInfo> Find(ICoreServerAPI api)
    {
        var world = api.World;
        IDictionary<string, RecipeRegistryBase>? found = null;
        for (var t = world.GetType(); t != null && found == null; t = t.BaseType)
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (typeof(IDictionary<string, RecipeRegistryBase>).IsAssignableFrom(f.FieldType) &&
                    f.GetValue(world) is IDictionary<string, RecipeRegistryBase> d)
                {
                    found = d;
                    break;
                }
        if (found == null)
            throw new InvalidOperationException(
                $"No recipe registry dictionary found on {world.GetType().FullName}; the engine's internals changed");

        var owners = Owners(api);
        return found.Select(kv => new RegistryInfo
        {
            Code = kv.Key,
            Registry = kv.Value,
            Mod = owners.GetValueOrDefault(kv.Value) ??
                  owners.GetValueOrDefault(RecipeList(kv.Value) ?? new object()) ?? "game",
        }).OrderBy(r => r.Code, StringComparer.Ordinal).ToList();
    }

    /// <summary>The recipe objects a registry holds, or null if it holds them in no list we can find.</summary>
    public static IList? RecipeList(RecipeRegistryBase registry)
    {
        var type = registry.GetType();
        foreach (var f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (typeof(IList).IsAssignableFrom(f.FieldType) && f.FieldType.IsGenericType && f.GetValue(registry) is IList l)
                return l;
        foreach (var p in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            if (typeof(IList).IsAssignableFrom(p.PropertyType) && p.PropertyType.IsGenericType &&
                p.GetIndexParameters().Length == 0 && p.GetValue(registry) is IList l)
                return l;
        return null;
    }

    public static Type? ElementType(IList list) =>
        list.GetType().IsGenericType ? list.GetType().GetGenericArguments()[0] : null;

    /// <summary>
    /// Which mod registered each registry: the mod whose mod system keeps a reference to the
    /// registry or its recipe list (they all do, to use the recipes). Registries no mod
    /// system holds (gridrecipes, created by the engine itself) belong to the game.
    /// </summary>
    private static Dictionary<object, string> Owners(ICoreServerAPI api)
    {
        var owners = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (var mod in api.ModLoader.Mods)
            foreach (var system in mod.Systems)
                foreach (var f in system.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (!typeof(IList).IsAssignableFrom(f.FieldType) && !typeof(RecipeRegistryBase).IsAssignableFrom(f.FieldType)) continue;
                    object? value;
                    try { value = f.GetValue(system); }
                    catch (Exception) { continue; }
                    if (value == null) continue;
                    // Other mods may keep a base game list too (to read recipes); the base game
                    // never keeps a mod's, so it wins.
                    if (!owners.TryGetValue(value, out var had) || (!BaseGameMods.Contains(had) && BaseGameMods.Contains(mod.Info.ModID)))
                        owners[value] = mod.Info.ModID;
                }
        return owners;
    }
}
