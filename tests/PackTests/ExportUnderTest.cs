using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeraphHorizons.PackTests;

/// <summary>
/// The export of the running test server, built once per process: a full export of the
/// pack takes a while and every export scenario reads the same document.
/// </summary>
internal static class ExportUnderTest
{
    public static readonly PackInfo Pack = new("seraphhorizons", "0.0.0-test", "test");

    private static readonly object Gate = new();
    private static JObject? _doc;

    public static JObject Get(ICoreAPI api)
    {
        lock (Gate)
            return _doc ??= Exporter.Build((ICoreServerAPI)api, Pack);
    }
}
