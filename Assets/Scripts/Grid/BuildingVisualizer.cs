using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Syncs ECS building entities to their structures and ground port arrows on the GridRenderer.
    /// BuildingPlacer should call Refresh() after placing a building at runtime.
    /// </summary>
    public class BuildingVisualizer : MonoBehaviour
    {
        [SerializeField] private GridRenderer gridRenderer;
        [SerializeField] private Material     _buildingMaterial;  // URP Unlit Transparent — assign in Inspector

        private EntityQuery                                       _buildingQuery;
        private EntityQuery                                       _footprintQuery;
        private EntityQuery                                       _portQuery;
        private EntityQuery                                       _powerStatusQuery;
        private EntityQuery                                       _segmentQuery;
        private bool                                              _segmentQueryReady;
        private bool                                              _buildingQueryReady;
        private bool                                              _footprintQueryReady;
        private bool                                              _portQueryReady;
        private bool                                              _powerStatusQueryReady;
        private float                                             _powerTintTimer;
        private readonly Dictionary<(int, int), (int, int)>      _footprints        = new();
        private readonly HashSet<(int, int)>                      _highlighted       = new();
        private readonly Dictionary<(int, int), GameObject>       _spawnedCubes      = new();
        private readonly Dictionary<(int, int), Color>            _baseColors        = new();
        // Collectors whose field had not spawned yet at visual-creation time (load ordering): keyed
        // to the time they were registered, so their field colour can be resolved once it appears.
        private readonly Dictionary<(int, int), float>            _pendingFieldColor = new();
        private readonly List<(int, int)>                         _resolvedFieldBuffer = new();
        private readonly HashSet<(int, int)>                      _portedCells       = new();
        private readonly Dictionary<(int, int), List<GameObject>> _spawnedPortArrows = new();

        // Each building's ground arrows, shown only while their port still needs a belt.
        private readonly struct PortArrowEntry
        {
            public readonly GameObject Arrow;
            public readonly int  FrontX, FrontY;
            public readonly bool IsOutput;

            public PortArrowEntry(GameObject arrow, int frontX, int frontY, bool isOutput)
            {
                Arrow = arrow; FrontX = frontX; FrontY = frontY; IsOutput = isOutput;
            }
        }
        private readonly Dictionary<(int, int), List<PortArrowEntry>> _portArrows = new();
        private readonly Dictionary<(int, int), (bool head, bool tail)> _beltCells = new();

        // The selected building's anchor cell: a building tints its grid tiles only while selected.
        private (int, int)                                        _selectedCell      = (-1, -1);
        private ConveyorVisualizer                                _conveyorVisualizer;

        // Maps every occupied cell (including non-anchor cells of multi-tile buildings)
        // back to the anchor cell so SetHover can find the right cube.
        private readonly Dictionary<(int, int), (int, int)>      _cellToAnchor      = new();

        private (int, int) _hoveredCell = (-1, -1);

        // While a power building is being positioned, the anchors whose cubes are currently tinted the
        // in-range preview colour, plus a flag so RefreshPowerTint stands down (it would otherwise fight
        // the preview on its 0.4s tick, same rationale as the _hoveredCell skip).
        private readonly HashSet<(int, int)> _rangeHighlighted = new();
        private bool _radiusPreviewActive;

        private static readonly Color DefaultCubeColor   = Color.white;
        private static readonly Color HoverCubeColor     = new Color(1f, 0.82f, 0.15f); // yellow hover highlight
        private static readonly Color UnpoweredCubeColor = new Color(0.9f,  0.25f, 0.25f); // red — no power in range
        private static readonly Color InRangePreviewColor = new Color(0.35f, 1f, 0.75f);  // bright teal-green — inside a generator's radius during placement
        private static readonly Color EntropySinkGold    = new Color(1.0f, 0.78f, 0.30f); // Maxwell's Demon torus base
        private static readonly Color AtomGeneratorGold  = new Color(1.0f, 0.66f, 0.22f); // Atom Generator nucleus base
        private static readonly Color IsotopicGreen      = new Color(0.5f, 0.85f, 0.55f); // Isotopic Manipulator nucleus base
        private static readonly Color GeneratorCyan       = new Color(0.30f, 0.85f, 1.0f); // Basic Generator spire base
        private static readonly Color RelayAmber          = new Color(1.0f, 0.70f, 0.25f); // Power Relay broadcast-pylon base
        private static readonly Color CombinerSteel       = new Color(0.40f, 0.45f, 0.54f); // Strong Force Combiner housing base
        private static readonly Color MoleculeTeal        = new Color(0.2f, 0.8f, 0.8f);   // Molecular Synthesizer base
        private static readonly Color ForgeSteel          = new Color(0.45f, 0.45f, 0.48f);// Materials Forge furnace base
        private static readonly Color FabricatorViolet     = new Color(0.6f, 0.4f, 0.95f);  // Component Fabricator base
        private static readonly Color ContainmentLead      = new Color(0.35f, 0.35f, 0.38f);// Radioactive Containment shell base
        private static readonly Color FusionPlinth         = new Color(0.20f, 0.22f, 0.30f);// Fusion Reactor plinth base
        private static readonly Color SplitterRock         = new Color(0.50f, 0.56f, 0.66f);// Belt Splitter rock base
        private const float LowHoleScale = 0.55f; // holes on a low plinth, which has little face to fill
        private static readonly Color OutputHoleColor    = new Color(1.0f, 0.85f, 0.45f); // lit emission aperture
        private static readonly Color InputHoleColor     = new Color(0.05f, 0.07f, 0.12f); // dark recessed intake mouth

        // Shared hole-fixture assets (port apertures on bespoke producer structures), built once at the
        // grid cell size and reused across every port so no per-hole allocation happens.
        private Mesh     _outHoleMesh;
        private Mesh     _inHoleMesh;
        private Material _outHoleMat;
        private Material _inHoleMat;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _buildingQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _buildingQueryReady = true;

            _footprintQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<BuildingFootprint>()
            );
            _footprintQueryReady = true;

            _portQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<PlacedPortData>()
            );
            _portQueryReady = true;

            _powerStatusQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<GridPosition>(),
                ComponentType.ReadOnly<PowerStatus>()
            );
            _powerStatusQueryReady = true;

            _segmentQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ConveyorSegmentData>());
            _segmentQueryReady = true;
            ConveyorVisualizer.BeltsChanged += RefreshPortArrows;

            StartCoroutine(ScanOnceLoaded());
        }

        private IEnumerator ScanOnceLoaded()
        {
            float timeout = 5f;
            while (_buildingQuery.IsEmpty && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }

            if (!_buildingQuery.IsEmpty)
                Refresh();
        }

        /// <summary>
        /// Highlights any building cells not yet marked. Safe to call repeatedly —
        /// already-highlighted cells are skipped.
        /// </summary>
        public void Refresh()
        {
            SyncBuildings();
            RefreshPortArrows();
        }

        private void SyncBuildings()
        {
            if (_buildingQuery == null || _buildingQuery.IsEmpty) return;

            // Pass 1: collect footprints
            if (_footprintQueryReady && !_footprintQuery.IsEmpty)
            {
                var fpPositions = _footprintQuery.ToComponentDataArray<GridPosition>(Allocator.Temp);
                var fpData      = _footprintQuery.ToComponentDataArray<BuildingFootprint>(Allocator.Temp);
                for (int i = 0; i < fpPositions.Length; i++)
                    _footprints[(fpPositions[i].Cell.x, fpPositions[i].Cell.y)] = (fpData[i].Width, fpData[i].Height);
                fpPositions.Dispose();
                fpData.Dispose();
            }

            // Pass 2: highlight tiles and spawn building visuals (cube, or spindle for collectors)
            var em        = World.DefaultGameObjectInjectionWorld.EntityManager;
            var bEntities = _buildingQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < bEntities.Length; i++)
            {
                var ent  = bEntities[i];
                var gp   = em.GetComponentData<GridPosition>(ent);
                int x    = gp.Cell.x;
                int y    = gp.Cell.y;
                var cell = (x, y);

                if (_highlighted.Contains(cell)) continue;

                int fw = 1, fh = 1;
                if (_footprints.TryGetValue(cell, out var fp)) { fw = fp.Item1; fh = fp.Item2; }

                bool isSink = em.HasComponent<EntropySinkTag>(ent);

                for (int dx = 0; dx < fw; dx++)
                    for (int dy = 0; dy < fh; dy++)
                    {
                        GridOccupancy.Instance?.Register(x + dx, y + dy);
                        _cellToAnchor[(x + dx, y + dy)] = cell;
                    }
                _highlighted.Add(cell);

                if (!_spawnedCubes.ContainsKey(cell))
                {
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cube.name = $"Building_{x}_{y}";
                    cube.transform.SetParent(gridRenderer.transform);
                    float cs = gridRenderer.CellSize;
                    cube.transform.localPosition = new Vector3(
                        (x + (fw - 1) * 0.5f) * cs, 0.5f, (y + (fh - 1) * 0.5f) * cs);
                    float gap = cs * 0.2f;
                    cube.transform.localScale = new Vector3(cs * fw - gap, 1f, cs * fh - gap);
                    var mr = cube.GetComponent<MeshRenderer>();
                    if (_buildingMaterial != null)
                        mr.sharedMaterial = _buildingMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;

                    // Field-collector buildings (anything carrying CollectorData) resemble the field
                    // they sit on, so their base colour is the field colour rather than plain white.
                    // Bespoke procedural structure selector (data-driven; set at placement/load).
                    var styleKind = em.HasComponent<BuildingVisualStyle>(ent)
                        ? (BuildingStructureKind)em.GetComponentData<BuildingVisualStyle>(ent).Kind
                        : BuildingStructureKind.None;

                    bool  isCollector  = em.HasComponent<CollectorData>(ent);
                    Color baseColor    = DefaultCubeColor;
                    bool  fieldPending = false;
                    if (isCollector)
                    {
                        var field = FieldGenerator.GetFieldAt(x, y);
                        if (field != null) baseColor = field.fieldColor;
                        else fieldPending = true; // field not spawned yet (load ordering) — resolve later
                    }
                    else if (isSink)
                    {
                        baseColor = EntropySinkGold; // gold base; power/hover tints restore to gold, not white
                    }
                    else if (styleKind == BuildingStructureKind.AtomGenerator)
                    {
                        baseColor = AtomGeneratorGold; // warm nucleus base; tints restore to gold, not white
                    }
                    else if (styleKind == BuildingStructureKind.IsotopicManipulator)
                    {
                        baseColor = IsotopicGreen; // green-tinged nucleus base
                    }
                    else if (styleKind == BuildingStructureKind.BasicGenerator)
                    {
                        baseColor = GeneratorCyan; // electric cyan spire base
                    }
                    else if (styleKind == BuildingStructureKind.PowerRelay)
                    {
                        baseColor = RelayAmber; // amber broadcast-pylon base
                    }
                    else if (styleKind == BuildingStructureKind.StrongForceCombiner)
                    {
                        baseColor = CombinerSteel; // pale steel knot base
                    }
                    else if (styleKind == BuildingStructureKind.MolecularSynthesizer)
                    {
                        baseColor = MoleculeTeal; // teal molecule base
                    }
                    else if (styleKind == BuildingStructureKind.MaterialsForge)
                    {
                        baseColor = ForgeSteel; // steel furnace base
                    }
                    else if (styleKind == BuildingStructureKind.ComponentFabricator)
                    {
                        baseColor = FabricatorViolet; // violet precision-core base
                    }
                    else if (styleKind == BuildingStructureKind.RadioactiveContainment)
                    {
                        baseColor = ContainmentLead; // lead-grey shell base
                    }
                    else if (styleKind == BuildingStructureKind.BeltSplitter)
                    {
                        baseColor = SplitterRock; // pale slate rock base
                    }
                    else if (styleKind == BuildingStructureKind.FusionReactor)
                    {
                        baseColor = FusionPlinth; // dark plinth base
                    }

                    // PresenceReceiver owns all colour state for this cube; _baseColors remembers the
                    // logical base so power/hover tints restore to it (not white) afterwards.
                    var pr = cube.AddComponent<PresenceReceiver>();
                    pr.SetBaseColor(baseColor);
                    _baseColors[cell] = baseColor;

                    // Swap the cube for the procedural spindle structure — a round body tapering to a
                    // singularity point, with a flat front facade + emission aperture facing the output.
                    if (styleKind == BuildingStructureKind.NucleonHarvester)
                    {
                        cube.name = $"NucleonHarvester_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        InitializeNucleonHarvester(cube, em, ent, x, y, fw, fh, baseColor);
                        if (fieldPending) _pendingFieldColor[cell] = Time.time;
                    }
                    else if (isCollector)
                    {
                        cube.name = $"Collector_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<CollectorStructure>().Initialize(ent, baseColor);
                        if (fieldPending) _pendingFieldColor[cell] = Time.time;
                    }

                    // Maxwell's Demon (entropy sink): swap the cube for a flat gold torus that fills the
                    // footprint. Ports are drawn separately (Pass 3) so they stay put; keep the donut low.
                    if (isSink)
                    {
                        cube.name = $"Sink_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = new Vector3(cs * fw - gap, cs * fw - gap, cs * fh - gap);
                        cube.AddComponent<EntropySinkStructure>().Initialize(ent);
                    }

                    // Bespoke producer structures (data-driven via BuildingVisualStyle). Each sits on the
                    // grid plane at uniform cell scale; their input/output holes are drawn in Pass 3 from
                    // PlacedPortData.
                    if (styleKind == BuildingStructureKind.AtomGenerator)
                    {
                        cube.name = $"AtomGenerator_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        // 2x1 since #275: a round nucleus can't stretch, so grow it a little to read as
                        // spanning both tiles while the port holes stay on the footprint edges.
                        cube.transform.localScale = Vector3.one * cs * (1f + 0.25f * (Mathf.Max(fw, fh) - 1));
                        cube.AddComponent<AtomGeneratorStructure>().Initialize(ent);
                    }
                    else if (styleKind == BuildingStructureKind.IsotopicManipulator)
                    {
                        cube.name = $"IsotopicManipulator_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<IsotopicManipulatorStructure>().Initialize(ent);
                    }
                    else if (styleKind == BuildingStructureKind.BasicGenerator)
                    {
                        cube.name = $"BasicGenerator_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<BasicGeneratorStructure>().Initialize(baseColor);
                    }
                    else if (styleKind == BuildingStructureKind.PowerRelay)
                    {
                        // Uniform cell scale; the mast body sizes itself to the footprint (fw x fh).
                        cube.name = $"PowerRelay_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<PowerRelayStructure>().Initialize(fw, fh);
                    }
                    else if (styleKind == BuildingStructureKind.StrongForceCombiner)
                    {
                        // Uniform cell scale; the rounded-box body sizes itself to the footprint (fw x fh)
                        // so it stays undistorted and the door fixtures sit flush on its flat faces.
                        cube.name = $"StrongForceCombiner_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<StrongForceCombinerStructure>().Initialize(ent, fw, fh, baseColor);
                    }
                    else if (styleKind == BuildingStructureKind.MolecularSynthesizer)
                    {
                        cube.name = $"MolecularSynthesizer_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<MolecularSynthesizerStructure>().Initialize(ent);
                    }
                    else if (styleKind == BuildingStructureKind.MaterialsForge)
                    {
                        cube.name = $"MaterialsForge_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<MaterialsForgeStructure>().Initialize(ent);
                    }
                    else if (styleKind == BuildingStructureKind.ComponentFabricator)
                    {
                        cube.name = $"ComponentFabricator_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<ComponentFabricatorStructure>().Initialize(ent);
                    }
                    else if (styleKind == BuildingStructureKind.RadioactiveContainment)
                    {
                        // 2x2 housing: uniform cell scale; the dome body sizes itself to the footprint.
                        cube.name = $"RadioactiveContainment_{x}_{y}";
                        cube.transform.localPosition = new Vector3(
                            (x + (fw - 1) * 0.5f) * cs, 0f, (y + (fh - 1) * 0.5f) * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<RadioactiveContainmentStructure>().Initialize(fw, fh);
                    }
                    else if (styleKind == BuildingStructureKind.FusionReactor)
                    {
                        cube.name = $"FusionReactor_{x}_{y}";
                        cube.transform.localPosition = new Vector3(x * cs, 0f, y * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        cube.AddComponent<FusionReactorStructure>().Initialize(ent, baseColor);
                    }
                    else if (styleKind == BuildingStructureKind.BeltSplitter)
                    {
                        cube.name = $"BeltSplitter_{x}_{y}";
                        cube.transform.localPosition = new Vector3(x * cs, 0f, y * cs);
                        cube.transform.localScale = Vector3.one * cs;
                        InitializeSplitter(cube, em, ent, baseColor);
                    }

                    _spawnedCubes[cell] = cube;
                }
            }
            bEntities.Dispose();

            // Pass 3: ground arrows and holes from the placed ports
            if (!_portQueryReady || _portQuery.IsEmpty) return;

            var entities = _portQuery.ToEntityArray(Allocator.Temp);
            float cs2        = gridRenderer.CellSize;

            for (int i = 0; i < entities.Length; i++)
            {
                var pos  = em.GetComponentData<GridPosition>(entities[i]);
                var cell = (pos.Cell.x, pos.Cell.y);

                if (_portedCells.Contains(cell)) continue;

                // Entropy sink (Maxwell's Demon): keep its input ports for gameplay but draw no
                // arrows — its own input visuals will be added later.
                if (em.HasComponent<EntropySinkTag>(entities[i])) { _portedCells.Add(cell); continue; }

                var ports     = em.GetBuffer<PlacedPortData>(entities[i]);
                var arrowList = new List<GameObject>();
                var entries   = new List<PortArrowEntry>(ports.Length);

                // Buildings with a bespoke procedural structure also get physical input/output holes at
                // their port edges (a lit emission aperture for outputs, a recessed intake mouth for
                // inputs). A Belt Splitter's river is its own opening, so it gets arrows only.
                bool hasStyle = em.HasComponent<BuildingVisualStyle>(entities[i]);
                int  styleId  = hasStyle ? em.GetComponentData<BuildingVisualStyle>(entities[i]).Kind : 0;
                bool drawHoles = hasStyle && styleId != (int)BuildingStructureKind.BeltSplitter;
                bool lowHoles = styleId == (int)BuildingStructureKind.FusionReactor
                             || styleId == (int)BuildingStructureKind.NucleonHarvester;

                for (int p = 0; p < ports.Length; p++)
                {
                    var  port   = ports[p];
                    int  wx     = port.CellX;
                    int  wy     = port.CellY;
                    var  dir    = (OutputDirection)port.Facing;
                    bool isOut  = port.PortType == (int)PortType.Output;

                    // localFacing = direction items travel. An output sits on its facing edge; an input
                    // sits on the opposite edge, where items arrive from.
                    var edgeDir = isOut ? dir : (OutputDirection)(((int)dir + 2) % 4);

                    var arrowGO = new GameObject($"Port_{(isOut ? "Out" : "In")}_{wx}_{wy}");
                    arrowGO.transform.SetParent(gridRenderer.transform, worldPositionStays: false);
                    arrowGO.transform.localPosition = new Vector3(wx * cs2, 0f, wy * cs2);
                    arrowGO.AddComponent<PortGroundArrow>().Initialize(edgeDir, isOut, cs2);
                    arrowList.Add(arrowGO);

                    var front = PortFrontCell(wx, wy, edgeDir);
                    entries.Add(new PortArrowEntry(arrowGO, front.Item1, front.Item2, isOut));

                    if (drawHoles)
                    {
                        var holeGO = CreateHoleFixture(wx, wy, edgeDir, isOut, cs2, lowHoles);
                        if (holeGO != null) arrowList.Add(holeGO);
                    }
                }

                _portedCells.Add(cell);
                _spawnedPortArrows[cell] = arrowList;
                _portArrows[cell] = entries;
            }

            entities.Dispose();
        }

        private static (int, int) PortFrontCell(int x, int y, OutputDirection edge) => edge switch
        {
            OutputDirection.North => (x, y + 1),
            OutputDirection.East  => (x + 1, y),
            OutputDirection.South => (x, y - 1),
            _                     => (x - 1, y),
        };

        // Shows each ground arrow only while its port still needs a belt. Runs after any belt or building
        // change, so deleting a belt brings its arrow straight back.
        private void RefreshPortArrows()
        {
            if (_portArrows.Count == 0 || gridRenderer == null) return;

            _beltCells.Clear();
            if (_segmentQueryReady && !_segmentQuery.IsEmpty)
            {
                var segments = _segmentQuery.ToComponentDataArray<ConveyorSegmentData>(Allocator.Temp);
                for (int i = 0; i < segments.Length; i++)
                {
                    var s = segments[i];
                    _beltCells[(s.Cell.x, s.Cell.y)] = (s.IsChainHead, s.NextSegment == Entity.Null);
                }
                segments.Dispose();
            }

            var occupancy = GridOccupancy.Instance;
            foreach (var list in _portArrows.Values)
            {
                foreach (var entry in list)
                {
                    if (entry.Arrow == null) continue;
                    var  cell    = (entry.FrontX, entry.FrontY);
                    bool hasBelt = _beltCells.TryGetValue(cell, out var belt);
                    bool blocked = occupancy != null && occupancy.IsOccupied(entry.FrontX, entry.FrontY) && !hasBelt;
                    bool free    = gridRenderer.IsInBounds(entry.FrontX, entry.FrontY) && !blocked;
                    entry.Arrow.SetActive(PortArrowRules.ShouldShow(entry.IsOutput, free, hasBelt, belt.head, belt.tail));
                }
            }
        }

        // The door and the nucleon's flight both follow the placed output port, wherever rotation put it.
        private static void InitializeNucleonHarvester(GameObject host, EntityManager em, Entity entity,
                                                       int x, int y, int fw, int fh, Color fieldColor)
        {
            var portEdge = new Vector2(0f, -fh * 0.5f);
            if (em.HasBuffer<PlacedPortData>(entity))
            {
                var ports = em.GetBuffer<PlacedPortData>(entity);
                for (int p = 0; p < ports.Length; p++)
                {
                    if (ports[p].PortType != (int)PortType.Output) continue;
                    portEdge = NucleonHarvesterStructure.PortEdgeLocal(ports[p].CellX, ports[p].CellY, ports[p].Facing, x, y, fw, fh);
                    break;
                }
            }
            host.AddComponent<NucleonHarvesterStructure>().Initialize(entity, fw, fh, portEdge, fieldColor);
        }

        // The river needs the placed ports (rotated with the building) and the belts' shared materials.
        private void InitializeSplitter(GameObject host, EntityManager em, Entity entity, Color rockColor)
        {
            int travelDir = (int)OutputDirection.North;
            var outputs   = new List<int>(3);
            if (em.HasBuffer<PlacedPortData>(entity))
            {
                var ports = em.GetBuffer<PlacedPortData>(entity);
                for (int p = 0; p < ports.Length; p++)
                {
                    if (ports[p].PortType == (int)PortType.Output) outputs.Add(ports[p].Facing);
                    else travelDir = ports[p].Facing;
                }
            }

            if (_conveyorVisualizer == null) _conveyorVisualizer = FindAnyObjectByType<ConveyorVisualizer>();
            if (_conveyorVisualizer == null)
            {
                GameLogger.Warning("[BuildingVisualizer] No ConveyorVisualizer in the scene; the Belt Splitter keeps its placeholder cube.");
                return;
            }

            host.AddComponent<SplitterStructure>().Initialize(entity, travelDir, outputs, rockColor,
                _conveyorVisualizer.GetFlowMaterial(), _conveyorVisualizer.GetChannelMaterial());
        }

        // ----------------------------------------------------------------
        // Power tint — persistently reddens consumer cubes with no generator in range
        // ----------------------------------------------------------------

        void Update()
        {
            if (_pendingFieldColor.Count > 0) ResolvePendingFieldColors();

            _powerTintTimer += Time.deltaTime;
            if (_powerTintTimer < 0.4f) return;
            _powerTintTimer = 0f;
            RefreshPowerTint();
        }

        // Collectors created before their field spawned (load ordering) start at the default colour;
        // once FieldGenerator has the field, apply its colour to the body + spindle/particles. Gives
        // up after 10s so a genuinely field-less collector doesn't poll forever.
        private void ResolvePendingFieldColors()
        {
            _resolvedFieldBuffer.Clear();
            foreach (var kv in _pendingFieldColor)
            {
                var cell  = kv.Key;
                var field = FieldGenerator.GetFieldAt(cell.Item1, cell.Item2);
                if (field != null)
                {
                    _baseColors[cell] = field.fieldColor;
                    if (_spawnedCubes.TryGetValue(cell, out var cube) && cube != null)
                    {
                        ApplyCubeColor(cube, field.fieldColor);
                        cube.GetComponent<CollectorStructure>()?.SetFieldColor(field.fieldColor);
                        cube.GetComponent<NucleonHarvesterStructure>()?.SetFieldColor(field.fieldColor);
                    }
                    _resolvedFieldBuffer.Add(cell);
                }
                else if (Time.time - kv.Value > 10f)
                {
                    _resolvedFieldBuffer.Add(cell); // give up; leave the default colour
                }
            }
            foreach (var c in _resolvedFieldBuffer) _pendingFieldColor.Remove(c);
        }

        private void RefreshPowerTint()
        {
            if (!_powerStatusQueryReady || _powerStatusQuery.IsEmpty) return;
            // Stand down while the placement radius preview owns the cube colours.
            if (_radiusPreviewActive) return;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return;
            var em = world.EntityManager;

            var entities = _powerStatusQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var pos    = em.GetComponentData<GridPosition>(entities[i]);
                var status = em.GetComponentData<PowerStatus>(entities[i]);
                var cell   = (pos.Cell.x, pos.Cell.y);

                // Don't fight the hover highlight; it reasserts on the next tick after un-hover.
                if (cell == _hoveredCell) continue;
                if (_spawnedCubes.TryGetValue(cell, out var cube) && cube != null)
                    ApplyCubeColor(cube, status.IsConnected == 0 ? UnpoweredCubeColor : BaseColorFor(cell));
            }
            entities.Dispose();
        }

        private static Vector3 FacingEdgeOffset(OutputDirection dir, float cellSize)
        {
            float h = cellSize * 0.5f;
            return dir switch
            {
                OutputDirection.North => new Vector3(0,   0,  h),
                OutputDirection.East  => new Vector3( h,  0,  0),
                OutputDirection.South => new Vector3(0,   0, -h),
                OutputDirection.West  => new Vector3(-h,  0,  0),
                _                     => Vector3.zero
            };
        }

        /// <summary>
        /// Spawns a port-hole fixture on the building face at world cell (wx, wy), on the
        /// <paramref name="edgeDir"/> edge: a lit emission aperture (convex arch door) for outputs, or a
        /// recessed intake mouth (concave funnel) for inputs. The mesh/material are shared singletons
        /// (built once at the cell size) so every hole is allocation-free. Parented to the GridRenderer
        /// like the port arrows; returned so the caller can track it for cleanup.
        /// </summary>
        private GameObject CreateHoleFixture(int wx, int wy, OutputDirection edgeDir, bool isOutput, float cs, bool low)
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return null;

            EnsureHoleAssets(opaque, cs);

            var go = new GameObject(isOutput ? $"OutHole_{wx}_{wy}" : $"InHole_{wx}_{wy}");
            go.transform.SetParent(gridRenderer.transform, worldPositionStays: false);

            // Sit on the chosen edge, mid-height; rotate so the fixture's local +Z faces outward
            // (local +Z = North, so the Y rotation is edgeDir * 90 degrees).
            float midH = low ? cs * 0.08f : cs * (isOutput ? 0.30f : 0.42f);
            go.transform.localPosition = new Vector3(wx * cs, midH, wy * cs) + FacingEdgeOffset(edgeDir, cs);
            go.transform.localRotation = Quaternion.Euler(0f, (int)edgeDir * 90f, 0f);

            if (low) go.transform.localScale = Vector3.one * LowHoleScale;

            go.AddComponent<MeshFilter>().sharedMesh = isOutput ? _outHoleMesh : _inHoleMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial      = isOutput ? _outHoleMat : _inHoleMat;
            mr.shadowCastingMode   = ShadowCastingMode.Off;
            mr.receiveShadows      = false;
            mr.lightProbeUsage     = LightProbeUsage.Off;
            return go;
        }

        private void EnsureHoleAssets(Material opaque, float cs)
        {
            if (_outHoleMesh == null) _outHoleMesh = ApertureMeshBuilder.BuildArchDoor(cs * 0.22f, cs * 0.34f, 10);
            if (_inHoleMesh  == null) _inHoleMesh  = ApertureMeshBuilder.BuildIntakeMouth(cs * 0.16f, cs * 0.12f, 14);
            if (_outHoleMat == null)
            {
                _outHoleMat = new Material(opaque);
                _outHoleMat.SetColor("_BaseColor", OutputHoleColor);
            }
            if (_inHoleMat == null)
            {
                _inHoleMat = new Material(opaque);
                _inHoleMat.SetColor("_BaseColor", InputHoleColor);
            }
        }

        /// <summary>
        /// Removes all visual artifacts for the building anchored at (x, y). Callers follow up with a belt
        /// refresh, which also re-evaluates neighbouring buildings' arrows.
        /// Call after destroying the corresponding ECS entity.
        /// </summary>
        public void RemoveBuilding(int x, int y)
        {
            var cell = (x, y);

            int fw = 1, fh = 1;
            if (_footprints.TryGetValue(cell, out var fp)) { fw = fp.Item1; fh = fp.Item2; }

            for (int dx = 0; dx < fw; dx++)
                for (int dy = 0; dy < fh; dy++)
                {
                    gridRenderer?.SetTileHighlight(x + dx, y + dy, false);
                    _cellToAnchor.Remove((x + dx, y + dy));
                }

            if (_hoveredCell == cell) _hoveredCell = (-1, -1);

            _highlighted.Remove(cell);
            _footprints.Remove(cell);
            if (_selectedCell == cell) _selectedCell = (-1, -1);

            if (_spawnedCubes.TryGetValue(cell, out var cube))
            {
                if (cube != null) Destroy(cube);
                _spawnedCubes.Remove(cell);
            }
            _baseColors.Remove(cell);
            _pendingFieldColor.Remove(cell);

            if (_spawnedPortArrows.TryGetValue(cell, out var arrows))
            {
                foreach (var go in arrows)
                    if (go != null) Destroy(go);
                _spawnedPortArrows.Remove(cell);
            }
            _portArrows.Remove(cell);
            _portedCells.Remove(cell);
        }

        // ----------------------------------------------------------------
        // Selection highlight
        // ----------------------------------------------------------------

        /// <summary>
        /// Marks the building containing (x, y) as selected, lighting up the grid beneath it. A building
        /// tints its tiles only while selected. Clears any previous selection highlight.
        /// </summary>
        public void SelectBuilding(int x, int y)
        {
            DeselectBuilding();
            if (!_cellToAnchor.TryGetValue((x, y), out var anchor)) return;

            SetFootprintHighlight(anchor, true);
            _selectedCell = anchor;
        }

        /// <summary>Removes the selection highlight from the grid, if one is active.</summary>
        public void DeselectBuilding()
        {
            if (_selectedCell == (-1, -1)) return;
            SetFootprintHighlight(_selectedCell, false);
            _selectedCell = (-1, -1);
        }

        private void SetFootprintHighlight((int, int) anchor, bool on)
        {
            int fw = 1, fh = 1;
            if (_footprints.TryGetValue(anchor, out var fp)) { fw = fp.Item1; fh = fp.Item2; }
            for (int dx = 0; dx < fw; dx++)
                for (int dy = 0; dy < fh; dy++)
                    gridRenderer?.SetTileHighlight(anchor.Item1 + dx, anchor.Item2 + dy, on);
        }

        // ----------------------------------------------------------------
        // Hover highlight
        // ----------------------------------------------------------------

        /// <summary>
        /// Tints the building at the given grid cell (any cell within its footprint).
        /// Automatically restores the previously hovered building to its default color.
        /// </summary>
        public void SetHover(int x, int y)
        {
            if (!_cellToAnchor.TryGetValue((x, y), out var anchorCell)) return;
            if (anchorCell == _hoveredCell) return;

            // Restore previous
            if (_spawnedCubes.TryGetValue(_hoveredCell, out var prev) && prev != null)
                ApplyCubeColor(prev, BaseColorFor(_hoveredCell));

            _hoveredCell = anchorCell;

            if (_spawnedCubes.TryGetValue(anchorCell, out var cur) && cur != null)
                ApplyCubeColor(cur, HoverCubeColor);
        }

        /// <summary>Removes the hover highlight from whichever building is currently hovered.</summary>
        public void ClearHover()
        {
            if (_spawnedCubes.TryGetValue(_hoveredCell, out var prev) && prev != null)
                ApplyCubeColor(prev, BaseColorFor(_hoveredCell));
            _hoveredCell = (-1, -1);
        }

        // ----------------------------------------------------------------
        // Power-radius placement preview
        // ----------------------------------------------------------------

        /// <summary>
        /// Lights up every placed building whose footprint overlaps the circular power area of the source
        /// footprint rect (gridX, gridY, w, h) with influence <paramref name="radius"/> tiles (the same
        /// overlap test PowerGridSystem uses) and restores those that fall outside. Used both while
        /// positioning a power building and while a placed one is selected in the inspector. Pass
        /// <paramref name="exclude"/> to keep one anchor (e.g. the selected source itself) un-highlighted.
        /// Call <see cref="ClearRangeHighlight"/> when the preview/selection ends. RefreshPowerTint stands
        /// down while this is active so the two don't fight.
        /// </summary>
        public void HighlightBuildingsInRange(int gridX, int gridY, int w, int h, float radius,
                                              (int, int)? exclude = null)
        {
            _radiusPreviewActive = true;
            int gMaxX = gridX + w - 1;
            int gMaxY = gridY + h - 1;

            foreach (var kv in _spawnedCubes)
            {
                var anchor = kv.Key;
                var cube   = kv.Value;
                if (cube == null) continue;

                int fw = 1, fh = 1;
                if (_footprints.TryGetValue(anchor, out var fp)) { fw = fp.Item1; fh = fp.Item2; }
                bool inRange = !(exclude.HasValue && anchor == exclude.Value)
                    && PowerCoverageMath.FootprintWithinRadius(
                        gridX, gridY, gMaxX, gMaxY,
                        anchor.Item1, anchor.Item2, anchor.Item1 + fw - 1, anchor.Item2 + fh - 1, radius);

                if (inRange)
                {
                    ApplyCubeColor(cube, InRangePreviewColor);
                    _rangeHighlighted.Add(anchor);
                }
                else if (_rangeHighlighted.Remove(anchor))
                {
                    ApplyCubeColor(cube, BaseColorFor(anchor));
                }
            }
        }

        /// <summary>Restores every building tinted by the radius preview back to its base colour.</summary>
        public void ClearRangeHighlight()
        {
            foreach (var anchor in _rangeHighlighted)
                if (_spawnedCubes.TryGetValue(anchor, out var cube) && cube != null)
                    ApplyCubeColor(cube, BaseColorFor(anchor));
            _rangeHighlighted.Clear();
            _radiusPreviewActive = false;
        }

        /// <summary>The logical base colour for a cell (field colour for collectors, else white).</summary>
        private Color BaseColorFor((int, int) cell) =>
            _baseColors.TryGetValue(cell, out var c) ? c : DefaultCubeColor;

        private static void ApplyCubeColor(GameObject go, Color color)
        {
            // Route through PresenceReceiver so the presence boost is preserved on top
            var pr = go.GetComponent<PresenceReceiver>();
            if (pr != null) { pr.SetBaseColor(color); return; }

            // Fallback for any cube that doesn't have a PresenceReceiver (shouldn't happen)
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) return;
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }

        void OnDestroy()
        {
            ConveyorVisualizer.BeltsChanged -= RefreshPortArrows;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated)
            {
                if (_segmentQueryReady)     _segmentQuery.Dispose();
                if (_buildingQueryReady)    _buildingQuery.Dispose();
                if (_footprintQueryReady)   _footprintQuery.Dispose();
                if (_portQueryReady)        _portQuery.Dispose();
                if (_powerStatusQueryReady) _powerStatusQuery.Dispose();
            }

            foreach (var go in _spawnedCubes.Values)
                if (go != null) Destroy(go);
            _spawnedCubes.Clear();

            foreach (var list in _spawnedPortArrows.Values)
                foreach (var go in list)
                    if (go != null) Destroy(go);
            _spawnedPortArrows.Clear();

            if (_outHoleMesh != null) Destroy(_outHoleMesh);
            if (_inHoleMesh  != null) Destroy(_inHoleMesh);
            if (_outHoleMat  != null) Destroy(_outHoleMat);
            if (_inHoleMat   != null) Destroy(_inHoleMat);
        }
    }
}
