using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "bonded molecule" structure for the Molecular Synthesizer (1x1): a solid central bulb
    /// (<see cref="AtomNucleusMeshBuilder"/> + the <c>MobileIdleBuilder/AtomGenerator</c> shader) with two
    /// smaller satellite lobes fused to it, reading as a space-filling molecule. The whole cluster tumbles
    /// slowly; a bond flare pulses on each synthesis. Teal. The central bulb stays on the host so it keeps
    /// receiving power/hover tints; the door fixtures are drawn by <see cref="BuildingVisualizer"/>.
    ///
    /// Selected data-drivenly by <see cref="BuildingStructureKind.MolecularSynthesizer"/>; production ticks
    /// are a DROP in <see cref="RecipeProcessData.Progress"/> (reset to 0 on completion).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MolecularSynthesizerStructure : MonoBehaviour
    {
        private const int   RadialSegments = 26;
        private const int   HeightSegments = 16;

        private const float InitialPulse    = 0.5f;
        private const float DecayRate        = 4f;
        private const float SettleThreshold = 0.001f;

        private const float TumbleSpeed = 28f; // deg/s — molecule rotates slowly
        private const float BobSpeed    = 0.9f;
        private const float BobAmp      = 0.04f;
        private const float BreatheAmp  = 0.03f;
        private const float PulsePuff   = 0.35f;

        private static readonly Color MoleculeColor = new Color(0.2f, 0.8f, 0.8f);  // teal
        private static readonly Color SatelliteColor = new Color(0.3f, 0.9f, 0.85f);

        // Fused satellite lobes (local position, scale) overlapping the central bulb so they read as one
        // bonded cluster rather than floating atoms.
        private static readonly (Vector3 pos, float scale)[] Satellites =
        {
            (new Vector3( 0.30f, 0.55f,  0.06f), 0.40f),
            (new Vector3(-0.28f, 0.40f, -0.10f), 0.32f),
        };

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

        private Material _bodyMat;
        private Material _satelliteMat;
        private Mesh     _bodyMesh;
        private readonly Transform[] _satellites = new Transform[Satellites.Length];

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private float   _tumble;
        private float   _lastProgress = -1f;
        private float   _pulseElapsed = Mathf.Infinity;
        private Vector3 _baseLocalPos;
        private Vector3 _baseScale = Vector3.one;

        public void Initialize(Entity entity)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;

            BuildBody();
            BuildSatellites();
        }

        private void BuildBody()
        {
            _bodyMesh = AtomNucleusMeshBuilder.Build(RadialSegments, HeightSegments);
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/AtomGenerator");
            if (shader == null)
            {
                GameLogger.Warning("[MolecularSynthesizerStructure] Shader 'MobileIdleBuilder/AtomGenerator' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, MoleculeColor);
            _bodyMat.SetColor(CoreColorId, Color.Lerp(MoleculeColor, Color.white, 0.4f));
            renderer.material = _bodyMat;
        }

        private void BuildSatellites()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return;

            _satelliteMat = new Material(opaque);
            _satelliteMat.SetColor(BaseColorId, SatelliteColor);

            for (int i = 0; i < Satellites.Length; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                s.name = $"Lobe_{i}";
                var col = s.GetComponent<Collider>();
                if (col != null) Destroy(col);
                s.transform.SetParent(transform, worldPositionStays: false);
                s.transform.localPosition = Satellites[i].pos;
                s.transform.localScale    = Vector3.one * Satellites[i].scale;
                var mr = s.GetComponent<MeshRenderer>();
                mr.sharedMaterial    = _satelliteMat;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows    = false;
                mr.lightProbeUsage   = LightProbeUsage.Off;
                _satellites[i] = s.transform;
            }
        }

        void Update()
        {
            PollProductionTick();

            float dt = Time.deltaTime;
            float t  = Time.time;
            _tumble += TumbleSpeed * dt;

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, pulse);

            float bobY   = (Mathf.Sin(t * BobSpeed) * BobAmp) * _baseScale.y;
            float scaleF = 1f + Mathf.Sin(t * BobSpeed) * BreatheAmp + pulse * PulsePuff;
            transform.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
            transform.localScale    = new Vector3(_baseScale.x * scaleF, _baseScale.y * scaleF, _baseScale.z * scaleF);
            transform.localRotation = Quaternion.Euler(0f, _tumble, 0f);
        }

        private void PollProductionTick()
        {
            if (!_hasEntity || !_world.IsCreated) return;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<RecipeProcessData>(_entity)) return;

            float progress = em.GetComponentData<RecipeProcessData>(_entity).Progress;
            if (_lastProgress >= 0f && progress < _lastProgress - 0.0001f)
                _pulseElapsed = 0f;
            _lastProgress = progress;
        }

        void OnDestroy()
        {
            if (_bodyMat != null)      Destroy(_bodyMat);
            if (_satelliteMat != null) Destroy(_satelliteMat);
            if (_bodyMesh != null)     Destroy(_bodyMesh);
        }
    }
}
