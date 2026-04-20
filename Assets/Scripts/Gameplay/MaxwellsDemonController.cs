using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Controls the Maxwell's Demon deposit panel.
    ///
    /// Opened by BuildingInspectorController when the player taps a building with EntropySinkTag.
    ///
    /// Layout:
    ///   Left  — scrollable grid of the player's harvestable particles (drag sources)
    ///   Right — the Demon drop zone (drag target)
    ///
    /// On drop, items are removed from the player's ECS InventorySlot buffer and
    ///   baseSellValue × quantity × GetMultiplier()
    /// is credited to PlayerProgressData.BaseCurrency.
    ///
    /// Multiplier hook: override GetMultiplier(ItemSO) to inject research/prestige bonuses.
    ///
    /// Scene setup:
    ///   • Attach to the same GameObject as HUDController (or any persistent object).
    ///   • Wire _uiDocument in the Inspector.
    ///   • BuildingInspectorController calls Open() / Close() directly.
    /// </summary>
    public class MaxwellsDemonController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;


        // ── Panel elements ────────────────────────────────────────────────
        private VisualElement _panel;
        private ScrollView    _inventoryGrid;
        private VisualElement _dropZone;
        private Label         _earnPreviewLabel;
        private Button        _btnClose;
        private Button        _btnDepositAll;
        private VisualElement _dragGhost;
        private Label         _dragGhostLabel;

        // ── Drag state ────────────────────────────────────────────────────
        private int    _dragItemId   = -1;
        private int    _dragQuantity;
        private float  _dragSellValue;

        // ── ECS ───────────────────────────────────────────────────────────
        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _progressQuery;
        private bool          _ecsReady;

        // ── CSS class constants ───────────────────────────────────────────
        private const string CSS_Item        = "demon-item";
        private const string CSS_ItemDragging= "demon-item--dragging";
        private const string CSS_DropActive  = "demon-drop-zone--active";
        private const string CSS_Hidden      = "hidden";

        public bool IsOpen { get; private set; }

        // ── Tutorial events ───────────────────────────────────────────────
        /// <summary>Fired when the panel is opened by the player.</summary>
        public event Action OnOpened;
        /// <summary>Fired when the panel is closed by the player.</summary>
        public event Action OnClosed;
        /// <summary>Fired after any successful deposit (manual drag or Deposit All).</summary>
        public event Action OnItemsDeposited;

        // ================================================================
        // Unity lifecycle
        // ================================================================

        void Start()
        {
            if (_uiDocument == null)
                _uiDocument = FindAnyObjectByType<UIDocument>();
            if (_uiDocument == null) return;


            BindElements(_uiDocument.rootVisualElement);

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _em             = world.EntityManager;
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>()
            );
            _progressQuery = _em.CreateEntityQuery(
                ComponentType.ReadWrite<PlayerProgressData>()
            );
            _ecsReady = true;
        }

        void OnDestroy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null && world.IsCreated && _ecsReady)
            {
                _inventoryQuery.Dispose();
                _progressQuery.Dispose();
            }
        }

        // ================================================================
        // Public API
        // ================================================================

        public void Open()
        {
            if (_panel == null) return;

            // ECS world may not have been ready at Start() — retry here.
            if (!_ecsReady)
            {
                var world = World.DefaultGameObjectInjectionWorld;
                if (world == null) return;
                _em             = world.EntityManager;
                _inventoryQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerInventoryTag>(),
                    ComponentType.ReadWrite<InventorySlot>()
                );
                _progressQuery = _em.CreateEntityQuery(
                    ComponentType.ReadWrite<PlayerProgressData>()
                );
                _ecsReady = true;
            }

            IsOpen = true;
            RefreshInventory();
            _panel.RemoveFromClassList(CSS_Hidden);
            OnOpened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            CancelDrag();
            _panel?.AddToClassList(CSS_Hidden);
            OnClosed?.Invoke();
        }

        // ================================================================
        // Tutorial API
        // ================================================================

        private const string CSS_ItemTutorial = "demon-item--tutorial-highlight";

        /// <summary>
        /// Adds a pulsing highlight border to every inventory row whose item ID matches.
        /// Call <see cref="ClearTutorialHighlight"/> to remove it when the step is done.
        /// </summary>
        public void HighlightTutorialItem(int itemId)
        {
            if (_inventoryGrid == null) return;
            _inventoryGrid.Query<VisualElement>(className: CSS_Item).ForEach(row =>
            {
                // Each row stores its itemId in UserData when built — check it.
                if (row.userData is int id && id == itemId)
                    row.AddToClassList(CSS_ItemTutorial);
            });
        }

        /// <summary>Removes the tutorial highlight from all inventory rows.</summary>
        public void ClearTutorialHighlight()
        {
            _inventoryGrid?.Query<VisualElement>(className: CSS_ItemTutorial)
                .ForEach(e => e.RemoveFromClassList(CSS_ItemTutorial));
        }

        // ================================================================
        // Element binding
        // ================================================================

        private void BindElements(VisualElement root)
        {
            _panel            = root.Q("demon-panel");
            _inventoryGrid    = root.Q<ScrollView>("demon-inventory-grid");
            _dropZone         = root.Q("demon-drop-zone");
            _earnPreviewLabel = root.Q<Label>("demon-earn-preview");
            _btnClose         = root.Q<Button>("btn-demon-close");
            _btnDepositAll    = root.Q<Button>("btn-demon-deposit-all");
            _dragGhost        = root.Q("demon-drag-ghost");
            _dragGhostLabel   = root.Q<Label>("demon-drag-ghost__label");

            _panel?.AddToClassList(CSS_Hidden);
            _dragGhost?.AddToClassList(CSS_Hidden);

            if (_btnClose    != null) _btnClose.clicked    += Close;
            if (_btnDepositAll != null) _btnDepositAll.clicked += DepositAll;
        }

        // ================================================================
        // Inventory population
        // ================================================================

        private void RefreshInventory()
        {
            if (_inventoryGrid == null || _inventoryQuery.IsEmpty) return;

            _inventoryGrid.Clear();

            var invEntity = _inventoryQuery.GetSingletonEntity();
            var buffer    = _em.GetBuffer<InventorySlot>(invEntity, isReadOnly: true);

            Debug.Log($"[Demon] Buffer length={buffer.Length}");

            bool anyItems = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                Debug.Log($"[Demon] Slot {i}: itemId={slot.ItemID}, qty={slot.Quantity}, found={itemSO != null}");
                if (slot.Quantity <= 0) continue;
                if (itemSO == null) continue;

                _inventoryGrid.Add(BuildItemRow(itemSO, slot.Quantity));
                anyItems = true;
            }

            if (!anyItems)
            {
                var empty = new Label("Inventory is empty.");
                empty.AddToClassList("demon-empty-label");
                _inventoryGrid.Add(empty);
            }

            UpdateEarnPreview();
        }

        private VisualElement BuildItemRow(ItemSO item, int quantity)
        {
            var row = new VisualElement();
            row.AddToClassList(CSS_Item);
            row.userData = item.itemId; // used by HighlightTutorialItem

            var nameLabel  = new Label(item.displayName);
            nameLabel.AddToClassList("demon-item__name");

            var countLabel = new Label($"×{quantity}");
            countLabel.AddToClassList("demon-item__count");

            long value    = CalcEntropy(item, quantity);
            var valLabel  = new Label($"+{value}");
            valLabel.AddToClassList("demon-item__value");

            row.Add(nameLabel);
            row.Add(countLabel);
            row.Add(valLabel);

            // ── Drag-and-drop via pointer capture ──────────────────────
            int   capturedItemId    = item.itemId;
            int   capturedQuantity  = quantity;
            float capturedSellValue = item.baseSellValue;
            string capturedName    = item.displayName;

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                _dragItemId    = capturedItemId;
                _dragQuantity  = capturedQuantity;
                _dragSellValue = capturedSellValue;

                row.AddToClassList(CSS_ItemDragging);
                row.CapturePointer(evt.pointerId);

                if (_dragGhost != null && _dragGhostLabel != null)
                {
                    _dragGhostLabel.text = capturedName;
                    _dragGhost.RemoveFromClassList(CSS_Hidden);
                    MoveDragGhost(evt.position);
                }

                UpdateEarnPreview();
                evt.StopPropagation();
            });

            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!row.HasPointerCapture(evt.pointerId)) return;

                MoveDragGhost(evt.position);

                // Highlight drop zone while hovering over it
                if (_dropZone != null)
                {
                    if (_dropZone.worldBound.Contains(evt.position))
                        _dropZone.AddToClassList(CSS_DropActive);
                    else
                        _dropZone.RemoveFromClassList(CSS_DropActive);
                }

                evt.StopPropagation();
            });

            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!row.HasPointerCapture(evt.pointerId)) return;
                row.ReleasePointer(evt.pointerId);

                bool droppedOnDemon = _dropZone != null &&
                                      _dropZone.worldBound.Contains(evt.position);
                if (droppedOnDemon && _dragItemId >= 0)
                    CommitDeposit(_dragItemId, _dragQuantity);

                CancelDrag();
                evt.StopPropagation();
            });

            return row;
        }

        // ================================================================
        // Drag helpers
        // ================================================================

        private void MoveDragGhost(Vector2 panelPos)
        {
            if (_dragGhost == null || _panel == null) return;
            // Convert from panel (root) space → demon panel local space
            var local = _panel.WorldToLocal(panelPos);
            float w = _dragGhost.resolvedStyle.width;
            float h = _dragGhost.resolvedStyle.height;
            _dragGhost.style.left = local.x - (w > 0 ? w * 0.5f : 40f);
            _dragGhost.style.top  = local.y - (h > 0 ? h * 0.5f : 16f);
        }

        private void CancelDrag()
        {
            _dragItemId   = -1;
            _dragQuantity = 0;
            _dragGhost?.AddToClassList(CSS_Hidden);
            _dropZone?.RemoveFromClassList(CSS_DropActive);

            if (_inventoryGrid != null)
            {
                _inventoryGrid.Query<VisualElement>(className: CSS_ItemDragging)
                    .ForEach(e => e.RemoveFromClassList(CSS_ItemDragging));
            }

            UpdateEarnPreview();
        }

        // ================================================================
        // Deposit logic
        // ================================================================

        private void CommitDeposit(int itemId, int quantity)
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || _progressQuery.IsEmpty) return;

            var itemSO = ItemDatabase.GetStatic(itemId);
            if (itemSO == null) return;

            // -- Remove from player inventory --
            var invEntity = _inventoryQuery.GetSingletonEntity();
            var buffer    = _em.GetBuffer<InventorySlot>(invEntity);

            int removed = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].ItemID != itemId) continue;
                int take  = Mathf.Min(buffer[i].Quantity, quantity);
                removed  += take;
                var slot  = buffer[i];
                slot.Quantity -= take;
                buffer[i] = slot;
                break;
            }

            if (removed <= 0) return;

            // -- Credit entropy --
            long earned  = CalcEntropy(itemSO, removed);
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency += earned;
            _progressQuery.SetSingleton(progress);

            // -- Feedback --
            InventoryPopupController.Notify("entropy", (int)earned);

            // -- Tutorial hook --
            OnItemsDeposited?.Invoke();

            // -- Refresh panel --
            RefreshInventory();
        }

        private void DepositAll()
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || _progressQuery.IsEmpty) return;

            var invEntity = _inventoryQuery.GetSingletonEntity();
            var buffer    = _em.GetBuffer<InventorySlot>(invEntity);
            long totalEarned = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                if (slot.Quantity <= 0) continue;

                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                if (itemSO == null) continue;

                totalEarned += CalcEntropy(itemSO, slot.Quantity);

                var updated = buffer[i];
                updated.Quantity = 0;
                buffer[i] = updated;
            }

            if (totalEarned <= 0) return;

            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency += totalEarned;
            _progressQuery.SetSingleton(progress);

            InventoryPopupController.Notify("entropy", (int)totalEarned);
            OnItemsDeposited?.Invoke();
            RefreshInventory();
        }

        // ================================================================
        // Entropy calculation
        // ================================================================

        /// <summary>
        /// Returns the entropy earned for depositing <paramref name="quantity"/> of
        /// <paramref name="item"/>. Extend this method to apply research/prestige multipliers.
        /// </summary>
        private long CalcEntropy(ItemSO item, int quantity)
        {
            float multiplier = GetMultiplier(item);
            return (long)(item.baseSellValue * quantity * multiplier);
        }

        /// <summary>
        /// Override point for research and prestige entropy multipliers.
        /// Returns 1f (no bonus) until unlockable upgrades are implemented.
        /// </summary>
        protected virtual float GetMultiplier(ItemSO item) => 1f;

        // ================================================================
        // Earn preview
        // ================================================================

        private void UpdateEarnPreview()
        {
            if (_earnPreviewLabel == null) return;

            if (_dragItemId >= 0)
            {
                long preview = (long)(_dragSellValue * _dragQuantity * GetMultiplier(null));
                _earnPreviewLabel.text = $"Will earn: +{preview} entropy";
                return;
            }

            // Show total of all harvestable items in inventory
            if (!_ecsReady || _inventoryQuery.IsEmpty) { _earnPreviewLabel.text = ""; return; }

            var invEntity = _inventoryQuery.GetSingletonEntity();
            var buffer    = _em.GetBuffer<InventorySlot>(invEntity, isReadOnly: true);
            long total    = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                if (slot.Quantity <= 0) continue;
                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                if (itemSO == null) continue;
                total += CalcEntropy(itemSO, slot.Quantity);
            }

            _earnPreviewLabel.text = total > 0 ? $"Total available: {total} entropy" : "";
        }
    }
}
