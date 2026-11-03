using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "precision core" structure for the Component Fabricator (1x1, endgame producer): a sleek
    /// tall rounded violet body (<see cref="RoundedBoxMeshBuilder"/> + the
    /// <c>MobileIdleBuilder/CollectorStructure</c> shader) crowned by a slowly rotating halo ring. A
    /// binding glow builds with craft progress; on completion a crystalline component assembles at the
    /// apex, rises, and the body fires the strongest flare of the production chain.
    ///
    /// Selected data-drivenly by <see cref="BuildingStructureKind.ComponentFabricator"/>; door fixtures
    /// drawn by <see cref="BuildingVisualizer"/>; production ticks are a DROP in
    /// <see cref="RecipeProcessData.Progress"/> (reset to 0 on completion).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ComponentFabricatorStructure : MonoBehaviour
    {
        private const int   LongitudeSegments = 28;
        private const int   LatitudeSegments  = 14;

        private const float BodyHalfExtent = 0.34f;
        private const float BodyHeight     = 0.98f;
        private const float BodyRoundness  = 0.5f;

        private const float InitialPulse    = 0.9f; // strongest flare of the chain
        private const float DecayRate        = 3.5f;
        private const float SettleThreshold = 0.001f;
        private const float ChargeGlowMax   = 0.35f;

        private const float HaloRadius   = 0.42f;
        private const float HaloHeight   = 0.78f;
        private const float HaloSpinSpeed = 55f;

        private const float ComponentDuration = 1.2f;
        private const float ComponentRise     = 0.32f;
        private const float ComponentScale    = 0.16f;

        private const float BobSpeed = 1.0f;

        private static readonly Color BodyColor      = new Color(0.6f, 0.4f, 0.95f); // violet
        private static readonly Color CrownColor     = new Color(0.95f, 0.9f, 1.0f); // bright crown
        private static readonly Color HaloColor      = new Color(0.8f, 0.7f, 1.0f);
        private static readonly Color ComponentColor = new Color(0.95f, 0.92f, 1.0f);

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");

        private Material  _bodyMat;
        private Material  _haloMat;
        private Material  _componentMat;
        private Mesh      _bodyMesh;
        private Mesh      _haloMesh;
        private Transform _halo;
        private Transform _component;

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private float   _lastProgress = -1f;
        private float   _pulseElapsed     = Mathf.Infinity;
        private float   _componentElapsed = Mathf.Infinity;
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
            BuildHaloAndComponent();
        }

        private void BuildBody()
        {
            _bodyMesh = RoundedBoxMeshBuilder.Build(LongitudeSegments, LatitudeSegments,
                new RoundedBoxMeshBuilder.RoundedBoxProfile
                {
                    HalfWidth = BodyHalfExtent,
                    HalfDepth = BodyHalfExtent,
                    Height    = BodyHeight,
                    Roundness = BodyRoundness
                });
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[ComponentFabricatorStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, BodyColor);
            _bodyMat.SetColor(TipColorId, CrownColor);
            renderer.material = _bodyMat;
        }

        private void BuildHaloAndComponent()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return;

            _haloMesh = TorusMeshBuilder.Build(40, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = HaloRadius, MinorRadius = 0.018f });
            _haloMat = new Material(opaque);
            _haloMat.SetColor(BaseColorId, HaloColor);

            var halo = new GameObject("Halo");
            halo.transform.SetParent(transform, worldPositionStays: false);
            halo.transform.localPosition = new Vector3(0f, HaloHeight, 0f);
            halo.transform.localRotation = Quaternion.Euler(18f, 0f, 0f); // slight tilt
            halo.AddComponent<MeshFilter>().sharedMesh = _haloMesh;
            var hmr = halo.AddComponent<MeshRenderer>();
            hmr.sharedMaterial    = _haloMat;
            hmr.shadowCastingMode = ShadowCastingMode.Off;
            hmr.receiveShadows    = false;
            hmr.lightProbeUsage   = LightProbeUsage.Off;
            _halo = halo.transform;

            var comp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            comp.name = "Component";
            var col = comp.GetComponent<Collider>();
            if (col != null) Destroy(col);
            comp.transform.SetParent(transform, worldPositionStays: false);
            comp.transform.localPosition = new Vector3(0f, BodyHeight, 0f);
            comp.transform.localScale    = Vector3.one * ComponentScale;
            comp.transform.localRotation = Quaternion.Euler(45f, 45f, 0f); // crystalline read
            _componentMat = new Material(opaque);
            _componentMat.SetColor(BaseColorId, ComponentColor);
            var cmr = comp.GetComponent<MeshRenderer>();
            cmr.sharedMaterial    = _componentMat;
            cmr.shadowCastingMode = ShadowCastingMode.Off;
            cmr.receiveShadows    = false;
            cmr.lightProbeUsage   = LightProbeUsage.Off;
            _component = comp.transform;
            _component.gameObject.SetActive(false);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float t  = Time.time;
            float cycle = ReadCycleFraction();
            PollProductionTick();

            if (_halo != null) _halo.Rotate(0f, HaloSpinSpeed * dt, 0f, Space.Self);

            float flare = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                flare = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (flare <= SettleThreshold) { flare = 0f; _pulseElapsed = Mathf.Infinity; }
            }
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, Mathf.Max(flare, cycle * ChargeGlowMax));

            AnimateComponent(dt);

            float bobY = Mathf.Sin(t * BobSpeed) * 0.02f * _baseScale.y;
            transform.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
        }

        private void AnimateComponent(float dt)
        {
            if (_component == null || float.IsInfinity(_componentElapsed)) return;

            _componentElapsed += dt;
            float f = _componentElapsed / ComponentDuration;
            if (f >= 1f)
            {
                _component.gameObject.SetActive(false);
                _componentElapsed = Mathf.Infinity;
                return;
            }

            _component.localPosition = new Vector3(0f, BodyHeight + ComponentRise * f, 0f);
            _component.Rotate(0f, 180f * dt, 0f, Space.Self);
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
            {
                _pulseElapsed     = 0f;
                _componentElapsed = 0f;
                if (_component != null) _component.gameObject.SetActive(true);
            }
            _lastProgress = progress;
        }

        void OnDestroy()
        {
            if (_bodyMat != null)      Destroy(_bodyMat);
            if (_haloMat != null)      Destroy(_haloMat);
            if (_componentMat != null) Destroy(_componentMat);
            if (_bodyMesh != null)     Destroy(_bodyMesh);
            if (_haloMesh != null)     Destroy(_haloMesh);
        }
    }
}
