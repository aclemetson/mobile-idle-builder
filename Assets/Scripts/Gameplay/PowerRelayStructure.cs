using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "broadcast pylon" structure for the Power Relay (a power SPREADER — it re-radiates the
    /// grid's shared eV over a wide radius but generates none). A tall smooth amber mast
    /// (<see cref="RoundedBoxMeshBuilder"/>) crowned by a horizontal broadcast ring
    /// (<see cref="TorusMeshBuilder"/>) that spins slowly, emitting wide, slow expanding ground ripples —
    /// reading as power broadcasting over a large area. Deliberately distinct from the Basic Generator's
    /// thin cyan spire (taller, amber, a prominent crown ring, and far larger/slower ripples).
    ///
    /// Reuses the registered <c>MobileIdleBuilder/CollectorStructure</c> shader (body emission + crown glow
    /// + _PulseAmp flare) so no new Android shader registration is needed. Selected data-drivenly by
    /// <see cref="BuildingStructureKind.PowerRelay"/>. Not a producer, so the pulse is time-driven.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class PowerRelayStructure : MonoBehaviour
    {
        private const int   LongitudeSegments = 24;
        private const int   LatitudeSegments  = 18;

        private const float MastHeight    = 1.7f;
        private const float MastHalfWidth = 0.16f;
        private const float MastRoundness = 0.7f;   // smooth tapered pylon

        private const float CrownRadius   = 0.34f;
        private const float CrownY        = 1.5f;   // near the top of the mast
        private const float CrownSpinSpeed = 24f;

        private const float HeartbeatPeriod = 2.2f; // slower than the generator's 1.5s
        private const float InitialPulse    = 0.6f;
        private const float DecayRate       = 3f;
        private const float SettleThreshold = 0.001f;

        private const float BobSpeed = 1.1f;
        private const float BobAmp   = 0.025f;

        private const int   RippleCount      = 3;
        private const float RipplePeriod     = 3.6f; // slower expand cycle than the generator's 2.4s
        private const float RippleStart      = 0.2f;
        private const float RippleEnd        = 4.2f; // wider reach than the generator's 2.4
        private const float RippleBaseRadius = 0.5f;

        private static readonly Color RelayColor  = new Color(1.0f, 0.70f, 0.25f); // warm amber
        private static readonly Color CrownColor  = new Color(1.0f, 0.82f, 0.45f);
        private static readonly Color RippleColor = new Color(1.0f, 0.75f, 0.35f);

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");

        private Material _bodyMat;
        private Material _crownMat;
        private Material _rippleMat;
        private Mesh     _bodyMesh;
        private Mesh     _crownMesh;
        private Mesh     _rippleMesh;
        private Transform _crown;
        private readonly Transform[]    _ripples  = new Transform[RippleCount];
        private readonly MeshRenderer[] _rippleMr = new MeshRenderer[RippleCount];
        private MaterialPropertyBlock   _rippleMpb;

        private float   _heartbeatTimer;
        private float   _pulseElapsed = Mathf.Infinity;
        private Vector3 _baseLocalPos;
        private Vector3 _baseScale = Vector3.one;

        public void Initialize(int fw, int fh)
        {
            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;
            BuildBody(fw, fh);
            BuildCrown();
            BuildRipples();
        }

        private void BuildBody(int fw, int fh)
        {
            // A thin tall pylon; keep the mast narrow regardless of footprint so it reads as a mast, but
            // never wider than the reserved footprint.
            float halfW = Mathf.Min(MastHalfWidth, fw * 0.5f - 0.05f);
            float halfD = Mathf.Min(MastHalfWidth, fh * 0.5f - 0.05f);
            _bodyMesh = RoundedBoxMeshBuilder.Build(LongitudeSegments, LatitudeSegments,
                new RoundedBoxMeshBuilder.RoundedBoxProfile
                {
                    HalfWidth = Mathf.Max(0.08f, halfW),
                    HalfDepth = Mathf.Max(0.08f, halfD),
                    Height    = MastHeight,
                    Roundness = MastRoundness
                });
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[PowerRelayStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, RelayColor);
            _bodyMat.SetColor(TipColorId, CrownColor);
            renderer.material = _bodyMat;
        }

        private void BuildCrown()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return;

            // Horizontal broadcast ring (TorusMeshBuilder lies flat in XZ, hole facing up).
            _crownMesh = TorusMeshBuilder.Build(44, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = CrownRadius, MinorRadius = 0.03f });
            _crownMat = new Material(opaque);
            _crownMat.SetColor(BaseColorId, CrownColor);

            var crown = new GameObject("BroadcastRing");
            crown.transform.SetParent(transform, worldPositionStays: false);
            crown.transform.localPosition = new Vector3(0f, CrownY, 0f);
            crown.AddComponent<MeshFilter>().sharedMesh = _crownMesh;
            var mr = crown.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = _crownMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = LightProbeUsage.Off;
            _crown = crown.transform;
        }

        private void BuildRipples()
        {
            var transparent = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Transparent : null;
            if (transparent == null) return;

            // Instance the shared transparent material so the flat ground ripples draw AFTER the floor tiles
            // (same queue 3000, no depth write) — otherwise the floor sometimes sorts on top of them.
            _rippleMat = new Material(transparent) { renderQueue = 3200 };

            _rippleMesh = TorusMeshBuilder.Build(40, 5,
                new TorusMeshBuilder.TorusProfile { MajorRadius = RippleBaseRadius, MinorRadius = 0.012f });
            _rippleMpb = new MaterialPropertyBlock();

            for (int i = 0; i < RippleCount; i++)
            {
                var go = new GameObject($"RelayRipple_{i}");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(0f, 0.02f, 0f); // flat on the ground plane
                go.AddComponent<MeshFilter>().sharedMesh = _rippleMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial    = _rippleMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows    = false;
                mr.lightProbeUsage   = LightProbeUsage.Off;
                _ripples[i]  = go.transform;
                _rippleMr[i] = mr;
            }
        }

        void Update()
        {
            float t  = Time.time;
            float dt = Time.deltaTime;

            _heartbeatTimer += dt;
            if (_heartbeatTimer >= HeartbeatPeriod)
            {
                _heartbeatTimer -= HeartbeatPeriod;
                _pulseElapsed = 0f;
            }

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }

            float bobY = Mathf.Sin(t * BobSpeed) * BobAmp * _baseScale.y;
            transform.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, pulse);

            if (_crown != null) _crown.Rotate(0f, CrownSpinSpeed * dt, 0f, Space.Self);

            AnimateRipples(t);
        }

        private void AnimateRipples(float t)
        {
            if (_rippleMpb == null) return;
            for (int i = 0; i < _ripples.Length; i++)
            {
                if (_ripples[i] == null) continue;
                float phase = (t / RipplePeriod + (float)i / RippleCount) % 1f; // 0..1 expanding cycle
                float scale = Mathf.Lerp(RippleStart, RippleEnd, phase);
                _ripples[i].localScale = new Vector3(scale, scale, scale);

                float alpha = Mathf.Lerp(0.45f, 0f, phase); // fade as it expands
                _rippleMpb.SetColor(BaseColorId, new Color(RippleColor.r, RippleColor.g, RippleColor.b, alpha));
                _rippleMr[i].SetPropertyBlock(_rippleMpb);
            }
        }

        void OnDestroy()
        {
            if (_bodyMat != null)   Destroy(_bodyMat);
            if (_crownMat != null)  Destroy(_crownMat);
            if (_rippleMat != null) Destroy(_rippleMat);
            if (_bodyMesh != null)  Destroy(_bodyMesh);
            if (_crownMesh != null) Destroy(_crownMesh);
            if (_rippleMesh != null) Destroy(_rippleMesh);
        }
    }
}
