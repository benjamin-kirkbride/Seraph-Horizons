using System.Collections;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SeraphHorizons.Mod.Core;

/// <summary>
/// Reads this mod's ModConfig files as the game's <c>LoadModConfig&lt;T&gt;</c> does
/// (<c>JsonConvert.DeserializeObject&lt;T&gt;</c> of the file's text), except that a list in the
/// file replaces the list the class starts with. Newtonsoft's default
/// (<see cref="ObjectCreationHandling.Auto"/>) reuses that list and appends the file's entries to
/// it, so a list setting with a non-empty default came back with one more copy of its contents
/// on every start, and was written back that way.
///
/// Only lists and other non-dictionary collections are replaced. Arrays already were (Newtonsoft
/// cannot append to one). Dictionaries still merge the file's keys into the defaults: that is how
/// a key added to a default dictionary in a later version (a machine's <c>Tailings</c>, a die's
/// <c>DieMetals</c>) reaches an existing install, which the pack's config defaults do not do for
/// a key the file lacks (docs/config-defaults.md); a key is switched off by its value instead.
/// </summary>
public static class ModConfigJson
{
    public static readonly JsonSerializerSettings Settings = new()
    {
        ContractResolver = new ReplaceCollectionsResolver(),
    };

    /// <summary>The file's object, or <c>default</c> when <paramref name="path"/> does not exist
    /// (as the game's). Throws on a file that is not readable as <typeparamref name="T"/>.</summary>
    public static T? Load<T>(string path) =>
        File.Exists(path) ? Deserialize<T>(File.ReadAllText(path)) : default;

    public static T? Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

    /// <summary>Removes repeated entries from <paramref name="list"/>, keeping the first of each
    /// (ordinal) and the order; returns how many it removed.</summary>
    public static int RemoveDuplicates(List<string>? list)
    {
        if (list == null)
            return 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return list.RemoveAll(s => s != null && !seen.Add(s));
    }

    private sealed class ReplaceCollectionsResolver : DefaultContractResolver
    {
        protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);
            // A property with no setter is left as it was: Replace would skip it altogether.
            if (property.Writable && IsReplacedCollection(property.PropertyType))
                property.ObjectCreationHandling = ObjectCreationHandling.Replace;
            return property;
        }

        private static bool IsReplacedCollection(Type? type) =>
            type != null && type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type) && !IsDictionary(type);

        private static bool IsDictionary(Type type) =>
            typeof(IDictionary).IsAssignableFrom(type)
            || (type.IsInterface && type.IsGenericType && IsGenericDictionary(type.GetGenericTypeDefinition()))
            || type.GetInterfaces().Any(i => i.IsGenericType && IsGenericDictionary(i.GetGenericTypeDefinition()));

        private static bool IsGenericDictionary(Type definition) =>
            definition == typeof(IDictionary<,>) || definition == typeof(IReadOnlyDictionary<,>);
    }
}
