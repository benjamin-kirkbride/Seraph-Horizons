namespace SeraphHorizons.Mod.Trading.Window.Core;

/// <summary>
/// The hold-to-trade ring's shape (<c>HoldRingRenderer</c>): a band from radius <see cref="Inner"/>
/// to 1, filling clockwise from the top (screen y down) in <see cref="Steps"/> steps.
///
/// <para>The vertex and index counts are the same at every progress: the vertices past the
/// progress sit on its end, so those quads are empty. That is what lets the renderer upload the
/// mesh once and update it in place. The game's <c>UpdateMesh</c> only writes the new data into
/// the buffers the first upload made and keeps that upload's index count, so the first version,
/// which sized the mesh to the progress, uploaded it a step or two long on the hold's first frame
/// and drew that sliver (a sixteenth of the ring) however far the hold went.</para>
/// </summary>
public static class HoldRingGeometry
{
    public const int Steps = 16;
    public const float Inner = 0.75f;

    /// <summary>Two vertices (outer, inner) per step edge.</summary>
    public const int VertexCount = (Steps + 1) * 2;

    /// <summary>Two triangles per step.</summary>
    public const int IndexCount = Steps * 6;

    /// <summary>The ring's vertices for <paramref name="progress"/> (clamped to 0..1), as x, y
    /// pairs on the unit circle's scale: outer then inner for each step edge.</summary>
    public static float[] Vertices(float progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var xy = new float[VertexCount * 2];
        for (int i = 0; i <= Steps; i++)
        {
            double angle = Math.Min(progress, i / (double)Steps) * Math.PI * 2;
            float x = (float)Math.Sin(angle), y = -(float)Math.Cos(angle);
            xy[i * 4] = x;
            xy[i * 4 + 1] = y;
            xy[i * 4 + 2] = x * Inner;
            xy[i * 4 + 3] = y * Inner;
        }
        return xy;
    }

    /// <summary>The triangles, the same at every progress.</summary>
    public static int[] Indices()
    {
        var idx = new int[IndexCount];
        for (int s = 0; s < Steps; s++)
        {
            int a = s * 2;
            idx[s * 6] = a;
            idx[s * 6 + 1] = a + 1;
            idx[s * 6 + 2] = a + 2;
            idx[s * 6 + 3] = a + 2;
            idx[s * 6 + 4] = a + 1;
            idx[s * 6 + 5] = a + 3;
        }
        return idx;
    }
}
