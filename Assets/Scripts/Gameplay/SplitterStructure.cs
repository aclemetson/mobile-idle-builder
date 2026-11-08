using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// "A river meeting a rock": the Belt Splitter's structure. The incoming belt's water forks around a
    /// teardrop rock whose sharp point faces upstream (two rocks on a 3-way, leaving a channel between them).
    /// Selected by <see cref="BuildingStructureKind.BeltSplitter"/>. The water shows the flow, so the
    /// building draws no port holes or arrows (<see cref="BuildingVisualizer"/> skips them).
    ///
    /// The rock is the host's mesh, so <see cref="PresenceReceiver"/> keeps driving its tint. It reuses the
    /// registered CollectorStructure shader (no new always-included shader), and the point flares each time
    /// <see cref="SplitterState.NextFacing"/> moves on, which is one item being dealt to a branch.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SplitterStructure : MonoBehaviour
    {
        private const int   LengthSegments = 20;
        private const int   ArchSegments   = 12;
        private const float InitialPulse   = 0.35f;
        private const float DecayRate      = 7f;
        private const float SettleThreshold = 0.001f;

        private static readonly Color TipColor = new Color(0.45f, 0.85f, 1.0f);

        private static readonly int PulseAmpId  = Shader.PropertyToID("_PulseAmp");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TipColorId  = Shader.PropertyToID("_TipColor");
        private static readonly int TipGlowId   = Shader.PropertyToID("_TipGlow");

        private Mesh     _rockMesh;
        private Mesh     _flowMesh;
        private Mesh     _bedMesh;
        private Material _rockMat;

        private World  _world;
        private Entity _entity;
        private bool   _hasEntity;
        private int    _lastFacing = -1;
        private float  _pulseElapsed = Mathf.Infinity;

        /// <summary>
        /// Builds the rock(s) and river. The host must already sit at the cell centre on the ground, at a
        /// uniform scale of one cell. <paramref name="travelDir"/> is the way items arrive moving (the input
        /// port's facing); <paramref name="outputDirs"/> are the output ports' facings.
        /// </summary>
        public void Initialize(Entity entity, int travelDir, IReadOnlyList<int> outputDirs,
                               Color rockColor, Material flowMaterial, Material bedMaterial)
        {
            _entity    = entity;
            _world     = World.DefaultGameObjectInjectionWorld;
            _hasEntity = _world != null && _world.IsCreated;

            // Local +Z becomes the travel direction, which points the rock's tip (local -Z) upstream.
            transform.localRotation = Quaternion.Euler(0f, travelDir * 90f, 0f);

            BuildRock(rockColor, outputDirs.Count >= 3 ? TeardropMeshBuilder.ThreeWay : TeardropMeshBuilder.TwoWay);
            BuildRiver(travelDir, outputDirs, flowMaterial, bedMaterial);
        }

        private void BuildRock(Color rockColor, IReadOnlyList<TeardropMeshBuilder.RockProfile> rocks)
        {
            _rockMesh = TeardropMeshBuilder.Build(LengthSegments, ArchSegments, rocks);
            GetComponent<MeshFilter>().sharedMesh = _rockMesh;

            var renderer = GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;

            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null)
            {
                GameLogger.Warning("[SplitterStructure] Shader 'MobileIdleBuilder/CollectorStructure' not found; the rock keeps the placeholder material.");
                return;
            }

            _rockMat = new Material(shader);
            _rockMat.SetFloat(PulseAmpId, 0f);
            _rockMat.SetColor(BaseColorId, rockColor); // fallback; PresenceReceiver's MPB overrides it
            _rockMat.SetColor(TipColorId, TipColor);
            _rockMat.SetFloat(TipGlowId, 0.9f);
            renderer.material = _rockMat;
        }

        private void BuildRiver(int travelDir, IReadOnlyList<int> outputDirs, Material flowMaterial, Material bedMaterial)
        {
            float cell = transform.localScale.x;
            int inEdge = (travelDir + 2) % 4;

            // Same world-unit meshes as the neighbouring belts, so undo the host's rotation and scale.
            var river = new GameObject("River");
            river.transform.SetParent(transform, worldPositionStays: false);
            river.transform.localRotation = Quaternion.Inverse(transform.localRotation);
            river.transform.localScale    = Vector3.one / cell;

            _bedMesh  = ConveyorFlowMeshBuilder.BuildFork(cell, inEdge, outputDirs,
                ConveyorFlowMeshBuilder.FloorY, ConveyorFlowMeshBuilder.FloorWidthFrac);
            _flowMesh = ConveyorFlowMeshBuilder.BuildFork(cell, inEdge, outputDirs);

            AddLayer("Channel", river.transform, _bedMesh, bedMaterial);
            AddLayer("Flow", river.transform, _flowMesh, flowMaterial);
        }

        private static void AddLayer(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial    = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            mr.lightProbeUsage   = LightProbeUsage.Off;
        }

        void Update()
        {
            PollDeal();
            if (_rockMat == null || float.IsInfinity(_pulseElapsed)) return;

            _pulseElapsed += Time.deltaTime;
            float pulse = WireBounce.Amplitude(_pulseElapsed, InitialPulse, DecayRate);
            if (pulse <= SettleThreshold) { pulse = 0f; _pulseElapsed = Mathf.Infinity; }
            _rockMat.SetFloat(PulseAmpId, pulse);
        }

        // ConveyorSystem moves NextFacing on after dealing an item, so any change means one just left.
        private void PollDeal()
        {
            if (!_hasEntity || !_world.IsCreated) return;
            var em = _world.EntityManager;
            if (!em.Exists(_entity) || !em.HasComponent<SplitterState>(_entity)) return;

            int facing = em.GetComponentData<SplitterState>(_entity).NextFacing;
            if (_lastFacing >= 0 && facing != _lastFacing) _pulseElapsed = 0f;
            _lastFacing = facing;
        }

        void OnDestroy()
        {
            if (_rockMat  != null) Destroy(_rockMat);
            if (_rockMesh != null) Destroy(_rockMesh);
            if (_flowMesh != null) Destroy(_flowMesh);
            if (_bedMesh  != null) Destroy(_bedMesh);
        }
    }
}
