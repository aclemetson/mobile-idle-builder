using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "lead shell" structure for the Radioactive Containment building (2x2): a sealed lead-grey
    /// dome (<see cref="RoundedBoxMeshBuilder"/> + the <c>MobileIdleBuilder/CollectorStructure</c> shader)
    /// that fills the footprint, ringed near its apex by a slow amber-green containment field
    /// (<see cref="TorusMeshBuilder"/>). Irregular decay flashes flare the shell and brighten the ring —
    /// the contained chaos showing through. It contains rather than emits, so it has no input/output doors.
    ///
    /// Selected data-drivenly by <see cref="BuildingStructureKind.RadioactiveContainment"/>. Not a recipe
    /// producer, so the flashes are time-driven (randomised intervals) rather than tied to production.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class RadioactiveContainmentStructure : MonoBehaviour
    {
        private const int   LongitudeSegments = 36;
        private const int   LatitudeSegments  = 16;

        private const float FootprintMargin = 0.08f;
        private const float DomeHeight      = 0.9f;
        private const float DomeRoundness   = 0.6f; // domed shell

        private const float FlashMinInterval = 1.0f;
        private const float FlashMaxInterval = 3.0f;
        private const float InitialPulse     = 0.55f;
        private const float DecayRate        = 5f;
        private const float SettleThreshold  = 0.001f;

        private const float RingRadius    = 0.36f;
        private const float RingHeight    = 0.86f; // fraction of DomeHeight
        private const float RingSpinSpeed = 30f;

        private static readonly Color LeadColor   = new Color(0.35f, 0.35f, 0.38f); // lead grey shell
        private static readonly Color HazardColor = new Color(0.6f, 1.0f, 0.3f);    // radioactive amber-green

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");

        private Material  _bodyMat;
        private Material  _ringMat;
        private Mesh      _bodyMesh;
        private Mesh      _ringMesh;
        private Transform _ring;

        private float   _flashTimer;
        private float   _nextFlash;
        private float   _pulseElapsed = Mathf.Infinity;
        private Vector3 _baseLocalPos;
        private Vector3 _baseScale = Vector3.one;

        public void Initialize(int fw, int fh)
        {
            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;
            _nextFlash    = Random.Range(FlashMinInterval, FlashMaxInterval);

            BuildBody(fw, fh);
            BuildRing();
        }

        private void BuildBody(int fw, int fh)
        {
            _bodyMesh = RoundedBoxMeshBuilder.Build(LongitudeSegments, LatitudeSegments,
                new RoundedBoxMeshBuilder.RoundedBoxProfile
                {
                    HalfWidth = Mathf.Max(0.1f, fw * 0.5f - FootprintMargin),
                    HalfDepth = Mathf.Max(0.1f, fh * 0.5f - FootprintMargin),
                    Height    = DomeHeight,
                    Roundness = DomeRoundness
                });
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[RadioactiveContainmentStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, LeadColor);
            _bodyMat.SetColor(TipColorId, HazardColor); // decay shows through the crown
            renderer.material = _bodyMat;
        }

        private void BuildRing()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return;

            _ringMesh = TorusMeshBuilder.Build(44, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = RingRadius, MinorRadius = 0.02f });
            _ringMat = new Material(opaque);
            _ringMat.SetColor(BaseColorId, HazardColor);

            var ring = new GameObject("ContainmentRing");
            ring.transform.SetParent(transform, worldPositionStays: false);
            ring.transform.localPosition = new Vector3(0f, DomeHeight * RingHeight, 0f);
            ring.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            ring.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
            var mr = ring.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = _ringMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = LightProbeUsage.Off;
            _ring = ring.transform;
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // Irregular decay flashes.
            _flashTimer += dt;
            if (_flashTimer >= _nextFlash)
            {
                _flashTimer = 0f;
                _nextFlash  = Random.Range(FlashMinInterval, FlashMaxInterval);
                _pulseElapsed = 0f;
            }

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, pulse);

            if (_ring != null) _ring.Rotate(0f, RingSpinSpeed * dt, 0f, Space.Self);
            if (_ringMat != null) _ringMat.SetColor(BaseColorId, HazardColor * (0.8f + pulse));
        }

        void OnDestroy()
        {
            if (_bodyMat != null)  Destroy(_bodyMat);
            if (_ringMat != null)  Destroy(_ringMat);
            if (_bodyMesh != null) Destroy(_bodyMesh);
            if (_ringMesh != null) Destroy(_ringMesh);
        }
    }
}
