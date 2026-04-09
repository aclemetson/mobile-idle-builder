using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Syncs ECS conveyor data to visual GameObjects.
    ///
    /// Belt tracks: flat gray cuboids with an orange directional stripe.
    ///   - Straight piece: single stripe along the travel axis.
    ///   - Turn piece: two shorter stripes (entry face + exit face) forming an L.
    ///
    /// Item visuals: colored spheres that interpolate between a segment's entry and
    /// exit edge positions based on ConveyorItemData.Progress.
    ///
    /// Call Refresh() after placing new segments. Item visuals update every frame in Update().
    /// </summary>
    public class ConveyorVisualizer : MonoBehaviour
    {
        [SerializeField] private GridRenderer gridRenderer;

        private EntityQuery _segmentQuery;
        private EntityQuery _itemQuery;
        private bool        _queriesReady;

        private readonly Dictionary<(int, int), GameObject> _spawnedBelts  = new();
        private readonly Dictionary<Entity, GameObject>      _itemSpheres   = new();

        private static readonly Color BeltBaseColor   = new Color(0.25f, 0.25f, 0.25f);
        private static readonly Color BeltStripeColor = new Color(1f, 0.5f, 0f);

        // ----------------------------------------------------------------
        // Unity lifecycle
        // ----------------------------------------------------------------

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _segmentQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ConveyorSegmentData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _itemQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<ConveyorSegmentData>(),
                ComponentType.ReadOnly<ConveyorItemData>()
            );
            _queriesReady = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _queriesReady)
            {
                _segmentQuery.Dispose();
                _itemQuery.Dispose();
            }

            foreach (var go in _spawnedBelts.Values) if (go) Destroy(go);
            foreach (var go in _itemSpheres.Values)  if (go) Destroy(go);
            _spawnedBelts.Clear();
            _itemSpheres.Clear();
        }

        // ----------------------------------------------------------------
        // Update — item sphere animation
        // ----------------------------------------------------------------

        void Update()
        {
            if (!_queriesReady || _itemQuery.IsEmpty) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em       = world.EntityManager;
            var entities = _itemQuery.ToEntityArray(Allocator.Temp);
            float cs     = gridRenderer.CellSize;

            // Track which entities we saw this frame to detect removals
            var seen = new HashSet<Entity>();

            for (int i = 0; i < entities.Length; i++)
            {
                var e    = entities[i];
                var seg  = em.GetComponentData<ConveyorSegmentData>(e);
                var item = em.GetComponentData<ConveyorItemData>(e);
                seen.Add(e);

                // Create sphere if not yet spawned
                if (!_itemSpheres.TryGetValue(e, out var sphere))
                {
                    sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    sphere.name = $"ConveyorItem_{e.Index}";
                    sphere.transform.SetParent(gridRenderer.transform);
                    sphere.transform.localScale = Vector3.one * 0.3f;
                    Destroy(sphere.GetComponent<Collider>());

                    var mr = sphere.GetComponent<MeshRenderer>();
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;
                    SetMeshColor(mr, ItemColor(item.ItemID));
                    _itemSpheres[e] = sphere;
                }

                // Animate position: lerp from entry edge to exit edge
                Vector3 cellCenter  = new Vector3(seg.Cell.x * cs, 0f, seg.Cell.y * cs);
                Vector3 entryEdge   = cellCenter + DirOffset(OppositeDir(seg.EntryDir), cs * 0.45f);
                Vector3 exitEdge    = cellCenter + DirOffset(seg.ExitDir, cs * 0.45f);
                float   t           = Mathf.Clamp01(item.Progress);
                Vector3 pos         = Vector3.Lerp(entryEdge, exitEdge, t);
                pos.y               = 0.3f;
                sphere.transform.localPosition = pos;
            }

            entities.Dispose();

            // Remove spheres for entities that no longer have ConveyorItemData
            var toRemove = new List<Entity>();
            foreach (var kvp in _itemSpheres)
            {
                if (!seen.Contains(kvp.Key))
                {
                    if (kvp.Value) Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var e in toRemove) _itemSpheres.Remove(e);
        }

        // ----------------------------------------------------------------
        // Refresh — spawn missing belt track visuals
        // ----------------------------------------------------------------

        /// <summary>Removes the belt track visual at (x, y). Call after destroying the segment entity.</summary>
        public void RemoveBelt(int x, int y)
        {
            var cell = (x, y);
            if (_spawnedBelts.TryGetValue(cell, out var go))
            {
                if (go != null) Destroy(go);
                _spawnedBelts.Remove(cell);
            }
        }

        /// <summary>Call after placing new conveyor segments to spawn their visual GameObjects.</summary>
        public void Refresh()
        {
            if (!_queriesReady || _segmentQuery.IsEmpty) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em       = world.EntityManager;
            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            float cs     = gridRenderer.CellSize;

            for (int i = 0; i < entities.Length; i++)
            {
                var seg  = em.GetComponentData<ConveyorSegmentData>(entities[i]);
                var cell = (seg.Cell.x, seg.Cell.y);

                if (_spawnedBelts.ContainsKey(cell)) continue;

                var parent = SpawnBeltTrack(seg.Cell.x, seg.Cell.y, seg.EntryDir, seg.ExitDir, cs);
                _spawnedBelts[cell] = parent;
            }

            entities.Dispose();
        }

        // ----------------------------------------------------------------
        // Belt track construction
        // ----------------------------------------------------------------

        private GameObject SpawnBeltTrack(int x, int y, int entryDir, int exitDir, float cs)
        {
            var root = new GameObject($"Belt_{x}_{y}");
            root.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
            root.transform.localPosition = new Vector3(x * cs, 0f, y * cs);

            // Gray platform
            var platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = "Platform";
            Destroy(platform.GetComponent<Collider>());
            platform.transform.SetParent(root.transform, worldPositionStays: false);
            platform.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            platform.transform.localScale    = new Vector3(cs * 0.8f, 0.1f, cs * 0.8f);
            SetupRenderer(platform, BeltBaseColor);

            bool isStraight = (entryDir == exitDir);

            if (isStraight)
            {
                // Single orange stripe aligned with travel direction
                AddStripe(root.transform, exitDir, cs, full: true);
            }
            else
            {
                // Turn piece: two shorter stripes
                int incomingFace = OppositeDir(entryDir); // direction items come from
                AddStripe(root.transform, incomingFace, cs, full: false);
                AddStripe(root.transform, exitDir, cs, full: false);
            }

            return root;
        }

        private void AddStripe(Transform parent, int dir, float cs, bool full)
        {
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "Stripe";
            Destroy(stripe.GetComponent<Collider>());
            stripe.transform.SetParent(parent, worldPositionStays: false);

            float length = full ? cs * 0.6f : cs * 0.35f;
            float width  = cs * 0.15f;
            float height = 0.02f;

            // Stripe sits slightly above the platform
            Vector3 offset = DirOffset(dir, cs * (full ? 0f : 0.2f));
            stripe.transform.localPosition = new Vector3(offset.x, 0.11f, offset.z);

            // Orient along the direction axis
            bool isNS = (dir == (int)OutputDirection.North || dir == (int)OutputDirection.South);
            stripe.transform.localScale = isNS
                ? new Vector3(width, height, length)
                : new Vector3(length, height, width);

            SetupRenderer(stripe, BeltStripeColor);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static void SetupRenderer(GameObject go, Color color)
        {
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            SetMeshColor(mr, color);
        }

        private static void SetMeshColor(MeshRenderer mr, Color color)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }

        private static Vector3 DirOffset(int dir, float magnitude)
        {
            return (OutputDirection)dir switch
            {
                OutputDirection.North => new Vector3(0,    0,  magnitude),
                OutputDirection.East  => new Vector3( magnitude, 0, 0),
                OutputDirection.South => new Vector3(0,    0, -magnitude),
                OutputDirection.West  => new Vector3(-magnitude, 0, 0),
                _                    => Vector3.zero
            };
        }

        private static int OppositeDir(int dir) => (dir + 2) % 4;

        /// <summary>Returns a deterministic bright color based on item ID.</summary>
        private static Color ItemColor(int itemID)
        {
            // Use HSV with fixed saturation/value, vary hue by item ID
            float hue = (itemID * 0.618034f) % 1f; // golden ratio spread
            return Color.HSVToRGB(hue, 0.9f, 1f);
        }
    }
}
