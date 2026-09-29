using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport.Recipes;

/// <summary>One recipe definition: entry <see cref="Index"/> of a recipe asset (0 for a single object).</summary>
public sealed class Definition
{
    public required DefinitionFile File;
    public required int Index;
    public required JToken Token;

    /// <summary>The definition parsed as the registry's recipe class, before any expansion. Null if it does not parse.</summary>
    public RecipeForm? Form;

    /// <summary>Why <see cref="Form"/> is null.</summary>
    public string? ParseError;

    /// <summary>
    /// Every way a loader may read it: codes without a domain get the asset's domain when
    /// parsed with JsonUtil.ToObject (the base game's loaders), but `game` when parsed with
    /// plain Newtonsoft ToObject (ACulinaryArtillery). Both readings are tried.
    /// </summary>
    public List<RecipeForm> Forms = new();

    /// <summary>Position in load order across the registry's files; the loaders register in this order.</summary>
    public int Ordinal;

    public bool Enabled => Form?.Enabled ?? Token["enabled"]?.Value<bool?>() != false;
}

public sealed class DefinitionFile
{
    public required AssetLocation Location;

    /// <summary>Id of the mod whose files hold the asset (not its domain: a mod can ship `game:` assets).</summary>
    public required string Mod;

    public List<Definition> Definitions = new();
}

/// <summary>
/// Re-reads recipe assets. The registered recipes no longer carry their source index or
/// their unexpanded ingredients, so definitions come from the assets themselves. Patched
/// assets keep their patched data in memory (AssetManager.UnloadUnpatchedAssets only
/// drops unpatched ones, which reload unchanged from disk).
/// </summary>
public sealed class DefinitionIndex
{
    private readonly ICoreServerAPI _api;
    private readonly AssetOrigins _origins;
    private readonly Dictionary<string, DefinitionFile> _files = new();

    public DefinitionIndex(ICoreServerAPI api)
    {
        _api = api;
        _origins = new AssetOrigins(api);
    }

    /// <summary>All recipe files under `recipes/&lt;folder&gt;/`, in the engine's asset order.</summary>
    public List<DefinitionFile> Folder(string folder, Type recipeType, IRecipeReader reader)
    {
        var list = new List<DefinitionFile>();
        foreach (var asset in _api.Assets.GetMany($"recipes/{folder}/", null, true))
        {
            if (!asset.Location.Path.EndsWith(".json")) continue;
            list.Add(File(asset, recipeType, reader));
        }
        return list;
    }

    /// <summary>The recipe file at a location, or null if there is no such asset.</summary>
    public DefinitionFile? At(AssetLocation location, Type recipeType, IRecipeReader reader)
    {
        if (_files.TryGetValue(recipeType.FullName + "|" + location, out var known)) return known;
        var asset = _api.Assets.TryGet(location, true);
        return asset == null || !location.Path.EndsWith(".json") ? null : File(asset, recipeType, reader);
    }

    private DefinitionFile File(IAsset asset, Type recipeType, IRecipeReader reader)
    {
        // Keyed by recipe class too: the same file read as another class is another reading.
        var key = recipeType.FullName + "|" + asset.Location;
        if (_files.TryGetValue(key, out var known)) return known;

        var file = new DefinitionFile { Location = asset.Location, Mod = _origins.ModOf(asset) };
        JToken? root;
        try { root = asset.ToObject<JToken>(); }
        catch (Exception e)
        {
            _api.Logger.Warning("[seraphexport] {0} is not valid JSON, skipped: {1}", key, e.Message);
            root = null;
        }
        var tokens = root switch
        {
            JObject o => new List<JToken> { o },
            JArray a => a.ToList(),
            _ => new List<JToken>(),
        };
        for (int i = 0; i < tokens.Count; i++)
        {
            var form = Parse(tokens[i], asset.Location.Domain, recipeType, reader, out var error);
            var def = new Definition { File = file, Index = i, Token = tokens[i], Form = form, ParseError = error };
            if (form != null) def.Forms.Add(form);
            if (asset.Location.Domain != "game" && Parse(tokens[i], "game", recipeType, reader, out _) is { } other)
            {
                def.Forms.Add(other);
                def.Form ??= other;
            }
            file.Definitions.Add(def);
        }
        _files[key] = file;
        return file;
    }

    private static readonly MethodInfo ToObject = typeof(JsonUtil).GetMethods()
        .Single(m => m.Name == nameof(JsonUtil.ToObject) && m.IsGenericMethod &&
                     m.GetParameters()[0].ParameterType == typeof(JToken));

    /// <summary>Parses like the loaders do (JsonUtil.ToObject with the asset's domain), without resolving.</summary>
    private static RecipeForm? Parse(JToken token, string domain, Type recipeType, IRecipeReader reader, out string? error)
    {
        error = null;
        if (token is not JObject)
        {
            error = $"a {token.Type}, not an object";
            return null;
        }
        try
        {
            var recipe = ToObject.MakeGenericMethod(recipeType).Invoke(null, new object?[] { token, domain, null });
            if (recipe == null) error = "parses to null";
            return recipe == null ? null : reader.Read(recipe);
        }
        catch (Exception e)
        {
            var inner = e is TargetInvocationException { InnerException: { } ie } ? ie : e;
            error = $"{inner.GetType().Name}: {inner.Message}";
            return null;
        }
    }
}

/// <summary>Maps an asset to the mod whose files it came from.</summary>
public sealed class AssetOrigins
{
    private readonly List<(string Prefix, string ModId)> _roots = new();
    private readonly string _gameAssets;

    public AssetOrigins(ICoreServerAPI api)
    {
        _gameAssets = Normalise(GamePaths.AssetsPath);
        foreach (var mod in api.ModLoader.Mods)
        {
            // ModContainer.FolderPath is where the engine unpacked a zip mod (or the folder
            // itself); its FolderOrigin reads from FolderPath/assets.
            var folder = mod.GetType().GetProperty("FolderPath")?.GetValue(mod) as string ?? mod.SourcePath;
            if (!string.IsNullOrEmpty(folder)) _roots.Add((Normalise(Path.Combine(folder, "assets")), mod.Info.ModID));
        }
        _roots.Sort((a, b) => b.Prefix.Length.CompareTo(a.Prefix.Length));
    }

    public string ModOf(IAsset asset)
    {
        var origin = asset.Origin?.OriginPath;
        if (origin == null) return asset.Location.Domain;
        origin = Normalise(origin);
        // The base game's own origins: assets/game (GameOrigin), assets/survival and
        // assets/creative (added by those mods with domain "game").
        if (origin.StartsWith(_gameAssets, StringComparison.Ordinal))
        {
            var rest = origin[_gameAssets.Length..];
            var first = rest.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return first ?? "game";
        }
        foreach (var (prefix, mod) in _roots)
            if (origin.StartsWith(prefix, StringComparison.Ordinal)) return mod;
        return asset.Location.Domain;
    }

    private static string Normalise(string path) =>
        Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/') + "/";
}
