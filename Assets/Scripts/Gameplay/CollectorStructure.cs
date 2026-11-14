using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "A field drawn to a point": the structure for field-collector buildings. Rings on the ground contract
    /// towards a low cone, as if pulling the field in, and a particle condenses at its sharp tip as the
    /// harvest timer fills. On each harvest the particle pops and a burst streams from a solid black
    /// "doorway" (rectangle with an arched top) on the cone's one flat <b>front</b> (local +Z). The whole
    /// structure is tinted to the field it sits on, and rotated so the front faces the output direction.
    ///
    /// BuildingVisualizer attaches this to any building carrying <see cref="CollectorData"/>, replacing
    /// the placeholder cube. The silhouette is built by <see cref="CollectorMeshBuilder"/>; the slow
    /// bob, breathing, and apex glow are animated in the <c>MobileIdleBuilder/CollectorStructure</c>
    /// shader (_Time-driven). Each production tick (detected via a drop in <see cref="CollectorData.Timer"/>)
    /// fires a decaying <c>_PulseAmp</c> flare (see <see cref="WireBounce"/>) and a particle burst.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class CollectorStructure : MonoBehaviour
    {
        private const int   RadialSegments  = 28;     // round body
        private const int   HeightSegments  = 20;     // smooth taper to the apex
        private const float InitialPulse    = 0.6f;   // flare strength on a production tick
        private const float DecayRate       = 4f;     // how fast the flare settles
        private const float SettleThreshold = 0.001f; // below this the flare is idle
        private const int   BurstCount      = 12;     // particles emitted from the aperture per harvest

        // Pulse motion is applied to the host TRANSFORM (not the shader) so the child door and particles
        // inherit it and stay locked to the animated surface.
        private const float PulsePuff  = 0.22f; // extra XZ scale at a flare peak

        // Low and wide, with a long concave taper: reads as the ground being drawn up to a point.
        private static readonly CollectorMeshBuilder.SpindleProfile ConeProfile =
            new CollectorMeshBuilder.SpindleProfile
            {
                Height           = 0.95f,
                BulbRadius       = 0.44f,
                BaseRadius       = 0.30f,
                ApexSharpness    = 2.1f,
                FrontFlattenFrac = 0.62f
            };

        private const int   RingCount   = 3;
        private const float RingPeriod  = 2.2f;  // one ring's contraction
        private const float RingOuter   = 1.5f;  // starting ring scale, just past the cell
        private const float RingInner   = 0.45f; // ends at the cone's foot
        private const float BeadMaxSize = 0.17f; // the condensing particle's diameter when ready

        private static readonly int PulseAmpId = Shader.PropertyToID("_PulseAmp");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Material       _material;
        private Material       _doorMat;
        private Material       _particleMat;
        private Mesh           _mesh;
        private Mesh           _doorMesh;
        private ParticleSystem _particles;

        private readonly StructureParts _parts = new();
        private GroundRipples _rings;
        private Transform     _bead;
        private Material      _beadMat;
        private Color         _fieldColor;

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private int     _currentDir   = -2;             // last applied output direction (-2 == unset)
        private float   _lastTimer     = -1f;           // < 0 == not yet sampled
        private float   _pulseElapsed  = Mathf.Infinity; // Infinity == not pulsing
        private Vector3 _baseScale = Vector3.one;        // host transform captured pre-animation

        /// <summary>
        /// Builds the spindle + aperture onto the host, tints everything with <paramref name="fieldColor"/>,
        /// and binds the collector entity so the structure can pulse and aim its front at the output
        /// direction. Call once after the host GameObject has been positioned and scaled.
        /// </summary>
        public void Initialize(Entity entity, Color fieldColor)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            // Capture the host transform as set by BuildingVisualizer, before we animate around it.
            _baseScale = transform.localScale;
            _fieldColor = fieldColor;

            BuildBody(fieldColor);
            BuildAperture(fieldColor);
            BuildFieldPull(fieldColor);
            ApplyOutputDirection(forceWrite: true);
        }

        private void BuildBody(Color fieldColor)
        {
            _mesh = CollectorMeshBuilder.Build(RadialSegments, HeightSegments, ConeProfile);
            GetComponent<MeshFilter>().sharedMesh = _mesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[CollectorStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — collector keeps placeholder mesh.");
                return;
            }

            _material = new Material(shader);
            _material.SetFloat(PulseAmpId, 0f);
            _material.SetColor(BaseColorId, fieldColor);                                 // fallback; PresenceReceiver MPB overrides per-frame
            _material.SetColor(TipColorId, Color.Lerp(fieldColor, Color.white, 0.45f));  // brighter singularity tip
            renderer.material = _material; // instance; PresenceReceiver's _BaseColor MPB still applies
        }

        // Solid black "doorway" (rectangle with an arched top) on the flat front facade, reading as a
        // hole, with a forward-firing particle jet streaming out of it.
        private void BuildAperture(Color fieldColor)
        {
            float flatZ = CollectorMeshBuilder.FrontFlatZ(ConeProfile);
            float baseY = ConeProfile.Height * 0.06f; // door sits low on the broad part of the flat face

            const float doorWidth  = 0.16f;
            const float doorHeight = 0.26f;

            var pivot = new GameObject("Aperture");
            pivot.transform.SetParent(transform, worldPositionStays: false);
            pivot.transform.localPosition = new Vector3(0f, baseY, flatZ);

            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque != null)
            {
                _doorMesh = ApertureMeshBuilder.BuildArchDoor(doorWidth, doorHeight, 12);
                _doorMat  = new Material(opaque);
                _doorMat.SetColor(BaseColorId, Color.black); // solid black doorway -> reads as a hole
                AddDecal("ApertureDoor", pivot.transform, _doorMesh, _doorMat, 0.004f);
            }

            BuildParticles(pivot.transform, fieldColor, doorHeight * 0.45f);
        }

        // The contracting rings and the particle that condenses at the tip.
        private void BuildFieldPull(Color fieldColor)
        {
            _rings = new GroundRipples(_parts, transform, RingCount, 0.014f);

            _beadMat = _parts.Flat(Color.Lerp(fieldColor, Color.white, 0.55f));
            if (_beadMat == null) return;
            _bead = StructureParts.AddPrimitive(PrimitiveType.Sphere, "Bead", transform, _beadMat,
                new Vector3(0f, ConeProfile.Height - 0.02f, 0f), Vector3.zero);
        }

        private static void AddDecal(string name, Transform parent, Mesh mesh, Material mat, float zOffset)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, 0f, zOffset);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial      = mat;
            mr.shadowCastingMode   = ShadowCastingMode.Off;
            mr.receiveShadows      = false;
            mr.lightProbeUsage     = LightProbeUsage.Off;
        }

        // A narrow forward jet from the hole, mirroring FieldEffect's Android-safe particle setup but
        // directional (+Z, the front) and burst-only — driven by production ticks, not a continuous stream.
        private void BuildParticles(Transform parent, Color fieldColor, float emitterY)
        {
            var template = FieldGenerator.ParticleMaterialTemplate
                        ?? (RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Transparent : null);
            if (template == null) return;

            var go = new GameObject("ApertureParticles");
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = new Vector3(0f, emitterY, 0.02f);
            _particles = go.AddComponent<ParticleSystem>();

            var main = _particles.main;
            main.loop            = true;  // stay alive (rate 0) so Emit() bursts simulate — as FieldEffect does
            main.playOnAwake     = false;
            main.startLifetime   = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startSpeed      = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            main.startSize       = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles    = 64;

            var emission = _particles.emission;
            emission.enabled      = true;
            emission.rateOverTime = 0f; // burst-only; see Emit() on each production tick

            var shape = _particles.shape;
            shape.enabled  = true;
            shape.shapeType = ParticleSystemShapeType.Cone; // emits along the emitter's local +Z (the front)
            shape.angle    = 16f;
            shape.radius   = 0.03f;

            var size = _particles.sizeOverLifetime;
            size.enabled = true;
            var curve = new AnimationCurve();
            curve.AddKey(0f, 1f);
            curve.AddKey(1f, 0f);
            size.size = new ParticleSystem.MinMaxCurve(1f, curve);

            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode   = ParticleSystemRenderMode.Billboard;
            rend.sortingOrder = 1;
            _particleMat      = new Material(template);
            rend.material     = _particleMat;

            TintParticles(fieldColor);
            _particles.Play(); // playing with rate 0 emits nothing until a production-tick Emit()
        }

        /// <summary>
        /// Re-tints the field-coloured parts (tip glow + particle jet) to <paramref name="fieldColor"/>.
        /// Used when a collector loaded before its field existed (so the colour was unknown at build
        /// time) — see BuildingVisualizer's deferred field-colour resolution. The body base colour is
        /// owned by BuildingVisualizer/PresenceReceiver; the doorway stays black.
        /// </summary>
        public void SetFieldColor(Color fieldColor)
        {
            if (_material != null)
            {
                _material.SetColor(BaseColorId, fieldColor);
                _material.SetColor(TipColorId, Color.Lerp(fieldColor, Color.white, 0.45f));
            }
            TintParticles(fieldColor);
            _fieldColor = fieldColor;
            if (_beadMat != null) _beadMat.SetColor(BaseColorId, Color.Lerp(fieldColor, Color.white, 0.55f));
        }

        private void TintParticles(Color fieldColor)
        {
            if (_particles == null) return;

            var main = _particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(fieldColor.r, fieldColor.g, fieldColor.b, 0.95f),
                Color.Lerp(fieldColor, Color.white, 0.3f));

            var col = _particles.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[] { new GradientColorKey(fieldColor, 0f), new GradientColorKey(fieldColor, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(grad);

            if (_particleMat != null) _particleMat.SetColor(BaseColorId, fieldColor);
        }

        void Update()
        {
            PollProductionTick();
            ApplyOutputDirection(forceWrite: false);
            AnimateTransform();
        }

        /// <summary>
        /// CollectorSystem subtracts the production interval from <see cref="CollectorData.Timer"/>
        /// on each deposit, so a frame-over-frame DROP in the timer means an item was just produced —
        /// our cue to kick the flare and spit particles out of the aperture.
        /// </summary>
        private void PollProductionTick()
        {
            if (!_hasEntity || !_world.IsCreated) return;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<CollectorData>(_entity)) return;

            float timer = em.GetComponentData<CollectorData>(_entity).Timer;
            if (_lastTimer >= 0f && timer < _lastTimer - 0.0001f)
            {
                _pulseElapsed = 0f;                              // start the flare
                if (_particles != null) _particles.Emit(BurstCount); // spit particles from the hole
            }
            _lastTimer = timer;
        }

        // Aim the flat front (local +Z) at the building's output direction. North=+Z..West, so the
        // host Y rotation is direction * 90 degrees. Defaults to South (toward camera) when unset.
        private void ApplyOutputDirection(bool forceWrite)
        {
            int dir = (int)OutputDirection.South;
            if (_hasEntity && _world.IsCreated)
            {
                var em = _world.EntityManager;
                if (em.Exists(_entity) && em.HasComponent<OutputDirectionData>(_entity))
                {
                    int d = em.GetComponentData<OutputDirectionData>(_entity).Direction;
                    if (d >= 0 && d <= 3) dir = d;
                }
            }

            if (!forceWrite && dir == _currentDir) return;
            _currentDir = dir;
            transform.localRotation = Quaternion.Euler(0f, dir * 90f, 0f);
        }

        // Pulse-puff on XZ, applied to the host transform so the door and particle emitter (children) move
        // with the surface and never detach from it. The flare's emission boost goes to the shader via
        // _PulseAmp. The rings contract at a steady rate; the bead grows as the harvest timer fills.
        private void AnimateTransform()
        {
            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += Time.deltaTime;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }

            float scaleF = 1f + pulse * PulsePuff;
            transform.localScale = new Vector3(_baseScale.x * scaleF, _baseScale.y, _baseScale.z * scaleF);

            if (_material != null) _material.SetFloat(PulseAmpId, pulse);
            _rings?.Animate(Time.time, RingPeriod, RingOuter, RingInner, 0f, 0.55f, _fieldColor);
            if (_bead != null) _bead.localScale = Vector3.one * (BeadMaxSize * ReadHarvestFraction());
        }

        private float ReadHarvestFraction()
        {
            if (!_hasEntity || !_world.IsCreated) return 0f;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<CollectorData>(_entity)) return 0f;
            var data = em.GetComponentData<CollectorData>(_entity);
            return Mathf.Clamp01(data.Timer * data.OutputRate);
        }

        void OnDestroy()
        {
            if (_material != null)    Destroy(_material);
            if (_doorMat != null)     Destroy(_doorMat);
            if (_particleMat != null) Destroy(_particleMat);
            if (_mesh != null)        Destroy(_mesh);
            if (_doorMesh != null)    Destroy(_doorMesh);
            _parts.Dispose();
        }
    }
}
