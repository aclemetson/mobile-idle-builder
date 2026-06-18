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

        // Transient single-cell placement preview (not tracked in _spawnedBelts so it never
        // collides with the real Refresh map). Lives only while a single candidate is pending.
        private GameObject _previewBelt;

        private static readonly Color BeltBaseColor   = new Color(0.25f, 0.25f, 0.25f);
        private static readonly Color BeltStripeColor = new Color(1f, 0.5f, 0f);
        private static readonly Color EndCapColor     = Color.yellow;

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
            ClearSinglePreview();
        }

        // ----------------------------------------------------------------
        // Single-cell placement preview
        // ----------------------------------------------------------------

        /// <summary>
        /// Shows a transient belt-track preview at (x, y) facing <paramref name="dir"/>, so the
        /// player can see (and rotate) a single conveyor's orientation before confirming it.
        /// Reuses the real belt visual (full arrow + end cap). Call ClearSinglePreview() to remove.
        /// </summary>
        public void ShowSinglePreview(int x, int y, int dir)
        {
            ClearSinglePreview();
            _previewBelt = SpawnBeltTrack(x, y, dir, dir, isTail: true, gridRenderer.CellSize);
        }

        /// <summary>Removes the single-cell placement preview, if any.</summary>
        public void ClearSinglePreview()
        {
            if (_previewBelt != null) Destroy(_previewBelt);
            _previewBelt = null;
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
                    if (RenderingMaterials.Instance?.Opaque != null)
                        mr.sharedMaterial = RenderingMaterials.Instance.Opaque;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;
                    SetMeshColor(mr, ItemColors.For(item.ItemID));
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

        /// <summary>
        /// Destroys and re-spawns the belt track at (x, y) with updated entry/exit directions.
        /// Call after modifying an existing segment's turn at a connection point.
        /// </summary>
        public void RefreshBelt(int x, int y, int entryDir, int exitDir)
        {
            var cell = (x, y);
            if (_spawnedBelts.TryGetValue(cell, out var existing))
            {
                if (existing != null) Destroy(existing);
                _spawnedBelts.Remove(cell);
            }

            if (!_queriesReady) return;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            bool isTail = IsTailCell(x, y, world.EntityManager);
            var  parent = SpawnBeltTrack(x, y, entryDir, exitDir, isTail, gridRenderer.CellSize);
            _spawnedBelts[cell] = parent;
        }

        /// <summary>Returns true if the segment at (x,y) has no NextSegment (chain tail).</summary>
        private bool IsTailCell(int x, int y, EntityManager em)
        {
            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var seg = em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (seg.Cell.x != x || seg.Cell.y != y) continue;
                bool isTail = seg.NextSegment == Entity.Null;
                entities.Dispose();
                return isTail;
            }
            entities.Dispose();
            return false;
        }

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

                bool isTail = seg.NextSegment == Entity.Null;
                var  parent = SpawnBeltTrack(seg.Cell.x, seg.Cell.y, seg.EntryDir, seg.ExitDir, isTail, cs);
                _spawnedBelts[cell] = parent;
            }

            entities.Dispose();
        }

        // ----------------------------------------------------------------
        // Belt track construction
        // ----------------------------------------------------------------

        private GameObject SpawnBeltTrack(int x, int y, int entryDir, int exitDir, bool isTail, float cs)
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
                AddArrow(root.transform, exitDir, cs, full: true);
            }
            else
            {
                // Turn: plain entry stub + arrowed exit stub
                AddStripe(root.transform, OppositeDir(entryDir), cs, full: false);
                AddArrow(root.transform, exitDir, cs, full: false);
            }

            if (isTail)
                AddEndCap(root.transform, exitDir, cs);

            return root;
        }

        /// <summary>
        /// Adds a yellow bar across the exit face to mark this segment as a chain endpoint.
        /// </summary>
        private void AddEndCap(Transform parent, int dir, float cs)
        {
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cap.name = "EndCap";
            Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(parent, worldPositionStays: false);

            // Position just inside the exit edge, above the arrow
            Vector3 offset = DirOffset(dir, cs * 0.42f);
            cap.transform.localPosition = new Vector3(offset.x, 0.14f, offset.z);

            // Bar runs perpendicular to the exit direction
            bool isNS = (dir == (int)OutputDirection.North || dir == (int)OutputDirection.South);
            cap.transform.localScale = isNS
                ? new Vector3(cs * 0.7f, 0.03f, cs * 0.06f)
                : new Vector3(cs * 0.06f, 0.03f, cs * 0.7f);

            SetupRenderer(cap, EndCapColor);
        }

        /// <summary>
        /// Draws a shaft cube + two angled wing cubes forming a ">" arrowhead pointing in dir.
        /// full = true  → full-width shaft centred on the cell (straight segments).
        /// full = false → short shaft offset toward the exit side (turn segments).
        /// </summary>
        private void AddArrow(Transform parent, int dir, float cs, bool full)
        {
            const float h         = 0.02f;
            float shaftWidth      = cs * 0.10f;
            float wingLen         = cs * 0.18f;
            float wingWidth       = cs * 0.08f;

            // Y rotation that makes local +Z face this direction
            float fwdAngle = dir switch
            {
                (int)OutputDirection.North => 0f,
                (int)OutputDirection.East  => 90f,
                (int)OutputDirection.South => 180f,
                _                          => 270f   // West
            };

            // --- Shaft ---
            float   shaftLen    = full ? cs * 0.45f : cs * 0.20f;
            float   shaftCenter = full ? 0f         : cs * 0.10f; // push toward exit for turn stubs
            Vector3 shaftPos    = DirOffset(dir, shaftCenter);

            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shaft.name = "Shaft";
            Destroy(shaft.GetComponent<Collider>());
            shaft.transform.SetParent(parent, worldPositionStays: false);
            shaft.transform.localPosition = new Vector3(shaftPos.x, 0.11f, shaftPos.z);
            shaft.transform.localRotation = Quaternion.Euler(0f, fwdAngle, 0f);
            shaft.transform.localScale    = new Vector3(shaftWidth, h, shaftLen);
            SetupRenderer(shaft, BeltStripeColor);

            // --- Arrowhead wings ---
            // Tip sits just past the shaft end, near the exit edge.
            Vector3 tipPos = DirOffset(dir, cs * 0.30f);

            // Two wings at fwdAngle+135° and fwdAngle+45°.
            // Each wing's local +X (scale axis) extends in the wing direction from the tip,
            // forming the backward-diagonal arms of the ">" chevron.
            foreach (float wAngle in new[] { fwdAngle + 135f, fwdAngle + 45f })
            {
                var     rot     = Quaternion.Euler(0f, wAngle, 0f);
                Vector3 wingDir = rot * Vector3.right;           // world direction the wing extends
                Vector3 center  = tipPos + wingDir * (wingLen * 0.5f);

                var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wing.name = "ArrowWing";
                Destroy(wing.GetComponent<Collider>());
                wing.transform.SetParent(parent, worldPositionStays: false);
                wing.transform.localPosition = new Vector3(center.x, 0.11f, center.z);
                wing.transform.localRotation = rot;
                wing.transform.localScale    = new Vector3(wingLen, h, wingWidth);
                SetupRenderer(wing, BeltStripeColor);
            }
        }

        // Entry-side stub for turn pieces — plain stripe, no arrowhead.
        private void AddStripe(Transform parent, int dir, float cs, bool full)
        {
            var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stripe.name = "Stripe";
            Destroy(stripe.GetComponent<Collider>());
            stripe.transform.SetParent(parent, worldPositionStays: false);

            float length = full ? cs * 0.6f : cs * 0.35f;
            float width  = cs * 0.15f;
            float height = 0.02f;

            Vector3 offset = DirOffset(dir, cs * (full ? 0f : 0.2f));
            stripe.transform.localPosition = new Vector3(offset.x, 0.11f, offset.z);

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
            if (RenderingMaterials.Instance?.Opaque != null)
                mr.sharedMaterial = RenderingMaterials.Instance.Opaque;
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
    }
}
