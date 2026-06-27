using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural "crucible" structure for the Materials Forge (1x1): a solid rounded furnace block
    /// (<see cref="RoundedBoxMeshBuilder"/> + the <c>MobileIdleBuilder/CollectorStructure</c> shader) with
    /// a glowing molten pool on top. On each forge a white-hot ingot rises from the pool, cools to steel,
    /// and the body flares. Steel body with an orange heat glow; door fixtures drawn by
    /// <see cref="BuildingVisualizer"/>.
    ///
    /// Selected data-drivenly by <see cref="BuildingStructureKind.MaterialsForge"/>; production ticks are a
    /// DROP in <see cref="RecipeProcessData.Progress"/> (reset to 0 on completion).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MaterialsForgeStructure : MonoBehaviour
    {
        private const int   LongitudeSegments = 28;
        private const int   LatitudeSegments  = 12;

        private const float BlockHalfExtent = 0.42f;
        private const float BlockHeight     = 0.58f;
        private const float BlockRoundness  = 0.45f;

        private const float InitialPulse    = 0.6f;
        private const float DecayRate        = 3.5f;
        private const float SettleThreshold = 0.001f;

        private const float IngotDuration = 1.1f;  // rise + cool time
        private const float IngotRise     = 0.35f;
        private const float IngotScale    = 0.17f;

        private const float BobSpeed = 0.8f;

        private static readonly Color BodyColor   = new Color(0.45f, 0.45f, 0.48f); // steel
        private static readonly Color HeatColor   = new Color(1.0f, 0.5f, 0.15f);   // orange heat crown
        private static readonly Color MoltenColor = new Color(1.0f, 0.45f, 0.1f);   // molten pool
        private static readonly Color IngotHot    = new Color(1.0f, 0.95f, 0.85f);  // white-hot
        private static readonly Color IngotCool   = new Color(0.6f, 0.62f, 0.68f);  // cooled steel

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");

        private Material  _bodyMat;
        private Material  _moltenMat;
        private Material  _ingotMat;
        private Mesh      _bodyMesh;
        private Transform _molten;
        private Transform _ingot;
        private float     _moltenBaseY;

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private float   _lastProgress = -1f;
        private float   _pulseElapsed = Mathf.Infinity;
        private float   _ingotElapsed = Mathf.Infinity;
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
            BuildMoltenAndIngot();
        }

        private void BuildBody()
        {
            _bodyMesh = RoundedBoxMeshBuilder.Build(LongitudeSegments, LatitudeSegments,
                new RoundedBoxMeshBuilder.RoundedBoxProfile
                {
                    HalfWidth = BlockHalfExtent,
                    HalfDepth = BlockHalfExtent,
                    Height    = BlockHeight,
                    Roundness = BlockRoundness
                });
            GetComponent<MeshFilter>().sharedMesh = _bodyMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[MaterialsForgeStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found — keeps placeholder mesh.");
                return;
            }

            _bodyMat = new Material(shader);
            _bodyMat.SetFloat(PulseAmpId, 0f);
            _bodyMat.SetColor(BaseColorId, BodyColor);
            _bodyMat.SetColor(TipColorId, HeatColor);
            renderer.material = _bodyMat;
        }

        private void BuildMoltenAndIngot()
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return;

            _moltenBaseY = BlockHeight * 0.86f;

            // Molten pool — a flattened lens sitting in the top of the block.
            var pool = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            pool.name = "MoltenPool";
            var pcol = pool.GetComponent<Collider>();
            if (pcol != null) Destroy(pcol);
            pool.transform.SetParent(transform, worldPositionStays: false);
            pool.transform.localPosition = new Vector3(0f, _moltenBaseY, 0f);
            pool.transform.localScale    = new Vector3(0.5f, 0.09f, 0.5f);
            _moltenMat = new Material(opaque);
            _moltenMat.SetColor(BaseColorId, MoltenColor);
            var pmr = pool.GetComponent<MeshRenderer>();
            pmr.sharedMaterial    = _moltenMat;
            pmr.shadowCastingMode = ShadowCastingMode.Off;
            pmr.receiveShadows    = false;
            pmr.lightProbeUsage   = LightProbeUsage.Off;
            _molten = pool.transform;

            // Ingot — hidden until a forge completes, then rises and cools.
            var ingot = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ingot.name = "Ingot";
            var icol = ingot.GetComponent<Collider>();
            if (icol != null) Destroy(icol);
            ingot.transform.SetParent(transform, worldPositionStays: false);
            ingot.transform.localPosition = new Vector3(0f, _moltenBaseY, 0f);
            ingot.transform.localScale    = new Vector3(IngotScale, IngotScale * 0.6f, IngotScale);
            _ingotMat = new Material(opaque);
            _ingotMat.SetColor(BaseColorId, IngotHot);
            var imr = ingot.GetComponent<MeshRenderer>();
            imr.sharedMaterial    = _ingotMat;
            imr.shadowCastingMode = ShadowCastingMode.Off;
            imr.receiveShadows    = false;
            imr.lightProbeUsage   = LightProbeUsage.Off;
            _ingot = ingot.transform;
            _ingot.gameObject.SetActive(false);
        }

        void Update()
        {
            PollProductionTick();

            float dt = Time.deltaTime;
            float t  = Time.time;

            float pulse = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += dt;
                pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            }
            if (_bodyMat != null) _bodyMat.SetFloat(PulseAmpId, pulse);

            // Molten pool shimmers, brightening on a forge.
            if (_moltenMat != null)
            {
                float shimmer = 0.85f + 0.15f * Mathf.Sin(t * 3f) + pulse;
                _moltenMat.SetColor(BaseColorId, MoltenColor * shimmer);
            }

            AnimateIngot(dt);

            // Subtle bob.
            float bobY = Mathf.Sin(t * BobSpeed) * 0.02f * _baseScale.y;
            transform.localPosition = _baseLocalPos + new Vector3(0f, bobY, 0f);
        }

        private void AnimateIngot(float dt)
        {
            if (_ingot == null) return;
            if (float.IsInfinity(_ingotElapsed)) return;

            _ingotElapsed += dt;
            float f = _ingotElapsed / IngotDuration;
            if (f >= 1f)
            {
                _ingot.gameObject.SetActive(false);
                _ingotElapsed = Mathf.Infinity;
                return;
            }

            _ingot.localPosition = new Vector3(0f, _moltenBaseY + IngotRise * f, 0f);
            if (_ingotMat != null)
                _ingotMat.SetColor(BaseColorId, Color.Lerp(IngotHot, IngotCool, f));
        }

        private void PollProductionTick()
        {
            if (!_hasEntity || !_world.IsCreated) return;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<RecipeProcessData>(_entity)) return;

            float progress = em.GetComponentData<RecipeProcessData>(_entity).Progress;
            if (_lastProgress >= 0f && progress < _lastProgress - 0.0001f)
            {
                _pulseElapsed = 0f;
                _ingotElapsed = 0f;
                if (_ingot != null) _ingot.gameObject.SetActive(true);
            }
            _lastProgress = progress;
        }

        void OnDestroy()
        {
            if (_bodyMat != null)   Destroy(_bodyMat);
            if (_moltenMat != null) Destroy(_moltenMat);
            if (_ingotMat != null)  Destroy(_ingotMat);
            if (_bodyMesh != null)  Destroy(_bodyMesh);
        }
    }
}
