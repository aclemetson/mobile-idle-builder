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
    }
}
