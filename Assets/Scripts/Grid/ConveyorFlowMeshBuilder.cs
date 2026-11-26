using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the flat ribbon mesh for a single conveyor cell, in cell-local space (centred on the
    /// origin, lying in the XZ plane). The companion <c>MobileIdleBuilder/ConveyorField</c> shader rolls
    /// wave packets through a field lattice along <c>uv.y</c>, so the flow direction reads straight from
    /// the moving packets — no arrows. <c>uv.x</c> runs 0→1 across the belt width (used to soften the
    /// edges); <c>uv.y</c> runs 0 (inbound edge) → 1 (outbound edge) along the flow. A merge also writes
    /// <c>uv1.x</c> = 1 on its secondary (turning) arms so the shader shows one lattice where arms overlap.
    ///
    /// Three shapes, all from one ribbon primitive:
    ///   - straight: a quad strip from the inbound edge through the centre to the exit edge;
    ///   - bend:     a quarter circle about the shared cell corner (radius half a cell), so the inner edge never pinches;
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
        /// <summary>Floor-layer width as a fraction of the cell. Belts no longer draw a bed (the field lattice
        /// sits straight on the tile); kept for callers that want a second, lower ribbon.</summary>
        public const float FloorWidthFrac = WaterWidthFrac;

        private const int   BendSegments = 12;         // arc samples for a 90° turn
        private const int   ForkSegments = 16;         // finer: a fork's streaks bend across a widening ribbon

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

            // uv1.x marks a merge's SECONDARY arms (1) vs the main arm (0). The arms overlap the main arm, so
            // the belt shader fades a secondary arm out wherever the main arm covers it (one grid, not two).
            // uv1.y is the arm's centreline length in cells (1 straight, ~0.79 for a quarter-circle turn) so
            // the shader can keep cross-line spacing even round corners.
            // uv2 (secondary arms only, when the main arm is straight) is the main arm's sideways axis divided
            // by the half-width: |dot(position.xz, uv2)| < 1 inside the main belt. Zero means "no straight main
            // arm", and the shader falls back to fading by distance along the arm.
            // uv3 is the flow direction (xz, unit) at each vertex, so the world-lattice shader pushes its lines
            // along the belt smoothly instead of estimating the direction from screen derivatives (blocky).
            var arms  = new List<Vector2>();
            var mains = new List<Vector2>();
            var flows = new List<Vector2>();

            if (edges.Count <= 1)
            {
                AppendRibbon(verts, uvs, tris, BuildPath(edges[0], exitDir, center, EdgeMid(edges[0]), EdgeMid(exitDir)), hw, 0f, 1f, y);
                AddStripFlow(verts, flows);
                FillArmFlags(arms, verts.Count, false, PathLengthCells(edges[0], exitDir));
            }
            else
            {
                // Each inbound is drawn as a COMPLETE edge→exit ribbon (uv.y 0→1): the straight-through
                // input is a full straight piece, every turning input a full bend piece. They all share
                // the exit half and overlay there; the shader hands each turning arm over to the main one.
                bool    straightMain = edges.Contains(Opposite(exitDir));
                int     main         = straightMain ? Opposite(exitDir) : edges[0];
                Vector3 side         = Vector3.Cross(Vector3.up, DirVec(exitDir)) / hw;
                var     mainAxis     = straightMain ? new Vector2(side.x, side.z) : Vector2.zero;
                foreach (var e in edges)
                {
                    AppendRibbon(verts, uvs, tris,
                        BuildPath(e, exitDir, center, EdgeMid(e), EdgeMid(exitDir)), hw, 0f, 1f, y);
                    AddStripFlow(verts, flows);
                    FillArmFlags(arms, verts.Count, e != main, PathLengthCells(e, exitDir));
                    var axis = e != main ? mainAxis : Vector2.zero;
                    while (mains.Count < verts.Count) mains.Add(axis);
                }
            }
            while (mains.Count < verts.Count) mains.Add(Vector2.zero);

            var mesh = new Mesh { name = "ConveyorFlow" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, arms);
            mesh.SetUVs(2, mains);
            mesh.SetUVs(3, flows);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// The reverse of a merge, for a Belt Splitter. Each exit takes its own slice of the inbound edge
        /// and widens to a full belt by its exit edge, so branches never overlap (translucent water would
        /// double up and its streaks would cross). Both ends line up exactly with the neighbouring belts:
        /// across the inbound edge uv.x runs 0 to 1 over all the slices together, and across each exit
        /// edge it runs 0 to 1 as on any belt. uv.y runs 0 (inbound) to 1 (exit).
        /// </summary>
        public static Mesh BuildFork(float cellSize, int inEdgeDir, IReadOnlyList<int> exitDirs,
                                     float y = FlowY, float widthFrac = WaterWidthFrac)
        {
            float half = cellSize * 0.5f;
            float hw   = cellSize * widthFrac * 0.5f;
            Vector3 lift    = new Vector3(0f, y, 0f);
            Vector3 forward = -DirVec(inEdgeDir);                      // travel direction
            Vector3 right   = Vector3.Cross(Vector3.up, forward);      // right of travel

            // Slices run left to right across the inbound edge, so exits must too or branches would cross.
            var exits = new List<int>(exitDirs);
            exits.Sort((p, q) => Vector3.Dot(DirVec(p), right).CompareTo(Vector3.Dot(DirVec(q), right)));

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();
            var flows = new List<Vector2>();

            for (int i = 0; i < exits.Count; i++)
            {
                float uv0 = (float)i / exits.Count;
                float uv1 = (float)(i + 1) / exits.Count;

                Vector3 exitDir   = DirVec(exits[i]);
                Vector3 exitRight = Vector3.Cross(Vector3.up, exitDir) * hw;
                Vector3 exitMid   = exitDir * half;

                int baseIndex = verts.Count;
                for (int s = 0; s <= ForkSegments; s++)
                {
                    float t = (float)s / ForkSegments;
                    verts.Add(lift + ForkEdge(Mathf.Lerp(-hw, hw, uv0), exitMid - exitRight, forward, right, half, t));
                    verts.Add(lift + ForkEdge(Mathf.Lerp(-hw, hw, uv1), exitMid + exitRight, forward, right, half, t));
                    uvs.Add(new Vector2(Mathf.Lerp(uv0, 0f, t), t));
                    uvs.Add(new Vector2(Mathf.Lerp(uv1, 1f, t), t));
                }
                for (int s = 0; s < ForkSegments; s++)
                {
                    int a = baseIndex + s * 2;
                    int b = a + 1, c = a + 2, d = a + 3;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
                AddStripFlow(verts, flows);
            }

            var mesh = new Mesh { name = "ConveyorFork" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            var forkArms = new List<Vector2>(verts.Count);
            FillArmFlags(forkArms, verts.Count, false, 1f);   // branches keep the straight cross-line count
            mesh.SetUVs(1, forkArms);
            mesh.SetUVs(3, flows);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // One side of a branch: leaves the inbound edge straight ahead, then curves onto the exit corner.
        private static Vector3 ForkEdge(float entryLateral, Vector3 exitCorner, Vector3 forward, Vector3 right, float half, float t)
        {
            Vector3 start   = right * entryLateral - forward * half;
            Vector3 control = right * entryLateral + forward * Vector3.Dot(exitCorner, forward);
            float   u       = 1f - t;
            return u * u * start + 2f * u * t * control + t * t * exitCorner;
        }

        /// <summary>Centreline length of an edge→exit path, in cells: 1 straight, a quarter circle of radius ½ for a turn.</summary>
        public static float PathLengthCells(int inEdgeDir, int exitDir) =>
            inEdgeDir == Opposite(exitDir) || inEdgeDir == exitDir ? 1f : Mathf.PI * 0.25f;

        /// <summary>
        /// Appends the flow direction for the vertices added since <paramref name="flows"/> last caught up:
        /// one ribbon strip of left/right pairs, in travel order. Each vertex follows its own side of the strip
        /// (central difference), so a fork's diverging edges and an arc both get their true direction.
        /// </summary>
        private static void AddStripFlow(List<Vector3> verts, List<Vector2> flows)
        {
            int start = flows.Count;
            int pairs = (verts.Count - start) / 2;
            for (int i = 0; i < pairs; i++)
            for (int side = 0; side < 2; side++)
            {
                int prev = start + Mathf.Max(i - 1, 0) * 2 + side;
                int next = start + Mathf.Min(i + 1, pairs - 1) * 2 + side;
                Vector3 d = verts[next] - verts[prev];
                var f = new Vector2(d.x, d.z);
                flows.Add(f.sqrMagnitude > 1e-12f ? f.normalized : Vector2.right);
            }
        }

        private static void FillArmFlags(List<Vector2> arms, int upTo, bool secondary, float lengthCells)
        {
            var flag = new Vector2(secondary ? 1f : 0f, lengthCells);
            while (arms.Count < upTo) arms.Add(flag);
        }

        /// <summary>
        /// Centreline points from the inbound edge to the exit edge: straight if collinear, else a quarter
        /// circle (see <see cref="CenterlinePoint"/>).
        /// </summary>
        private static Vector3[] BuildPath(int inEdge, int exitDir, Vector3 center, Vector3 inMid, Vector3 outMid)
        {
            if (inEdge == Opposite(exitDir) || inEdge == exitDir)
                return new[] { inMid, center, outMid };

            float cellSize = (inMid - center).magnitude * 2f;
            var pts = new Vector3[BendSegments + 1];
            for (int i = 0; i <= BendSegments; i++)
                pts[i] = center + CenterlinePoint(cellSize, inEdge, exitDir, (float)i / BendSegments);
            return pts;
        }

        /// <summary>
        /// A point on a cell's belt centreline, relative to the cell centre at y = 0. <paramref name="t"/> runs
        /// 0 (middle of the <paramref name="inEdgeDir"/> edge) to 1 (middle of the <paramref name="exitDir"/>
        /// edge), uniformly by distance. A turn is a quarter circle about the cell corner the two edges share,
        /// radius half a cell: the belt's inner edge keeps a real radius (half a cell minus the half-width), so
        /// the ribbon never pinches to a point and its lattice lines stay apart. Items follow the same curve.
        /// </summary>
        public static Vector3 CenterlinePoint(float cellSize, int inEdgeDir, int exitDir, float t)
        {
            float   r    = cellSize * 0.5f;
            Vector3 inV  = DirVec(inEdgeDir);
            Vector3 outV = DirVec(exitDir);
            if (inEdgeDir == Opposite(exitDir) || inEdgeDir == exitDir)
                return Vector3.Lerp(inV * r, outV * r, t);

            Vector3 corner = (inV + outV) * r;
            float   a      = t * Mathf.PI * 0.5f;
            // From the corner, -outV points at the inbound edge midpoint and -inV at the exit edge midpoint.
            return corner + (-outV * Mathf.Cos(a) - inV * Mathf.Sin(a)) * r;
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

        // ----------------------------------------------------------------
        // Overpasses (BeltBridge)
        // ----------------------------------------------------------------

        /// <summary>Unit vector of a direction (N = +z, E = +x).</summary>
        public static Vector3 Dir(int dir) => DirVec(dir);

        /// <summary>
        /// How far a point on a ramp tile (relative to the tile centre) is lifted. A ramp up climbs over the half of the
        /// tile toward the span; a ramp down comes down over the half the span arrives on. Smooth at both ends, and
        /// <see cref="OverpassRules.DeckHeight"/> at the span edge, so ramps meet the deck.
        /// </summary>
        public static float RampLift(Vector3 local, int spanDir, float cellSize, bool rampUp)
        {
            float a = Vector3.Dot(new Vector3(local.x, 0f, local.z), DirVec(spanDir)) / (cellSize * 0.5f);
            float t = Mathf.Clamp01(rampUp ? a : -a);
            return OverpassRules.DeckHeight * t * t * (3f - 2f * t);
        }

        /// <summary>Lifts a ramp tile's ribbon in place (<see cref="RampLift"/>).</summary>
        public static void LiftRamp(Mesh mesh, int spanDir, float cellSize, bool rampUp)
        {
            var v = mesh.vertices;
            for (int i = 0; i < v.Length; i++) v[i].y += RampLift(v[i], spanDir, cellSize, rampUp);
            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        /// <summary>An axis-aligned box (the overpass's side girders), centred on <paramref name="center"/>.</summary>
        public static Mesh BuildBox(Vector3 center, Vector3 size)
        {
            var h = size * 0.5f;
            var c = new[]
            {
                new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(-h.x, h.y, -h.z),
                new Vector3(-h.x, -h.y,  h.z), new Vector3(h.x, -h.y,  h.z), new Vector3(h.x, h.y,  h.z), new Vector3(-h.x, h.y,  h.z),
            };
            int[][] faces =
            {
                new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
                new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 },
            };
            var verts = new List<Vector3>();
            var tris  = new List<int>();
            foreach (var f in faces)
            {
                int b = verts.Count;
                foreach (int k in f) verts.Add(center + c[k]);
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
            var mesh = new Mesh { name = "OverpassGirder" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
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
