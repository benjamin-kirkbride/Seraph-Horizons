using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// Writes the export to the file named by SERAPH_EXPORT_PATH once the server is running.
/// Does nothing when the variable is unset.
/// </summary>
public class ExportModSystem : ModSystem
{
    public const string PathVariable = "SERAPH_EXPORT_PATH";

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Server;

    public override void StartServerSide(ICoreServerAPI api)
    {
        var path = Environment.GetEnvironmentVariable(PathVariable);
        if (string.IsNullOrEmpty(path)) return;

        api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () =>
        {
            var pack = new PackInfo(
                Environment.GetEnvironmentVariable("SERAPH_PACK_ID") ?? "unknown",
                Environment.GetEnvironmentVariable("SERAPH_PACK_VERSION") ?? "unknown",
                GameVersion.ShortGameVersion);
            var doc = Exporter.Build(api, pack);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path, doc.ToString(Formatting.None));
            api.Logger.Notification("[seraphexport] wrote {0}", path);
        });
    }
}
