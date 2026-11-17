using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "A star in a magnetic bottle": the Fusion Reactor. A ring of plasma circles inside field coils, and a
    /// small star above it swells with craft progress, then flares when two nuclei fuse. Selected by
    /// <see cref="BuildingStructureKind.FusionReactor"/>. The low plinth is the host's mesh, so it takes the
    /// power/hover tints. Reuses the registered EntropySinkTorus shader for the plasma's moving streaks and the
    /// AtomGenerator shader for the star, so it needs no new always-included shader.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class FusionReactorStructure : MonoBehaviour
    {
        private const float RingRadius = 0.29f;
        private const float TubeRadius = 0.078f;
        private const float RingY      = 0.2f;
        private const int   CoilCount  = 6;
        private const float PlasmaSpin = 24f;  // deg/s

        private const float StarY       = 0.42f; // centre of the star above the ring's hole
        private const float StarMinSize = 0.22f;
        private const float StarMaxSize = 0.36f;

        private const float InitialPulse    = 0.8f;
        private const float DecayRate       = 4f;
        private const float SettleThreshold = 0.001f;

        private const int NoisePattern = 4; // EntropySinkTorus's drifting noise reads as hot streaks of gas

        private static readonly Color PlinthColor  = new Color(0.20f, 0.22f, 0.30f);
        private static readonly Color PlasmaDeep   = new Color(0.78f, 0.12f, 0.44f);
        private static readonly Color PlasmaHot    = new Color(1.00f, 0.62f, 0.80f);
        private static readonly Color CoilColor    = new Color(0.55f, 0.60f, 0.70f);
        private static readonly Color StarColor    = new Color(1.00f, 0.72f, 0.28f);

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

        private readonly StructureParts _parts = new();
        private Transform _plasma;
        private Transform _star;
        private Material  _starMat;

        private World  _world;
        private Entity _entity;
        private bool   _hasEntity;
        private float  _lastProgress = -1f;
        private float  _pulseElapsed = Mathf.Infinity;

        public void Initialize(Entity entity, Color plinthColor)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            BuildPlinth(plinthColor);
            BuildPlasma();
            BuildCoils();
            BuildStar();
        }

        private void BuildPlinth(Color color)
        {
            var mesh = _parts.Own(RevolveMeshBuilder.Build(new[]
            {
                new Vector2(0.47f, 0f), new Vector2(0.47f, 0.03f), new Vector2(0.42f, 0.085f), new Vector2(0f, 0.085f),
            }, 36));
            GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = GetComponent<MeshRenderer>();
            StructureParts.Plain(renderer);
            renderer.sharedMaterial = _parts.Shaded(color, PlinthColor, 0f);
        }

        private void BuildPlasma()
        {
            var mesh = _parts.Own(TorusMeshBuilder.Build(48, 14,
                new TorusMeshBuilder.TorusProfile { MajorRadius = RingRadius, MinorRadius = TubeRadius }));

            var shader = Shader.Find("MobileIdleBuilder/EntropySinkTorus");
            Material material;
            if (shader != null)
            {
                material = _parts.Own(new Material(shader));
                material.SetColor("_BaseColor", PlasmaDeep);
                material.SetColor("_AccentColor", PlasmaHot);
                material.SetFloat("_PatternA", NoisePattern);
                material.SetFloat("_PatternB", NoisePattern);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_SwirlSpeed", 0.5f);
                material.SetFloat("_Emission", 1f);
            }
            else
            {
                GameLogger.Warning("[FusionReactorStructure] Shader 'MobileIdleBuilder/EntropySinkTorus' not found; plasma is a flat colour.");
                material = _parts.Flat(PlasmaDeep);
            }
            _plasma = StructureParts.AddMesh("Plasma", transform, mesh, material, new Vector3(0f, RingY, 0f));
        }

        private void BuildCoils()
        {
            var mesh = _parts.Own(TorusMeshBuilder.Build(28, 6,
                new TorusMeshBuilder.TorusProfile { MajorRadius = TubeRadius + 0.03f, MinorRadius = 0.017f }));
            var material = _parts.Flat(CoilColor);
            if (material == null) return;

            for (int i = 0; i < CoilCount; i++)
            {
                float a = 2f * Mathf.PI * (i + 0.5f) / CoilCount;
                var position = new Vector3(RingRadius * Mathf.Cos(a), RingY, RingRadius * Mathf.Sin(a));
                var coil = StructureParts.AddMesh($"Coil_{i}", transform, mesh, material, position);
                // A torus's axis is local Y; a coil's axis follows the ring at this point.
                coil.localRotation = Quaternion.FromToRotation(Vector3.up, new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)));
            }
        }

        private void BuildStar()
        {
            var mesh = _parts.Own(AtomNucleusMeshBuilder.Build(18, 12));
            var shader = Shader.Find("MobileIdleBuilder/AtomGenerator");
            if (shader != null)
            {
                _starMat = _parts.Own(new Material(shader));
                _starMat.SetColor("_BaseColor", StarColor);
                _starMat.SetColor(CoreColorId, Color.Lerp(StarColor, Color.white, 0.7f));
                _starMat.SetFloat(PulseAmpId, 0f);
            }
            else
            {
                _starMat = _parts.Flat(StarColor);
            }
            _star = StructureParts.AddMesh("Star", transform, mesh, _starMat, new Vector3(0f, StarY, 0f));
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

            if (_plasma != null) _plasma.Rotate(0f, PlasmaSpin * Time.deltaTime, 0f, Space.Self);

            if (_star != null)
            {
                // The nucleus mesh is 0.8 tall with its base at 0, so keep its centre at StarY as it grows.
                float size = Mathf.Lerp(StarMinSize, StarMaxSize, cycle) * (1f + flare * 0.8f);
                _star.localScale    = Vector3.one * size;
                _star.localPosition = new Vector3(0f, StarY - 0.4f * size, 0f);
            }
            if (_starMat != null) _starMat.SetFloat(PulseAmpId, Mathf.Max(flare, cycle * 0.25f));
        }

        // ProductionSystem resets Progress to 0 on completion, so a frame-over-frame drop means a fusion.
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
