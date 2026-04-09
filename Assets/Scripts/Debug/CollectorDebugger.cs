using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Logs the state of all collector entities every <see cref="logInterval"/> seconds.
    /// Attach to any GameObject in the scene to diagnose collector output issues.
    /// Remove this component (or GameObject) once the issue is resolved.
    /// </summary>
    public class CollectorDebugger : MonoBehaviour
    {
        [SerializeField] private float logInterval = 2f;

        private float         _timer;
        private EntityManager _em;
        private EntityQuery   _collectorQuery;
        private bool          _ready;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) { Debug.LogWarning("[CollectorDebugger] No ECS world found."); return; }
            _em = world.EntityManager;
            _collectorQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<CollectorData>(),
                ComponentType.ReadOnly<BuildingData>(),
                ComponentType.ReadOnly<GridPosition>()
            );
            _ready = true;
            Debug.Log("[CollectorDebugger] Initialized — will log collector state every " + logInterval + "s.");
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ready)
                _collectorQuery.Dispose();
        }

        void Update()
        {
            if (!_ready) return;
            _timer += Time.deltaTime;
            if (_timer < logInterval) return;
            _timer = 0f;
            LogAll();
        }

        private void LogAll()
        {
            if (_collectorQuery.IsEmpty)
            {
                Debug.Log("[CollectorDebugger] No collector entities found. " +
                          "Ensure the building was placed via BuildingPlacer with a non-null outputDirection.");
                return;
            }

            var entities = _collectorQuery.ToEntityArray(Allocator.Temp);
            for (int i = 0; i < entities.Length; i++)
            {
                var e    = entities[i];
                var col  = _em.GetComponentData<CollectorData>(e);
                var bldg = _em.GetComponentData<BuildingData>(e);
                var pos  = _em.GetComponentData<GridPosition>(e);

                // Recipe output slots
                string recipeOut = "(no RecipeOutputSlot buffer)";
                if (_em.HasBuffer<RecipeOutputSlot>(e))
                {
                    var buf = _em.GetBuffer<RecipeOutputSlot>(e, isReadOnly: true);
                    recipeOut = buf.Length == 0
                        ? "EMPTY — CollectorSystem will skip this entity"
                        : "";
                    for (int j = 0; j < buf.Length; j++)
                        recipeOut += $"itemID={buf[j].ItemID} x{buf[j].Quantity} ";
                }

                // Output buffer
                string outBuf = "(no BuildingOutputSlot buffer)";
                if (_em.HasBuffer<BuildingOutputSlot>(e))
                {
                    var buf = _em.GetBuffer<BuildingOutputSlot>(e, isReadOnly: true);
                    outBuf = buf.Length == 0 ? "empty" : "";
                    for (int j = 0; j < buf.Length; j++)
                        outBuf += $"itemID={buf[j].ItemID} x{buf[j].Quantity} ";
                }

                // PlacedPortData — must have an Output port for ConveyorSystem to connect
                string ports = "(no PlacedPortData buffer)";
                if (_em.HasBuffer<PlacedPortData>(e))
                {
                    var buf = _em.GetBuffer<PlacedPortData>(e, isReadOnly: true);
                    ports = buf.Length == 0
                        ? "EMPTY — ConveyorSystem cannot connect (belt will not pull items)"
                        : "";
                    for (int j = 0; j < buf.Length; j++)
                        ports += $"[type={buf[j].PortType} cell=({buf[j].CellX},{buf[j].CellY}) facing={buf[j].Facing}] ";
                }

                // Output capacity
                int outputCap = 20;
                if (_em.HasComponent<BuildingInventoryConfig>(e))
                    outputCap = _em.GetComponentData<BuildingInventoryConfig>(e).OutputCapacity;

                float interval = col.OutputRate > 0f ? 1f / col.OutputRate : 1f;

                Debug.Log($"[CollectorDebugger] Entity {e.Index} @ cell ({pos.Cell.x},{pos.Cell.y})\n" +
                          $"  IsActive={bldg.IsActive} | BuildingType={bldg.BuildingType}\n" +
                          $"  Rate={col.OutputRate}/s | Interval={interval:F2}s | Timer={col.Timer:F2}s\n" +
                          $"  OutputCapacity={outputCap}\n" +
                          $"  RecipeOut: {recipeOut}\n" +
                          $"  OutputBuf: {outBuf}\n" +
                          $"  Ports: {ports}");
            }
            entities.Dispose();
        }
    }
}
