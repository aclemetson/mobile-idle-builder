using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the flat "river" ribbon mesh for a single conveyor cell, in cell-local space (centred
    /// on the origin, lying in the XZ plane). The companion <c>MobileIdleBuilder/ConveyorFlow</c>
    /// shader scrolls a current pattern along <c>uv.y</c>, so the flow direction reads straight from
    /// the moving water — no arrows. <c>uv.x</c> runs 0→1 across the channel width (used to darken the
    /// banks); <c>uv.y</c> runs 0 (inbound edge) → 1 (outbound edge) along the flow.
    ///
    /// Three shapes, all from one ribbon primitive:
    ///   - straight: a quad strip from the inbound edge through the centre to the exit edge;
    ///   - bend:     a quadratic-Bézier curve (control = cell centre) so the current sweeps the corner;
    ///   - merge:    one FULL edge→exit ribbon per inbound (uv.y 0→1) — the straight-through input is a
    ///               complete straight piece and each turning input a complete bend piece, all sharing
    ///               the exit half. They overlay there and, flowing the same direction under the same
    ///               shader, blend seamlessly into one outflow.
    ///
    /// Pure construction (no scene) so EditMode tests can assert counts, monotonic flow UVs, the flat
    /// low profile, and that geometry stays within the cell. Mirrors the house mesh-builder pattern
    /// (<see cref="TorusMeshBuilder"/>).
    /// </summary>
    public static class ConveyorFlowMeshBuilder
    {
        /// <summary>Water ribbon width as a fraction of the cell.</summary>
        public const float WaterWidthFrac = 0.7f;
        /// <summary>Channel floor width as a fraction of the cell. Matches the water (no raised banks/rails);
        /// it is just an opaque dark bed under the translucent ribbon for depth/sorting.</summary>
        public const float FloorWidthFrac = WaterWidthFrac;

        private const int   BendSegments = 6;          // Bézier samples for a 90° turn

        /// <summary>Water ribbon height above the grid plane.</summary>
        public const float FlowY  = 0.06f;
        /// <summary>Channel floor height (just below the water, above the grid tiles).</summary>
        public const float FloorY = 0.04f;

        /// <summary>
        /// Builds the ribbon for a cell. <paramref name="entryDir"/>/<paramref name="exitDir"/> are
        /// the segment's travel directions; <paramref name="inboundEdgeDirs"/> lists the edge
        /// directions of conveyor inbounds (empty/null for a head — the inbound edge is then derived
        /// from <paramref name="entryDir"/>). 2+ inbound edges produce a merge junction.
        /// </summary>
        public static Mesh Build(float cellSize, int entryDir, int exitDir,
                                 IReadOnlyList<int> inboundEdgeDirs, float y = FlowY,
                                 float widthFrac = WaterWidthFrac)
        {
            float half = cellSize * 0.5f;
            float hw   = cellSize * widthFrac * 0.5f;
            var   center = new Vector3(0f, y, 0f);

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            // Collect distinct inbound edge directions; fall back to the entry side for a head.
            var edges = new List<int>();
            if (inboundEdgeDirs != null)
                foreach (var e in inboundEdgeDirs)
                    if (e >= 0 && e < 4 && !edges.Contains(e)) edges.Add(e);
            if (edges.Count == 0) edges.Add(Opposite(entryDir));

            Vector3 EdgeMid(int dir) => center + DirVec(dir) * half;

            if (edges.Count <= 1)
            {
                var path = BuildPath(edges[0], exitDir, center, EdgeMid(edges[0]), EdgeMid(exitDir));
                AppendRibbon(verts, uvs, tris, path, hw, 0f, 1f, y);
            }
            else
            {
                // Each inbound is drawn as a COMPLETE edge→exit ribbon (uv.y 0→1): the straight-through
                // input is a full straight piece, every turning input a full bend piece. They all share
                // the exit half and overlay there; flowing the same direction under the same shader,
                // they blend into one seamless outflow instead of meeting at a hard centre seam.
                foreach (var e in edges)
                    AppendRibbon(verts, uvs, tris,
                        BuildPath(e, exitDir, center, EdgeMid(e), EdgeMid(exitDir)), hw, 0f, 1f, y);
            }

            var mesh = new Mesh { name = "ConveyorFlow" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Centreline points from the inbound edge to the exit edge: straight if collinear, else a corner Bézier.</summary>
        private static Vector3[] BuildPath(int inEdge, int exitDir, Vector3 center, Vector3 inMid, Vector3 outMid)
        {
            if (inEdge == Opposite(exitDir) || inEdge == exitDir)
                return new[] { inMid, center, outMid };

            var pts = new Vector3[BendSegments + 1];
            for (int i = 0; i <= BendSegments; i++)
            {
                float t = (float)i / BendSegments;
                float u = 1f - t;
                pts[i] = u * u * inMid + 2f * u * t * center + t * t * outMid; // quadratic Bézier
            }
            return pts;
        }

        /// <summary>Emits a flat quad strip along the centreline, width 2*hw, UV.y lerped uvY0→uvY1 by arc length.</summary>
        private static void AppendRibbon(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
                                         IReadOnlyList<Vector3> pts, float hw, float uvY0, float uvY1, float y)
        {
            int n = pts.Count;
            if (n < 2) return;

            var cum = new float[n];
            for (int i = 1; i < n; i++) cum[i] = cum[i - 1] + Vector3.Distance(pts[i], pts[i - 1]);
            float total = cum[n - 1];
            if (total < 1e-5f) return;

            int baseIndex = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 tangent =
                    i == 0       ? pts[1] - pts[0] :
                    i == n - 1   ? pts[n - 1] - pts[n - 2] :
                                   pts[i + 1] - pts[i - 1];
                tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-8f) tangent = Vector3.forward;
                tangent.Normalize();

                Vector3 perp = Vector3.Cross(Vector3.up, tangent) * hw;
                float   vY   = Mathf.Lerp(uvY0, uvY1, cum[i] / total);
                var     p    = new Vector3(pts[i].x, y, pts[i].z);

                verts.Add(p - perp); uvs.Add(new Vector2(0f, vY));
                verts.Add(p + perp); uvs.Add(new Vector2(1f, vY));
            }

            for (int i = 0; i < n - 1; i++)
            {
                int a = baseIndex + i * 2;
                int b = a + 1, c = a + 2, d = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }
        }

        private static Vector3 DirVec(int dir) => dir switch
        {
            0 => new Vector3(0f, 0f, 1f),   // North
            1 => new Vector3(1f, 0f, 0f),   // East
            2 => new Vector3(0f, 0f, -1f),  // South
            3 => new Vector3(-1f, 0f, 0f),  // West
            _ => Vector3.zero
        };

        private static int Opposite(int dir) => (dir + 2) % 4;
    }
}
