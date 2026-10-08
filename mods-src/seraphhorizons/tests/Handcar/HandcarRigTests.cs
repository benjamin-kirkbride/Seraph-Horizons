using System.Text.Json;
using SeraphHorizons.Mod.Handcar.Core;
using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.Handcar;

/// <summary>
/// The handcar's generated files (Handcar/tools/make_shape.py) and its entity type, held together:
/// the rig parses for the gameplay and for the shared rig maths, every reference pose gives the
/// matrices the Python reference wrote, and the entity type's bogies, seats, animations and drive
/// agree with the rig and the settings.
/// </summary>
public class HandcarRigTests
{
    private static string File(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name);
        Assert.True(System.IO.File.Exists(path), $"no {name}");
        return System.IO.File.ReadAllText(path);
    }

    private static readonly JsonDocumentOptions Lenient = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private static JsonElement Entity() => JsonDocument.Parse(File("handcar-entity.json"), Lenient).RootElement;

    private static JsonElement Rig() => JsonDocument.Parse(File("handcar-rig.json")).RootElement;

    [Fact]
    public void The_shipped_rig_gives_the_cycle_and_both_seats()
    {
        var rig = HandcarRig.Parse(File("handcar-rig.json"));
        Assert.Equal("pump", rig.BodyAnimation);
        Assert.Equal(60, rig.Frames);
        Assert.Equal(3, rig.AxleTurns);
        Assert.Equal(5 / 16.0, rig.WheelRadius, 6);
        Assert.Equal(6 * Math.PI * 5 / 16, rig.DistancePerCycle, 5);
        Assert.Equal(["front", "rear"], rig.Seats.Keys.Order());
        Assert.Equal(-1, rig.Seats["front"].Facing);
        Assert.Equal(1, rig.Seats["rear"].Facing);
        Assert.Equal("seraphhorizons-handcar-grip-front", rig.Seats["front"].Grip);
        Assert.Equal("seraphhorizons-handcar-pump-rear", rig.Seats["rear"].Pump);
    }

    [Theory]
    [InlineData("{}", "cycle")]
    [InlineData("""{"cycle": {"animation": "pump", "frames": 60, "distancePerCycle": 5, "wheelRadius": 0.3125, "axleTurns": 3}}""", "distancePerCycle")]
    [InlineData("""{"cycle": {"animation": "pump", "frames": 1, "distancePerCycle": 5.890486, "wheelRadius": 0.3125, "axleTurns": 3}}""", "frames")]
    [InlineData("""{"cycle": {"animation": "pump", "frames": 60, "distancePerCycle": 5.890486, "wheelRadius": 0.3125, "axleTurns": 3}}""", "riders")]
    [InlineData("""{"cycle": {"animation": "pump", "frames": 60, "distancePerCycle": 5.890486, "wheelRadius": 0.3125, "axleTurns": 3}, "riders": {"front": {"grip": "a", "pump": "b"}}}""", "seatFront")]
    public void A_broken_rig_is_refused(string json, string names)
    {
        var e = Assert.Throws<FormatException>(() => HandcarRig.Parse(json));
        Assert.Contains(names, e.Message);
    }

    [Fact]
    public void The_shipped_rig_matches_the_python_reference_poses()
    {
        var parts = RigParts.Parse(Rig().GetProperty("parts"), new HashSet<string> { "left", "straight", "right" }, null);
        using var doc = JsonDocument.Parse(File("handcar-rig-reference.json"));
        int poses = 0;
        foreach (var pose in doc.RootElement.GetProperty("poses").EnumerateArray())
        {
            var mats = parts.Matrices(new RigInput(pose.GetProperty("theta").GetDouble()));
            var expected = pose.GetProperty("matrices");
            for (int i = 0; i < parts.Parts.Count; i++)
            {
                var rows = expected.GetProperty(parts.Parts[i].Id).EnumerateArray().Select(r => r.EnumerateArray().Select(v => v.GetDouble()).ToArray()).ToArray();
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 4; col++)
                        Assert.True(Math.Abs(mats[i][col * 4 + row] - rows[row][col]) < 2e-4,
                            $"{parts.Parts[i].Id} at theta {pose.GetProperty("theta")}: [{row},{col}] is {mats[i][col * 4 + row]}, the reference {rows[row][col]}");
            }
            poses++;
        }
        Assert.True(poses >= 20, $"only {poses} poses");
    }

    [Fact]
    public void The_entity_types_bogies_are_the_rigs()
    {
        var bogies = Rig().GetProperty("bogies");
        var render = Entity().GetProperty("attributes").GetProperty("SGLocomotive").GetProperty("Render");
        Assert.Equal(bogies.GetProperty("bodyOffsetForward").GetDouble(), render.GetProperty("BodyOffsetForward").GetDouble(), 6);
        Assert.Equal(0, render.GetProperty("BodyOffsetVertical").GetDouble());
        var list = render.GetProperty("Bogies").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.Equal(bogies.GetProperty("front").GetDouble(), list[0].GetProperty("OffsetForward").GetDouble(), 6);
        Assert.Equal(bogies.GetProperty("rear").GetDouble(), list[1].GetProperty("OffsetForward").GetDouble(), 6);
        Assert.All(list, b => Assert.Equal(bogies.GetProperty("shape").GetString(), b.GetProperty("Shape").GetString()));
    }

    [Fact]
    public void The_entity_types_seats_and_animations_are_the_rigs_on_both_sides()
    {
        var rig = HandcarRig.Parse(File("handcar-rig.json"));
        var entity = Entity();
        Assert.Equal("yangtransport.sglocomotive", entity.GetProperty("class").GetString());
        var anim = entity.GetProperty("client").GetProperty("animations").EnumerateArray().Single();
        Assert.Equal(rig.BodyAnimation, anim.GetProperty("code").GetString());
        Assert.True(anim.GetProperty("clientSide").GetBoolean());
        foreach (var side in new[] { "client", "server" })
        {
            var behaviors = entity.GetProperty(side).GetProperty("behaviors").EnumerateArray().ToList();
            Assert.Contains(behaviors, b => b.GetProperty("code").GetString() == "seraphhorizons.HumanPowered");
            var seats = behaviors.Single(b => b.GetProperty("code").GetString() == "seatable").GetProperty("seats").EnumerateArray().ToList();
            Assert.Equal(rig.Seats.Count, seats.Count);
            foreach (var seat in seats)
            {
                var riding = rig.Seats[seat.GetProperty("seatId").GetString()!];
                Assert.Equal(riding.Grip, seat.GetProperty("animation").GetString());
                float turn = seat.TryGetProperty("mountRotation", out var r) ? r.GetProperty("y").GetSingle() : 0f;
                Assert.Equal(riding.Turn, turn);
                Assert.Equal("FixateYaw", seat.GetProperty("angleMode").GetString());
            }
        }
    }

    [Fact]
    public void The_entity_types_drive_starts_at_the_settings_defaults_and_reverses_at_full_speed()
    {
        var drive = Entity().GetProperty("attributes").GetProperty("SGLocomotive").GetProperty("Drive");
        Assert.Equal(HandcarConfig.Defaults.CoastDrag, drive.GetProperty("Roll0").GetSingle(), 5);
        Assert.Equal(HandcarConfig.Defaults.DragPerWeight, drive.GetProperty("RollW").GetSingle(), 5);
        Assert.Equal(HandcarConfig.Defaults.BrakeDeceleration, drive.GetProperty("BrakeDecel").GetSingle(), 5);
        Assert.Equal(1.0, drive.GetProperty("ReverseMaxSpeedMul").GetDouble());
        Assert.Equal(HandcarDrive.StopEpsilon, drive.GetProperty("StopEpsilon").GetDouble());
        var attributes = Entity().GetProperty("attributes");
        Assert.True(attributes.GetProperty("deconstructible").GetBoolean());
        Assert.Equal("seraphhorizons:handcar", attributes.GetProperty("deconstructDrops").EnumerateArray().Single().GetProperty("code").GetString());
    }

    [Fact]
    public void The_rider_reference_names_the_rigs_animations_and_seats()
    {
        var rig = HandcarRig.Parse(File("handcar-rig.json"));
        using var doc = JsonDocument.Parse(File("handcar-rider-reference.json"));
        var root = doc.RootElement;
        Assert.Equal(rig.Frames, root.GetProperty("frames").GetInt32());
        foreach (var (id, seat) in rig.Seats)
        {
            var names = root.GetProperty("animations").GetProperty(id);
            Assert.Equal(seat.Grip, names.GetProperty("grip").GetString());
            Assert.Equal(seat.Pump, names.GetProperty("pump").GetString());
            Assert.Equal(seat.Turn, root.GetProperty("seats").GetProperty(id).GetProperty("turn").GetSingle());
        }
        Assert.True(root.GetProperty("poses").GetArrayLength() >= 2 * 8);
    }
}
