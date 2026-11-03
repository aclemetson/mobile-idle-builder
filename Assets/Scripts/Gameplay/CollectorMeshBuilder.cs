using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the procedural "spindle" mesh used by <see cref="CollectorStructure"/> — a surface of
    /// revolution whose silhouette swells into a round bulb and then eases to a single sharp apex
    /// (a singularity point). The body is round on every side except the <b>front</b> (local +Z),
    /// which is sliced to a flat facade so the building clearly reads as having a "front"; an emission
    /// aperture (see <see cref="ApertureMeshBuilder"/>) is placed on that flat face. This is the house
    /// pattern for structure geometry: a 2D profile curve revolved around the local Y axis.
    ///
    /// Pure construction (no scene needed) so EditMode tests can assert vertex counts, the single
    /// shared apex, the flattened front, bounds, and unit-length normals. Mirrors
    /// <see cref="FieldWireMeshBuilder"/>.
    /// </summary>
    public static class CollectorMeshBuilder
    {
        /// <summary>Silhouette parameters for the revolved spindle (all in local units, ~1 = one grid cell).</summary>
        public struct SpindleProfile
        {
            public float Height;          // base (y=0) to apex
            public float BulbRadius;      // peak radius of the round body
            public float BaseRadius;      // small planted footprint where it meets the field
            public float ApexSharpness;   // >1 = finer, sharper singularity point
            public float FrontFlattenFrac;// fraction of BulbRadius at which the front (+Z) is sliced flat

            public static SpindleProfile Default => new SpindleProfile
            {
                Height           = 1.25f,
                BulbRadius       = 0.42f,
                BaseRadius       = 0.10f,
                ApexSharpness    = 1.3f,
                FrontFlattenFrac = 0.5f
            };
        }

        /// <summary>Local +Z plane at which the front facade is sliced flat.</summary>
        public static float FrontFlatZ(in SpindleProfile p) => p.BulbRadius * p.FrontFlattenFrac;

        /// <summary>
        /// Profile radius at normalized height <paramref name="u"/> (0 = base on the tile, 1 = apex).
        /// The body bulges low (biased sine) for a round form, then tapers to 0 at the apex; a small
        /// floor near the base keeps it planted rather than balancing on a point.
        /// </summary>
        public static float Radius(float u, in SpindleProfile p)
        {
            u = Mathf.Clamp01(u);
            float bulb      = Mathf.Sin(Mathf.PI * Mathf.Pow(u, 0.7f));  // 0 at both ends, swells low
            float taper     = Mathf.Pow(1f - u, p.ApexSharpness);        // sharpens the apex
            float r         = p.BulbRadius * bulb * taper;
            // Small planted footprint that fades out by u=0.12 — a GLSL-style smoothstep over the
            // base, NOT Mathf.SmoothStep (which interpolates between its first two args, not edges).
            float baseBlend = 1f - SmoothStepEdge(0f, 0.12f, u);
            return Mathf.Max(r, p.BaseRadius * baseBlend);
        }

        /// <summary>GLSL-style smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth between.</summary>
        private static float SmoothStepEdge(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Builds the spindle with the default profile.</summary>
        public static Mesh Build(int radialSegments, int heightSegments)
            => Build(radialSegments, heightSegments, SpindleProfile.Default);

        /// <summary>
        /// Builds the spindle as a surface of revolution. <paramref name="radialSegments"/> rings the
        /// body, <paramref name="heightSegments"/> stacks it vertically; the very top collapses to one
        /// shared apex vertex (the sharp point). The front (+Z) is sliced flat at <see cref="FrontFlatZ"/>.
        /// UV.y runs 0 at the base to 1 at the apex so the shader can glow the tip.
        /// </summary>
        public static Mesh Build(int radialSegments, int heightSegments, SpindleProfile profile)
        {
            int s = Mathf.Max(3, radialSegments);
            int h = Mathf.Max(2, heightSegments);

            int cols      = s + 1;            // duplicate the seam column for a clean wrap
            int ringCount = h;                // rings i = 0..h-1, u = 0 .. (h-1)/h; apex closes the top
            int vertCount = ringCount * cols + 1;

            float flatZ = FrontFlatZ(profile);

            var verts = new Vector3[vertCount];
            var uvs   = new Vector2[vertCount];

            for (int i = 0; i < ringCount; i++)
            {
                float u = (float)i / h;
                float y = u * profile.Height;
                float r = Radius(u, profile);
                for (int j = 0; j < cols; j++)
                {
                    float a   = (2f * Mathf.PI * j) / s;
                    int   idx = i * cols + j;
                    float x   = r * Mathf.Cos(a);
                    float z   = r * Mathf.Sin(a);
                    if (z > flatZ) z = flatZ; // slice the front (+Z) into a flat facade
                    verts[idx] = new Vector3(x, y, z);
                    uvs[idx]   = new Vector2((float)j / s, u);
                }
            }

            int apex = vertCount - 1;
            verts[apex] = new Vector3(0f, profile.Height, 0f);
            uvs[apex]   = new Vector2(0.5f, 1f);

            var tris = new List<int>(ringCount * s * 6);

            // Body quads between adjacent rings (two triangles each).
            for (int i = 0; i < ringCount - 1; i++)
            for (int j = 0; j < s; j++)
            {
                int a = i * cols + j;
                int b = i * cols + j + 1;
                int c = (i + 1) * cols + j;
                int d = (i + 1) * cols + j + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            // Fan the top ring up to the single apex vertex.
            int top = (ringCount - 1) * cols;
            for (int j = 0; j < s; j++)
            {
                tris.Add(top + j);
                tris.Add(apex);
                tris.Add(top + j + 1);
            }

            var mesh = new Mesh { name = "CollectorSpindle" };
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
