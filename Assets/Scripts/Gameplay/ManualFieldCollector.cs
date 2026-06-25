using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles field-based item collection.
    ///
    /// Tapping a field collects exactly <em>one</em> item and puts that field on a cooldown (a radial
    /// wheel rendered by <see cref="FieldCooldownIndicator"/>). The field cannot be tapped again until
    /// the cooldown elapses. Cooldown length comes from <see cref="FieldSO.tapCooldownSeconds"/>,
    /// shortened by unlocked research (<see cref="ResearchService.GetFieldCooldownMultiplier"/>) and the
    /// permanent "Quick Hands" upgrade (<see cref="UpgradeEffectType.FieldCooldownReduction"/>).
    ///
    /// Each collection fires <see cref="InventoryPopupController.Notify"/> so the player sees floating
    /// "+1 Item" text.
    /// </summary>
    public class ManualFieldCollector : MonoBehaviour
    {
        [Tooltip("Physics layers checked by the tap raycast. Leave as Everything when fields use the Default layer.")]
        [SerializeField] private LayerMask fieldLayerMask = ~0;

        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _tutorialQuery;
        private bool          _queriesReady;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                GameLogger.Error("[ManualFieldCollector] No default DOTS world found.");
                return;
            }

            _em = world.EntityManager;
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>()
            );
            _tutorialQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<TutorialStateData>()
            );
            _queriesReady = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_queriesReady && world != null && world.IsCreated)
            {
                _inventoryQuery.Dispose();
                _tutorialQuery.Dispose();
            }
        }

        // ----------------------------------------------------------------
        // Tap entry points (called from PlayerInputRouter)
        // ----------------------------------------------------------------

        /// <summary>
        /// Handles a tap on the given grid cell: collects one item (and starts the cooldown) when a
        /// field there is ready, or shows the tutorial toast when it is filtered.
        /// Returns true when a field occupied the cell and the tap was <em>consumed</em> — collected,
        /// on cooldown, or filtered. Returns false only when there is no field to act on, so the
        /// caller can fall back to the raycast path. This prevents a cooled-down tap from falling
        /// through to a neighbouring field (which would post the wrong tutorial warning).
        /// Primary tap path — works for clicks anywhere on the tile quad.
        /// </summary>
        public bool TryCollectAtGridCell(int cx, int cy)
        {
            // A building on this cell handles its own collection — open the inspector instead.
            if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(cx, cy))
                return false;

            var instance = FieldGenerator.GetFieldInstanceAt(cx, cy);
            if (instance == null) return false;

            TryCollectField(instance);
            return true; // a field owns this cell — consume the tap regardless of collect outcome
        }

        /// <summary>
        /// Handles a tap via Physics raycast against the field's 3D collider. Kept as a fallback for
        /// taps that land on the particle effect above the tile. Returns true when the ray hit a
        /// field (tap consumed), false when it hit nothing.
        /// </summary>
        public bool TryCollect(Vector2 screenPos)
        {
            if (Camera.main == null) return false;
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (!Physics.Raycast(ray, out var hit, 100f, fieldLayerMask))
                return false;

            var instance = hit.collider.GetComponentInParent<FieldInstance>();
            if (instance == null) return false;

            TryCollectField(instance);
            return true;
        }

        // ----------------------------------------------------------------
        // Shared collection logic
        // ----------------------------------------------------------------

        private bool TryCollectField(FieldInstance instance)
        {
            if (instance == null) return false;

            var field = instance.Field;
            if (field == null || field.drops == null || field.drops.Count == 0)
            {
                GameLogger.Warning($"[FieldCollector] Field '{instance.name}' has no drops configured.");
                return false;
            }

            // Tutorial gates — block or filter, posting a toast so the player gets feedback.
            if (!IsCollectionAllowed(out var filterType))
            {
                ToastService.Instance?.Post(FieldTypeToTriggerId(field.fieldType));
                return false;
            }

            if (filterType != FieldType.None && field.fieldType != filterType)
            {
                ToastService.Instance?.Post(FieldTypeToTriggerId(field.fieldType));
                return false;
            }

            // Cooldown gate — ignore taps while the field is recharging.
            var cooldown = instance.Cooldown;
            if (cooldown != null && cooldown.IsOnCooldown)
                return false;

            if (!CollectOne(field))
                return false;

            cooldown?.StartCooldown(EffectiveCooldownFor(field.tapCooldownSeconds));
            return true;
        }

        private bool CollectOne(FieldSO field)
        {
            var item = PickWeightedItem(field);
            if (item == null) return false;
            if (_inventoryQuery.IsEmpty) return false;

            var entity = _inventoryQuery.GetSingletonEntity();
            var buffer = _em.GetBuffer<InventorySlot>(entity);
            AddToBuffer(ref buffer, item.itemId, 1);

            InventoryPopupController.Notify(item.displayName, 1);
            TelemetryService.Instance?.NotifyFieldCollected();
            return true;
        }

        // ----------------------------------------------------------------
        // Cooldown math
        // ----------------------------------------------------------------

        /// <summary>Effective tap cooldown for the given base, applying research + prestige reductions.</summary>
        private static float EffectiveCooldownFor(float baseCooldown)
        {
            float researchMult = ResearchService.Instance != null
                ? ResearchService.Instance.GetFieldCooldownMultiplier() : 1f;
            float prestigeReduction = PersistentUpgradeService.Instance != null
                ? PersistentUpgradeService.Instance.GetEffect(UpgradeEffectType.FieldCooldownReduction) : 0f;
            return FieldCooldownCalculator.Effective(baseCooldown, researchMult, prestigeReduction);
        }

        /// <summary>
        /// Effective cooldown for a representative field, used by telemetry. Reads the base from the
        /// first spawned field (falls back to the JSON default) so the value tracks any rebalancing.
        /// </summary>
        public static float CurrentEffectiveFieldCooldown()
        {
            float baseCd = 1.5f;
            foreach (var kv in FieldGenerator.GetAllFields())
                if (kv.Value != null) { baseCd = kv.Value.tapCooldownSeconds; break; }
            return EffectiveCooldownFor(baseCd);
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        internal static ItemSO PickWeightedItem(FieldSO field)
        {
            if (field.drops.Count == 1)
                return field.drops[0].item;

            float total = 0f;
            foreach (var d in field.drops)
                total += Mathf.Max(0f, d.weight);

            if (total <= 0f)
                return field.drops[0].item;

            float roll = Random.Range(0f, total);
            float cumulative = 0f;
            foreach (var d in field.drops)
            {
                cumulative += Mathf.Max(0f, d.weight);
                if (roll <= cumulative)
                    return d.item;
            }

            return field.drops[field.drops.Count - 1].item;
        }

        private static void AddToBuffer(ref DynamicBuffer<InventorySlot> buf, int itemId, int qty)
        {
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ItemID != itemId) continue;
                buf[i] = new InventorySlot { ItemID = itemId, Quantity = buf[i].Quantity + qty };
                return;
            }
            buf.Add(new InventorySlot { ItemID = itemId, Quantity = qty });
        }

        // ----------------------------------------------------------------
        // Tutorial helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true if manual field collection is permitted given the current tutorial step.
        /// Also outputs the <paramref name="collectionFilter"/> field type so callers can
        /// restrict collection to a specific field without a second ECS read.
        /// Both values are read directly from TutorialFlowSO — no step names in code.
        /// </summary>
        private bool IsCollectionAllowed(out FieldType collectionFilter)
        {
            collectionFilter = FieldType.None;
            if (!_queriesReady || _tutorialQuery.IsEmpty) return true;

            var state = _tutorialQuery.GetSingleton<TutorialStateData>();
            if (!state.IsActive) return true;

            var flow = TutorialFlowSO.Current;
            if (flow == null || state.CurrentStepIndex >= flow.steps.Length) return true;

            var enter = flow.steps[state.CurrentStepIndex].onEnter;
            if (enter == null) return true;

            if (enter.blockCollection) return false;

            collectionFilter = enter.collectionFilter;
            return true;
        }

        private static string FieldTypeToTriggerId(FieldType fieldType) =>
            fieldType switch
            {
                FieldType.Quark  => "quark_field",
                FieldType.Lepton => "electron_field",
                _                => $"{fieldType.ToString().ToLowerInvariant()}_field"
            };
    }
}
