using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.RecipeExport.Items;

internal static class Json
{
    // schema/recipe-export.schema.json, $defs/code
    private static readonly Regex CodePattern = new(@"^[a-z0-9_-]+:[^\s:]+$", RegexOptions.CultureInvariant);

    public static bool IsValidCode(string code) => CodePattern.IsMatch(code);

    public static string Kind(EnumItemClass c) => c == EnumItemClass.Block ? "block" : "item";

    public static string Kind(CollectibleObject c) => Kind(c.ItemClass);

    /// <summary>A $defs/stack, or null when the stack did not resolve.</summary>
    public static JObject? Stack(ItemStack? stack)
    {
        if (stack?.Collectible?.Code == null) return null;
        var code = stack.Collectible.Code.ToString();
        if (!IsValidCode(code)) return null;
        return new JObject
        {
            ["code"] = code,
            ["kind"] = Kind(stack.Class),
            ["quantity"] = stack.StackSize,
        };
    }

    public static JObject? Stack(JsonItemStack? stack) => Stack(stack?.ResolvedItemstack);

    public static JObject Quantity(NatFloat? q)
    {
        var o = new JObject { ["avg"] = Round(q?.avg ?? 1) };
        if (q != null && q.var != 0) o["var"] = Round(q.var);
        return o;
    }

    /// <summary>Floats go through decimal so 0.1f is written as 0.1, not 0.100000001.</summary>
    public static double Round(float f) =>
        float.IsFinite(f) && Math.Abs(f) < 1e15 ? (double)Math.Round((decimal)f, 4) : f;

    public static string Lower(object value) => value.ToString()!.ToLowerInvariant();
}
