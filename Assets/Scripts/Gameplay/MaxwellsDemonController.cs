using System;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Controls the Maxwell's Demon deposit panel.
    ///
    /// Interaction model:
    ///   • Tap an inventory row  → selects the item; a draggable chip appears at the tap point.
    ///   • Drag the chip         → move it over the Demon drop zone to deposit.
    ///   • Tap the sell tray     → adjust quantity and confirm for a partial deposit.
    ///   • Tap elsewhere / close → deselects and hides the chip.
    ///
    /// Layout:
    ///   Landscape — Left: scrollable inventory, Right: Demon drop zone
    ///   Portrait  — Top: inventory (scrollbar-only scroll when overflow), Bottom: drop zone
    /// </summary>
    public class MaxwellsDemonController : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;


        // ── Panel elements ────────────────────────────────────────────────
        private VisualElement _panel;
        private VisualElement _bodyElement;
        private VisualElement _dividerElement;
        private VisualElement _inventoryColumnElement;
        private VisualElement _demonColumnElement;
        private ScrollView    _inventoryGrid;
        private VisualElement _dropZone;
        private Label         _earnPreviewLabel;
        private Button        _btnClose;
        private Button        _btnDepositAll;
        private VisualElement _dragGhost;
        private Label         _dragGhostLabel;

        // ── Drag state (set when chip is selected; drives earn preview) ───
        private int    _dragItemId    = -1;
        private string _dragItemName  = "";
        private int    _dragQuantity;
        private float  _dragSellValue;

        // ── Selection / sell tray state ───────────────────────────────────
        private int  _selectedItemId = -1;
        private int  _selectedMax;
        private int  _sellQty;

        // ── Sell tray elements ────────────────────────────────────────────
        private VisualElement _footerNormal;
        private VisualElement _sellTray;
        private Label         _sellItemNameLabel;
        private Label         _sellQtyLabel;
        private Button        _btnSellDecrease;
        private Button        _btnSellIncrease;
        private Button        _btnSellConfirm;

        // ── Chip drag gesture state ───────────────────────────────────────
        private bool _isDragging;
        private int  _activePointerId = -1;

        // ── Portrait/landscape layout tracking ────────────────────────────
        private bool _isPortrait;
        private bool _inventoryOverflows;

        // ── ECS ───────────────────────────────────────────────────────────
        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _progressQuery;
        private bool          _ecsReady;

        // ── Camera ────────────────────────────────────────────────────────
        private CameraController _cameraController;

        // ── CSS class constants ───────────────────────────────────────────
        private const string CSS_Item         = "demon-item";
        private const string CSS_ItemSelected = "demon-item--selected";
        private const string CSS_DropActive   = "demon-drop-zone--active";
        private const string CSS_Hidden       = "hidden";

        // ── Portrait layout CSS classes ───────────────────────────────────
        private const string CSS_BodyPortrait    = "demon-panel__body--portrait";
        private const string CSS_DividerPortrait = "demon-divider--portrait";
        private const string CSS_InvColPortrait  = "demon-inventory-column--portrait";
        private const string CSS_DmnColPortrait  = "demon-demon-column--portrait";

        // ── Tap vs. scroll discrimination ─────────────────────────────────
        private Vector2      _rowPointerDownPos;
        private const float  TapThreshold = 10f;

        public bool IsOpen { get; private set; }

        // ── Tutorial events ───────────────────────────────────────────────
        public event Action OnOpened;
        public event Action OnClosed;
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
            _cameraController = FindAnyObjectByType<CameraController>();

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

        void Update()
        {
            if (!IsOpen) return;
            bool portrait = Screen.height > Screen.width;
            if (portrait == _isPortrait) return;
            _isPortrait = portrait;
            ApplyOrientationLayout();
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
            _isPortrait = Screen.height > Screen.width;
            ApplyOrientationLayout();
            _cameraController?.SetPanLocked(true);
            RefreshInventory();
            _panel.RemoveFromClassList(CSS_Hidden);
            OnOpened?.Invoke();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _cameraController?.SetPanLocked(false);
            HideChip();
            DeselectItem();
            _panel?.AddToClassList(CSS_Hidden);
            OnClosed?.Invoke();
        }

        // ================================================================
        // Tutorial API
        // ================================================================

        private const string CSS_ItemTutorial = "demon-item--tutorial-highlight";

        public void HighlightTutorialItem(int itemId)
        {
            if (_inventoryGrid == null) return;
            _inventoryGrid.Query<VisualElement>(className: CSS_Item).ForEach(row =>
            {
                if (row.userData is int id && id == itemId)
                    row.AddToClassList(CSS_ItemTutorial);
            });
        }

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

            _bodyElement            = _panel?.Q(className: "demon-panel__body");
            _dividerElement         = _panel?.Q(className: "demon-divider");
            _inventoryColumnElement = _panel?.Q(className: "demon-inventory-column");
            _demonColumnElement     = _panel?.Q(className: "demon-demon-column");

            _footerNormal      = root.Q("demon-footer-normal");
            _sellTray          = root.Q("demon-sell-tray");
            _sellItemNameLabel = root.Q<Label>("demon-sell-item-name");
            _sellQtyLabel      = root.Q<Label>("demon-sell-qty");
            _btnSellDecrease   = root.Q<Button>("btn-sell-decrease");
            _btnSellIncrease   = root.Q<Button>("btn-sell-increase");
            _btnSellConfirm    = root.Q<Button>("btn-sell-confirm");

            _panel?.AddToClassList(CSS_Hidden);
            _dragGhost?.AddToClassList(CSS_Hidden);

            if (_btnClose      != null) _btnClose.clicked      += Close;
            if (_btnDepositAll != null) _btnDepositAll.clicked += DepositAll;
            if (_btnSellDecrease != null)
                _btnSellDecrease.clicked += () => { _sellQty = _sellQty <= 1 ? _selectedMax : _sellQty - 1; UpdateSellTray(); };
            if (_btnSellIncrease != null)
                _btnSellIncrease.clicked += () => { _sellQty = _sellQty >= _selectedMax ? 1 : _sellQty + 1; UpdateSellTray(); };
            if (_btnSellConfirm != null)
                _btnSellConfirm.clicked += OnSellConfirm;

            // Ghost chip drag handlers. The chip lives in _panel's absolute space,
            // outside the ScrollView, so ContentDragger can never interfere.
            if (_dragGhost != null)
            {
                _dragGhost.RegisterCallback<PointerDownEvent>(OnChipPointerDown);
                _dragGhost.RegisterCallback<PointerMoveEvent>(OnChipPointerMove);
                _dragGhost.RegisterCallback<PointerUpEvent>(OnChipPointerUp);
                _dragGhost.RegisterCallback<PointerCancelEvent>(OnChipPointerCancel);
            }

            // In portrait + overflow: block ContentDragger from scrolling via touch so the
            // scrollbar is the only scroll mechanism.  No drag-active check needed here —
            // the chip lives outside the ScrollView, so its events never reach this handler.
            _inventoryGrid?.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isPortrait && _inventoryOverflows)
                    evt.StopImmediatePropagation();
            }, TrickleDown.TrickleDown);
        }

        // ================================================================
        // Portrait/landscape layout
        // ================================================================

        private void ApplyOrientationLayout()
        {
            _bodyElement?.EnableInClassList(CSS_BodyPortrait, _isPortrait);
            _dividerElement?.EnableInClassList(CSS_DividerPortrait, _isPortrait);
            _inventoryColumnElement?.EnableInClassList(CSS_InvColPortrait, _isPortrait);
            _demonColumnElement?.EnableInClassList(CSS_DmnColPortrait, _isPortrait);

            _inventoryGrid?.schedule.Execute(UpdateScrollability);
        }

        private void UpdateScrollability()
        {
            if (_inventoryGrid == null) return;

            float contentH  = _inventoryGrid.contentContainer.layout.height;
            float viewportH = _inventoryGrid.contentViewport.layout.height;
            _inventoryOverflows = contentH > viewportH + 1f;

            _inventoryGrid.verticalScrollerVisibility = _inventoryOverflows
                ? (_isPortrait ? ScrollerVisibility.AlwaysVisible : ScrollerVisibility.Auto)
                : ScrollerVisibility.Hidden;
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

            GameLogger.Develop($"[Demon] Buffer length={buffer.Length}");

            bool anyItems = false;
            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                var itemSO = ItemDatabase.GetStatic(slot.ItemID);
                GameLogger.Develop($"[Demon] Slot {i}: itemId={slot.ItemID}, qty={slot.Quantity}, found={itemSO != null}");
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

            // Sync sell tray if an item is selected
            if (_selectedItemId >= 0)
            {
                int newMax = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i].ItemID == _selectedItemId && buffer[i].Quantity > 0)
                    {
                        newMax = buffer[i].Quantity;
                        break;
                    }
                }

                if (newMax <= 0)
                    DeselectItem();
                else
                {
                    _selectedMax = newMax;
                    _sellQty     = Mathf.Min(_sellQty, _selectedMax);
                    UpdateSellTray();
                    _inventoryGrid?.Query<VisualElement>(className: CSS_Item).ForEach(row =>
                    {
                        bool isSelected = row.userData is int id && id == _selectedItemId;
                        row.EnableInClassList(CSS_ItemSelected, isSelected);
                    });
                }
            }
            else
            {
                UpdateEarnPreview();
            }

            _inventoryGrid.schedule.Execute(UpdateScrollability);
        }

        // ── Row builder ───────────────────────────────────────────────────
        // Rows are tap-only. Dragging is done exclusively via the ghost chip
        // that appears at the tap position when SelectItem() is called.
        private VisualElement BuildItemRow(ItemSO item, int quantity)
        {
            var row = new VisualElement();
            row.AddToClassList(CSS_Item);
            row.userData = item.itemId;

            var nameLabel  = new Label(item.displayName);  nameLabel.AddToClassList("demon-item__name");
            var countLabel = new Label($"×{quantity}");    countLabel.AddToClassList("demon-item__count");
            var valLabel   = new Label($"+{CalcEntropy(item, quantity)}"); valLabel.AddToClassList("demon-item__value");
            row.Add(nameLabel);
            row.Add(countLabel);
            row.Add(valLabel);

            int   capturedItemId   = item.itemId;
            int   capturedQuantity = quantity;

            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                _rowPointerDownPos = evt.position;
                evt.PreventDefault();  // stop ScrollView starting a scroll on this touch
                evt.StopPropagation();
            });

            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                // Only register as a tap if the finger didn't travel far (not a scroll swipe)
                if (Vector2.Distance(evt.position, _rowPointerDownPos) <= TapThreshold)
                {
                    if (_selectedItemId == capturedItemId)
                        DeselectItem();
                    else
                        SelectItem(capturedItemId, capturedQuantity, evt.position);
                }
                evt.StopPropagation();
            });

            return row;
        }

        // ================================================================
        // Ghost chip — the only draggable element
        // ================================================================

        private void PositionChip(Vector2 screenPos)
        {
            if (_dragGhost == null || _panel == null) return;
            var local = _panel.WorldToLocal(screenPos);
            float w = _dragGhost.resolvedStyle.width;
            float h = _dragGhost.resolvedStyle.height;
            // Appear above and centred on the tap point so it's not hidden under the finger
            _dragGhost.style.left = local.x - (w > 0 ? w * 0.5f : 30f);
            _dragGhost.style.top  = local.y - (h > 0 ? h + 8f   : 36f);
        }

        private void HideChip()
        {
            _isDragging      = false;
            _activePointerId = -1;
            _dragItemId      = -1;
            _dragQuantity    = 0;
            _dragGhost?.AddToClassList(CSS_Hidden);
            _dropZone?.RemoveFromClassList(CSS_DropActive);
            UpdateEarnPreview();
        }

        private void OnChipPointerDown(PointerDownEvent evt)
        {
            if (_selectedItemId < 0) return;
            _activePointerId = evt.pointerId;
            _isDragging      = true;
            _dragGhost?.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnChipPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _activePointerId) return;
            MoveChip(evt.position);
            _dropZone?.EnableInClassList(CSS_DropActive, _dropZone.worldBound.Contains(evt.position));
            evt.StopPropagation();
        }

        private void OnChipPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _activePointerId) return;

            bool droppedOnDemon = _dropZone != null && _dropZone.worldBound.Contains(evt.position);
            int  savedItemId    = _dragItemId;
            int  savedQty       = _dragQuantity;

            _activePointerId = -1;
            _isDragging      = false;
            _dropZone?.RemoveFromClassList(CSS_DropActive);
            _dragGhost?.ReleasePointer(evt.pointerId);

            if (droppedOnDemon && savedItemId >= 0)
            {
                _dragGhost?.AddToClassList(CSS_Hidden);
                _dragItemId = -1;
                CommitDeposit(savedItemId, savedQty);
                DeselectItem();
            }
            else
            {
                // Missed the drop zone: leave chip at release position so the user
                // can grab it again without having to re-tap the inventory row.
                PositionChip(evt.position);
                _dragGhost?.RemoveFromClassList(CSS_Hidden);
            }

            evt.StopPropagation();
        }

        private void OnChipPointerCancel(PointerCancelEvent evt)
        {
            if (evt.pointerId != _activePointerId) return;
            _activePointerId = -1;
            _isDragging      = false;
            // Keep chip visible at last position — OS interrupt shouldn't deselect the item
            _dropZone?.RemoveFromClassList(CSS_DropActive);
        }

        private void MoveChip(Vector2 screenPos)
        {
            if (_dragGhost == null || _panel == null) return;
            var local = _panel.WorldToLocal(screenPos);
            float w = _dragGhost.resolvedStyle.width;
            float h = _dragGhost.resolvedStyle.height;
            _dragGhost.style.left = local.x - (w > 0 ? w * 0.5f : 0f);
            _dragGhost.style.top  = local.y - (h > 0 ? h * 0.5f : 0f);
        }

        // ================================================================
        // Selection / sell tray
        // ================================================================

        private void SelectItem(int itemId, int maxQty, Vector2 tapPosition)
        {
            // Reset previous drag state (hides old chip if any)
            HideChip();

            _selectedItemId = itemId;
            _selectedMax    = maxQty;
            _sellQty        = 1;

            var itemSO = ItemDatabase.GetStatic(itemId);

            // Populate chip label and position it at the tap point
            _dragItemId    = itemId;
            _dragQuantity  = maxQty;
            _dragSellValue = itemSO?.baseSellValue ?? 0f;
            _dragItemName  = itemSO?.displayName ?? "";
            if (_dragGhostLabel != null) _dragGhostLabel.text = _dragItemName;
            PositionChip(tapPosition);
            _dragGhost?.RemoveFromClassList(CSS_Hidden);

            if (_sellItemNameLabel != null)
                _sellItemNameLabel.text = _dragItemName;

            _inventoryGrid?.Query<VisualElement>(className: CSS_Item).ForEach(row =>
            {
                bool isSelected = row.userData is int id && id == itemId;
                row.EnableInClassList(CSS_ItemSelected, isSelected);
            });

            UpdateSellTray();
            _footerNormal?.AddToClassList(CSS_Hidden);
            _sellTray?.RemoveFromClassList(CSS_Hidden);
            UpdateEarnPreview();
        }

        private void DeselectItem()
        {
            HideChip();
            _selectedItemId = -1;
            _inventoryGrid?.Query<VisualElement>(className: CSS_ItemSelected)
                .ForEach(e => e.RemoveFromClassList(CSS_ItemSelected));
            _sellTray?.AddToClassList(CSS_Hidden);
            _footerNormal?.RemoveFromClassList(CSS_Hidden);
            UpdateEarnPreview();
        }

        private void UpdateSellTray()
        {
            if (_sellQtyLabel != null) _sellQtyLabel.text = _sellQty.ToString();

            var itemSO = ItemDatabase.GetStatic(_selectedItemId);
            if (_btnSellConfirm != null && itemSO != null)
            {
                long val = CalcEntropy(itemSO, _sellQty);
                _btnSellConfirm.text = $"Sell (+{val} entropy)";
            }
        }

        private void OnSellConfirm()
        {
            if (_selectedItemId < 0) return;
            CommitDeposit(_selectedItemId, _sellQty);
            DeselectItem();
        }

        // ================================================================
        // Deposit logic
        // ================================================================

        private void CommitDeposit(int itemId, int quantity)
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || _progressQuery.IsEmpty) return;

            var itemSO = ItemDatabase.GetStatic(itemId);
            if (itemSO == null) return;

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

            long earned  = CalcEntropy(itemSO, removed);
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency += earned;
            _progressQuery.SetSingleton(progress);

            InventoryPopupController.Notify("entropy", (int)earned);
            OnItemsDeposited?.Invoke();
            RefreshInventory();
        }

        private void DepositAll()
        {
            if (!_ecsReady || _inventoryQuery.IsEmpty || _progressQuery.IsEmpty) return;
            DeselectItem();

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

        private long CalcEntropy(ItemSO item, int quantity)
        {
            float multiplier = GetMultiplier(item);
            return (long)(item.baseSellValue * quantity * multiplier);
        }

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
