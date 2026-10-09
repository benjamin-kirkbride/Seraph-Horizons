namespace SeraphHorizons.Mod.Machines.Core;

/// <summary>
/// The game's placed block info (the text shown while looking at a block) ends with the block's
/// description, the same text its item tooltip and handbook page show. A machine's description
/// says what it is built of and how to assemble it: useful in the hand, noise under the live state
/// once placed. This takes it back out.
/// </summary>
public static class PlacedInfo
{
    /// <summary><paramref name="info"/> without <paramref name="description"/>: the text is cut
    /// from the line it sits on, a line left empty by it is dropped, and so is trailing whitespace.
    /// An empty description, or one not in the info, leaves the info as it is (trimmed).</summary>
    public static string WithoutDescription(string info, string? description)
    {
        if (string.IsNullOrEmpty(description) || !info.Contains(description, StringComparison.Ordinal))
            return info.TrimEnd();
        var lines = info.Split('\n');
        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (!line.Contains(description, StringComparison.Ordinal))
            {
                kept.Add(line);
                continue;
            }
            var rest = line.Replace(description, "", StringComparison.Ordinal);
            if (rest.Trim().Length > 0)
                kept.Add(rest);
        }
        return string.Join('\n', kept).TrimEnd();
    }
}
