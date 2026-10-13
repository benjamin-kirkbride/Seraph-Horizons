using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SeraphHorizons.OreSurvey;

/// <summary>
/// Experiment (ORE_TRACE_CHUNK=x,z, a chunk): logs every decision the pack's cell rule makes on a
/// vein try from that source chunk, in every chunk column's GenDeposits pass: the column being
/// generated, the try's centre, the block found there and what <c>CentreInHostRock</c> and
/// <c>Approve</c> answered. Read-only: postfixes and a prefix that only record. Writes
/// ORE_SURVEY_OUT + ".trace".
/// </summary>
public static class OreTrace
{
    [ThreadStatic] static (int X, int Z)? target;
    static (int X, int Z) chunk;
    // With a third number r: every source, for the columns within r of the chunk being generated.
    static int around = -1;
    static string path = "";
    static readonly object Lock = new();
    static FieldInfo? accessorField, variantField;
    static ICoreServerAPI? sapi;

    public static void Bind(ICoreServerAPI api, string outPath, string spec)
    {
        var p = spec.Split(',');
        chunk = (int.Parse(p[0]), int.Parse(p[1]));
        if (p.Length > 2) around = int.Parse(p[2]);
        path = outPath + ".trace";
        sapi = api;
        File.WriteAllText(path, "");
        var placement = AccessTools.TypeByName("SeraphHorizons.Mod.Ore.OreCellPlacement");
        accessorField = AccessTools.Field(placement, "_passAccessor");
        variantField = AccessTools.Field(typeof(DepositGeneratorBase), "variant");
        var h = new Harmony("seraphoresurvey.trace");
        h.Patch(AccessTools.Method(typeof(GenDeposits), nameof(GenDeposits.GeneratePartial),
                [typeof(IServerChunk[]), typeof(int), typeof(int), typeof(int), typeof(int)]),
            prefix: new HarmonyMethod(typeof(OreTrace), nameof(PartialPrefix)));
        h.Patch(AccessTools.Method(placement, "CentreInHostRock"), postfix: new HarmonyMethod(typeof(OreTrace), nameof(CentrePostfix)));
        h.Patch(AccessTools.Method(placement, "Approve"), postfix: new HarmonyMethod(typeof(OreTrace), nameof(ApprovePostfix)));
    }

    static void PartialPrefix(int __1, int __2) => target = (__1, __2);

    static bool Traced(BlockPos pos) => around >= 0
        ? target is { } t && Math.Abs(t.X - chunk.X) <= around && Math.Abs(t.Z - chunk.Z) <= around
        : pos.X >> 5 == chunk.X && pos.Z >> 5 == chunk.Z;

    static void CentrePostfix(DepositGeneratorBase __0, BlockPos __1, bool? __result)
    {
        if (!Traced(__1)) return;
        string block = "?";
        int y = -1;
        try
        {
            var type = __0.GetType();
            if (AccessTools.Field(type, "YPosRel")?.GetValue(__0) is NatFloat yPosRel && AccessTools.Field(type, "Radius")?.GetValue(__0) is NatFloat radius
                && __0.DepositRand is { } src)
            {
                var rand = new LCGRandom { worldSeed = src.worldSeed, mapGenSeed = src.mapGenSeed, currentSeed = src.currentSeed };
                radius.nextFloat(1f, rand);
                rand.NextFloat();
                y = (int)yPosRel.nextFloat(1f, rand);
                if (accessorField?.GetValue(null) is IBlockAccessor acc && acc.GetChunkAtBlockPos(new BlockPos(__1.X, y, __1.Z)) is IServerChunk { Data: { } data })
                    block = sapi!.World.Blocks[data.GetBlockIdUnsafe((GameMath.Mod(y, 32) * 32 + GameMath.Mod(__1.Z, 32)) * 32 + GameMath.Mod(__1.X, 32))].Code.Path;
                else block = "(anchor chunk not reachable)";
            }
        }
        catch (Exception e) { block = "error " + e.Message; }
        Write($"centre column={target} try={Variant(__0)} at {__1.X},{y},{__1.Z} block={block} result={(__result?.ToString() ?? "null")}");
    }

    static void ApprovePostfix(DepositGeneratorBase __0, string __1, BlockPos __2, bool __result)
    {
        if (!Traced(__2) || !__result) return;
        Write($"APPROVED column={target} try={Variant(__0)} metal={__1} at {__2.X},{__2.Z}");
    }

    static string Variant(DepositGeneratorBase g) => (variantField?.GetValue(g) as DepositVariant)?.Code ?? "?";

    static void Write(string line)
    {
        lock (Lock) File.AppendAllText(path, line + "\n");
    }
}
