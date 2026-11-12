using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Syncs ECS conveyor data to visual GameObjects.
    ///
    /// Each cell is a flat "river" ribbon (<see cref="ConveyorFlowMeshBuilder"/>) shaded by the
    /// <c>MobileIdleBuilder/ConveyorFlow</c> shader, which scrolls a current pattern toward the exit
    /// edge — so the flow direction reads from the moving water, with no arrows. Straight, bend and
    /// 2-3 input merge-junction shapes are all built from the same ribbon primitive.
    ///
    /// Item visuals: colored spheres that float on the river, interpolating between a segment's entry
    /// and exit edge by each ConveyorItem.Progress (several items per tile).
    ///
    /// Call RefreshAll() after placing/removing segments (links may have changed across the whole
    /// graph). Item visuals update every frame in Update().
    /// </summary>
    public class ConveyorVisualizer : MonoBehaviour
    {
        [SerializeField] private GridRenderer gridRenderer;

        private EntityQuery _segmentQuery;
        private EntityQuery _itemQuery;
        private EntityQuery _portQuery;
        private bool        _queriesReady;

        // Tail tiles drawn exiting toward the input they actually feed, when that differs from their
        // stored ExitDir (#223). Keyed by cell; read by the item animation so items glide into the
        // building instead of straight past it. Rebuilt with the belts.
        private readonly Dictionary<(int, int), int> _tailVisualExit = new();

        private readonly Dictionary<(int, int), GameObject> _spawnedBelts = new();
        private readonly Dictionary<(Entity, int), GameObject> _itemSpheres = new();
        private readonly Dictionary<(Entity, int), int>       _itemSpriteIds = new();   // which item each visual shows

        // Red "not connected" seam markers: a belt whose forward output is blocked (drawn up to
        // but not merged with) points at a neighbour that also has a belt — they look aligned but
        // items do not flow across. Rebuilt in RefreshAll alongside the belts.
        private readonly List<GameObject> _gapMarkers = new();
        private Material _gapMaterial;
        private bool     _ownsGapMaterial;
        private static readonly Color GapColor = new Color(0.95f, 0.25f, 0.20f);

        // Transient single-cell placement preview (not tracked in _spawnedBelts so it never
        // collides with the real Refresh map). Lives only while a single candidate is pending.
        private GameObject _previewBelt;

        // Shared materials (created from the custom shaders once; fall back to the opaque primitive
        // material if a shader is missing so belts never render magenta).
        private Material _flowMaterial;
        private bool     _ownsFlowMaterial;
        private Material _channelMaterial;
        private bool     _ownsChannelMaterial;
        private Camera   _cam;

        private static readonly Color FlowDeep  = new Color(0.06f, 0.20f, 0.35f);
        private static readonly Color FlowCrest = new Color(0.30f, 0.80f, 1.00f);

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
                ComponentType.ReadOnly<ConveyorItem>()
            );
            _portQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<PlacedPortData>());
            _queriesReady = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _queriesReady)
            {
                _segmentQuery.Dispose();
                _itemQuery.Dispose();
                _portQuery.Dispose();
            }

            foreach (var go in _spawnedBelts.Values) DestroyBelt(go);
            foreach (var go in _itemSpheres.Values)  if (go) Destroy(go);
            _spawnedBelts.Clear();
            _itemSpheres.Clear();
            _itemSpriteIds.Clear();
            ClearGapMarkers();
            ClearSinglePreview();

            if (_ownsFlowMaterial && _flowMaterial != null) Destroy(_flowMaterial);
            if (_ownsChannelMaterial && _channelMaterial != null) Destroy(_channelMaterial);
            if (_ownsGapMaterial && _gapMaterial != null) Destroy(_gapMaterial);
        }

        // ----------------------------------------------------------------
        // Single-cell placement preview
        // ----------------------------------------------------------------

        /// <summary>
        /// Shows a transient flow-ribbon preview at (x, y) facing <paramref name="dir"/>, so the
        /// player can see (and rotate) a single conveyor's orientation before confirming it.
        /// Call ClearSinglePreview() to remove.
        /// </summary>
        public void ShowSinglePreview(int x, int y, int dir)
        {
            ClearSinglePreview();
            _previewBelt = SpawnBeltTrack(x, y, dir, dir, null, gridRenderer.CellSize);
        }

        /// <summary>Removes the single-cell placement preview, if any.</summary>
        public void ClearSinglePreview()
        {
            if (_previewBelt != null) DestroyBelt(_previewBelt);
            _previewBelt = null;
        }

        // ----------------------------------------------------------------
        // Update — item sphere animation
        // ----------------------------------------------------------------

        /// <summary>True when a segment's visual was made for a different item than the one now on it (#334).</summary>
        internal static bool VisualIsStale<TSlot>(IReadOnlyDictionary<TSlot, int> shownIds, TSlot slot, int itemId) =>
            !shownIds.TryGetValue(slot, out int shown) || shown != itemId;

        void Update()
        {
            if (!_queriesReady) return;
            if (_itemQuery.IsEmpty)
            {
                // No belts left: drop any item visuals still showing.
                if (_itemSpheres.Count > 0)
                {
                    foreach (var go in _itemSpheres.Values) if (go) Destroy(go);
                    _itemSpheres.Clear();
                    _itemSpriteIds.Clear();
                }
                return;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em       = world.EntityManager;
            var entities = _itemQuery.ToEntityArray(Allocator.Temp);
            float cs     = gridRenderer.CellSize;

            // Track which item slots (segment, index in its buffer) we saw this frame to detect removals.
            // A slot's visual follows whichever item sits in it; position is recomputed every frame.
            var seen = new HashSet<(Entity, int)>();

            for (int i = 0; i < entities.Length; i++)
            {
                var e     = entities[i];
                var seg   = em.GetComponentData<ConveyorSegmentData>(e);
                var items = em.GetBuffer<ConveyorItem>(e, isReadOnly: true);

                Vector3 cellCenter = new Vector3(seg.Cell.x * cs, 0f, seg.Cell.y * cs);
                Vector3 entryEdge  = cellCenter + DirOffset(OppositeDir(seg.EntryDir), cs * 0.45f);
                int     exitDir    = _tailVisualExit.TryGetValue((seg.Cell.x, seg.Cell.y), out int ve) ? ve : seg.ExitDir;
                Vector3 exitEdge   = cellCenter + DirOffset(exitDir, cs * 0.45f);

                for (int k = 0; k < items.Length; k++)
                {
                    var item = items[k];
                    var slot = (e, k);
                    seen.Add(slot);

                    // Prefer the item's periodic-table tile sprite as a camera-facing billboard; fall back to
                    // a colour-coded sphere when no icon is available (e.g. in tests without an ItemDatabase).
                    // The sprite must follow the item, not the slot: items move between slots (#334).
                    if (_itemSpheres.TryGetValue(slot, out var sphere) && VisualIsStale(_itemSpriteIds, slot, item.ItemID))
                    {
                        if (sphere) Destroy(sphere);
                        _itemSpheres.Remove(slot);
                        sphere = null;
                    }
                    if (sphere == null)
                    {
                        var icon = ItemDatabase.Instance?.Get(item.ItemID)?.icon;
                        sphere = (icon != null && icon.texture != null)
                            ? CreateItemTile(gridRenderer.transform, icon.texture)
                            : CreateItemSphere(gridRenderer.transform, item.ItemID);
                        _itemSpheres[slot]   = sphere;
                        _itemSpriteIds[slot] = item.ItemID;
                    }

                    // Animate position: lerp from entry edge to exit edge
                    Vector3 pos = Vector3.Lerp(entryEdge, exitEdge, Mathf.Clamp01(item.Progress));
                    pos.y       = 0.3f;
                    sphere.transform.localPosition = pos;

                    // Billboard tile quads to face the camera so the sprite stays readable as the
                    // camera orbits (harmless no-op for the symmetric sphere fallback).
                    if (_cam == null) _cam = Camera.main;
                    if (_cam != null)
                        sphere.transform.rotation =
                            Quaternion.LookRotation(sphere.transform.position - _cam.transform.position, Vector3.up);
                }
            }

            entities.Dispose();

            // Remove visuals for slots that no longer hold an item
            var toRemove = new List<(Entity, int)>();
            foreach (var kvp in _itemSpheres)
            {
                if (!seen.Contains(kvp.Key))
                {
                    if (kvp.Value) Destroy(kvp.Value);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var slot in toRemove) { _itemSpheres.Remove(slot); _itemSpriteIds.Remove(slot); }
        }

        // ----------------------------------------------------------------
        // Refresh — spawn / rebuild belt ribbon visuals
        // ----------------------------------------------------------------

        /// <summary>Destroys and re-spawns the belt ribbon at (x, y) with updated entry/exit directions
        /// (single-cell, non-merge — merge geometry is recomputed by RefreshAll).</summary>
        public void RefreshBelt(int x, int y, int entryDir, int exitDir)
        {
            var cell = (x, y);
            if (_spawnedBelts.TryGetValue(cell, out var existing))
            {
                DestroyBelt(existing);
                _spawnedBelts.Remove(cell);
            }
            if (!_queriesReady) return;

            _spawnedBelts[cell] = SpawnBeltTrack(x, y, entryDir, exitDir, null, gridRenderer.CellSize);
        }

        /// <summary>Removes the belt ribbon at (x, y). Call after destroying the segment entity.</summary>
        public void RemoveBelt(int x, int y)
        {
            var cell = (x, y);
            if (_spawnedBelts.TryGetValue(cell, out var go))
            {
                DestroyBelt(go);
                _spawnedBelts.Remove(cell);
            }
        }

        /// <summary>Spawns ribbon visuals for any segments that don't yet have one.</summary>
        public void Refresh()
        {
            if (!_queriesReady || _segmentQuery.IsEmpty) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em       = world.EntityManager;
            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            float cs     = gridRenderer.CellSize;

            // Map every cell to its output direction so we can detect inbound edges (merges). A cell
            // whose output is blocked (a dead-end that runs up to but does not merge with a neighbour)
            // is omitted, so the neighbour it points at is not drawn as a merge junction.
            var exitMap = new Dictionary<(int, int), int>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                var seg = em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (seg.OutputBlocked) continue;
                exitMap[(seg.Cell.x, seg.Cell.y)] = seg.ExitDir;
            }

            var inputPorts = CollectInputPorts(em);
            var feeds      = new bool[4];
            var sinks      = new bool[4];

            for (int i = 0; i < entities.Length; i++)
            {
                var seg  = em.GetComponentData<ConveyorSegmentData>(entities[i]);
                var cell = (seg.Cell.x, seg.Cell.y);
                if (_spawnedBelts.ContainsKey(cell)) continue;

                // A tail is drawn exiting toward the input it really feeds (same ranking as
                // ConveyorSystem.TryPickInput, via TailDelivery), not straight past it (#223). Data is
                // untouched, so extending the belt from this tile still links along its stored ExitDir.
                int exitDir = seg.ExitDir;
                if (seg.NextSegment == Entity.Null)
                {
                    for (int d = 0; d < 4; d++)
                    {
                        int2 nb = Adjacent(seg.Cell, d);
                        feeds[d] = inputPorts.TryGetValue((nb.x, nb.y, d), out bool sink);
                        sinks[d] = feeds[d] && sink;
                    }
                    exitDir = TailDelivery.VisualExit(seg.EntryDir, seg.ExitDir, feeds, sinks);
                }
                if (exitDir != seg.ExitDir) _tailVisualExit[cell] = exitDir;
                else                        _tailVisualExit.Remove(cell);

                var inbound = ComputeInboundEdges(seg.Cell, exitMap);
                _spawnedBelts[cell] = SpawnBeltTrack(seg.Cell.x, seg.Cell.y, seg.EntryDir, exitDir, inbound, cs);
            }

            entities.Dispose();
        }

        /// <summary>
        /// Every building input port as (cell x, cell y, facing) -> whether its building is an entropy
        /// sink. A tail at cell C feeds direction d when (C + d, d) is a key, the same test as
        /// ConveyorSystem.HasMatchingPort; the sink flag feeds TailInputPriority (Demon last).
        /// </summary>
        private Dictionary<(int, int, int), bool> CollectInputPorts(EntityManager em)
        {
            var map = new Dictionary<(int, int, int), bool>();
            if (_portQuery.IsEmpty) return map;
            var buildings = _portQuery.ToEntityArray(Allocator.Temp);
            for (int b = 0; b < buildings.Length; b++)
            {
                bool isSink = em.HasComponent<EntropySinkTag>(buildings[b]);
                var ports = em.GetBuffer<PlacedPortData>(buildings[b], isReadOnly: true);
                for (int p = 0; p < ports.Length; p++)
                    if (ports[p].PortType == (int)PortType.Input)
                        map[(ports[p].CellX, ports[p].CellY, ports[p].Facing)] = isSink;
            }
            buildings.Dispose();
            return map;
        }

        /// <summary>Destroys all belt ribbons and rebuilds them from current ECS data. Call after any
        /// placement/removal, since RelinkAll may have changed many cells' geometry (incl. merges).</summary>
        public void RefreshAll()
        {
            foreach (var go in _spawnedBelts.Values) DestroyBelt(go);
            _spawnedBelts.Clear();
            _tailVisualExit.Clear();
            Refresh();
            RefreshGapMarkers();
            BeltsChanged?.Invoke();
        }

        /// <summary>Raised after any belt is placed, deleted or relinked; every such change ends in <see cref="RefreshAll"/>.</summary>
        public static event System.Action BeltsChanged;

        // ----------------------------------------------------------------
        // "Not connected" seam markers
        // ----------------------------------------------------------------

        private void ClearGapMarkers()
        {
            foreach (var go in _gapMarkers) if (go) Destroy(go);
            _gapMarkers.Clear();
        }

        /// <summary>
        /// Rebuilds the red seam markers. A belt whose forward output is intentionally blocked
        /// (drawn up to, but not merged onto, an existing belt) is flagged when the cell it points
        /// at also contains a belt — so the player sees the two are aligned but NOT connected.
        /// </summary>
        private void RefreshGapMarkers()
        {
            ClearGapMarkers();
            if (!_queriesReady || _segmentQuery.IsEmpty) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;

            var em       = world.EntityManager;
            var entities = _segmentQuery.ToEntityArray(Allocator.Temp);
            float cs     = gridRenderer.CellSize;

            var allCells = new HashSet<(int, int)>(entities.Length);
            for (int i = 0; i < entities.Length; i++)
            {
                var seg = em.GetComponentData<ConveyorSegmentData>(entities[i]);
                allCells.Add((seg.Cell.x, seg.Cell.y));
            }

            for (int i = 0; i < entities.Length; i++)
            {
                var seg = em.GetComponentData<ConveyorSegmentData>(entities[i]);
                if (!seg.OutputBlocked) continue;                 // only intentional dead-ends
                int2 nc = Adjacent(seg.Cell, seg.ExitDir);
                if (allCells.Contains((nc.x, nc.y)))              // …that point at another belt
                    _gapMarkers.Add(SpawnGapMarker(seg.Cell.x, seg.Cell.y, seg.ExitDir, cs));
            }

            entities.Dispose();
        }

        private GameObject SpawnGapMarker(int x, int y, int exitDir, float cs)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = $"ConveyorGap_{x}_{y}";
            if (marker.TryGetComponent<Collider>(out var col)) Destroy(col);

            marker.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
            // Sit on the shared edge between this cell and the neighbour it fails to connect to.
            marker.transform.localPosition =
                new Vector3(x * cs, 0.06f, y * cs) + DirOffset(exitDir, cs * 0.5f);

            // Thin across the flow direction, long along the shared edge.
            bool alongX = exitDir == (int)OutputDirection.East || exitDir == (int)OutputDirection.West;
            float thin = cs * 0.08f, longSide = cs * 0.55f, h = 0.08f;
            marker.transform.localScale = alongX
                ? new Vector3(thin, h, longSide)
                : new Vector3(longSide, h, thin);

            var mr = marker.GetComponent<MeshRenderer>();
            mr.sharedMaterial    = GetGapMaterial();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            return marker;
        }

        private Material GetGapMaterial()
        {
            if (_gapMaterial != null) return _gapMaterial;

            // URP Unlit so the marker never renders magenta on Android (CLAUDE.md rule).
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _gapMaterial     = new Material(shader != null ? shader : Shader.Find("Sprites/Default"));
            _ownsGapMaterial = true;
            if (_gapMaterial.HasProperty("_BaseColor")) _gapMaterial.SetColor("_BaseColor", GapColor);
            if (_gapMaterial.HasProperty("_Color"))     _gapMaterial.SetColor("_Color", GapColor);
            _gapMaterial.color = GapColor;
            return _gapMaterial;
        }

        /// <summary>Returns the edge directions (0..3) from which conveyor flow enters this cell.</summary>
        private static List<int> ComputeInboundEdges(int2 cell, Dictionary<(int, int), int> exitMap)
        {
            var edges = new List<int>();
            for (int d = 0; d < 4; d++)
            {
                int2 nc = Adjacent(cell, d);
                if (!exitMap.TryGetValue((nc.x, nc.y), out int nExit)) continue;
                int2 nExitCell = Adjacent(nc, nExit);
                if (nExitCell.x == cell.x && nExitCell.y == cell.y) edges.Add(d);
            }
            return edges;
        }

        // ----------------------------------------------------------------
        // Ribbon construction
        // ----------------------------------------------------------------

        private GameObject SpawnBeltTrack(int x, int y, int entryDir, int exitDir, List<int> inboundEdges, float cs)
        {
            var root = new GameObject($"Belt_{x}_{y}");
            root.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
            root.transform.localPosition = new Vector3(x * cs, 0f, y * cs);

            // Opaque dark bed (same footprint as the water, no rails) — gives the translucent ribbon a
            // base and writes depth so the grid tiles beneath are occluded and the water draws on top.
            var channel = new GameObject("Channel", typeof(MeshFilter), typeof(MeshRenderer));
            channel.transform.SetParent(root.transform, worldPositionStays: false);
            channel.GetComponent<MeshFilter>().sharedMesh = ConveyorFlowMeshBuilder.Build(
                cs, entryDir, exitDir, inboundEdges,
                ConveyorFlowMeshBuilder.FloorY, ConveyorFlowMeshBuilder.FloorWidthFrac);

            var cmr = channel.GetComponent<MeshRenderer>();
            cmr.sharedMaterial    = GetChannelMaterial();
            cmr.shadowCastingMode = ShadowCastingMode.Off;
            cmr.receiveShadows    = false;

            // Flowing water ribbon (translucent) on top, inside the channel.
            var flow = new GameObject("Flow", typeof(MeshFilter), typeof(MeshRenderer));
            flow.transform.SetParent(root.transform, worldPositionStays: false);
            flow.GetComponent<MeshFilter>().sharedMesh =
                ConveyorFlowMeshBuilder.Build(cs, entryDir, exitDir, inboundEdges);

            var wmr = flow.GetComponent<MeshRenderer>();
            wmr.sharedMaterial    = GetFlowMaterial();
            wmr.shadowCastingMode = ShadowCastingMode.Off;
            wmr.receiveShadows    = false;

            return root;
        }

        /// <summary>The shared belt water material; a Belt Splitter's river draws with it to match its belts.</summary>
        public Material GetFlowMaterial()
        {
            if (_flowMaterial != null) return _flowMaterial;

            var shader = Shader.Find("MobileIdleBuilder/ConveyorFlow");
            if (shader != null)
            {
                _flowMaterial = new Material(shader);
                _flowMaterial.SetColor("_BaseColor", FlowDeep);
                _flowMaterial.SetColor("_FlowColor", FlowCrest);
                _ownsFlowMaterial = true;
            }
            else
            {
                GameLogger.Warning("[ConveyorVisualizer] Shader 'MobileIdleBuilder/ConveyorFlow' not found — belts fall back to flat opaque.");
                _flowMaterial     = RenderingMaterials.Instance?.Opaque;
                _ownsFlowMaterial = false;
            }
            return _flowMaterial;
        }

        /// <summary>The shared belt bed material; see <see cref="GetFlowMaterial"/>.</summary>
        public Material GetChannelMaterial()
        {
            if (_channelMaterial != null) return _channelMaterial;

            var shader = Shader.Find("MobileIdleBuilder/ConveyorChannel");
            if (shader != null)
            {
                _channelMaterial     = new Material(shader);
                _ownsChannelMaterial = true;
            }
            else
            {
                GameLogger.Warning("[ConveyorVisualizer] Shader 'MobileIdleBuilder/ConveyorChannel' not found — channel falls back to flat opaque.");
                _channelMaterial     = RenderingMaterials.Instance?.Opaque;
                _ownsChannelMaterial = false;
            }
            return _channelMaterial;
        }

        /// <summary>Destroys a belt root and its procedural meshes (avoids leaking meshes on RefreshAll).</summary>
        private void DestroyBelt(GameObject go)
        {
            if (go == null) return;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
            Destroy(go);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static void SetMeshColor(MeshRenderer mr, Color color)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }

        // A flat quad textured with the item's tile sprite (transparent-unlit material +
        // per-item texture via MaterialPropertyBlock, so a single shared material serves all
        // items). Billboarded toward the camera each frame in the update loop above.
        private GameObject CreateItemTile(Transform parent, Texture tex)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "ConveyorItemTile";
            quad.transform.SetParent(parent);
            quad.transform.localScale = Vector3.one * 0.4f;
            Destroy(quad.GetComponent<Collider>());

            var mr = quad.GetComponent<MeshRenderer>();
            // Tiles are fully opaque (dark corners), so use the opaque material — it renders solid
            // and never washes out against the bright belt the way an alpha-blended quad did.
            var mat = RenderingMaterials.Instance != null
                ? (RenderingMaterials.Instance.Opaque != null
                    ? RenderingMaterials.Instance.Opaque
                    : RenderingMaterials.Instance.Transparent)
                : null;
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var mpb = new MaterialPropertyBlock();
            mpb.SetTexture("_BaseMap", tex);
            mpb.SetColor("_BaseColor", Color.white);
            mr.SetPropertyBlock(mpb);
            return quad;
        }

        // Fallback visual: a colour-coded sphere (the original belt-item look) used when an
        // item has no tile sprite.
        private GameObject CreateItemSphere(Transform parent, int itemId)
        {
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "ConveyorItem";
            sphere.transform.SetParent(parent);
            sphere.transform.localScale = Vector3.one * 0.3f;
            Destroy(sphere.GetComponent<Collider>());

            var mr = sphere.GetComponent<MeshRenderer>();
            if (RenderingMaterials.Instance?.Opaque != null)
                mr.sharedMaterial = RenderingMaterials.Instance.Opaque;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            SetMeshColor(mr, ItemColors.For(itemId));
            return sphere;
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

        private static int2 Adjacent(int2 cell, int dir)
        {
            return (OutputDirection)dir switch
            {
                OutputDirection.North => new int2(cell.x,     cell.y + 1),
                OutputDirection.East  => new int2(cell.x + 1, cell.y    ),
                OutputDirection.South => new int2(cell.x,     cell.y - 1),
                OutputDirection.West  => new int2(cell.x - 1, cell.y    ),
                _                    => cell
            };
        }

        private static int OppositeDir(int dir) => (dir + 2) % 4;
    }
}
