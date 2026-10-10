namespace SeraphHorizons.Mod.Ore.Core;

/// <summary>
/// <c>config/ore-processing.json</c> as written (epic #684, #685): the recovery figures, as plain
/// properties and string-keyed tables so the game's Newtonsoft reader and the tests'
/// System.Text.Json both read it. <see cref="OreRecovery"/> is built from it and is what callers
/// use; the file's comments say what each figure means.
/// </summary>
public sealed class OreProcessingConfig
{
    /// <summary>Units of metal in one crushed, ground or concentrate item (a nugget's 5).</summary>
    public double ConcentrateUnits { get; set; } = 5;

    /// <summary>Base recovery per <see cref="Concentrator"/>, keyed by its lower-case name.</summary>
    public Dictionary<string, double> Concentrators { get; set; } = new();

    public double Unclassified { get; set; } = 1;
    public double FineUnground { get; set; } = 1;
    public double FreeUnamalgamated { get; set; } = 1;

    /// <summary>Recovery per <see cref="Roaster"/>, keyed by its lower-case name.</summary>
    public Dictionary<string, double> Roasters { get; set; } = new();

    /// <summary>A by-product's recovery per <see cref="PartingMethod"/> and then per
    /// <see cref="OreTier"/> (<c>hand</c>, <c>tier1</c>..<c>tier4</c>); a tier left out has no
    /// station.</summary>
    public Dictionary<string, Dictionary<string, double>> Parting { get; set; } = new();

    /// <summary>The share of its units each <see cref="OreForm"/> smelts to, keyed by its lower-case
    /// name.</summary>
    public Dictionary<string, double> Smelt { get; set; } = new();

    /// <summary>The bone-ash cupel at the forge (#722).</summary>
    public CupelEntry Cupel { get; set; } = new();

    /// <summary>The clay liquation pan in a firepit or the forge (#724).</summary>
    public LiquationEntry Liquation { get; set; } = new();

    /// <summary>Per ore (the ore part of its code: <c>galena</c>, <c>quartz_nativegold</c>).</summary>
    public Dictionary<string, OreEntry> Ores { get; set; } = new();

    /// <summary>Retorting in the still (#726).</summary>
    public RetortEntry Retort { get; set; } = new();

    public sealed class RetortEntry
    {
        /// <summary>The mercury the still collects and the amalgam pan uses: a liquid portion item
        /// (Expanded Matter's <c>em:mercuryportion</c>, 100 to the litre).</summary>
        public string Mercury { get; set; } = "em:mercuryportion";
        /// <summary>Portions of mercury the amalgam pan puts into one amalgam (#718).</summary>
        public double AmalgamMercury { get; set; } = 10;
        /// <summary>The share of an amalgam's mercury the still returns.</summary>
        public double MercuryReturn { get; set; } = 0.9;
        /// <summary>Portions of mercury one item of cinnabar gives, by item code.</summary>
        public Dictionary<string, double> Cinnabar { get; set; } = new();
        /// <summary>Portions a lit still drives over per second (the game's still ticks ten times a
        /// second, so at most 10).</summary>
        public double PortionsPerSecond { get; set; } = 2;
    }

    public sealed class OreEntry
    {
        /// <summary><c>oxide</c> (default), <c>sulfide</c>, <c>native</c> or <c>placer</c>.</summary>
        public string? Class { get; set; }
        public double Density { get; set; } = 1;
        /// <summary>Free gold or silver: loses <see cref="FreeUnamalgamated"/> unless amalgamated.</summary>
        public bool Free { get; set; }
        /// <summary>The share of the main metal smelting gives without parting.</summary>
        public double Unparted { get; set; } = 1;
        public List<ByProductEntry> ByProducts { get; set; } = new();
    }

    public sealed class ByProductEntry
    {
        public string Metal { get; set; } = "";
        /// <summary>Units of the by-product per unit of the main metal recovered.</summary>
        public double Share { get; set; }
        /// <summary>A <see cref="PartingMethod"/>'s lower-case name.</summary>
        public string PartedBy { get; set; } = "";
    }

    public sealed class CupelEntry
    {
        /// <summary>The most metal units a charge holds, ore and lead added together.</summary>
        public double CapacityUnits { get; set; } = 200;
        /// <summary>Lead units a charge needs per unit of an ore whose main metal is not lead.</summary>
        public double LeadPerOreUnit { get; set; } = 1;
        /// <summary>The heat (°C) the forge must hold the cupel at.</summary>
        public double MeltingPoint { get; set; } = 950;
        /// <summary>Seconds per 100 units of charge with the blast gate open.</summary>
        public double SecondsPerIngot { get; set; } = 60;
    }

    public sealed class LiquationEntry
    {
        /// <summary>The most metal units a charge holds.</summary>
        public double CapacityUnits { get; set; } = 100;
        /// <summary>The heat (°C) at which the tin sweats out.</summary>
        public double TinPoint { get; set; } = 240;
        /// <summary>The heat (°C) over which the lead runs with the tin and is lost.</summary>
        public double LeadPoint { get; set; } = 327;
        /// <summary>Seconds at the tin point per 100 units of charge.</summary>
        public double SecondsPerIngot { get; set; } = 15;
    }
}
