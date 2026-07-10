using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the procedural torus ("donut") mesh used by <see cref="EntropySinkStructure"/> for
    /// Maxwell's Demon — a surface of revolution where a small circular tube (radius
    /// <see cref="TorusProfile.MinorRadius"/>) is swept around the local Y axis at distance
    /// <see cref="TorusProfile.MajorRadius"/>, so the ring lies <b>flat</b> in the XZ plane with its hole
    /// facing up. Sized so the outer diameter is ~1 local unit; <see cref="BuildingVisualizer"/> scales
    /// the host to fill the 3x3 footprint.
    ///
    /// Pure construction (no scene needed) so EditMode tests can assert vertex/triangle counts, the
    /// central hole, bounds, and unit-length normals. This is the house pattern for structure geometry:
    /// a 2D profile revolved around the local Y axis (mirrors <see cref="CollectorMeshBuilder"/>).
    /// </summary>
    public static class TorusMeshBuilder
    {
        /// <summary>Silhouette parameters for the revolved torus (all in local units, outer diameter ~1).</summary>
        public struct TorusProfile
        {
            public float MajorRadius;  // distance from the centre to the middle of the tube
            public float MinorRadius;  // tube (cross-section) radius — controls thickness/height

            public static TorusProfile Default => new TorusProfile
            {
                MajorRadius = 0.38f,
                MinorRadius = 0.10f
            };
        }

        /// <summary>Builds the torus with the default profile.</summary>
        public static Mesh Build(int tubularSegments, int radialSegments)
            => Build(tubularSegments, radialSegments, TorusProfile.Default);

        /// <summary>
        /// Builds the torus as a surface of revolution. <paramref name="tubularSegments"/> steps around
        /// the main ring (the big loop), <paramref name="radialSegments"/> steps around the tube
        /// cross-section. The seam column and row are duplicated for a clean UV wrap.
        /// UV.x runs 0->1 around the ring, UV.y runs 0->1 around the tube — these two coordinates drive
        /// the swirling shader patterns.
        /// </summary>
        public static Mesh Build(int tubularSegments, int radialSegments, TorusProfile profile)
        {
            int tub = Mathf.Max(3, tubularSegments);
            int rad = Mathf.Max(3, radialSegments);

            int tubCols = tub + 1; // duplicate the ring seam for a clean wrap
            int radRows = rad + 1; // duplicate the tube seam for a clean wrap
            int vertCount = tubCols * radRows;

            float R = profile.MajorRadius;
            float r = profile.MinorRadius;

            var verts = new Vector3[vertCount];
            var uvs   = new Vector2[vertCount];

            for (int i = 0; i < tubCols; i++)
            {
                float u   = (float)i / tub;          // 0..1 around the main ring
                float phi = 2f * Mathf.PI * u;
                float cphi = Mathf.Cos(phi);
                float sphi = Mathf.Sin(phi);

                for (int j = 0; j < radRows; j++)
                {
                    float v     = (float)j / rad;     // 0..1 around the tube
                    float theta = 2f * Mathf.PI * v;
                    float ct    = Mathf.Cos(theta);
                    float st    = Mathf.Sin(theta);

                    float ringR = R + r * ct;          // distance from the Y axis at this tube angle
                    int   idx   = i * radRows + j;
                    verts[idx]  = new Vector3(ringR * cphi, r * st, ringR * sphi);
                    uvs[idx]    = new Vector2(u, v);
                }
            }

            var tris = new List<int>(tub * rad * 6);
            for (int i = 0; i < tub; i++)
            for (int j = 0; j < rad; j++)
            {
                int a = i * radRows + j;
                int b = i * radRows + j + 1;
                int c = (i + 1) * radRows + j;
                int d = (i + 1) * radRows + j + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            var mesh = new Mesh { name = "EntropySinkTorus" };
            if (vertCount > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.uv       = uvs;
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
