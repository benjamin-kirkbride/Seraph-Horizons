using System.IO.Compression;

namespace SeraphHorizons.Mod.MapReveal.Core;

/// <summary>
/// The wire format of a batch of <see cref="ColumnSample"/>s: compact, then deflated. Per column, the
/// distinct (block, kind) pairs as a palette and one palette index per cell (a byte, or two once a
/// column has more than 256 pairs), the 1024 shadow bytes, and the heights when the sample has them.
/// A typical column comes to well under 1 KB.
/// </summary>
public static class RevealCodec
{
    private const byte FormatVersion = 1;

    public static byte[] Encode(IReadOnlyCollection<ColumnSample> columns)
    {
        using var buffer = new MemoryStream();
        using (var deflate = new DeflateStream(buffer, CompressionLevel.Fastest, leaveOpen: true))
        using (var w = new BinaryWriter(deflate))
        {
            w.Write(FormatVersion);
            w.Write7BitEncodedInt(columns.Count);
            foreach (var c in columns) Write(w, c);
        }
        return buffer.ToArray();
    }

    /// <exception cref="InvalidDataException">The data is not a batch this version can read.</exception>
    public static List<ColumnSample> Decode(byte[] data)
    {
        try
        {
            using var r = new BinaryReader(new DeflateStream(new MemoryStream(data), CompressionMode.Decompress));
            byte version = r.ReadByte();
            if (version != FormatVersion) throw new InvalidDataException($"map reveal batch format {version}, expected {FormatVersion}");
            int count = r.Read7BitEncodedInt();
            if (count < 0) throw new InvalidDataException("negative column count");
            var columns = new List<ColumnSample>(Math.Min(count, 4096));
            for (int i = 0; i < count; i++) columns.Add(Read(r));
            return columns;
        }
        catch (EndOfStreamException e) { throw new InvalidDataException("truncated map reveal batch", e); }
        catch (FormatException e) { throw new InvalidDataException("malformed map reveal batch", e); }
    }

    private static void Write(BinaryWriter w, ColumnSample c)
    {
        const int area = TerrainShade.Area;
        w.Write7BitEncodedInt(c.X);
        w.Write7BitEncodedInt(c.Z);
        w.Write(c.Heights is null ? (byte)0 : (byte)1);

        var palette = new Dictionary<(int, CellKind), int>();
        var entries = new List<(int id, CellKind kind)>();
        var indices = new int[area];
        for (int k = 0; k < area; k++)
        {
            var key = (c.BlockIds[k], c.Kinds[k]);
            if (!palette.TryGetValue(key, out int index))
            {
                palette[key] = index = entries.Count;
                entries.Add(key);
            }
            indices[k] = index;
        }
        w.Write7BitEncodedInt(entries.Count);
        foreach (var (id, kind) in entries)
        {
            w.Write7BitEncodedInt(id);
            w.Write((byte)kind);
        }
        bool wide = entries.Count > 256;
        foreach (int index in indices)
        {
            if (wide) w.Write((ushort)index);
            else w.Write((byte)index);
        }
        w.Write(c.Shadow, 0, area);
        if (c.Heights is { } heights)
            foreach (ushort h in heights) w.Write(h);
    }

    private static ColumnSample Read(BinaryReader r)
    {
        const int area = TerrainShade.Area;
        int x = r.Read7BitEncodedInt(), z = r.Read7BitEncodedInt();
        bool hasHeights = r.ReadByte() != 0;

        int paletteCount = r.Read7BitEncodedInt();
        if (paletteCount < 1 || paletteCount > area) throw new InvalidDataException($"palette of {paletteCount} entries");
        var ids = new int[paletteCount];
        var kinds = new CellKind[paletteCount];
        for (int i = 0; i < paletteCount; i++)
        {
            ids[i] = r.Read7BitEncodedInt();
            byte kind = r.ReadByte();
            if (kind > (byte)CellKind.None) throw new InvalidDataException($"cell kind {kind}");
            kinds[i] = (CellKind)kind;
        }
        var sample = new ColumnSample { X = x, Z = z, Heights = hasHeights ? new ushort[area] : null };
        bool wide = paletteCount > 256;
        for (int k = 0; k < area; k++)
        {
            int index = wide ? r.ReadUInt16() : r.ReadByte();
            if (index >= paletteCount) throw new InvalidDataException($"palette index {index} of {paletteCount}");
            sample.BlockIds[k] = ids[index];
            sample.Kinds[k] = kinds[index];
        }
        byte[] shadow = r.ReadBytes(area);
        if (shadow.Length != area) throw new EndOfStreamException();
        shadow.CopyTo(sample.Shadow, 0);
        if (sample.Heights is { } heights)
            for (int k = 0; k < area; k++) heights[k] = r.ReadUInt16();
        return sample;
    }
}
