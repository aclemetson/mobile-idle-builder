using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Renders a noise-fluctuating wire-mesh overlay across a field's 1x1 tile and makes it bounce
    /// when the field is tapped. The mesh (line topology) is built by <see cref="FieldWireMeshBuilder"/>
    /// and animated entirely in the <c>MobileIdleBuilder/FieldWireMesh</c> shader: the ambient ripple
    /// is <c>_Time</c>-driven, while the tap bounce is a decaying <c>_BounceAmp</c> envelope written
    /// here each frame (see <see cref="WireBounce"/>).
    ///
    /// Added in <see cref="FieldGenerator.OccupyAndSpawn"/>. The shader is force-included via
    /// GraphicsSettings' Always Included Shaders, so the in-code material is safe in device builds.
    /// </summary>
    public class FieldWireMesh : MonoBehaviour
    {
        private const int   Resolution      = 10;     // cells per side -> 11x11 grid points
        private const float InitialBounce   = 0.18f;  // peak Y displacement on tap (world units)
        private const float DecayRate       = 5f;     // how fast the bounce settles
        private const float SettleThreshold = 0.001f; // below this we stop writing and idle
        private const float SurfaceYOffset  = -0.47f; // field GO sits at y=0.5; lift mesh just above the tile

        private static readonly int BounceAmpId = Shader.PropertyToID("_BounceAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Material _material;
        private float    _bounceElapsed = Mathf.Infinity; // Infinity == not bouncing

        /// <summary>
        /// Builds the wire mesh, sizes it to the cell, and tints it with the field colour.
        /// The renderer lives on a child GameObject because the field GO already carries a
        /// ParticleSystemRenderer (FieldEffect), and Unity forbids two renderers on one object.
        /// </summary>
        public void Initialize(Color fieldColor, float cellSize)
        {
            var mesh = FieldWireMeshBuilder.Build(Resolution, cellSize);

            var meshGO = new GameObject("WireMesh");
            meshGO.transform.SetParent(transform, worldPositionStays: false);
            meshGO.transform.localPosition = new Vector3(0f, SurfaceYOffset, 0f);

            var filter = meshGO.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = meshGO.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/FieldWireMesh");
            if (shader == null)
            {
                GameLogger.Warning("[FieldWireMesh] Shader 'MobileIdleBuilder/FieldWireMesh' not found — wire overlay disabled.");
                return;
            }

            _material = new Material(shader);
            _material.SetColor(BaseColorId, new Color(fieldColor.r, fieldColor.g, fieldColor.b, 1f));
            _material.SetFloat(BounceAmpId, 0f);
            renderer.material = _material;
        }

        /// <summary>Kicks off a bounce pulse (called when the field is tapped).</summary>
        public void TriggerBounce() => _bounceElapsed = 0f;

        void Update()
        {
            if (_material == null || float.IsInfinity(_bounceElapsed)) return;

            _bounceElapsed += Time.deltaTime;
            float amp = WireBounce.Amplitude(_bounceElapsed, InitialBounce, DecayRate);
            if (amp <= SettleThreshold)
            {
                amp            = 0f;
                _bounceElapsed = Mathf.Infinity; // settle and stop writing
            }
            _material.SetFloat(BounceAmpId, amp);
        }

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
