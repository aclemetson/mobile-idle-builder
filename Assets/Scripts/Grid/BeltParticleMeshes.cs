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

            /// <summary>
            /// Adds a torus (a thin ring) centred on the origin in the plane perpendicular to
            /// <paramref name="normal"/>. Every vertex jitters with <paramref name="seed"/>.
            /// </summary>
            public void Ring(Vector3 normal, float radius, float thickness, Color color, float seed,
                             int segments = 40, int sides = 5)
            {
                Vector3 n = normal.normalized;
                Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
                Vector3 v = Vector3.Cross(n, u);
                int start = Positions.Count;
                for (int s = 0; s <= segments; s++)
                {
                    float a = (float)s / segments * Mathf.PI * 2f;
                    Vector3 radial = u * Mathf.Cos(a) + v * Mathf.Sin(a);
                    Vector3 centre = radial * radius;
                    for (int i = 0; i <= sides; i++)
                    {
                        float b = (float)i / sides * Mathf.PI * 2f;
                        Vector3 off = radial * Mathf.Cos(b) + n * Mathf.Sin(b);
                        Positions.Add(centre + off * thickness);
                        Normals.Add(off);
                        Colors.Add(color);
                        Jitter.Add(new Vector4(seed, seed, 0f, 0f));
                    }
                }
                int stride = sides + 1;
                for (int s = 0; s < segments; s++)
                for (int i = 0; i < sides; i++)
                {
                    int a0 = start + s * stride + i, a1 = a0 + 1;
                    int b0 = a0 + stride,           b1 = b0 + 1;
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

        // ------------------------------------------------------------------ atoms (AtomStructure)

        /// <summary>
        /// <paramref name="count"/> points roughly filling a ball of <paramref name="radius"/> (golden-spiral
        /// shells, lightly jittered). Deterministic for a given seed.
        /// </summary>
        public static Vector3[] PackedPoints(int count, float radius, int seed)
        {
            var pts = new Vector3[Mathf.Max(0, count)];
            if (count == 1) return pts;
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble() * 2f - 1f;
            for (int i = 0; i < count; i++)
            {
                float f   = (i + 0.5f) / count;
                float r   = radius * Mathf.Pow(f, 1f / 3f);
                float phi = Mathf.Acos(1f - 2f * ((i * 0.618034f) % 1f));
                float th  = i * 2.399963f;
                pts[i] = new Vector3(r * Mathf.Sin(phi) * Mathf.Cos(th), r * Mathf.Cos(phi), r * Mathf.Sin(phi) * Mathf.Sin(th))
                       + new Vector3(R(), R(), R()) * radius * 0.08f;
            }
            return pts;
        }

        /// <summary>
        /// A light nucleus: <paramref name="protons"/> + <paramref name="neutrons"/> beads packed into a ball,
        /// protons and neutrons mixed. Each bead shakes on its own seed. Hydrogen is a single proton bead.
        /// </summary>
        public static Mesh NucleusCluster(int protons, int neutrons, Color protonColor, Color neutronColor,
                                          float beadRadius = AtomStructure.BeadRadius)
        {
            int count = Mathf.Max(1, protons + neutrons);
            var flavours = new bool[count];   // true = proton
            for (int i = 0; i < protons && i < count; i++) flavours[i] = true;
            var rnd = new System.Random(protons * 7 + neutrons);
            for (int i = count - 1; i > 0; i--) { int j = rnd.Next(i + 1); (flavours[i], flavours[j]) = (flavours[j], flavours[i]); }

            var b = new Builder();
            var pts = PackedPoints(count, beadRadius * 0.84f * Mathf.Pow(count, 1f / 3f), protons * 7 + neutrons);
            for (int i = 0; i < count; i++)
                b.Sphere(pts[i], beadRadius, flavours[i] ? protonColor : neutronColor, i + 1, 10, 6);
            return b.ToMesh($"Nucleus_{protons}p{neutrons}n");
        }

        /// <summary>A heavy nucleus: a beaded ball of <paramref name="beads"/> spheres in one colour.</summary>
        public static Mesh BeadedBall(Color color, float radius, int seed, int beads = 26, float beadRadius = 0.017f)
        {
            var b = new Builder();
            foreach (var p in PackedPoints(beads, Mathf.Max(0f, radius - beadRadius), seed))
                b.Sphere(p, beadRadius, color, seed % 7 + 1, 8, 5);
            return b.ToMesh($"HeavyNucleus_{seed}");
        }

        /// <summary>
        /// Every shell ring of an atom in one mesh. Rings are faint; the outer (valence) ring is thicker and
        /// brighter (alpha in the vertex colour). Normals come from <see cref="AtomStructure.ShellNormal"/>.
        /// </summary>
        public static Mesh AtomRings(int z, float nucleusRadius, Color color, float innerAlpha = 0.35f, float outerAlpha = 0.7f)
        {
            var shells = AtomStructure.ShellOccupancy(z);
            var b = new Builder();
            for (int k = 0; k < shells.Length; k++)
            {
                bool outer = k == shells.Length - 1;
                var c = color; c.a = outer ? outerAlpha : innerAlpha;
                b.Ring(AtomStructure.ShellNormal(z, k), AtomStructure.ShellRadius(k, shells.Length, nucleusRadius),
                       outer ? 0.0045f : 0.003f, c, 0f, 40, 4);
            }
            return b.ToMesh($"AtomRings_{z}");
        }

        /// <summary>
        /// <paramref name="count"/> beads evenly spaced on a circle in the local XZ plane (the orbiting outer
        /// electrons; their parent is turned so local Y is the ring's normal, and the shader spins them about Y).
        /// </summary>
        public static Mesh BeadsOnCircle(int count, float radius, Color color, float beadRadius = 0.016f)
        {
            var b = new Builder();
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / Mathf.Max(1, count);
                b.Sphere(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius), beadRadius, color, 0f, 8, 5);
            }
            return b.ToMesh($"Electrons_{count}");
        }

        /// <summary>Short decay sparks shooting out of a nucleus, each with its own jitter seed.</summary>
        public static Mesh DecaySparks(float startRadius, Color color, int seed, int count = 4)
        {
            var b = new Builder();
            var rnd = new System.Random(seed);
            float R() => (float)rnd.NextDouble();
            for (int i = 0; i < count; i++)
            {
                var d = new Vector3(R() * 2f - 1f, R() * 1.3f - 0.3f, R() * 2f - 1f).normalized;
                Vector3 from = d * (startRadius + 0.01f);
                b.Tube(from, from + d * Mathf.Lerp(0.05f, 0.1f, R()), 0.006f, color, i + 1, i + 1, 0f, 0f, 4);
            }
            return b.ToMesh("DecaySparks");
        }

        // ------------------------------------------------------------------ fast / light particles

        /// <summary>A comet tail streaming toward local -Z (upstream): beads shrinking and fading behind the head.</summary>
        public static Mesh CometTail(Color color, int beads = 6, float headGap = 0.045f, float spacing = 0.05f,
                                     float headRadius = 0.0275f)
        {
            var b = new Builder();
            for (int i = 0; i < beads; i++)
            {
                float t = (i + 1f) / beads;
                var c = color; c.a = Mathf.Lerp(0.85f, 0.15f, t);
                b.Sphere(new Vector3(0f, 0f, -headGap - spacing * i), headRadius * (1f - t * 0.75f), c, i + 1, 8, 5);
            }
            return b.ToMesh("CometTail");
        }

        /// <summary>
        /// A photon: a short wave along local Z under a Gaussian envelope, drawn as one tube polyline. It is a helix
        /// (circular polarisation), so it reads as a sine wave from the side and never goes flat when the camera
        /// looks along the belt. Each segment gets a seed so the wave shimmers.
        /// </summary>
        public static Mesh WavePacket(Color color, float halfLength = 0.2f, float amplitude = 0.07f,
                                      float waveNumber = 70f, float envelope = 0.09f, float thickness = 0.009f,
                                      int segments = 40)
        {
            var b = new Builder();
            Vector3 Point(int i)
            {
                float x = -halfLength + 2f * halfLength * i / segments;
                float env = Mathf.Exp(-(x / envelope) * (x / envelope));
                return new Vector3(amplitude * env * Mathf.Cos(x * waveNumber), amplitude * env * Mathf.Sin(x * waveNumber), x);
            }
            for (int i = 0; i < segments; i++)
                b.Tube(Point(i), Point(i + 1), thickness, color, i % 5 + 1, (i + 1) % 5 + 1, 0f, 1f, 5);
            return b.ToMesh("WavePacket");
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
