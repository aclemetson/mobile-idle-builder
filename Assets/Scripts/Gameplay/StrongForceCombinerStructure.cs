using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural body for the Strong Force Combiner (2x1): a soft rounded rectangular prism
    /// (<see cref="RoundedBoxMeshBuilder"/>) that fills the footprint and flows smoothly from its two
    /// inlet faces to its single outlet face, so the input/output door fixtures (drawn by
    /// <see cref="BuildingVisualizer"/>) sit flush on the body like the collector's door. A faint binding
    /// glow builds as a craft nears completion, then flares when a nucleon is forged.
    ///
    /// The body sits on the host (uniform cell scale) so it still receives power/hover tints. Selected
    /// data-drivenly by <see cref="BuildingStructureKind.StrongForceCombiner"/>; production ticks are a
    /// DROP in <see cref="RecipeProcessData.Progress"/> (reset to 0 on completion).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class StrongForceCombinerStructure : MonoBehaviour
    {
        private const int   LongitudeSegments = 32; // multiple of 4 -> verts land on face centres
        private const int   LatitudeSegments  = 12;

        private const float Height          = 0.8f;  // local units (host is uniform cell scale)
        private const float FootprintMargin = 0.06f; // shrink from the footprint edge so tiles don't touch
        private const float Roundness       = 0.5f;  // rounder ends + softer wall->dome transition

        private const float InitialPulse    = 0.6f;
        private const float DecayRate        = 4f;
        private const float SettleThreshold = 0.001f;
        private const float ChargeGlowMax   = 0.3f;  // steady glow at full craft progress (pre-flare)

        private const float BobSpeed   = 0.9f;
        private const float BreatheAmp = 0.02f;

        private static readonly Color BodyColor = new Color(0.7f, 0.78f, 0.85f); // pale steel
        private static readonly Color TipColor  = new Color(1.0f, 0.9f, 0.7f);   // warm binding glow crown

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");

        private Material _bodyMat;
        private Mesh     _bodyMesh;

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private float   _lastProgress = -1f;
        private float   _pulseElapsed = Mathf.Infinity;
        private Vector3 _baseLocalPos;
        private Vector3 _baseScale = Vector3.one;

        /// <summary>
        /// Builds the rounded-box body sized to the (rotation-applied) footprint <paramref name="fw"/> x
        /// <paramref name="fh"/> cells and binds the entity for production flares. The host is positioned
        /// at the footprint centre with uniform cell scale by <see cref="BuildingVisualizer"/>.
        /// </summary>
        public void Initialize(Entity entity, int fw, int fh)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;

            BuildBody(fw, fh);
        }

        private void BuildBody(int fw, int fh)
        {
            var profile = new RoundedBoxMeshBuilder.RoundedBoxProfile
            {
                HalfWidth = Mathf.Max(0.1f, fw * 0.5f - FootprintMargin),
                HalfDepth = Mathf.Max(0.1f, fh * 0.5f - FootprintMargin),
                Height    = Height,
                Roundness = Roundness
            };
            _bodyMesh = RoundedBoxMeshBuilder.Build(LongitudeSegments, LatitudeSegments, profile);
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[StrongForceCombinerStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, BodyColor);
            _bodyMat.SetColor(TipColorId, TipColor);
            renderer.material = _bodyMat;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float cycle = ReadCycleFraction();
            PollProductionTick();

            float flare = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                flare = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (flare <= SettleThreshold) { flare = 0f; _pulseElapsed = Mathf.Infinity; }
            }

            // Binding glow builds with craft progress, then the completion flare spikes it.
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, Mathf.Max(flare, cycle * ChargeGlowMax));

            float t = Time.time;
            float scaleF = 1f + Mathf.Sin(t * BobSpeed) * BreatheAmp + flare * 0.15f;
            transform.localScale = new Vector3(_baseScale.x * scaleF, _baseScale.y, _baseScale.z * scaleF);
        }

        private float ReadCycleFraction()
        {
            if (!_hasEntity || !_world.IsCreated) return 0f;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<RecipeProcessData>(_entity)) return 0f;
            var rp = em.GetComponentData<RecipeProcessData>(_entity);
            return rp.CraftTime > 0f ? Mathf.Clamp01(rp.Progress / rp.CraftTime) : 0f;
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
            if (_bodyMat != null)  Destroy(_bodyMat);
            if (_bodyMesh != null) Destroy(_bodyMesh);
        }
    }
}
