using SeraphHorizons.Mod.Machines.Core;

namespace SeraphHorizons.Mod.Tests;

/// <summary>The single-pass part split's game-free half (<see cref="PartSplit"/>): which part
/// claims each element of a synthetic shape tree, and cutting a tagged mesh back into each part's
/// faces, exactly or not at all.</summary>
public class PartSplitTests
{
    private sealed record El(string Name, int Faces, params El[] Children);

    private static RigParts Parts() => new(
    [
        new RigPart("arm", ["arm*"], null, null, []),
        new RigPart("wheel", ["wheel", "spoke*"], "wheel", null, []),
        new RigPart("cover", ["cover"], "cover", null, []),
    ]);

    // frame (no part) holds an arm with an unnamed pin, a loose spoke, and a wheel whose child
    // is called "arm2" (the first part matching any name in the chain wins: arm, not wheel)
    private static El[] Tree() =>
    [
        new El("frame", 6,
            new El("arm1", 6, new El("pin", 4)),
            new El("spoke3", 2)),
        new El("wheel", 5, new El("arm2", 1), new El("hub", 0)),
        new El("cover", 0),
    ];

    private static List<(El El, int Part)> Walked()
    {
        var seen = new List<(El, int)>();
        PartSplit.Walk(Tree(), e => e.Name, e => e.Children, Parts(), (e, p) => seen.Add((e, p)));
        return seen;
    }

    [Fact]
    public void Walk_visits_every_element_depth_first_with_its_chains_part()
    {
        var walked = Walked().Select(w => (w.El.Name, w.Part)).ToList();
        Assert.Equal(
        [
            ("frame", -1), ("arm1", 0), ("pin", 0), ("spoke3", 1),
            ("wheel", 1), ("arm2", 0), ("hub", 1), ("cover", 2),
        ], walked);
    }

    /// <summary>What the tesselator writes with joint ids: every face of every element, in walk
    /// order, four vertices each carrying the element's tag (part + 1, 0 for none).</summary>
    private static (int[] Tags, List<int>[] Expected) Tessellate()
    {
        var tags = new List<int>();
        var expected = Enumerable.Range(0, 3).Select(_ => new List<int>()).ToArray();
        foreach (var (el, part) in Walked())
            for (int f = 0; f < el.Faces; f++)
            {
                if (part >= 0)
                    expected[part].Add(tags.Count / 4);
                tags.AddRange(Enumerable.Repeat(part + 1, 4));
            }
        return (tags.ToArray(), expected);
    }

    [Fact]
    public void Every_face_lands_in_its_parts_mesh_and_faces_of_no_part_are_dropped()
    {
        var (tags, expected) = Tessellate();
        var faces = PartSplit.FacesByPart(tags, tags.Length, 3);
        Assert.NotNull(faces);
        for (int p = 0; p < 3; p++)
            Assert.Equal(expected[p], faces[p]);
        Assert.Equal(6 + 4 + 1, faces[0].Count);  // arm1, pin, arm2
        Assert.Equal(2 + 5 + 0, faces[1].Count);  // spoke3, wheel, hub
        Assert.Empty(faces[2]);                   // the cover has no faces: its mesh is null
        Assert.Equal(tags.Length / 4 - 6, faces.Sum(f => f.Count)); // all but the frame's
    }

    [Fact]
    public void A_face_whose_vertices_disagree_refuses_the_split()
    {
        var (tags, _) = Tessellate();
        tags[5] = tags[4] + 1;
        Assert.Null(PartSplit.FacesByPart(tags, tags.Length, 3));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void A_tag_out_of_range_refuses_the_split(int tag)
    {
        var tags = new[] { 1, 1, 1, 1, tag, tag, tag, tag };
        Assert.Null(PartSplit.FacesByPart(tags, 8, 3));
    }

    [Fact]
    public void Too_few_tags_or_a_partial_face_refuses_the_split()
    {
        var tags = new[] { 1, 1, 1, 1, 1, 1 };
        Assert.Null(PartSplit.FacesByPart(tags, 6, 3));
        Assert.Null(PartSplit.FacesByPart(tags, 8, 3));
        Assert.NotNull(PartSplit.FacesByPart(tags, 4, 3));
    }

    [Fact]
    public void Faces_drawn_from_their_own_vertices_are_self_contained()
    {
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];
        Assert.True(PartSplit.FacesAreSelfContained(indices, 12, 8));
        Assert.False(PartSplit.FacesAreSelfContained(indices, 6, 8));  // too few indices
        int[] crossing = [0, 1, 2, 0, 2, 4, 4, 5, 6, 4, 6, 7];
        Assert.False(PartSplit.FacesAreSelfContained(crossing, 12, 8));
    }
}
