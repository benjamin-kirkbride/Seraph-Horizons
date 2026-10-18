using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SeraphHorizons.Mod.Trading.Core;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.ServerMods;

namespace SeraphHorizons.Mod.Trading;

/// <summary>
/// Keeps the game's ponds out of trader camps. The pond generator (<c>GenPonds</c>, VSEssentials
/// 1.22.7, TerrainFeatures at 0.4) makes surface ponds and 600 underground tries per chunk column:
/// a try walks down through air to the first block and floods that layer outward
/// (<c>TryPlacePondAt</c>) wherever a floor holds water and nothing on the sides stops it. Its only
/// structure guard is the story structures. A camp is placed after it in its own column, but writes
/// into neighbours whose pass hasn't run yet, so a neighbour's underground try could find a cellar's
/// air and flood its floor, under the furniture (a <c>basement1</c> camp across a chunk edge).
///
/// <para>A transpiler on <c>TryPlacePondAt</c> calls <see cref="OnPondFilled"/> right after the flood
/// fill, before any water is set (at the first of its two <c>-1</c>s, the start of the placement):
/// if any cell of the fill (<c>pondPositions</c>) touches a recorded camp's location
/// (<see cref="CampPonds"/>), the fill is emptied, so that pond sets nothing, its lake bed included.
/// Guarding the fill rather than the try's start catches a pond started outside that would spread in
/// through a doorway, and a surface pond on a levelled footprint as well as a cellar's; the
/// generator's random draws are left as they were, so the rest of the world generates the same.
/// Camps of the map regions around the pond's search window are read, as the grid's overlap test
/// reads them. Patched when the grid captures its camp structures (worlds with the grid only); left
/// as it is, with a warning, if the method doesn't look as expected.</para>
/// </summary>
public static class CampPondGuard
{
    public const string HarmonyId = "seraphhorizons.camppondguard";

    // How far past the pond's window a camp's map region is looked for: a camp is recorded in the
    // region of the chunk it was placed from and reaches less than this into its neighbours.
    private const int RegionPad = 64;

    private static Harmony? _harmony;
    private static ILogger? _logger;
    private static AccessTools.FieldRef<GenPonds, QueueOfInt>? _pondPositions;
    private static AccessTools.FieldRef<GenPonds, IWorldGenBlockAccessor>? _blockAccessor;
    private static AccessTools.FieldRef<GenPonds, int>? _mapOffset, _searchSize;
    private static volatile bool _on;
    private static int _hooked;
    private static long _keptOut;

    /// <summary>Whether the pond generator is hooked and the guard on.</summary>
    public static bool On => _on;

    /// <summary>The generator's calls routed through <see cref="OnPondFilled"/> (1 when patched).</summary>
    public static int Hooked => _hooked;

    /// <summary>Ponds kept out of camps in this process.</summary>
    public static long KeptOut => Interlocked.Read(ref _keptOut);

    /// <summary>Patches the pond generator (once per process) and turns the guard on.</summary>
    public static void Bind(ILogger logger)
    {
        _logger = logger;
        if (!Harmony.HasAnyPatches(HarmonyId))
        {
            var method = AccessTools.DeclaredMethod(typeof(GenPonds), nameof(GenPonds.TryPlacePondAt));
            if (method == null || !BindFields())
            {
                logger.Warning("[seraphhorizons] Trading: the game's GenPonds.TryPlacePondAt or its fields are not as expected, so ponds may flood trader camps");
                return;
            }
            _harmony = new Harmony(HarmonyId);
            _harmony.Patch(method, transpiler: new HarmonyMethod(typeof(CampPondGuard), nameof(PondTranspiler)));
        }
        _on = _hooked == 1;
        logger.Notification("[seraphhorizons] Trading: ponds are kept out of trader camps ({0} of 1 pond generator hooks)", _hooked);
    }

    /// <summary>Turns the guard off and takes the patch out.</summary>
    public static void Unbind()
    {
        _on = false;
        if (_harmony == null) return;
        _harmony.UnpatchAll(HarmonyId);
        _harmony = null;
        _hooked = 0;
    }

    private static bool BindFields()
    {
        FieldInfo? Field(string name, Type type) => AccessTools.Field(typeof(GenPonds), name) is { } f && f.FieldType == type ? f : null;
        if (Field("pondPositions", typeof(QueueOfInt)) is not { } positions
            || Field("blockAccessor", typeof(IWorldGenBlockAccessor)) is not { } blocks
            || Field("mapOffset", typeof(int)) is not { } offset
            || Field("searchSize", typeof(int)) is not { } size)
            return false;
        _pondPositions = AccessTools.FieldRefAccess<GenPonds, QueueOfInt>(positions);
        _blockAccessor = AccessTools.FieldRefAccess<GenPonds, IWorldGenBlockAccessor>(blocks);
        _mapOffset = AccessTools.FieldRefAccess<GenPonds, int>(offset);
        _searchSize = AccessTools.FieldRefAccess<GenPonds, int>(size);
        return true;
    }

    // GenPonds.TryPlacePondAt(dx, pondYPos, dz, chunkX, chunkZ, depth): the placement starts with its
    // two `-1`s (the last chunk seen); OnPondFilled(this, pondYPos, chunkX, chunkZ) goes before the
    // first. Left as it is, with a warning, when there are not exactly two.
    private static IEnumerable<CodeInstruction> PondTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = instructions.ToList();
        var starts = list.Select((ins, i) => (ins, i)).Where(p => p.ins.opcode == OpCodes.Ldc_I4_M1).Select(p => p.i).ToList();
        if (starts.Count != 2 || !list[starts[0] + 1].IsStloc())
        {
            _logger?.Warning("[seraphhorizons] Trading: the game's GenPonds.TryPlacePondAt has {0} -1s, not 2, so ponds may flood trader camps", starts.Count);
            return list;
        }
        var call = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(list[starts[0]]);
        list.InsertRange(starts[0],
        [
            call,
            new CodeInstruction(OpCodes.Ldarg_2),
            new CodeInstruction(OpCodes.Ldarg_S, (byte)4),
            new CodeInstruction(OpCodes.Ldarg_S, (byte)5),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(CampPondGuard), nameof(OnPondFilled))),
        ]);
        _hooked++;
        return list;
    }

    /// <summary>The generator has filled a pond at <paramref name="pondY"/>, started in chunk
    /// (<paramref name="chunkX"/>, <paramref name="chunkZ"/>), and is about to set its water: empties
    /// the fill if it touches a camp.</summary>
    public static void OnPondFilled(GenPonds ponds, int pondY, int chunkX, int chunkZ)
    {
        if (!_on) return;
        try
        {
            var queue = _pondPositions!(ponds);
            if (queue.Count == 0) return;
            var camps = CampsNear(_blockAccessor!(ponds), pondY, chunkX, chunkZ);
            if (camps.Count == 0) return;
            int offset = _mapOffset!(ponds), size = _searchSize!(ponds);
            // The fill as the generator encodes it ((dz + offset) * size + dx + offset), read by
            // dequeuing and put back in the same order.
            var cells = new int[queue.Count];
            for (int i = 0; i < cells.Length; i++) cells[i] = queue.Dequeue();
            foreach (int c in cells) queue.Enqueue(c);
            if (CampPonds.Floods(camps, pondY, cells.Select(c => (chunkX * 32 + c % size - offset, chunkZ * 32 + c / size - offset))))
            {
                queue.Clear();
                Interlocked.Increment(ref _keptOut);
            }
        }
        catch (Exception e)
        {
            _on = false;
            _logger?.Warning("[seraphhorizons] Trading: keeping ponds out of trader camps failed, so it stops for this run: {0}", e.Message);
        }
    }

    /// <summary>The camps of the map regions around a pond's window that it could touch.</summary>
    private static List<CampBox> CampsNear(IWorldGenBlockAccessor blocks, int pondY, int chunkX, int chunkZ)
    {
        var camps = new List<CampBox>();
        var window = CampPonds.Window(chunkX, chunkZ);
        int regionSize = blocks.RegionSize;
        int maxX = blocks.MapSizeX / regionSize, maxZ = blocks.MapSizeZ / regionSize;
        for (int rx = GameMath.Clamp((window.X1 - RegionPad) / regionSize, 0, maxX); rx <= GameMath.Clamp((window.X2 + RegionPad) / regionSize, 0, maxX); rx++)
            for (int rz = GameMath.Clamp((window.Z1 - RegionPad) / regionSize, 0, maxZ); rz <= GameMath.Clamp((window.Z2 + RegionPad) / regionSize, 0, maxZ); rz++)
            {
                if (blocks.GetMapRegion(rx, rz)?.GeneratedStructures is not { } placed) continue;
                foreach (var g in placed)
                {
                    if (g.Group != TraderCamps.CampGroup || g.Location is not { } l) continue;
                    var box = new CampBox(l.X1, l.Y1, l.Z1, l.X2, l.Y2, l.Z2);
                    if (CampPonds.Near(box, pondY, window)) camps.Add(box);
                }
            }
        return camps;
    }
}
