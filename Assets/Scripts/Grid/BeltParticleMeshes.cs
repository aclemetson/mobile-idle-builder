using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Meshes for the animated belt particles (<see cref="BeltItemVisuals"/>). Every vertex carries a
    /// colour and a jitter channel (TEXCOORD1 = seedA, seedB, blend, unused) read by the
    /// <c>MobileIdleBuilder/BeltParticle</c> shader: a vertex shakes with the jitter of seedA, or with a
    /// blend toward seedB, so a gluon tube stays attached to the two quarks it joins while they shake.
    /// Pure geometry, so it is unit-testable without a scene.
    /// </summary>
    public static class BeltParticleMeshes
    {
        /// <summary>Accumulates vertices for one combined mesh.</summary>
        public sealed class Builder
        {
            public readonly List<Vector3> Positions = new();
            public readonly List<Vector3> Normals   = new();
            public readonly List<Color>   Colors    = new();
            public readonly List<Vector4> Jitter    = new();
            public readonly List<int>     Triangles = new();

            /// <summary>Adds a UV sphere. Every vertex jitters with <paramref name="seed"/>.</summary>
            public void Sphere(Vector3 centre, float radius, Color color, float seed,
                               int segments = 12, int rings = 8)
            {
                int start = Positions.Count;
                for (int r = 0; r <= rings; r++)
                {
                    float v     = (float)r / rings;
                    float theta = v * Mathf.PI;
                    float y     = Mathf.Cos(theta);
                    float ring  = Mathf.Sin(theta);
                    for (int s = 0; s <= segments; s++)
                    {
                        float phi = (float)s / segments * Mathf.PI * 2f;
                        var n = new Vector3(ring * Mathf.Cos(phi), y, ring * Mathf.Sin(phi));
                        Positions.Add(centre + n * radius);
                        Normals.Add(n);
                        Colors.Add(color);
                        Jitter.Add(new Vector4(seed, seed, 0f, 0f));
                    }
                }
                int stride = segments + 1;
                for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    int a = start + r * stride + s;
                    int b = a + stride;
                    Triangles.Add(a); Triangles.Add(a + 1); Triangles.Add(b);
                    Triangles.Add(b); Triangles.Add(a + 1); Triangles.Add(b + 1);
                }
            }

            /// <summary>
            /// Adds an open tube from <paramref name="from"/> to <paramref name="to"/>. Its jitter blends from
            /// <paramref name="fromBlend"/> to <paramref name="toBlend"/> between seedA and seedB.
            /// </summary>
            public void Tube(Vector3 from, Vector3 to, float radius, Color color,
                             float seedA, float seedB, float fromBlend, float toBlend, int sides = 5)
            {
                Vector3 axis = to - from;
                if (axis.sqrMagnitude < 1e-10f) return;
                Vector3 dir  = axis.normalized;
                Vector3 side = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 up   = Vector3.Cross(side, dir);
                int start = Positions.Count;
                for (int end = 0; end < 2; end++)
                {
                    Vector3 c = end == 0 ? from : to;
                    float   w = end == 0 ? fromBlend : toBlend;
                    for (int i = 0; i < sides; i++)
                    {
                        float a = (float)i / sides * Mathf.PI * 2f;
                        Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                        Positions.Add(c + n * radius);
                        Normals.Add(n);
                        Colors.Add(color);
                        Jitter.Add(new Vector4(seedA, seedB, w, 0f));
                    }
                }
                for (int i = 0; i < sides; i++)
                {
                    int a0 = start + i, a1 = start + (i + 1) % sides;
                    int b0 = a0 + sides, b1 = a1 + sides;
                    Triangles.Add(a0); Triangles.Add(b0); Triangles.Add(a1);
                    Triangles.Add(a1); Triangles.Add(b0); Triangles.Add(b1);
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(Positions);
                mesh.SetNormals(Normals);
                mesh.SetColors(Colors);
                mesh.SetUVs(1, Jitter);
                mesh.SetTriangles(Triangles, 0);
                mesh.RecalculateBounds();
                // The shader shakes vertices a little; pad bounds so the item is never culled mid-shake.
                var b = mesh.bounds;
                b.Expand(0.1f);
                mesh.bounds = b;
                return mesh;
            }
        }

        /// <summary>A unit-diameter white sphere for a loose particle (quark bead, electron core, nucleon shell).</summary>
        public static Mesh UnitSphere(int segments = 16, int rings = 10)
        {
            var b = new Builder();
            b.Sphere(Vector3.zero, 0.5f, Color.white, 0f, segments, rings);
            return b.ToMesh("BeltParticleSphere");
        }

        /// <summary>
        /// A nucleon's interior: three quark beads in a ring joined by gluon tubes that bow toward the
        /// centre, combined into one mesh. Each quark gets its own jitter seed (1, 2, 3).
        /// </summary>
        public static Mesh NucleonCore(Color q0, Color q1, Color q2, Color gluon,
                                       float ringRadius = 0.075f, float quarkRadius = 0.045f, float gluonRadius = 0.007f)
        {
            var b = new Builder();
            var colors = new[] { q0, q1, q2 };
            var pts = new Vector3[3];
            for (int i = 0; i < 3; i++)
            {
                float a = i * Mathf.PI * 2f / 3f + Mathf.PI / 2f;
                pts[i] = new Vector3(Mathf.Cos(a) * ringRadius, (i == 0 ? 0.012f : -0.008f), Mathf.Sin(a) * ringRadius);
                b.Sphere(pts[i], quarkRadius, colors[i], i + 1);
            }
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                Vector3 mid = (pts[i] + pts[j]) * 0.5f;
                mid -= mid * 0.45f;   // bow the string toward the centre
                b.Tube(pts[i], mid, gluonRadius, gluon, i + 1, j + 1, 0f, 0.5f);
                b.Tube(mid, pts[j], gluonRadius, gluon, i + 1, j + 1, 0.5f, 1f);
            }
            return b.ToMesh("NucleonCore");
        }

        /// <summary>
        /// An electron's cloud: several overlapping low-poly puffs in one mesh, each with its own seed so
        /// the cloud shader wobbles them separately. Deterministic for a given seed.
        /// </summary>
        public static Mesh ElectronCloud(int puffs = 7, float spread = 0.06f, float minRadius = 0.06f,
                                         float maxRadius = 0.11f, int seed = 31)
        {
            var b = new Builder();
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            for (int i = 0; i < puffs; i++)
            {
                var offset = new Vector3(R() * 2f - 1f, (R() * 2f - 1f) * 0.6f, R() * 2f - 1f) * spread;
                b.Sphere(offset, Mathf.Lerp(minRadius, maxRadius, R()), Color.white, i + 1, 10, 6);
            }
            return b.ToMesh("ElectronCloud");
        }
    }
}
