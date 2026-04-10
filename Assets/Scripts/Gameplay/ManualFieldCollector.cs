using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles field-based item collection in two ways:
    ///   1. Rate-based auto-collect — while the player is inside a field's proximity radius,
    ///      one item is added per (1 / field.collectionRate) seconds.
    ///   2. Manual tap — if the player taps directly on the field's collider while in range,
    ///      one item is added immediately and the auto-collect timer resets.
    ///
    /// Both paths fire <see cref="InventoryPopupController.Notify"/> so the player sees
    /// floating "+1 Item" text above their character.
    ///
    /// Scene setup: attach alongside <see cref="FieldProximityChecker"/> on the Character.
    /// Wire <see cref="proximityChecker"/> in the Inspector.
    /// </summary>
    public class ManualFieldCollector : MonoBehaviour
    {
        [SerializeField] private FieldProximityChecker proximityChecker;
        [Tooltip("Physics layers checked by the tap raycast. Leave as Everything when fields use the Default layer.")]
        [SerializeField] private LayerMask fieldLayerMask = ~0;

        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private float         _collectTimer;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                Debug.LogError("[ManualFieldCollector] No default DOTS world found.");
                return;
            }

            _em = world.EntityManager;
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>()
            );
        }

        // ----------------------------------------------------------------
        // Rate-based auto-collect
        // ----------------------------------------------------------------

        void Update()
        {
            if (proximityChecker == null) return;
            var currentField = proximityChecker.CurrentField;
            if (currentField == null) { _collectTimer = 0f; return; }

            var field = currentField.Field;
            if (field == null || field.drops == null || field.drops.Count == 0) return;

            _collectTimer += Time.deltaTime;
            float interval = 1f / Mathf.Max(0.01f, field.collectionRate);

            while (_collectTimer >= interval)
            {
                _collectTimer -= interval;
                CollectOne(field);
            }
        }

        // ----------------------------------------------------------------
        // Manual tap (called from PlayerInputRouter)
        // ----------------------------------------------------------------

        /// <summary>
        /// Returns true if the tap landed on a reachable field and an item was added.
        /// Resets the auto-collect timer so the next auto-collect is a full interval away.
        /// </summary>
        public bool TryCollect(Vector2 screenPos)
        {
            if (proximityChecker == null)
            {
                Debug.LogWarning("[FieldCollector] proximityChecker is not wired up.");
                return false;
            }

            var currentField = proximityChecker.CurrentField;
            if (currentField == null)
            {
                Debug.Log("[FieldCollector] No field in proximity range.");
                return false;
            }

            var field = currentField.Field;
            if (field == null || field.drops == null || field.drops.Count == 0)
            {
                Debug.LogWarning($"[FieldCollector] Field '{currentField.name}' has no drops configured.");
                return false;
            }

            // Confirm the tap landed on this field's collider
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (!Physics.Raycast(ray, out var hit, 100f, fieldLayerMask))
            {
                Debug.Log($"[FieldCollector] Raycast missed (layerMask={fieldLayerMask.value}). In-range field: {currentField.name}");
                return false;
            }

            Debug.Log($"[FieldCollector] Raycast hit '{hit.collider.gameObject.name}' on layer {hit.collider.gameObject.layer}.");

            var tappedInstance = hit.collider.GetComponentInParent<FieldInstance>();
            if (tappedInstance == null)
            {
                Debug.Log("[FieldCollector] Hit object has no FieldInstance — tapped something else.");
                return false;
            }

            if (tappedInstance != currentField)
            {
                Debug.Log($"[FieldCollector] Tapped field '{tappedInstance.name}' doesn't match in-range field '{currentField.name}'.");
                return false;
            }

            CollectOne(field);
            _collectTimer = 0f;
            return true;
        }

        // ----------------------------------------------------------------
        // Shared collection logic
        // ----------------------------------------------------------------

        private void CollectOne(FieldSO field)
        {
            var item = PickWeightedItem(field);
            if (item == null) return;
            if (_inventoryQuery.IsEmpty) return;

            var entity = _inventoryQuery.GetSingletonEntity();
            var buffer = _em.GetBuffer<InventorySlot>(entity);
            AddToBuffer(ref buffer, item.itemId, 1);

            InventoryPopupController.Notify(item.displayName, 1);
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
    }
}
