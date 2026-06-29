using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the collector's emission "door" on the flat front facade: a solid filled shape — a
    /// rectangle with an arched (semicircular) top — that reads as a dark doorway/hole the harvest
    /// particles stream out of. Lies on the local XY plane facing +Z with its base at y=0, built
    /// double-sided so back-face culling can never hide it. Pure construction so it is EditMode-testable.
    /// </summary>
    public static class ApertureMeshBuilder
    {
        /// <summary>
        /// A filled "doorway": a <paramref name="width"/>-wide rectangle topped by a semicircle of
        /// radius width/2, spanning y in [0, <paramref name="height"/>], centred on x=0.
        /// </summary>
        public static Mesh BuildArchDoor(float width, float height, int archSegments)
        {
            archSegments = Mathf.Max(2, archSegments);
            float halfW   = width * 0.5f;
            float archR   = halfW;
            float rectTop = Mathf.Max(0f, height - archR); // springline where the arch begins

            // CCW boundary: bottom-left -> bottom-right -> up the right wall -> over the arch -> left wall.
            var boundary = new List<Vector2>
            {
                new Vector2(-halfW, 0f),
                new Vector2( halfW, 0f),
                new Vector2( halfW, rectTop),
            };
            for (int i = 1; i < archSegments; i++)
            {
                float a = Mathf.PI * i / archSegments; // 0 = right springline, PI = left
                boundary.Add(new Vector2(Mathf.Cos(a) * archR, rectTop + Mathf.Sin(a) * archR));
            }
            boundary.Add(new Vector2(-halfW, rectTop));

            // The doorway is convex, so a triangle fan from an interior point on the centreline fills it.
            var verts = new List<Vector3>(boundary.Count + 1) { new Vector3(0f, height * 0.4f, 0f) };
            foreach (var p in boundary) verts.Add(new Vector3(p.x, p.y, 0f));

            int n = boundary.Count;
            var tris = new List<int>(n * 6);
            for (int i = 0; i < n; i++)
            {
                int cur  = 1 + i;
                int next = 1 + (i + 1) % n;
                tris.Add(0); tris.Add(cur); tris.Add(next);  // front
                tris.Add(0); tris.Add(next); tris.Add(cur);  // back (double-sided)
            }

            var mesh = new Mesh { name = "ApertureDoor" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A recessed circular "intake mouth": an outer rim ring of <paramref name="radius"/> at z=0
        /// funnelling back to a smaller throat at z = -<paramref name="depth"/>, capped by a disc — so it
        /// reads as a concave hole the conveyor feeds items INTO (the visual opposite of the lit, convex
        /// emission door). Centred on the origin in the XY plane, opening toward +Z, built double-sided so
        /// back-face culling can never hide it. Pure construction so it is EditMode-testable.
        /// </summary>
        public static Mesh BuildIntakeMouth(float radius, float depth, int segments)
        {
            segments = Mathf.Max(6, segments);
            float rOuter = Mathf.Max(0.0001f, radius);
            float rInner = rOuter * 0.35f;

            // Vertices: throat-cap centre (0), inner throat ring, outer rim ring.
            var verts = new List<Vector3>(2 * segments + 1) { new Vector3(0f, 0f, -depth) };
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(rInner * c, rInner * s, -depth)); // 1 .. segments     (throat)
            }
            for (int i = 0; i < segments; i++)
            {
                float a = 2f * Mathf.PI * i / segments;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts.Add(new Vector3(rOuter * c, rOuter * s, 0f));     // 1+segments .. 2*segments (rim)
            }

            int inner0 = 1;
            int outer0 = 1 + segments;
            var tris = new List<int>(segments * 12);

            // Funnel wall between the outer rim and the inner throat (double-sided).
            for (int i = 0; i < segments; i++)
            {
                int oa = outer0 + i;
                int ob = outer0 + (i + 1) % segments;
                int ia = inner0 + i;
                int ib = inner0 + (i + 1) % segments;
                tris.Add(oa); tris.Add(ia); tris.Add(ob);
                tris.Add(ob); tris.Add(ia); tris.Add(ib);
                tris.Add(ob); tris.Add(ia); tris.Add(oa); // back
                tris.Add(ib); tris.Add(ia); tris.Add(ob); // back
            }

            // Throat cap fan (double-sided) — the dark bottom of the hole.
            for (int i = 0; i < segments; i++)
            {
                int ia = inner0 + i;
                int ib = inner0 + (i + 1) % segments;
                tris.Add(0); tris.Add(ia); tris.Add(ib);
                tris.Add(0); tris.Add(ib); tris.Add(ia); // back
            }

            var mesh = new Mesh { name = "ApertureIntake" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
