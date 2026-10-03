using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The export of the running test server, built once per server: a full export of the
/// pack takes a while and every export scenario reads the same document. A scenario that
/// needs a second fill to compare with (determinism, language) compares it with this one,
/// so it is rebuilt when another class has booted a new server.
/// </summary>
internal static class ExportUnderTest
{
    public static readonly PackInfo Pack = new("seraphhorizons", "0.0.0-test", "test");

    private static readonly object Gate = new();
    private static ICoreAPI? _api;
    private static JObject? _doc;
    private static ISet<string>? _referenced;

    public static JObject Get(ICoreAPI api) => Export(api).Doc;

    /// <summary>The codes the document's recipes reference: what its item section was filled with.</summary>
    public static ISet<string> Referenced(ICoreAPI api) => Export(api).Referenced;

    private static (JObject Doc, ISet<string> Referenced) Export(ICoreAPI api)
    {
        lock (Gate)
        {
            if (!ReferenceEquals(_api, api) || _doc == null || _referenced == null)
            {
                _doc = Exporter.Build((ICoreServerAPI)api, Pack, out _referenced);
                _api = api;
            }
            return (_doc, _referenced);
        }
    }
}
