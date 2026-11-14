using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "Quark confinement": the Strong Force Combiner (2x1). Above a low rounded housing, three quarks in the
    /// three colour charges circle each other, joined by flux tubes. As a craft progresses they spin faster
    /// and the tubes pull tighter, then they snap together into a flash of white as a nucleon forms.
    ///
    /// The housing (<see cref="RoundedBoxMeshBuilder"/>) is on the host (uniform cell scale) so it keeps the
    /// power/hover tints, and is tall enough for the door fixtures <see cref="BuildingVisualizer"/> draws on
    /// its faces. Selected by <see cref="BuildingStructureKind.StrongForceCombiner"/>; a production tick is a
    /// DROP in <see cref="RecipeProcessData.Progress"/> (reset to 0 on completion).
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class StrongForceCombinerStructure : MonoBehaviour
    {
        private const int   LongitudeSegments = 32; // multiple of 4 -> verts land on face centres
        private const int   LatitudeSegments  = 12;

        private const float HousingHeight   = 0.8f;
        private const float FootprintMargin = 0.06f; // shrink from the footprint edge so tiles don't touch
        private const float Roundness       = 0.5f;

        private const float TriadY      = 1.14f;
        private const float QuarkSize   = 0.21f;
        private const float RestRadius  = 0.27f;  // quarks' distance from the centre, idle
        private const float TightRadius = 0.13f;  // ...at full craft progress
        private const float RestSpin    = 35f;    // deg/s
        private const float TightSpin   = 190f;
        private const float RestTube    = 0.035f; // flux tube diameter, idle
        private const float TightTube   = 0.07f;

        private const float InitialPulse    = 1f;
        private const float DecayRate       = 3.5f;
        private const float SettleThreshold = 0.001f;
        private const float NucleonSize     = 0.5f;  // flash diameter at the flare's peak

        private static readonly Color[] QuarkColors =
        {
            new Color(1.00f, 0.28f, 0.28f),
            new Color(0.30f, 0.92f, 0.42f),
            new Color(0.34f, 0.52f, 1.00f),
        };
        private static readonly Color TubeColor    = new Color(1.0f, 0.85f, 0.45f);
        private static readonly Color HousingGlow  = new Color(1.0f, 0.9f, 0.7f);
        private static readonly Color NucleonColor = new Color(1.0f, 0.97f, 0.88f);

        private static readonly int PulseAmpId = Shader.PropertyToID("_PulseAmp");

        private readonly StructureParts _parts = new();
        private readonly Transform[] _quarks = new Transform[3];
        private readonly Transform[] _tubes  = new Transform[3];
        private Transform _triad;
        private Transform _nucleon;
        private Material  _housingMat;
        private float     _spinAngle;

        private World   _world;
        private Entity  _entity;
        private bool    _hasEntity;
        private float   _lastProgress = -1f;
        private float   _pulseElapsed = Mathf.Infinity;

        /// <summary>
        /// Builds the housing sized to the (rotation-applied) footprint <paramref name="fw"/> x
        /// <paramref name="fh"/> cells, then the quark triad. The host is at the footprint centre, uniform cell scale.
        /// </summary>
        public void Initialize(Entity entity, int fw, int fh, Color housingColor)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            BuildHousing(fw, fh, housingColor);
            BuildTriad();
        }

        private void BuildHousing(int fw, int fh, Color color)
        {
            var mesh = _parts.Own(RoundedBoxMeshBuilder.Build(LongitudeSegments, LatitudeSegments,
                new RoundedBoxMeshBuilder.RoundedBoxProfile
                {
                    HalfWidth = Mathf.Max(0.1f, fw * 0.5f - FootprintMargin),
                    HalfDepth = Mathf.Max(0.1f, fh * 0.5f - FootprintMargin),
                    Height    = HousingHeight,
                    Roundness = Roundness
                }));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = GetComponent<MeshRenderer>();
            StructureParts.Plain(renderer);
            _housingMat = _parts.Shaded(color, HousingGlow, 0.5f);
            renderer.sharedMaterial = _housingMat;
        }

        private void BuildTriad()
        {
            var pivot = new GameObject("Triad");
            pivot.transform.SetParent(transform, worldPositionStays: false);
            pivot.transform.localPosition = new Vector3(0f, TriadY, 0f);
            _triad = pivot.transform;

            var tubeMat = _parts.Flat(TubeColor);
            if (tubeMat == null) return; // no URP material available; the housing still renders

            for (int i = 0; i < 3; i++)
                _tubes[i] = StructureParts.AddPrimitive(PrimitiveType.Cylinder, $"FluxTube_{i}", _triad, tubeMat, Vector3.zero, Vector3.one);
            for (int i = 0; i < 3; i++)
                _quarks[i] = StructureParts.AddPrimitive(PrimitiveType.Sphere, $"Quark_{i}", _triad,
                    _parts.Shaded(QuarkColors[i], Color.white, 0.8f), Vector3.zero, Vector3.one * QuarkSize);

            _nucleon = StructureParts.AddPrimitive(PrimitiveType.Sphere, "Nucleon", _triad, _parts.Flat(NucleonColor), Vector3.zero, Vector3.zero);
        }

        void Update()
        {
            float cycle = ReadCycleAndPollTick();

            float flare = 0f;
            if (!float.IsInfinity(_pulseElapsed))
            {
                _pulseElapsed += Time.deltaTime;
                flare = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
                if (flare <= SettleThreshold) { flare = 0f; _pulseElapsed = Mathf.Infinity; }
            }

            if (_housingMat != null) _housingMat.SetFloat(PulseAmpId, Mathf.Max(flare * 0.6f, cycle * 0.2f));
            AnimateTriad(cycle, flare);
        }

        private void AnimateTriad(float cycle, float flare)
        {
            if (_quarks[0] == null) return;

            _spinAngle += Mathf.Lerp(RestSpin, TightSpin, cycle) * Time.deltaTime;
            _triad.localRotation = Quaternion.Euler(14f, _spinAngle, 0f);

            // A completed craft resets progress, so the flare holds the quarks tight until it fades.
            float tension = Mathf.Max(cycle, flare);
            float radius  = Mathf.Lerp(RestRadius, TightRadius, tension);
            float tube    = Mathf.Lerp(RestTube, TightTube, tension);

            for (int i = 0; i < 3; i++)
            {
                float a = 2f * Mathf.PI * i / 3f;
                _quarks[i].localPosition = new Vector3(radius * Mathf.Cos(a), 0f, radius * Mathf.Sin(a));
            }
            for (int i = 0; i < 3; i++)
                StructureParts.StretchBetween(_tubes[i], _quarks[i].localPosition, _quarks[(i + 1) % 3].localPosition, tube);

            if (_nucleon != null) _nucleon.localScale = Vector3.one * (NucleonSize * flare);
        }

        private float ReadCycleAndPollTick()
        {
            if (!_hasEntity || !_world.IsCreated) return 0f;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<RecipeProcessData>(_entity)) return 0f;

            var process = em.GetComponentData<RecipeProcessData>(_entity);
            if (_lastProgress >= 0f && process.Progress < _lastProgress - 0.0001f) _pulseElapsed = 0f;
            _lastProgress = process.Progress;
            return process.CraftTime > 0f ? Mathf.Clamp01(process.Progress / process.CraftTime) : 0f;
        }

        void OnDestroy() => _parts.Dispose();
    }
}
