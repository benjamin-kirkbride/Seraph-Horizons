using SeraphHorizons.Mod.Machines.Core;
using Xunit;

namespace SeraphHorizons.Tests.Arrastra;

/// <summary>The arrastra's particle points (<c>stone1</c>..<c>stone4</c>): one a drag stone, each carried round the
/// post by the stones' part as the game poses it (BEArrastra's grit), at the charge's top.</summary>
public class ArrastraPointsTests
{
    private static OreMachineRig Rig() =>
        OreMachineRig.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "rigs/arrastra-rig.json")), ["charge", "paste"]);

    [Fact]
    public void Each_stone_point_goes_round_the_post_at_the_charges_top()
    {
        var rig = Rig();
        int stones = rig.Parts.IndexOf("stones");
        Assert.True(stones >= 0);
        var post = new Float3(0.5f, 0f, 1f);   // the post's axis, shipped (the pit's centre)
        for (int n = 1; n <= 4; n++)
        {
            var rest = rig.Point($"stone{n}") ?? throw new Xunit.Sdk.XunitException($"no stone{n}");
            double r = Math.Sqrt((rest.X - post.X) * (rest.X - post.X) + (rest.Z - post.Z) * (rest.Z - post.Z));
            Assert.InRange(r, 0.45, 0.55);
            Assert.Equal(3.0 / 16, rest.Y, 4);
            // three axle turns bring the post round once; on the way the point keeps its radius and height
            foreach (double theta in new[] { 0.7, 2.0 * Math.PI, 3.0 * Math.PI })
            {
                var at = Mat4.Apply(rig.Parts.Matrices(new RigInput(theta))[stones], rest);
                Assert.Equal(r, Math.Sqrt((at.X - post.X) * (at.X - post.X) + (at.Z - post.Z) * (at.Z - post.Z)), 3);
                Assert.Equal(rest.Y, at.Y, 4);
                Assert.True(Math.Abs(at.X - rest.X) + Math.Abs(at.Z - rest.Z) > 0.05, $"stone{n} did not move at theta {theta}");
            }
        }
    }
}
