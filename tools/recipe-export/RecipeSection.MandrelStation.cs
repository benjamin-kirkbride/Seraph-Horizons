using Newtonsoft.Json.Linq;
using SeraphHorizons.RecipeExport.Recipes;

namespace SeraphHorizons.RecipeExport;

/// <summary>The mandrel forging station's process (seraphhorizons, MandrelStation/). See docs/recipe-browser/schema.md.</summary>
public static partial class RecipeSection
{
    public const string MandrelStationType = "mandrelstation";

    private static void FillMandrelStation(Context ctx, List<JObject> records, SortedDictionary<string, JObject> types)
    {
        if (MandrelStationExport.Read(ctx.Api) is not { } station)
            return;
        if (station.Frame == null)
            ctx.Api.Logger.Warning("[seraphexport] the mandrel station's {0} is not registered; its process is exported without it", station.FrameCode);
        var mine = station.Classes.Select(c => MandrelStationRecord(ctx, station, c)).ToList();
        records.AddRange(mine);
        AddType(types, MandrelStationType, "Mandrel forging station", mine.Count, "machine", "MandrelStationSettings", station.Mod);
    }

    /// <summary>The job: by hand, its turns the blows (the work: that many <c>blows</c>), the mandrel
    /// kept, and the hammer, when it pays, worn a fixed amount (its toolDurabilityCost) a job.</summary>
    private static JObject Machine(ForgeClass k, bool hammer)
    {
        var machine = new JObject
        {
            ["power"] = "hand",
            ["turns"] = Num(k.BlowsPerHollow),
            ["work"] = new JObject { ["amount"] = Num(k.BlowsPerHollow), ["unit"] = "blows" },
            ["kept"] = new JArray(1),
        };
        if (hammer)
            machine["wear"] = new JObject { ["ingredient"] = 2, ["rule"] = "fixed" };
        return machine;
    }

    /// <summary>
    /// One metal on the mandrel station: the hollow section (consumed), the mandrel (kept: fitted,
    /// never consumed), the hammer (a tool worn a fixed amount a job, its wear a blow times the blows) and the station; two pipe
    /// sections of the metal. Worked by hand (power <c>hand</c>): its turns are the hammer's blows, a
    /// right-click each. Nothing of the station wears and there is no oil.
    /// </summary>
    private static JObject MandrelStationRecord(Context ctx, MandrelStationData s, ForgeClass k)
    {
        var ingredients = new JArray(
            Def(k.Hollow.Code.ToString(), "item", 1),
            Def(MandrelStationExport.MandrelCodes[0], "item", 1, "kept"));
        bool hammer = s.Hammers.Count > 0 && s.HammerWearPerBlow > 0;
        if (hammer)
        {
            var tool = Def(s.Hammers.Any(h => h.Code.ToString() == MandrelStationExport.HammerTemplate) ? MandrelStationExport.HammerTemplate : s.Hammers[0].Code.ToString(), "item", 1, "tool");
            tool["isTool"] = true;
            tool["toolDurabilityCost"] = k.BlowsPerHollow * s.HammerWearPerBlow;
            ingredients.Add(tool);
        }
        ingredients.Add(Def(s.FrameCode, "block", 1, "station"));

        // (new JArray(JArray) would copy the inner array, not nest it)
        var stacks = new JArray
        {
            new JArray(Stack(ctx, k.Hollow, 1)),
            new JArray(s.Mandrels.Select(i => Stack(ctx, i, 1))),
        };
        if (hammer)
            stacks.Add(new JArray(s.Hammers.Select(i => Stack(ctx, i, 1))));
        stacks.Add(s.Frame != null ? new JArray(Stack(ctx, s.Frame, 1)) : new JArray());

        return new JObject
        {
            ["id"] = $"{MandrelStationType}|{k.Hollow.Code}|0",
            ["type"] = MandrelStationType,
            ["mod"] = s.Mod,
            ["ingredients"] = ingredients,
            ["outputs"] = new JArray(Def(k.Section.Code.ToString(), "item", s.SectionsPerHollow)),
            ["variants"] = new JArray(new JObject
            {
                ["ingredients"] = stacks,
                ["outputs"] = new JArray(Stack(ctx, k.Section, s.SectionsPerHollow)),
            }),
            ["machine"] = Machine(k, hammer),
            ["extra"] = new JObject { ["rig"] = MandrelStationExport.RigAsset.ToString(), ["class"] = k.Name, ["metal"] = k.Metal, ["blows"] = k.BlowsPerHollow },
        };
    }
}
