using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// General-purpose builders for structure parts: a surface of revolution from any (radius, height)
    /// profile, and a flat pie slice. Use these for shapes the dedicated builders can't describe (a plinth,
    /// a stepped column, half a floor). <c>uv.y</c> runs 0 at the lowest profile point to 1 at the highest,
    /// which drives the <c>MobileIdleBuilder/CollectorStructure</c> shader's shading and tip glow.
    /// Pure construction, so EditMode tests can assert the shape without a scene.
    /// </summary>
    public static class RevolveMeshBuilder
    {
        /// <summary>
        /// Revolves <paramref name="profile"/> (x = radius, y = height, bottom to top) around local Y.
        /// The seam column is duplicated for a clean UV wrap.
        /// </summary>
        public static Mesh Build(IReadOnlyList<Vector2> profile, int segments)
        {
            int segs  = Mathf.Max(3, segments);
            int rows  = profile.Count;
            int cols  = segs + 1;
            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var p in profile)
            {
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }
            float span = Mathf.Max(1e-5f, maxY - minY);

            var verts = new Vector3[rows * cols];
            var uvs   = new Vector2[rows * cols];
            for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                float a = 2f * Mathf.PI * j / segs;
                verts[i * cols + j] = new Vector3(profile[i].x * Mathf.Cos(a), profile[i].y, profile[i].x * Mathf.Sin(a));
                uvs[i * cols + j]   = new Vector2((float)j / segs, (profile[i].y - minY) / span);
            }

            var tris = new List<int>((rows - 1) * segs * 6);
            for (int i = 0; i < rows - 1; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = i * cols + j;
                int b = a + 1;
                int c = a + cols;
                int d = c + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            var mesh = new Mesh { name = "Revolve" };
            mesh.vertices = verts;
            mesh.uv       = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// A flat pie slice at height <paramref name="y"/>, from <paramref name="startDegrees"/> to
        /// <paramref name="endDegrees"/> (0 = +X, 90 = +Z).
        /// </summary>
        public static Mesh BuildSector(float radius, float y, float startDegrees, float endDegrees, int segments)
        {
            int segs  = Mathf.Max(1, segments);
            var verts = new Vector3[segs + 2];
            var uvs   = new Vector2[segs + 2];
            verts[0] = new Vector3(0f, y, 0f);
            uvs[0]   = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= segs; i++)
            {
                float a = Mathf.Deg2Rad * Mathf.Lerp(startDegrees, endDegrees, (float)i / segs);
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                verts[i + 1] = new Vector3(radius * c, y, radius * s);
                uvs[i + 1]   = new Vector2(0.5f + 0.5f * c, 0.5f + 0.5f * s);
            }

            var tris = new int[segs * 3];
            for (int i = 0; i < segs; i++)
            {
                tris[i * 3]     = 0;
                tris[i * 3 + 1] = i + 2;
                tris[i * 3 + 2] = i + 1;
            }

            var mesh = new Mesh { name = "Sector" };
            mesh.vertices  = verts;
            mesh.uv        = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
