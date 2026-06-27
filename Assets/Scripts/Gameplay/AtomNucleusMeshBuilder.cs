using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the procedural "nucleus" mesh used by <see cref="AtomGeneratorStructure"/> (the Atom
    /// Generator building) — a round, near-spherical bulb that eases to a small apex, sitting planted on
    /// the tile. Unlike <see cref="CollectorMeshBuilder"/>'s spindle this is <b>round on every side</b>
    /// (no flat front facade): the Atom Generator's input/output holes are placed as separate fixtures at
    /// its port edges by <see cref="BuildingVisualizer"/>, so the body itself stays a clean nucleus that
    /// the electron orbit rings circle. House pattern: a 2D silhouette revolved around the local Y axis.
    ///
    /// Pure construction (no scene needed) so EditMode tests can assert vertex counts, the single shared
    /// apex on the axis, the round (un-flattened) cross-section, bounds, and unit-length normals.
    /// </summary>
    public static class AtomNucleusMeshBuilder
    {
        /// <summary>Silhouette parameters for the revolved nucleus (all in local units, ~1 = one grid cell).</summary>
        public struct NucleusProfile
        {
            public float Height;        // base (y=0) to apex
            public float EquatorRadius; // peak radius at the nucleus equator (u = 0.5)
            public float BaseRadius;    // small planted footprint where it meets the tile

            public static NucleusProfile Default => new NucleusProfile
            {
                Height        = 0.80f,
                EquatorRadius = 0.40f,
                BaseRadius    = 0.12f
            };
        }

        /// <summary>
        /// Profile radius at normalized height <paramref name="u"/> (0 = base on the tile, 1 = apex).
        /// An ellipse silhouette (perfect round dome) peaking at the equator, with a small planted floor
        /// near the base so the nucleus sits on the tile rather than balancing on a point.
        /// </summary>
        public static float Radius(float u, in NucleusProfile p)
        {
            u = Mathf.Clamp01(u);
            float e       = 2f * u - 1f;                              // -1 at base, 0 at equator, +1 at apex
            float ellipse = p.EquatorRadius * Mathf.Sqrt(Mathf.Max(0f, 1f - e * e));
            float baseBlend = 1f - SmoothStepEdge(0f, 0.12f, u);      // fades out by u = 0.12
            return Mathf.Max(ellipse, p.BaseRadius * baseBlend);
        }

        /// <summary>GLSL-style smoothstep: 0 below <paramref name="edge0"/>, 1 above <paramref name="edge1"/>, smooth between.</summary>
        private static float SmoothStepEdge(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Builds the nucleus with the default profile.</summary>
        public static Mesh Build(int radialSegments, int heightSegments)
            => Build(radialSegments, heightSegments, NucleusProfile.Default);

        /// <summary>
        /// Builds the nucleus as a surface of revolution. <paramref name="radialSegments"/> rings the
        /// body, <paramref name="heightSegments"/> stacks it vertically; the very top collapses to one
        /// shared apex vertex. UV.y runs 0 at the base to 1 at the apex so the shader can glow the core
        /// and tip.
        /// </summary>
        public static Mesh Build(int radialSegments, int heightSegments, NucleusProfile profile)
        {
            int s = Mathf.Max(3, radialSegments);
            int h = Mathf.Max(2, heightSegments);

            int cols      = s + 1;            // duplicate the seam column for a clean wrap
            int ringCount = h;                // rings i = 0..h-1; apex closes the top
            int vertCount = ringCount * cols + 1;

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
                    verts[idx] = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
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

            var mesh = new Mesh { name = "AtomNucleus" };
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
