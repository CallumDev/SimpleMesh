using System;
using System.Numerics;
using SimpleMesh.Convex.Quickhull;

namespace SimpleMesh.Util;

static class Triangulation
{
    public static int Triangulate(
        ReadOnlySpan<Vector3> positions,
        Span<int> outputIndices)
    {
        if (positions.Length <= 3)
        {
            for (int i = 0; i < positions.Length; i++)
                outputIndices[i] = i;
            return positions.Length;
        }

        if (positions.Length == 4)
        {
            int start = 0;
            for (int i = 0; i < 4; i++)
            {
                var v0 = positions[(i + 3) % 4];
                var v1 = positions[(i + 2) % 4];
                var v2 = positions[(i + 1) % 4];
                var v = positions[i];

                var left = (v0 - v).Normalized();
                var diag = (v1 - v).Normalized();
                var right = (v2 - v).Normalized();

                var angle = MathF.Acos(Vector3.Dot(left,diag)) +  MathF.Acos(Vector3.Dot(right,diag));
                if (angle > MathF.PI)
                {
                    start = i;
                    break;
                }
            }

            outputIndices[0] = start;
            outputIndices[1] = (start + 1) % 4;
            outputIndices[2] = (start + 2) % 4;


            outputIndices[3] = start;
            outputIndices[4] = (start + 2) % 4;
            outputIndices[5] = (start + 3) % 4;
            return 6;
        }

        // Triangle fan triangulation
        // Should be replaced with ear-clipping later
        int oidx = 0;
        for (int i = 1; i < positions.Length - 1; i++)
        {
            outputIndices[oidx++] = 0;
            outputIndices[oidx++] = i;
            outputIndices[oidx++] = i + 1;
        }
        return oidx;
    }
}
