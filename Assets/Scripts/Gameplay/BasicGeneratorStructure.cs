using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "energy spire" structure for the Basic Generator (a power source, not a producer): a
    /// tall thin spindle tapering up to a crackling singularity tip, with a heartbeat pulse that flares
    /// the tip and emits expanding flat light-rings on the ground — reading as power broadcasting to
    /// neighbours. Omnidirectional, so it has no input/output holes.
    ///
    /// Reuses <see cref="CollectorMeshBuilder"/> (with FrontFlattenFrac = 1 so the spire stays round on
    /// every side) and the <c>MobileIdleBuilder/CollectorStructure</c> shader (body emission + tip glow +
    /// _PulseAmp flare). Selected data-drivenly by <see cref="BuildingStructureKind.BasicGenerator"/>.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BasicGeneratorStructure : MonoBehaviour
    {
        private const int   RadialSegments = 22;
        private const int   HeightSegments = 22;

        private const float HeartbeatPeriod = 1.5f;  // seconds between power pulses
        private const float InitialPulse    = 0.7f;
        private const float DecayRate       = 3f;
        private const float SettleThreshold = 0.001f;

        private const float BobSpeed = 1.4f;
        private const float BobAmp   = 0.03f;

        private const int   RippleCount    = 3;
        private const float RipplePeriod   = 2.4f;   // one ring's expand-and-fade cycle
        private const float RippleStart    = 0.15f;  // starting ring scale
        private const float RippleEnd      = 2.4f;   // ending ring scale (≈ link/influence reach)
        private const float RippleBaseRadius = 0.5f; // torus MajorRadius the scale multiplies

        private static readonly Color SpireColor  = new Color(0.30f, 0.85f, 1.0f); // electric cyan
        private static readonly Color RippleColor = new Color(0.40f, 0.9f, 1.0f);

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");

        private static readonly CollectorMeshBuilder.SpindleProfile SpireProfile =
            new CollectorMeshBuilder.SpindleProfile
            {
                Height           = 1.5f,
                BulbRadius       = 0.20f,
                BaseRadius       = 0.06f,
                ApexSharpness    = 1.9f,
                FrontFlattenFrac = 1.0f // no front slice — round spire
            };

        private Material   _bodyMat;
        private Mesh       _bodyMesh;
        private Mesh       _rippleMesh;
        private readonly Transform[]            _ripples    = new Transform[RippleCount];
        private readonly MeshRenderer[]         _rippleMr   = new MeshRenderer[RippleCount];
        private MaterialPropertyBlock           _rippleMpb;

        private float   _heartbeatTimer;
        private float   _pulseElapsed = Mathf.Infinity;
        private Vector3 _baseLocalPos;
        private Vector3 _baseScale = Vector3.one;

        public void Initialize()
        {
            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;
            BuildBody();
            BuildRipples();
        }

        private void BuildBody()
        {
            _bodyMesh = CollectorMeshBuilder.Build(RadialSegments, HeightSegments, SpireProfile);
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[BasicGeneratorStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, SpireColor);
            _bodyMat.SetColor(TipColorId, Color.Lerp(SpireColor, Color.white, 0.6f));
            renderer.material = _bodyMat;
        }

        private void BuildRipples()
        {
            var transparent = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Transparent : null;
            if (transparent == null) return;

            _rippleMesh = TorusMeshBuilder.Build(40, 5,
                new TorusMeshBuilder.TorusProfile { MajorRadius = RippleBaseRadius, MinorRadius = 0.012f });
            _rippleMpb = new MaterialPropertyBlock();

            for (int i = 0; i < RippleCount; i++)
            {
                var go = new GameObject($"PowerRipple_{i}");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.transform.localPosition = new Vector3(0f, 0.02f, 0f); // flat on the ground plane
                go.AddComponent<MeshFilter>().sharedMesh = _rippleMesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial    = transparent;
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

            // Heartbeat — fire a pulse on each beat.
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

            // Subtle bob + tip flare.
            float bobY = Mathf.Sin(t * BobSpeed) * BobAmp * _baseScale.y;
            transform.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, pulse);

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

                float alpha = Mathf.Lerp(0.5f, 0f, phase); // fade as it expands
                _rippleMpb.SetColor(BaseColorId, new Color(RippleColor.r, RippleColor.g, RippleColor.b, alpha));
                _rippleMr[i].SetPropertyBlock(_rippleMpb);
            }
        }

        void OnDestroy()
        {
            if (_bodyMat != null)    Destroy(_bodyMat);
            if (_bodyMesh != null)   Destroy(_bodyMesh);
            if (_rippleMesh != null) Destroy(_rippleMesh);
        }
    }
}
