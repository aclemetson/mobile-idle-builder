using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "breathing nucleus" structure for the Isotopic Manipulator building — a sibling of the
    /// Atom Generator (same <see cref="AtomNucleusMeshBuilder"/> body + <c>MobileIdleBuilder/AtomGenerator</c>
    /// shader) distinguished by its motion: a slow, heavy breathing swell (it is slower by design) and a
    /// single slow equatorial ring whose grey neutrons drift radially IN and OUT of the nucleus rather
    /// than orbiting fast. Green-tinged to read as "isotopes / radioactivity-adjacent".
    ///
    /// Selected data-drivenly by <see cref="BuildingStructureKind.IsotopicManipulator"/>; its input/output
    /// holes are drawn by <see cref="BuildingVisualizer"/> at the port edges. A production tick is detected
    /// as a frame-over-frame DROP in <see cref="RecipeProcessData.Progress"/> (reset to 0 on completion).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class IsotopicManipulatorStructure : MonoBehaviour
    {
        private const int   RadialSegments  = 28;
        private const int   HeightSegments  = 18;
        private const float InitialPulse    = 0.45f;
        private const float DecayRate       = 3.5f;
        private const float SettleThreshold = 0.001f;

        // Deliberately slower + heavier than the Atom Generator's breathing.
        private const float BobSpeed   = 0.6f;
        private const float BobAmp     = 0.05f;
        private const float BreatheAmp = 0.06f;
        private const float PulsePuff  = 0.4f;
        private const float PulseLift  = 0.1f;

        // Neutron radial drift (in toward the nucleus and back out), the building's signature motion.
        private const float NeutronInnerRadius = 0.30f;
        private const float NeutronOuterRadius = 0.58f;
        private const float NeutronDriftSpeed  = 1.1f;  // radial in/out cycles
        private const float RingSpinSpeed      = 24f;   // slow along-ring drift (deg/s)
        private const float NeutronScale       = 0.08f;
        private const int   NeutronCount       = 2;

        private static readonly Color NucleusColor = new Color(0.5f, 0.85f, 0.55f);  // green-tinged nucleus
        private static readonly Color NeutronColor = new Color(0.72f, 0.72f, 0.74f); // neutral grey neutrons

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

        private Material   _bodyMat;
        private Material   _ringMat;
        private Material   _neutronMat;
        private Mesh       _bodyMesh;
        private Mesh       _ringMesh;
        private Transform  _ringPivot;
        private readonly Transform[] _neutrons = new Transform[NeutronCount];

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
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
            BuildRing();
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
                GameLogger.Warning("[IsotopicManipulatorStructure] Shader 'MobileIdleBuilder/AtomGenerator' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, NucleusColor);
            _bodyMat.SetColor(CoreColorId, Color.Lerp(NucleusColor, Color.white, 0.45f));
            renderer.material = _bodyMat;
        }

        private void BuildRing()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return;

            _ringMesh = TorusMeshBuilder.Build(40, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = NeutronOuterRadius, MinorRadius = 0.01f });
            _ringMat  = new Material(opaque);
            _ringMat.SetColor(BaseColorId, new Color(0.45f, 0.7f, 0.5f));
            _neutronMat = new Material(opaque);
            _neutronMat.SetColor(BaseColorId, NeutronColor);

            float equatorY = AtomNucleusMeshBuilder.NucleusProfile.Default.Height * 0.5f;

            var pivot = new GameObject("NeutronRing");
            pivot.transform.SetParent(transform, worldPositionStays: false);
            pivot.transform.localPosition = new Vector3(0f, equatorY, 0f);
            pivot.transform.localRotation = Quaternion.Euler(12f, 0f, 0f); // slight tilt for depth
            _ringPivot = pivot.transform;

            AddRenderer("Ring", pivot.transform, _ringMesh, _ringMat);

            for (int i = 0; i < NeutronCount; i++)
            {
                float ang = 360f * i / NeutronCount;
                var holder = new GameObject($"Neutron_{i}");
                holder.transform.SetParent(pivot.transform, worldPositionStays: false);
                holder.transform.localRotation = Quaternion.Euler(0f, ang, 0f);

                var neutron = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                neutron.name = "NeutronSphere";
                var col = neutron.GetComponent<Collider>();
                if (col != null) Destroy(col);
                neutron.transform.SetParent(holder.transform, worldPositionStays: false);
                neutron.transform.localPosition = new Vector3(NeutronOuterRadius, 0f, 0f);
                neutron.transform.localScale    = Vector3.one * NeutronScale;
                var nmr = neutron.GetComponent<MeshRenderer>();
                nmr.sharedMaterial    = _neutronMat;
                nmr.shadowCastingMode = ShadowCastingMode.Off;
                nmr.receiveShadows    = false;
                nmr.lightProbeUsage   = LightProbeUsage.Off;
                _neutrons[i] = neutron.transform;
            }
        }

        private static void AddRenderer(string name, Transform parent, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
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
            AnimateRing();
            AnimateTransform();
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

        private void AnimateRing()
        {
            float t  = Time.time;
            float dt = Time.deltaTime;

            if (_ringPivot != null)
                _ringPivot.Rotate(0f, RingSpinSpeed * dt, 0f, Space.Self);

            for (int i = 0; i < _neutrons.Length; i++)
            {
                if (_neutrons[i] == null) continue;
                // Radial in/out drift; the two neutrons breathe in opposite phase.
                float phase  = i * Mathf.PI;
                float k      = 0.5f + 0.5f * Mathf.Sin(t * NeutronDriftSpeed + phase);
                float radius = Mathf.Lerp(NeutronInnerRadius, NeutronOuterRadius, k);
                var p = _neutrons[i].localPosition;
                p.x = radius;
                _neutrons[i].localPosition = p;
            }
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
            if (_bodyMat != null)    Destroy(_bodyMat);
            if (_ringMat != null)    Destroy(_ringMat);
            if (_neutronMat != null) Destroy(_neutronMat);
            if (_bodyMesh != null)   Destroy(_bodyMesh);
            if (_ringMesh != null)   Destroy(_ringMesh);
        }
    }
}
