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
    ///   Landscape — Left: scrollable inventory (drag sources), Right: Demon drop zone (drag target)
    ///   Portrait  — Top: inventory, Bottom: Demon drop zone (50/50 vertical split)
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

        // ── Drag state ────────────────────────────────────────────────────
        private int    _dragItemId    = -1;
        private string _dragItemName  = "";
        private int    _dragQuantity;
        private float  _dragSellValue;

        // ── Selection / sell tray state ───────────────────────────────────
        private int  _selectedItemId = -1;
        private int  _selectedMax;
        private int  _sellQty;

        // ── New sell tray elements ────────────────────────────────────────
        private VisualElement _footerNormal;
        private VisualElement _sellTray;
        private Label         _sellItemNameLabel;
        private Label         _sellQtyLabel;
        private Button        _btnSellDecrease;
        private Button        _btnSellIncrease;
        private Button        _btnSellConfirm;

        // ── Per-gesture state ─────────────────────────────────────────────
        private Vector2 _pointerDownPos;
        private bool    _isDragging;
        private int     _activePointerId = -1;

        // ── Portrait/landscape layout tracking ────────────────────────────
        private bool _isPortrait;

        // ── ECS ───────────────────────────────────────────────────────────
        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _progressQuery;
        private bool          _ecsReady;

        // ── Camera ────────────────────────────────────────────────────────
        private CameraController _cameraController;

        // ── CSS class constants ───────────────────────────────────────────
        private const string CSS_Item         = "demon-item";
        private const string CSS_ItemDragging = "demon-item--dragging";
        private const string CSS_ItemSelected = "demon-item--selected";
        private const string CSS_DropActive   = "demon-drop-zone--active";
        private const string CSS_Hidden       = "hidden";

        // ── Portrait layout CSS classes ───────────────────────────────────
        private const string CSS_BodyPortrait    = "demon-panel__body--portrait";
        private const string CSS_DividerPortrait = "demon-divider--portrait";
        private const string CSS_InvColPortrait  = "demon-inventory-column--portrait";
        private const string CSS_DmnColPortrait  = "demon-demon-column--portrait";

        // ── Drag threshold (pixels before a touch is treated as a drag) ───
        private const float DragThreshold = 10f;

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
            CancelDrag();
            DeselectItem();
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

            // Panel-level fallback drag handlers: fire when pointer capture routing fails on
            // mobile (i.e. the row-level handlers never receive the event). Each handler
            // no-ops if _activePointerId is unset or the pointerId doesn't match.
            _panel?.RegisterCallback<PointerMoveEvent>(OnPanelPointerMove);
            _panel?.RegisterCallback<PointerUpEvent>(OnPanelPointerUp);
            _panel?.RegisterCallback<PointerCancelEvent>(OnPanelPointerCancel);

            // ScrollView registers its scroll-tracking handler in the trickle-down phase,
            // so it fires before our bubbling row handlers and ignores StopPropagation.
            // We intercept PointerMove in trickle-down to prevent scroll from firing:
            //   • In portrait: always suppress touch-drag scroll (scrollbar is the only scroll).
            //   • In landscape: suppress only while an item drag is active.
            _inventoryGrid?.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (_isPortrait || (_activePointerId >= 0 && evt.pointerId == _activePointerId))
                    evt.PreventDefault();
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

            // In portrait, keep the scrollbar always visible so it's the clear scroll mechanism.
            if (_inventoryGrid != null)
                _inventoryGrid.verticalScrollerVisibility = _isPortrait
                    ? ScrollerVisibility.AlwaysVisible
                    : ScrollerVisibility.Auto;
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
                    // Re-apply selected highlight on the freshly rebuilt rows
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
                _dragItemId      = capturedItemId;
                _dragItemName    = capturedName;
                _dragQuantity    = capturedQuantity;
                _dragSellValue   = capturedSellValue;
                _pointerDownPos  = evt.position;
                _isDragging      = false;
                _activePointerId = evt.pointerId;

                row.CapturePointer(evt.pointerId);
                // Prevent the ScrollView from treating this touch as a scroll gesture.
                evt.PreventDefault();
                evt.StopPropagation();
            });

            row.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (evt.pointerId != _activePointerId) return;

                if (!_isDragging && Vector2.Distance(evt.position, _pointerDownPos) > DragThreshold)
                {
                    _isDragging = true;
                    row.AddToClassList(CSS_ItemDragging);
                    if (_dragGhost != null && _dragGhostLabel != null)
                    {
                        _dragGhostLabel.text = capturedName;
                        _dragGhost.RemoveFromClassList(CSS_Hidden);
                    }
                    UpdateEarnPreview();
                }

                if (_isDragging)
                {
                    MoveDragGhost(evt.position);
                    _dropZone?.EnableInClassList(CSS_DropActive,
                        _dropZone.worldBound.Contains(evt.position));
                }

                // StopPropagation prevents the panel-level fallback from double-processing
                // when capture routing works correctly.
                evt.StopPropagation();
            });

            row.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != _activePointerId) return;

                // Snapshot state before CancelDrag clears it.
                bool wasDragging  = _isDragging;
                int  savedItemId  = _dragItemId;
                int  savedQty     = _dragQuantity;
                _activePointerId  = -1;

                // Reset drag state before ReleasePointer so PointerCaptureOutEvent
                // sees _isDragging == false and doesn't call CancelDrag a second time.
                CancelDrag();
                row.ReleasePointer(evt.pointerId);

                if (wasDragging)
                {
                    bool droppedOnDemon = _dropZone != null &&
                                          _dropZone.worldBound.Contains(evt.position);
                    if (droppedOnDemon && savedItemId >= 0)
                        CommitDeposit(savedItemId, savedQty);
                }
                else
                {
                    if (_selectedItemId == capturedItemId)
                        DeselectItem();
                    else
                        SelectItem(capturedItemId, capturedQuantity);
                }

                evt.StopPropagation();
            });

            row.RegisterCallback<PointerCancelEvent>(evt =>
            {
                if (evt.pointerId != _activePointerId) return;
                _activePointerId = -1;
                CancelDrag();
            });

            // Fires when pointer capture is lost unexpectedly (e.g. OS interrupt).
            row.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                if (_isDragging)
                {
                    _activePointerId = -1;
                    CancelDrag();
                }
            });

            return row;
        }

        // ================================================================
        // Panel-level fallback drag handlers
        //
        // On some Android/iOS builds, UIElements pointer capture routing is
        // unreliable — move/up events reach the element under the finger
        // rather than the capturing row. These handlers catch those events
        // at the panel level so drag + tap still work when that happens.
        //
        // They are no-ops when the row-level handlers already handled the
        // event (row sets _activePointerId = -1 first, then StopPropagation
        // prevents the panel from seeing it at all).
        // ================================================================

        private void OnPanelPointerMove(PointerMoveEvent evt)
        {
            if (_activePointerId < 0 || evt.pointerId != _activePointerId) return;

            if (!_isDragging && Vector2.Distance(evt.position, _pointerDownPos) > DragThreshold)
            {
                _isDragging = true;
                if (_dragGhost != null && _dragGhostLabel != null)
                {
                    _dragGhostLabel.text = _dragItemName;
                    _dragGhost.RemoveFromClassList(CSS_Hidden);
                }
                // Highlight the source row (capture routing failed, so we query for it).
                _inventoryGrid?.Query<VisualElement>(className: CSS_Item).ForEach(r =>
                {
                    if (r.userData is int id && id == _dragItemId)
                        r.AddToClassList(CSS_ItemDragging);
                });
                UpdateEarnPreview();
            }

            if (_isDragging)
            {
                MoveDragGhost(evt.position);
                _dropZone?.EnableInClassList(CSS_DropActive,
                    _dropZone.worldBound.Contains(evt.position));
            }
        }

        private void OnPanelPointerUp(PointerUpEvent evt)
        {
            if (_activePointerId < 0 || evt.pointerId != _activePointerId) return;

            bool wasDragging = _isDragging;
            int  savedItemId = _dragItemId;
            int  savedQty    = _dragQuantity;
            _activePointerId = -1;
            CancelDrag();

            if (wasDragging)
            {
                if (savedItemId >= 0 && _dropZone != null &&
                    _dropZone.worldBound.Contains(evt.position))
                    CommitDeposit(savedItemId, savedQty);
            }
            else if (savedItemId >= 0)
            {
                if (_selectedItemId == savedItemId)
                    DeselectItem();
                else
                    SelectItem(savedItemId, savedQty);
            }
        }

        private void OnPanelPointerCancel(PointerCancelEvent evt)
        {
            if (_activePointerId < 0 || evt.pointerId != _activePointerId) return;
            _activePointerId = -1;
            CancelDrag();
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
            // Fall back to 0 when size isn't resolved yet (first drag frame) so the
            // ghost doesn't jump by a platform-scaled offset on high-DPI Android.
            _dragGhost.style.left = local.x - (w > 0 ? w * 0.5f : 0f);
            _dragGhost.style.top  = local.y - (h > 0 ? h * 0.5f : 0f);
        }

        private void CancelDrag()
        {
            _isDragging   = false;
            _activePointerId = -1;
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
        // Selection / sell tray
        // ================================================================

        private void SelectItem(int itemId, int maxQty)
        {
            _selectedItemId = itemId;
            _selectedMax    = maxQty;
            _sellQty        = 1;

            var itemSO = ItemDatabase.GetStatic(itemId);
            if (_sellItemNameLabel != null)
                _sellItemNameLabel.text = itemSO?.displayName ?? "";

            _inventoryGrid?.Query<VisualElement>(className: CSS_Item).ForEach(row =>
            {
                bool isSelected = row.userData is int id && id == itemId;
                row.EnableInClassList(CSS_ItemSelected, isSelected);
            });

            UpdateSellTray();
            _footerNormal?.AddToClassList(CSS_Hidden);
            _sellTray?.RemoveFromClassList(CSS_Hidden);
        }

        private void DeselectItem()
        {
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
        /// Override point for research and prestige entropy multipliers. Base returns 1f.
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
