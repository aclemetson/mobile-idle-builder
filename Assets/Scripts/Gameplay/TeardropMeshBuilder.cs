using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the rock(s) a Belt Splitter's river forks around: a dome that is teardrop-shaped from above,
    /// swelling from a sharp point at local -Z (upstream) to a rounded back. It sits on the ground with an
    /// open underside, which the opaque channel bed hides. One cell is 1 local unit.
    ///
    /// <c>uv.y</c> drives <c>MobileIdleBuilder/CollectorStructure</c>: 1 at the tip (the shader's glowing
    /// singularity), high along the crown and 0 at the back of the base, so the body reads as lit from above.
    /// Pure construction, so EditMode tests can assert the shape without a scene.
    /// </summary>
    public static class TeardropMeshBuilder
    {
        /// <summary>Plan-view and height parameters for one rock, in local units.</summary>
        public struct RockProfile
        {
            public float CenterX;    // sideways offset from the cell centre
            public float TipZ;       // Z of the sharp upstream point
            public float Length;     // tip to back, along +Z
            public float HalfWidth;  // widest half-width
            public float Height;     // crown height
        }

        /// <summary>A 2-way fork: one rock splits the river to the left and right.</summary>
        public static readonly RockProfile[] TwoWay =
        {
            new RockProfile { CenterX = 0f, TipZ = -0.26f, Length = 0.56f, HalfWidth = 0.15f, Height = 0.32f },
        };

        /// <summary>A 3-way fork: two smaller rocks sit on the partings, leaving a channel straight ahead.</summary>
        public static readonly RockProfile[] ThreeWay =
        {
            new RockProfile { CenterX = -0.13f, TipZ = -0.2f, Length = 0.44f, HalfWidth = 0.075f, Height = 0.24f },
            new RockProfile { CenterX =  0.13f, TipZ = -0.2f, Length = 0.44f, HalfWidth = 0.075f, Height = 0.24f },
        };

        /// <summary>Half-width of the plan-view teardrop at <paramref name="u"/> (0 = tip, 1 = back).</summary>
        public static float PlanRadius(float u, float halfWidth)
        {
            if (u <= 0f || u >= 1f) return 0f;
            // The exponent moves the widest point towards the back, so the front tapers long and sharp.
            return halfWidth * Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Pow(1f - u, 0.55f)), 0.8f);
        }

        /// <summary>
        /// Builds every rock into one mesh. <paramref name="lengthSegments"/> steps from tip to back,
        /// <paramref name="archSegments"/> steps over the dome from one side to the other.
        /// </summary>
        public static Mesh Build(int lengthSegments, int archSegments, IReadOnlyList<RockProfile> rocks)
        {
            int len  = Mathf.Max(2, lengthSegments);
            int arch = Mathf.Max(2, archSegments);

            var verts = new List<Vector3>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            foreach (var rock in rocks)
            {
                int start = verts.Count;
                for (int i = 0; i <= len; i++)
                {
                    float u = (float)i / len;
                    float radius = PlanRadius(u, rock.HalfWidth);
                    float z = rock.TipZ + rock.Length * u;

                    // Falls off sharply so only the point reaches full glow; the crown catches a little as a highlight.
                    float tipness = Mathf.Pow(1f - u, 4f);

                    for (int j = 0; j <= arch; j++)
                    {
                        float t  = Mathf.PI * j / arch;
                        float up = Mathf.Sin(t);
                        verts.Add(new Vector3(
                            rock.CenterX + radius * Mathf.Cos(t),
                            rock.Height / rock.HalfWidth * radius * up,
                            z));

                        float crown = 0.85f * up * radius / rock.HalfWidth;
                        uvs.Add(new Vector2((float)j / arch, 1f - (1f - crown) * (1f - tipness)));
                    }
                }

                int stride = arch + 1;
                for (int i = 0; i < len; i++)
                for (int j = 0; j < arch; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    tris.Add(b); tris.Add(d); tris.Add(c);
                }
            }

            var mesh = new Mesh { name = "SplitterRock" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
