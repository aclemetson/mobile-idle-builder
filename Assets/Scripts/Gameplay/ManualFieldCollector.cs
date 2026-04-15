using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Handles field-based item collection.
    ///
    /// A field must be both in proximity range AND explicitly tapped to become the
    /// <em>active</em> field. Only the active field auto-collects items on its timer.
    /// Only one field can be active at a time — tapping a different in-range field
    /// switches the active field immediately. Walking out of range deactivates it.
    ///
    /// Both the tap and auto-timer paths fire <see cref="InventoryPopupController.Notify"/>
    /// so the player sees floating "+1 Item" text.
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

        /// <summary>The field the player has explicitly activated by tapping. Null when none.</summary>
        public FieldInstance ActiveField => _activeField;
        private FieldInstance _activeField;

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
            // Deactivate if the player walked out of range of the active field.
            if (_activeField != null && proximityChecker != null &&
                !proximityChecker.IsInRange(_activeField))
            {
                _activeField  = null;
                _collectTimer = 0f;
            }

            if (_activeField == null) return;

            var field = _activeField.Field;
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
        /// Activates the tapped field (if it is within proximity range) and collects one item.
        /// Switches away from any previously active field. Returns true if a field was activated.
        /// </summary>
        public bool TryCollect(Vector2 screenPos)
        {
            if (proximityChecker == null)
            {
                Debug.LogWarning("[FieldCollector] proximityChecker is not wired up.");
                return false;
            }

            // Raycast to find which field was tapped.
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));
            if (!Physics.Raycast(ray, out var hit, 100f, fieldLayerMask))
                return false;

            var tappedInstance = hit.collider.GetComponentInParent<FieldInstance>();
            if (tappedInstance == null)
                return false;

            // Must be within the proximity radius — not necessarily the single closest field,
            // so the player can activate either of two adjacent fields.
            if (!proximityChecker.IsInRange(tappedInstance))
            {
                Debug.Log($"[FieldCollector] Tapped '{tappedInstance.name}' is out of range.");
                return false;
            }

            var field = tappedInstance.Field;
            if (field == null || field.drops == null || field.drops.Count == 0)
            {
                Debug.LogWarning($"[FieldCollector] Field '{tappedInstance.name}' has no drops configured.");
                return false;
            }

            // Switch active field (deactivates the previous one automatically).
            _activeField  = tappedInstance;
            _collectTimer = 0f;

            // Collect one item immediately on tap.
            CollectOne(field);
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
