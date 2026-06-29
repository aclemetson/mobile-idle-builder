using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds a rounded rectangular prism — a superellipsoid dome — used as a building body that needs to
    /// fill a rectangular footprint while still reading as a soft, flowing form (e.g. the Strong Force
    /// Combiner, whose body connects its two inlet faces to its single outlet face). The widest
    /// cross-section is a "squircle" (rounded rectangle) at the base; it eases up to a domed top. With a
    /// low <see cref="RoundedBoxProfile.Roundness"/> the side walls stay near-vertical with rounded
    /// vertical edges, so the building's input/output door fixtures sit flush on the flat face centres.
    ///
    /// Pure construction (no scene needed) so EditMode tests can assert vertex counts, the single apex,
    /// the footprint half-extents, bounds (base at y=0), and unit-length normals. House pattern sibling of
    /// <see cref="CollectorMeshBuilder"/> / <see cref="AtomNucleusMeshBuilder"/>.
    /// </summary>
    public static class RoundedBoxMeshBuilder
    {
        /// <summary>Dimensions of the rounded box (local units; ~1 = one grid cell).</summary>
        public struct RoundedBoxProfile
        {
            public float HalfWidth;  // x half-extent at the base
            public float HalfDepth;  // z half-extent at the base
            public float Height;     // base (y=0) to domed top
            public float Roundness;  // 0 -> hard box, 1 -> ellipsoid; ~0.35 = rounded prism

            public static RoundedBoxProfile Default => new RoundedBoxProfile
            {
                HalfWidth = 0.9f,
                HalfDepth = 0.45f,
                Height    = 0.8f,
                Roundness = 0.35f
            };
        }

        // Signed power: sign(c) * |c|^e — the superellipse/superellipsoid primitive.
        private static float Fexp(float c, float e) => Mathf.Sign(c) * Mathf.Pow(Mathf.Abs(c), e);

        /// <summary>Builds the rounded box with the default profile.</summary>
        public static Mesh Build(int longitudeSegments, int latitudeSegments)
            => Build(longitudeSegments, latitudeSegments, RoundedBoxProfile.Default);

        /// <summary>
        /// Builds the rounded box as a superellipsoid dome. <paramref name="longitudeSegments"/> rings the
        /// body (around), <paramref name="latitudeSegments"/> stacks it from the base squircle up to the
        /// domed top, which closes to a single shared apex vertex. UV.y runs 0 (base) -> 1 (apex) so the
        /// shared structure shader can glow the crown.
        /// </summary>
        public static Mesh Build(int longitudeSegments, int latitudeSegments, RoundedBoxProfile profile)
        {
            int s = Mathf.Max(8, longitudeSegments); // multiple of 4 keeps verts on the face centres
            int t = Mathf.Max(2, latitudeSegments);
            float e = Mathf.Clamp(profile.Roundness, 0.01f, 1f);

            int cols      = s + 1;          // duplicate the longitude seam for a clean wrap
            int ringCount = t;              // latitude rings j = 0..t-1; apex closes the top
            int vertCount = ringCount * cols + 1;

            var verts = new Vector3[vertCount];
            var uvs   = new Vector2[vertCount];

            for (int j = 0; j < ringCount; j++)
            {
                float lat  = (Mathf.PI * 0.5f) * j / t; // 0 at base -> ~pi/2 at top
                float cLat = Mathf.Cos(lat);
                float sLat = Mathf.Sin(lat);
                float ringScale = Fexp(cLat, e);        // 1 at base -> 0 at top
                float y = profile.Height * Fexp(sLat, e);

                for (int i = 0; i < cols; i++)
                {
                    float lon  = 2f * Mathf.PI * i / s;
                    float x = profile.HalfWidth * ringScale * Fexp(Mathf.Cos(lon), e);
                    float z = profile.HalfDepth * ringScale * Fexp(Mathf.Sin(lon), e);
                    int   idx = j * cols + i;
                    verts[idx] = new Vector3(x, y, z);
                    uvs[idx]   = new Vector2((float)i / s, (float)j / t);
                }
            }

            int apex = vertCount - 1;
            verts[apex] = new Vector3(0f, profile.Height, 0f);
            uvs[apex]   = new Vector2(0.5f, 1f);

            var tris = new List<int>(ringCount * s * 6);

            for (int j = 0; j < ringCount - 1; j++)
            for (int i = 0; i < s; i++)
            {
                int a = j * cols + i;
                int b = j * cols + i + 1;
                int c = (j + 1) * cols + i;
                int d = (j + 1) * cols + i + 1;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(d);
            }

            int top = (ringCount - 1) * cols;
            for (int i = 0; i < s; i++)
            {
                tris.Add(top + i);
                tris.Add(apex);
                tris.Add(top + i + 1);
            }

            var mesh = new Mesh { name = "RoundedBox" };
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
