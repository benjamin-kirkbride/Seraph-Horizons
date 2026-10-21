using SeraphHorizons.Mod.Machines.Core;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeraphHorizons.Mod.Machines;

/// <summary>
/// A puff of particles: how many, their colour, velocity range, life, gravity, size and model, and the few extras
/// (<see cref="MaxCount"/>, <see cref="MaxSize"/>, the evolves, the glow, wind) the smokes and flames take. The
/// presets of <see cref="MachineParticles"/> make one; <c>with</c> changes any of it, <see cref="Toward"/> its
/// direction, and <see cref="Spawn"/> or <see cref="SpawnBetween"/> spawns it (client side, where
/// <see cref="BEMachine.FrameInput"/> and the renderers run).
/// </summary>
public sealed record Puff(int Count, int Colour, Vec3f MinVelocity, Vec3f MaxVelocity, float Life, float Gravity, float Size,
    EnumParticleModel Model = EnumParticleModel.Quad)
{
    /// <summary>At most this many a spawn (from <see cref="Count"/>); 0, always <see cref="Count"/>.</summary>
    public int MaxCount { get; init; }
    /// <summary>The largest size (from <see cref="Size"/>); 0, always <see cref="Size"/>.</summary>
    public float MaxSize { get; init; }
    public EvolvingNatFloat? SizeEvolve { get; init; }
    public EvolvingNatFloat? OpacityEvolve { get; init; }
    /// <summary>The particle's own light (0 to 255; 128 and up glows in the dark), as the vanilla anvil's sparks.</summary>
    public int VertexFlags { get; init; }
    public bool SelfPropelled { get; init; }
    public bool WindAffected { get; init; }
    public bool TerrainCollision { get; init; } = true;

    /// <summary>The same, moving along <paramref name="velocity"/> (blocks a second, world frame:
    /// <see cref="MachineParticles.WorldDirection"/> turns a native one), each axis up to <paramref name="jitter"/> either way.</summary>
    public Puff Toward(Vec3f velocity, float jitter) => this with
    {
        MinVelocity = new Vec3f(velocity.X - jitter, velocity.Y - jitter, velocity.Z - jitter),
        MaxVelocity = new Vec3f(velocity.X + jitter, velocity.Y + jitter, velocity.Z + jitter),
    };

    // the game's own short call does for a plain puff; the rest need the properties
    private bool Plain => MaxCount <= Count && MaxSize <= Size && SizeEvolve == null && OpacityEvolve == null
                          && VertexFlags == 0 && !SelfPropelled && !WindAffected && TerrainCollision;

    /// <summary>Spawns it in the box <paramref name="at"/> ± <paramref name="spread"/> (half the box's size each way; none, a point).</summary>
    public void Spawn(IWorldAccessor world, Vec3d at, Vec3d? spread = null)
    {
        var s = spread ?? new Vec3d();
        SpawnIn(world, new Vec3d(at.X - s.X, at.Y - s.Y, at.Z - s.Z), new Vec3d(at.X + s.X, at.Y + s.Y, at.Z + s.Z));
    }

    /// <summary>Spawns it in the box with corners <paramref name="a"/> and <paramref name="b"/>, in either order (a span
    /// worked out in the native frame and turned into the world).</summary>
    public void SpawnBetween(IWorldAccessor world, Vec3d a, Vec3d b) =>
        SpawnIn(world, new Vec3d(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z)),
            new Vec3d(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z)));

    private void SpawnIn(IWorldAccessor world, Vec3d min, Vec3d max)
    {
        if (Plain)
        {
            world.SpawnParticles(Count, Colour, min, max, MinVelocity, MaxVelocity, Life, Gravity, Size, Model, null);
            return;
        }
        var props = new SimpleParticleProperties(Count, Math.Max(Count, MaxCount), Colour, min, max, MinVelocity, MaxVelocity,
            Life, Gravity, Size, Math.Max(Size, MaxSize), Model)
        {
            VertexFlags = VertexFlags,
            SelfPropelled = SelfPropelled,
            WindAffected = WindAffected,
            WithTerrainCollision = TerrainCollision,
        };
        if (SizeEvolve is { } size)
            props.SizeEvolve = size;
        if (OpacityEvolve is { } opacity)
            props.OpacityEvolve = opacity;
        world.SpawnParticles(props);
    }
}

/// <summary>
/// What the machines' particles share: a timer (<see cref="ParticleTimer"/>), the presets (each a
/// <see cref="Puff"/>, its look the machine that first had it), the colours, a material's colour, and the spread
/// and direction helpers. Counts stay small: a machine shows its work, it does not fog the room.
/// <para>
/// A machine spawns from its <see cref="BEMachine.FrameInput"/> (each frame, client side; <c>far</c> true past the
/// renderer's detail range, where it spawns nothing) at a point of its rig turned into the world
/// (<see cref="BEMachine.WorldPoint"/>; the generic rigs' named points are <see cref="OreMachineRig.Point"/>):
/// </para>
/// <list type="bullet">
/// <item>A steady effect while something lasts (the cut runs, the tank has oil): a <see cref="ParticleTimer"/> field
/// and <c>if (!far &amp;&amp; running &amp;&amp; _chips.Due(dt)) MachineParticles.Chips(colour).Spawn(Api.World, WorldPoint(rig.Chips), MachineParticles.Around(0.04));</c>
/// (the gear cutter). The timer only counts while the condition before it holds.</item>
/// <item>An event (a blow lands, a stroke ends, a shake turns back): an edge, found by keeping last frame's value of
/// what the rig animates by and comparing. A phase in turns (the clock's θ, or the work W) crosses a whole number,
/// or a fraction of the cycle where the event is, when <c>Math.Floor(now - at) != Math.Floor(before - at)</c>;
/// a state (a bool, the clock's stage) changes when it differs from last frame's. The bucking mill counts its
/// strokes this way (<c>MillMotion.StrokesBetween</c>). Spawn once on the edge, with a bigger count than a steady
/// effect's tick; keep the remembered value updated even while <c>far</c>, so coming near does not fire a stale edge.</item>
/// </list>
/// Colours are <see cref="ColorUtil.ToRgba"/> values as the existing particles write them: the machines' metals give
/// blue before red (the draw bench's copper is <c>ToRgba(a, 70, 120, 190)</c>), so a colour for a particle is
/// written <see cref="Rgb"/>.
/// </summary>
public static class MachineParticles
{
    // ---- Colours ----

    /// <summary>A particle colour from red, green and blue as one means them: <c>ToRgba(a, b, g, r)</c>, the order the
    /// machines' metal colours (copper red, steel bluish) are written in.</summary>
    public static int Rgb(int alpha, int r, int g, int b) => ColorUtil.ToRgba(alpha, b, g, r);

    /// <summary>Lubricating oil, the gear cutter's spray and the oilers' drips.</summary>
    public static readonly int Oil = ColorUtil.ToRgba(190, 40, 140, 190);
    /// <summary>Clear water, a little blue.</summary>
    public static readonly int Water = Rgb(150, 150, 185, 215);
    /// <summary>Muddy water off the gravel, the rocker's.</summary>
    public static readonly int MuddyWater = ColorUtil.ToRgba(140, 160, 190, 220);
    /// <summary>Molten metal, bright orange (spawn it glowing, <see cref="Puff.VertexFlags"/>).</summary>
    public static readonly int Molten = Rgb(255, 255, 150, 40);
    /// <summary>A grey slurry of crushed rock and water.</summary>
    public static readonly int Slurry = Rgb(200, 120, 112, 100);
    /// <summary>Rock dust and fines, the riddle's.</summary>
    public static readonly int RockDust = ColorUtil.ToRgba(200, 105, 95, 90);

    /// <summary>The fumes the smelting gives off: nitrous fumes, red-brown (the acid parting).</summary>
    public static readonly int FumeNitrous = Rgb(150, 150, 70, 30);
    /// <summary>Litharge fume, white (lead oxide off the cupel and the Parkes kettle).</summary>
    public static readonly int FumeLead = Rgb(130, 235, 232, 220);
    /// <summary>Sulfur dioxide, yellowish (the roasters).</summary>
    public static readonly int FumeSulfur = Rgb(120, 215, 210, 140);
    /// <summary>Zinc oxide, a bluish white (the zinc distilled off in the Parkes process).</summary>
    public static readonly int FumeZinc = Rgb(130, 215, 225, 235);

    /// <summary>A metal's colour for its dust, chips or drips, at <paramref name="alpha"/>; <paramref name="fallback"/>
    /// for a metal it does not know (or none). Lead, copper, iron and steel are the draw bench's and borer's.</summary>
    public static int MetalColour(string? metal, int alpha, int fallback) => metal switch
    {
        "lead" => ColorUtil.ToRgba(alpha, 120, 110, 105),
        "copper" => ColorUtil.ToRgba(alpha, 70, 120, 190),
        "iron" or "meteoriciron" => ColorUtil.ToRgba(alpha, 95, 100, 105),
        "steel" => ColorUtil.ToRgba(alpha, 150, 145, 130),
        "stainlesssteel" => Rgb(alpha, 175, 178, 182),
        "tin" => Rgb(alpha, 200, 200, 195),
        "zinc" => Rgb(alpha, 165, 175, 180),
        "silver" => Rgb(alpha, 210, 210, 215),
        "gold" => Rgb(alpha, 230, 180, 40),
        "tinbronze" or "bismuthbronze" or "blackbronze" => Rgb(alpha, 180, 125, 60),
        "brass" => Rgb(alpha, 200, 170, 70),
        "nickel" or "cupronickel" => Rgb(alpha, 170, 170, 160),
        _ => fallback,
    };

    /// <summary>
    /// The colour of <paramref name="stack"/>'s material at <paramref name="alpha"/>: its <c>metal</c> (or
    /// <c>material</c>) variant's colour (<see cref="MetalColour"/>); else, given the client, a random pixel of its
    /// texture as the game colours a block's breaking (ore, rock, wood); else <paramref name="fallback"/>.
    /// </summary>
    public static int MaterialColour(ItemStack? stack, int alpha, int fallback, ICoreClientAPI? capi = null)
    {
        if (stack?.Collectible is not { } c)
            return fallback;
        string? metal = c.Variant["metal"] ?? c.Variant["material"];
        int known = MetalColour(metal, alpha, 0);
        if (known != 0)
            return known;
        return capi != null ? (c.GetRandomColor(capi, stack) & 0xffffff) | (alpha << 24) : fallback;
    }

    // ---- Where and which way ----

    /// <summary>A spread of <paramref name="r"/> each way.</summary>
    public static Vec3d Around(double r) => new(r, r, r);

    /// <summary>A spread of <paramref name="x"/>, <paramref name="y"/> and <paramref name="z"/> each way (world axes).</summary>
    public static Vec3d Around(double x, double y, double z) => new(x, y, z);

    /// <summary>A spread of <paramref name="native"/> each way along the machine's own axes (wide across its front, say),
    /// as world axes for a machine facing <paramref name="facing"/>.</summary>
    public static Vec3d Around(Float3 native, Side facing)
    {
        var d = WorldDirection(native, facing);
        return new Vec3d(Math.Abs(d.X), Math.Abs(d.Y), Math.Abs(d.Z));
    }

    /// <summary>A direction (or velocity) in the machine's native frame as it points in the world, the machine facing
    /// <paramref name="facing"/>.</summary>
    public static Vec3f WorldDirection(Float3 native, Side facing)
    {
        var a = Footprint.WorldPoint(Int3.Zero, native, facing);
        var o = Footprint.WorldPoint(Int3.Zero, new Float3(0, 0, 0), facing);
        return new Vec3f((float)(a.X - o.X), (float)(a.Y - o.Y), (float)(a.Z - o.Z));
    }

    // ---- Presets ----

    /// <summary>Fine metal or rock dust, falling: the draw bench's at the die.</summary>
    public static Puff Dust(int colour) =>
        new(2, colour, new Vec3f(-0.3f, -0.1f, -0.3f), new Vec3f(0.3f, 0.3f, 0.3f), 0.6f, 1f, 0.15f, EnumParticleModel.Cube);

    /// <summary>Chips flung off a cutter: the gear cutter's.</summary>
    public static Puff Chips(int colour) =>
        new(2, colour, new Vec3f(-0.8f, -0.2f, -0.8f), new Vec3f(0.8f, 0.6f, 0.8f), 0.8f, 1f, 0.25f, EnumParticleModel.Cube);

    /// <summary>Glowing sparks, the vanilla anvil's colour: the mandrel station's off a blow.</summary>
    public static Puff Sparks(int? colour = null) =>
        new(3, colour ?? ColorUtil.ToRgba(255, 255, 233, 83), new Vec3f(-1.2f, 0.6f, -1.2f), new Vec3f(1.2f, 2.2f, 1.2f), 0.12f, 1f, 0.1f,
            EnumParticleModel.Cube) { MaxCount = 6, MaxSize = 0.2f, VertexFlags = 128 };

    /// <summary>Water thrown up and falling back: a splash where water lands or is worked.</summary>
    public static Puff Splash(int? colour = null) =>
        new(3, colour ?? Water, new Vec3f(-0.3f, 0f, -0.3f), new Vec3f(0.3f, 0.4f, 0.3f), 0.5f, 1f, 0.2f);

    /// <summary>A drop falling: oil (<see cref="Oil"/>), water or molten metal (<see cref="Molten"/>, <paramref name="glowing"/>);
    /// the oilers'.</summary>
    public static Puff Drip(int colour, bool glowing = false) =>
        new(1, colour, new Vec3f(0, -0.4f, 0), new Vec3f(0, -0.2f, 0), 0.6f, 1f, 0.12f) { VertexFlags = glowing ? 160 : 0 };

    /// <summary>Wet slurry slopping over: heavier than a splash, slow.</summary>
    public static Puff SlurryDrops(int? colour = null) =>
        new(2, colour ?? Slurry, new Vec3f(-0.1f, -0.3f, -0.1f), new Vec3f(0.1f, 0.05f, 0.1f), 0.8f, 1f, 0.18f);

    /// <summary>Dark smoke drifting up and spreading: the dry machine's (<see cref="MachineOiling"/>).</summary>
    public static Puff Smoke(int? colour = null) =>
        new(1, colour ?? ColorUtil.ToRgba(110, 45, 42, 40), new Vec3f(-0.08f, 0.15f, -0.08f), new Vec3f(0.08f, 0.35f, 0.08f), 2.5f, -0.01f, 0.35f)
        {
            MaxCount = 2,
            MaxSize = 0.8f,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.2f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -40f),
            SelfPropelled = true,
            WindAffected = true,
        };

    /// <summary>A chimney's smoke rising off its top: the crucible furnace's.</summary>
    public static Puff ChimneySmoke() =>
        new(1, ColorUtil.ToRgba(80, 60, 60, 60), new Vec3f(-0.1f, 0.3f, -0.1f), new Vec3f(0.1f, 0.6f, 0.1f), 3, -0.02f, 0.5f)
        {
            MaxCount = 2,
            MaxSize = 1.5f,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.5f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -40),
        };

    /// <summary>Flames licking up, glowing: the crucible furnace's at its open hole.</summary>
    public static Puff Flame() =>
        new(2, ColorUtil.ToRgba(220, 255, 160, 40), new Vec3f(-0.05f, 0.3f, -0.05f), new Vec3f(0.05f, 0.8f, 0.05f), 0.5f, -0.05f, 0.4f)
        {
            MaxCount = 4,
            MaxSize = 0.9f,
            VertexFlags = 200,
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -200),
        };

    /// <summary>A few embers drifting up out of a fire, glowing.</summary>
    public static Puff Embers() =>
        new(1, Rgb(255, 255, 140, 40), new Vec3f(-0.15f, 0.2f, -0.15f), new Vec3f(0.15f, 0.6f, 0.15f), 0.9f, -0.02f, 0.06f, EnumParticleModel.Cube)
        {
            MaxCount = 2,
            MaxSize = 0.1f,
            VertexFlags = 255,
        };

    /// <summary>Coloured gas rising and spreading slowly (<see cref="FumeNitrous"/>, <see cref="FumeLead"/>,
    /// <see cref="FumeSulfur"/>, <see cref="FumeZinc"/>).</summary>
    public static Puff Fume(int colour) =>
        new(1, colour, new Vec3f(-0.05f, 0.1f, -0.05f), new Vec3f(0.05f, 0.25f, 0.05f), 3f, -0.01f, 0.3f)
        {
            MaxCount = 2,
            MaxSize = 0.7f,
            SizeEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, 1.5f),
            OpacityEvolve = EvolvingNatFloat.create(EnumTransformFunction.LINEAR, -30f),
            SelfPropelled = true,
            WindAffected = true,
        };

    /// <summary>A fire seen through its door: small flames and now and then an ember, at <paramref name="at"/> (the
    /// rig's <c>fireDoor</c>, on the door's face) over <paramref name="spread"/> (across the door; <see cref="FireDoorSpread"/>).
    /// The fired ore machines call it while lit (<see cref="CrucibleFurnace.BEFiredOreMachine.FireDoorEffect"/>).</summary>
    public static void FireDoor(IWorldAccessor world, Vec3d at, Vec3d spread)
    {
        (Flame() with { Count = 1, MaxCount = 2, Size = 0.25f, MaxSize = 0.5f, MaxVelocity = new Vec3f(0.05f, 0.5f, 0.05f) })
            .Spawn(world, at, spread);
        if (world.Rand.NextDouble() < 0.3)
            Embers().Spawn(world, at, spread);
    }

    /// <summary>A fire door's spread in the native frame: across it (x), a little up and down, hardly out of its face (z).</summary>
    public static readonly Float3 FireDoorSpread = new(0.2f, 0.08f, 0.05f);
}

/// <summary>A particle timer: <see cref="Due"/> counts the frame's seconds and says when an interval has passed,
/// starting over then (a steady effect's tick; <see cref="MachineParticles"/>).</summary>
public sealed class ParticleTimer(float interval)
{
    private float _seconds;

    public float Interval { get; } = interval;

    /// <summary>Adds <paramref name="dt"/>; true, and from 0 again, once <see cref="Interval"/> has passed.</summary>
    public bool Due(float dt)
    {
        if ((_seconds += dt) < Interval)
            return false;
        _seconds = 0;
        return true;
    }

    /// <summary>From 0 again.</summary>
    public void Reset() => _seconds = 0;
}
