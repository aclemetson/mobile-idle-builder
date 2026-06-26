using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "spindle" structure for field-collector buildings: a round body that tapers to a
    /// single sharp apex (a singularity point), rounded on every side except a flat <b>front</b>
    /// facade (local +Z) that carries a solid black "doorway" (rectangle with an arched top) reading
    /// as a hole, with particles streaming out of it on each harvest. The whole structure is tinted to
    /// resemble the field it sits on, and is rotated so the front faces the building's output direction.
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

        // Bob/breathe/pulse motion is applied to the host TRANSFORM (not the shader) so the child door
        // and particles inherit it and stay locked to the animated surface.
        private const float BobSpeed   = 1.0f;
        private const float BobAmp     = 0.05f; // vertical bob (in base-scale units)
        private const float BreatheAmp = 0.03f; // XZ breathing
        private const float PulsePuff  = 0.6f;  // extra XZ scale at a flare peak
        private const float PulseLift  = 0.15f; // extra vertical lift at a flare peak

        private static readonly int PulseAmpId = Shader.PropertyToID("_PulseAmp");
        private static readonly int TipColorId = Shader.PropertyToID("_TipColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Material       _material;
        private Material       _doorMat;
        private Material       _particleMat;
        private Mesh           _mesh;
        private Mesh           _doorMesh;
        private ParticleSystem _particles;

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private int     _currentDir   = -2;             // last applied output direction (-2 == unset)
        private float   _lastTimer     = -1f;           // < 0 == not yet sampled
        private float   _pulseElapsed  = Mathf.Infinity; // Infinity == not pulsing
        private Vector3 _baseLocalPos;                   // host transform captured pre-animation
        private Vector3 _baseScale = Vector3.one;

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
            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;

            BuildBody(fieldColor);
            BuildAperture(fieldColor);
            ApplyOutputDirection(forceWrite: true);
        }

        private void BuildBody(Color fieldColor)
        {
            _mesh = CollectorMeshBuilder.Build(RadialSegments, HeightSegments);
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
            var   prof  = CollectorMeshBuilder.SpindleProfile.Default;
            float flatZ = CollectorMeshBuilder.FrontFlatZ(prof);
            float baseY = prof.Height * 0.13f; // door sits low on the broad part of the flat face

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

        // Bob + pulse-lift on Y, breathe + pulse-puff on XZ — applied to the host transform so the door
        // and particle emitter (children) move with the surface and never detach from it. The flare's
        // emission boost is fed to the shader via _PulseAmp.
        private void AnimateTransform()
        {
            float t = Time.time;

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += Time.deltaTime;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }

            float bobY   = (Mathf.Sin(t * BobSpeed) * BobAmp + pulse * PulseLift) * _baseScale.y;
            float scaleF = 1f + Mathf.Sin(t * BobSpeed) * BreatheAmp + pulse * PulsePuff;

            transform.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
            transform.localScale    = new Vector3(_baseScale.x * scaleF, _baseScale.y, _baseScale.z * scaleF);

            if (_material != null) _material.SetFloat(PulseAmpId, pulse);
        }

        void OnDestroy()
        {
            if (_material != null)    Destroy(_material);
            if (_doorMat != null)     Destroy(_doorMat);
            if (_particleMat != null) Destroy(_particleMat);
            if (_mesh != null)        Destroy(_mesh);
            if (_doorMesh != null)    Destroy(_doorMesh);
        }
    }
}
