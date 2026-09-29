using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace SeraphHorizons.RecipeExport;

/// <summary>
/// Writes the export to the file named by SERAPH_EXPORT_PATH once the server is running.
/// Does nothing when the variable is unset.
/// </summary>
/// <remarks>
/// RunGame is late enough for every registry to be complete (mods register recipes up to
/// AssetsFinalize) and runs before "Dedicated Server now running" is logged, which is what
/// `packtool.py smoke` waits for. By then the asset packet thread may already have called
/// GridRecipe.FreeRAMServer; the exporter does not need what that drops (see
/// docs/recipe-browser/spike-findings.md).
/// </remarks>
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
            try
            {
                var doc = Exporter.Build(api, pack);
                var full = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                // Write then rename, so a reader never sees half a file.
                File.WriteAllText(full + ".part", doc.ToString(Formatting.None));
                File.Move(full + ".part", full, overwrite: true);
                api.Logger.Notification("[seraphexport] wrote {0} ({1} recipes)", path, doc["recipes"]?.Count() ?? 0);
            }
            catch (Exception e)
            {
                // No file is written; packtool smoke fails on the missing file and prints this.
                api.Logger.Error("[seraphexport] export failed: {0}", e.Message);
                api.Logger.Error(e);
            }
        });
    }
}
