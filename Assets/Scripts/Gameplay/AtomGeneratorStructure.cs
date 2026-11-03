using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "orbital nucleus" structure for the Atom Generator building: a round glowing nucleus
    /// (<see cref="AtomNucleusMeshBuilder"/>) circled by tilted electron orbit rings, each carrying a
    /// bright electron that travels around it. Replaces the placeholder cube for any building whose
    /// <see cref="BuildingVisualStyle"/> declares <see cref="BuildingStructureKind.AtomGenerator"/>.
    ///
    /// Mirrors <see cref="CollectorStructure"/>'s house pattern: the body is shaded by the
    /// <c>MobileIdleBuilder/AtomGenerator</c> shader; the breathe/pulse motion and the orbit spin live on
    /// the CPU (so children inherit the surface motion). A production tick — detected as a frame-over-frame
    /// DROP in <see cref="RecipeProcessData.Progress"/> (ProductionSystem resets it to 0 on completion) —
    /// fires a decaying <c>_PulseAmp</c> flare (see <see cref="WireBounce"/>). The input/output holes are
    /// drawn separately by <see cref="BuildingVisualizer"/> at the building's port edges.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class AtomGeneratorStructure : MonoBehaviour
    {
        private const int   RadialSegments  = 28;
        private const int   HeightSegments  = 18;
        private const float InitialPulse    = 0.6f;
        private const float DecayRate       = 4f;
        private const float SettleThreshold = 0.001f;

        private const float BobSpeed    = 1.0f;
        private const float BobAmp      = 0.04f;
        private const float BreatheAmp  = 0.03f;
        private const float PulsePuff   = 0.5f;
        private const float PulseLift   = 0.12f;

        private static readonly Color NucleusColor  = new Color(1.0f, 0.66f, 0.22f); // warm gold nucleus
        private static readonly Color ElectronColor = new Color(0.6f, 0.85f, 1.0f);  // pale blue electron
        private static readonly Color OrbitColor    = new Color(0.35f, 0.6f, 0.95f); // dim blue orbit ring

        // Each orbit ring: a tilt (Euler) and an angular speed (deg/s). The torus is symmetric about its
        // own normal, so spinning the whole pivot about its local Y leaves the ring looking fixed while
        // the attached electron travels around it.
        private static readonly (Vector3 tilt, float speed)[] Orbits =
        {
            (new Vector3(72f,  0f,  0f),   70f),
            (new Vector3( 0f,  0f, 72f),  -96f),
            (new Vector3(42f,  0f, -54f), 120f),
        };

        private const float OrbitMajorRadius = 0.52f;
        private const float OrbitMinorRadius = 0.012f;
        private const float ElectronScale    = 0.07f;

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

        private Material   _bodyMat;
        private Material   _orbitMat;
        private Material   _electronMat;
        private Mesh       _bodyMesh;
        private Mesh       _orbitMesh;
        private readonly Transform[] _orbitPivots = new Transform[Orbits.Length];

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private float   _lastProgress = -1f;
        private float   _pulseElapsed = Mathf.Infinity;
        private Vector3 _baseLocalPos;
        private Vector3 _baseScale = Vector3.one;

        /// <summary>
        /// Builds the nucleus + electron orbits onto the host and binds the entity so the structure can
        /// flare on each production tick. Call once after the host has been positioned and scaled.
        /// </summary>
        public void Initialize(Entity entity)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            _baseLocalPos = transform.localPosition;
            _baseScale    = transform.localScale;

            BuildBody();
            BuildOrbits();
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
                GameLogger.Warning("[AtomGeneratorStructure] Shader 'MobileIdleBuilder/AtomGenerator' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, NucleusColor);                                 // fallback; PresenceReceiver MPB overrides per-frame
            _bodyMat.SetColor(CoreColorId, Color.Lerp(NucleusColor, Color.white, 0.5f));  // hot core
            renderer.material = _bodyMat; // instance; PresenceReceiver's _BaseColor MPB still applies
        }

        private void BuildOrbits()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return; // no URP material available — skip orbits (body still renders)

            _orbitMesh   = TorusMeshBuilder.Build(40, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = OrbitMajorRadius, MinorRadius = OrbitMinorRadius });
            _orbitMat    = new Material(opaque);
            _orbitMat.SetColor(BaseColorId, OrbitColor);
            _electronMat = new Material(opaque);
            _electronMat.SetColor(BaseColorId, ElectronColor);

            float nucleusY = AtomNucleusMeshBuilder.NucleusProfile.Default.Height * 0.5f;

            for (int i = 0; i < Orbits.Length; i++)
            {
                var pivot = new GameObject($"Orbit_{i}");
                pivot.transform.SetParent(transform, worldPositionStays: false);
                pivot.transform.localPosition = new Vector3(0f, nucleusY, 0f);
                pivot.transform.localRotation = Quaternion.Euler(Orbits[i].tilt);
                _orbitPivots[i] = pivot.transform;

                AddRenderer("Ring", pivot.transform, _orbitMesh, _orbitMat, Vector3.zero, Vector3.one);

                var electron = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                electron.name = "Electron";
                var col = electron.GetComponent<Collider>();
                if (col != null) Destroy(col); // visual only — never block raycasts/placement
                electron.transform.SetParent(pivot.transform, worldPositionStays: false);
                electron.transform.localPosition = new Vector3(OrbitMajorRadius, 0f, 0f);
                electron.transform.localScale    = Vector3.one * ElectronScale;
                var emr = electron.GetComponent<MeshRenderer>();
                emr.sharedMaterial      = _electronMat;
                emr.shadowCastingMode   = ShadowCastingMode.Off;
                emr.receiveShadows      = false;
                emr.lightProbeUsage     = LightProbeUsage.Off;
            }
        }

        private static void AddRenderer(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Vector3 localScale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localScale    = localScale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial    = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = LightProbeUsage.Off;
        }

        void Update()
        {
            PollProductionTick();
            AnimateOrbits();
            AnimateTransform();
        }

        // ProductionSystem resets RecipeProcessData.Progress to 0 on completion, so a frame-over-frame
        // DROP means an atom was just assembled — fire the nucleus flare.
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

        private void AnimateOrbits()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < _orbitPivots.Length; i++)
                if (_orbitPivots[i] != null)
                    _orbitPivots[i].Rotate(0f, Orbits[i].speed * dt, 0f, Space.Self);
        }

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

            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, pulse);
        }

        void OnDestroy()
        {
            if (_bodyMat != null)     Destroy(_bodyMat);
            if (_orbitMat != null)    Destroy(_orbitMat);
            if (_electronMat != null) Destroy(_electronMat);
            if (_bodyMesh != null)    Destroy(_bodyMesh);
            if (_orbitMesh != null)   Destroy(_orbitMesh);
        }
    }
}
