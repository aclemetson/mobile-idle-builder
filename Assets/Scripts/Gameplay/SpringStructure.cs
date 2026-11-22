using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Chemistry's Spring as a rock pool (concept A, 2026-11-22): a ring of rough stones around a pool of
    /// water welling up, with a gap on the output side where the water spills into the channel. Selected
    /// by <see cref="BuildingStructureKind.Spring"/>.
    ///
    /// The stones are the host's mesh, so <see cref="PresenceReceiver"/> keeps driving their tint (hover,
    /// selection). The pool and the spill use the channels' water material
    /// (<see cref="ConveyorVisualizer.GetStreamMaterial"/>): its caustics drift along each vertex's flow
    /// direction, which here points outward from the centre, so the light spreads out like welling water.
    /// A small dome of water in the middle bobs to show the spring is running.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SpringStructure : MonoBehaviour
    {
        private const int   Rocks       = 11;
        private const float RingRadius  = 0.36f;   // in cells
        private const float PoolRadius  = 0.33f;
        private const float GapHalfAngle = 0.42f;  // radians either side of the output, left open for the spill
        private const float WaterHeight = 0.035f;

        private Mesh _rockMesh, _poolMesh, _spillMesh, _domeMesh;
        private Material _rockMat;
        private Transform _dome;

        /// <summary>
        /// Builds the pool. The host sits at the cell centre on the ground at a uniform scale of one cell.
        /// <paramref name="outputDir"/> is the output port's facing (the side the stream leaves from).
        /// </summary>
        public void Initialize(int outputDir, Color rockColor, Material waterMaterial)
        {
            // Local +Z becomes the output direction.
            transform.localRotation = Quaternion.Euler(0f, outputDir * 90f, 0f);

            _rockMesh = BuildRocks();
            GetComponent<MeshFilter>().sharedMesh = _rockMesh;
            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;
            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader != null)
            {
                _rockMat = new Material(shader);
                _rockMat.SetFloat("_PulseAmp", 0f);
                _rockMat.SetColor("_BaseColor", rockColor);
                _rockMat.SetFloat("_TipGlow", 0f);
                renderer.material = _rockMat;
            }

            if (waterMaterial == null) return;
            _poolMesh  = BuildPool();
            _spillMesh = BuildSpill();
            _domeMesh  = BuildDome();
            AddLayer("Pool",  _poolMesh,  waterMaterial);
            AddLayer("Spill", _spillMesh, waterMaterial);
            _dome = AddLayer("Upwell", _domeMesh, waterMaterial);
        }

        void Update()
        {
            if (_dome == null) return;
            // A slow bob: the spring is pushing water up.
            float t = Time.time;
            float s = 1f + 0.08f * Mathf.Sin(t * 2.6f) + 0.04f * Mathf.Sin(t * 6.1f);
            _dome.localScale = new Vector3(s, 0.55f + 0.12f * Mathf.Sin(t * 3.3f), s);
        }

        private Transform AddLayer(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, worldPositionStays: false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial    = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = LightProbeUsage.Off;
            return go.transform;
        }

        // ── Meshes (local space, one cell = 1 unit, +Z = output) ──────────────

        /// <summary>Rough low-poly stones in a ring, open toward +Z.</summary>
        internal static Mesh BuildRocks()
        {
            var verts = new List<Vector3>();
            var tris  = new List<int>();
            var rng   = new System.Random(7);
            float step = 2f * Mathf.PI / (Rocks + 1);
            for (int i = 0; i < Rocks + 1; i++)
            {
                // Angle measured from +Z; skip the stones that would block the spill.
                float a = (i + 0.5f) * step;
                float fromOut = Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 0f)) * Mathf.Deg2Rad;
                if (fromOut < GapHalfAngle) continue;
                float r    = RingRadius + (float)(rng.NextDouble() * 0.04 - 0.01);
                float size = 0.085f + (float)rng.NextDouble() * 0.05f;
                var c = new Vector3(Mathf.Sin(a) * r, size * 0.45f, Mathf.Cos(a) * r);
                AddStone(verts, tris, c, size, (float)rng.NextDouble() * 6.28f, rng);
            }
            var m = new Mesh { name = "SpringRocks" };
            m.SetVertices(verts); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // A squashed, jittered octahedron-ish stone with flat faces (each triangle has its own vertices).
        private static void AddStone(List<Vector3> verts, List<int> tris, Vector3 c, float s, float yaw, System.Random rng)
        {
            var q = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
            var pts = new Vector3[8];
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                float jitter = 0.8f + (float)rng.NextDouble() * 0.4f;
                pts[k] = new Vector3(Mathf.Cos(a) * 1.2f, 0f, Mathf.Sin(a)) * s * jitter;
            }
            pts[6] = new Vector3(0f, s * (0.7f + (float)rng.NextDouble() * 0.3f), 0f);
            pts[7] = new Vector3(0f, -s * 0.45f, 0f);
            for (int k = 0; k < 6; k++)
            {
                int n = (k + 1) % 6;
                Tri(verts, tris, c, q, pts[6], pts[n], pts[k]);
                Tri(verts, tris, c, q, pts[7], pts[k], pts[n]);
            }
        }

        private static void Tri(List<Vector3> verts, List<int> tris, Vector3 c, Quaternion q, Vector3 a, Vector3 b, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(c + q * a); verts.Add(c + q * b); verts.Add(c + q * d);
            tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        }

        /// <summary>
        /// Water disc. uv.x runs 0.5 at the centre to 0 at the rim (the shader is deepest at 0.5 and fades
        /// toward 0); uv.y is the distance from the centre, the shader's "along the flow" axis, so the light
        /// ripples move outward like welling water.
        /// </summary>
        internal static Mesh BuildPool()
        {
            const int seg = 28;
            var v = new List<Vector3> { new Vector3(0f, WaterHeight, 0f) };
            var uv = new List<Vector2> { new Vector2(0.5f, 0f) };
            var flow = new List<Vector2> { new Vector2(0f, 1f) };
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * 2f * Mathf.PI / seg;
                var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                v.Add(new Vector3(d.x * PoolRadius, WaterHeight, d.y * PoolRadius));
                uv.Add(new Vector2(0.02f, 0.6f));
                flow.Add(d);
                if (i > 0) { tris.Add(0); tris.Add(i); tris.Add(i + 1); }
            }
            var m = new Mesh { name = "SpringPool" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(3, flow); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>The lip of water from the pool's open side to the cell edge, flowing toward +Z.</summary>
        internal static Mesh BuildSpill()
        {
            float w = 0.2f, z0 = PoolRadius * 0.6f, z1 = 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-w, WaterHeight, z0), new Vector3(w, WaterHeight, z0),
                new Vector3(-w * 1.2f, WaterHeight * 0.6f, z1), new Vector3(w * 1.2f, WaterHeight * 0.6f, z1),
            };
            var uv = new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            var flow = new List<Vector2> { Vector2.up, Vector2.up, Vector2.up, Vector2.up };
            var m = new Mesh { name = "SpringSpill" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(3, flow);
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        /// <summary>A low dome of water in the middle of the pool (scaled flat and bobbed in Update).</summary>
        internal static Mesh BuildDome()
        {
            const int rings = 5, seg = 16;
            float r = 0.11f;
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var flow = new List<Vector2>();
            var tris = new List<int>();
            for (int j = 0; j <= rings; j++)
            {
                float phi = j * 0.5f * Mathf.PI / rings;           // 0 at the top, pi/2 at the rim
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * 2f * Mathf.PI / seg;
                    var d = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    v.Add(new Vector3(d.x * Mathf.Sin(phi) * r, WaterHeight + Mathf.Cos(phi) * r, d.y * Mathf.Sin(phi) * r));
                    uv.Add(new Vector2(0.5f - 0.4f * j / rings, 0.15f * j / rings));
                    flow.Add(d);
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = j * (seg + 1) + i, b = a + seg + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            var m = new Mesh { name = "SpringUpwell" };
            m.SetVertices(v); m.SetUVs(0, uv); m.SetUVs(3, flow); m.SetTriangles(tris, 0);
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        void OnDestroy()
        {
            if (_rockMesh)  Destroy(_rockMesh);
            if (_poolMesh)  Destroy(_poolMesh);
            if (_spillMesh) Destroy(_spillMesh);
            if (_domeMesh)  Destroy(_domeMesh);
            if (_rockMat)   Destroy(_rockMat);
        }
    }
}
