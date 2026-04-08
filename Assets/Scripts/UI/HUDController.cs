using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    [RequireComponent(typeof(UIDocument))]
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private ManualCraftService          craftService;
        [SerializeField] private BuildingPlacementController placementController;
        [SerializeField] private float                       notificationDuration = 3f;

        // ---- ECS ----
        private EntityManager _em;
        private EntityQuery   _progressQuery;
        private EntityQuery   _prestigeQuery;
        private EntityQuery   _powerQuery;
        private bool          _ecsReady;

        // ---- Panels ----
        private VisualElement _recipePanel, _buildingsPanel, _codexPanel,
                              _researchPanel, _upgradesPanel, _prestigePanel,
                              _placementOverlay;
        private VisualElement[] _allPanels;

        // ---- Panel content ----
        private ScrollView _recipeList, _buildingsList, _codexList,
                           _researchList, _upgradesList;
        private Label      _prestigeSummary, _prestigeCurrency, _placementLabel;

        // ---- HUD chrome ----
        private VisualElement _inventoryBar;
        private Label         _powerLabel;
        private Button        _btnPrestige;

        // ---- Notification banner ----
        private VisualElement _notificationBanner;
        private Label         _notificationIcon, _notificationMessage;
        private Coroutine     _hideNotificationCoroutine;

        // ---- Tooltip ----
        private VisualElement _tooltipPopup;
        private Label         _tooltipTitle, _tooltipBody;

        // ---- Inventory label cache ----
        private readonly Dictionary<int, Label> _inventoryLabels = new();

        // ============================================================
        // Unity lifecycle
        // ============================================================

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            QueryElements(root);
            BindButtons(root);

            CloseAllPanels();
            SetElementVisible(_placementOverlay, false);
            SetElementVisible(_notificationBanner, false);
            SetElementVisible(_tooltipPopup, false);

            if (placementController != null)
                placementController.OnPlacingChanged += OnPlacingChanged;
        }

        void OnDisable()
        {
            if (placementController != null)
                placementController.OnPlacingChanged -= OnPlacingChanged;
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em            = world.EntityManager;
            _progressQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery = _em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
            _powerQuery    = _em.CreateEntityQuery(ComponentType.ReadOnly<PowerNodeData>());
            _ecsReady      = true;
        }

        void Update()
        {
            RefreshInventoryBar();
            RefreshPowerLabel();
            RefreshPrestigeButton();
        }

        // ============================================================
        // Element queries & button binding
        // ============================================================

        private void QueryElements(VisualElement root)
        {
            // HUD chrome
            _inventoryBar = root.Q("inventory-bar");
            _powerLabel   = root.Q<Label>("power-label");
            _btnPrestige  = root.Q<Button>("btn-prestige");

            // Panels
            _recipePanel    = root.Q("recipe-panel");
            _buildingsPanel = root.Q("buildings-panel");
            _codexPanel     = root.Q("codex-panel");
            _researchPanel  = root.Q("research-panel");
            _upgradesPanel  = root.Q("upgrades-panel");
            _prestigePanel  = root.Q("prestige-panel");
            _placementOverlay = root.Q("placement-overlay");

            _allPanels = new[]
            {
                _recipePanel, _buildingsPanel, _codexPanel,
                _researchPanel, _upgradesPanel, _prestigePanel
            };

            // Panel content
            _recipeList    = root.Q<ScrollView>("recipe-list");
            _buildingsList = root.Q<ScrollView>("buildings-list");
            _codexList     = root.Q<ScrollView>("codex-list");
            _researchList  = root.Q<ScrollView>("research-list");
            _upgradesList  = root.Q<ScrollView>("upgrades-list");

            _prestigeSummary  = root.Q<Label>("prestige-summary");
            _prestigeCurrency = root.Q<Label>("prestige-currency");
            _placementLabel   = root.Q<Label>("placement-label");

            // Notification banner — inner element inside "notification-instance" TemplateContainer
            _notificationBanner  = root.Q("notification-banner");
            _notificationIcon    = root.Q<Label>("notification-banner__icon");
            _notificationMessage = root.Q<Label>("notification-banner__message");

            // Tooltip — inner element inside "tooltip-instance" TemplateContainer
            _tooltipPopup = root.Q("tooltip-popup");
            _tooltipTitle = root.Q<Label>("tooltip-popup__title");
            _tooltipBody  = root.Q<Label>("tooltip-popup__body");
        }

        private void BindButtons(VisualElement root)
        {
            // Bottom bar panel launchers
            root.Q<Button>("btn-recipes").clicked   += OpenRecipePanel;
            root.Q<Button>("btn-buildings").clicked += OpenBuildingsPanel;
            root.Q<Button>("btn-codex").clicked     += OpenCodexPanel;
            root.Q<Button>("btn-research").clicked  += OpenResearchPanel;
            root.Q<Button>("btn-upgrades").clicked  += OpenUpgradesPanel;

            // Top bar
            root.Q<Button>("btn-prestige").clicked += OpenPrestigePanel;
            root.Q<Button>("btn-settings").clicked += () => Debug.Log("[HUD] Settings — coming soon");

            // Panel close buttons
            root.Q<Button>("btn-close-recipes").clicked   += () => SetElementVisible(_recipePanel,    false);
            root.Q<Button>("btn-close-buildings").clicked += () => SetElementVisible(_buildingsPanel, false);
            root.Q<Button>("btn-close-codex").clicked     += () => SetElementVisible(_codexPanel,     false);
            root.Q<Button>("btn-close-research").clicked  += () => SetElementVisible(_researchPanel,  false);
            root.Q<Button>("btn-close-upgrades").clicked  += () => SetElementVisible(_upgradesPanel,  false);
            root.Q<Button>("btn-close-prestige").clicked  += () => SetElementVisible(_prestigePanel,  false);

            // Placement
            root.Q<Button>("btn-cancel-placement").clicked += () => placementController?.CancelPlacement();

            // Prestige confirm
            root.Q<Button>("btn-confirm-prestige").clicked += OnPrestigeConfirmed;
        }

        // ============================================================
        // Per-frame refresh
        // ============================================================

        private void RefreshInventoryBar()
        {
            if (craftService == null || !craftService.IsReady) return;

            var counts = craftService.GetInventoryCounts();

            foreach (var (itemId, qty) in counts)
            {
                var item = ItemDatabase.Instance?.Get(itemId);
                if (item == null) continue;

                if (!_inventoryLabels.TryGetValue(itemId, out var lbl))
                {
                    lbl = new Label();
                    lbl.AddToClassList("inventory-item");
                    _inventoryBar.Add(lbl);
                    _inventoryLabels[itemId] = lbl;
                }

                lbl.text = $"{item.symbol ?? item.displayName}: {qty}";
                lbl.style.display = DisplayStyle.Flex;
            }

            foreach (var (itemId, lbl) in _inventoryLabels)
            {
                if (!counts.ContainsKey(itemId))
                    lbl.style.display = DisplayStyle.None;
            }
        }

        private void RefreshPowerLabel()
        {
            if (!_ecsReady || _powerLabel == null || _powerQuery.IsEmpty) return;

            var nodes = _powerQuery.ToComponentDataArray<PowerNodeData>(Allocator.Temp);
            float current = 0f, max = 0f;
            for (int i = 0; i < nodes.Length; i++)
            {
                current += nodes[i].CurrentEV;
                max     += nodes[i].MaxEV;
            }
            nodes.Dispose();

            _powerLabel.text = FormatPowerLabel(current, max);
        }

        /// <summary>Formats the power readout for the top bar label.</summary>
        internal static string FormatPowerLabel(float current, float max) =>
            max > 0f ? $"⚡ {current:0.#} / {max:0.#} eV" : "⚡ No power";

        private void RefreshPrestigeButton()
        {
            if (!_ecsReady || _btnPrestige == null || _progressQuery.IsEmpty) return;

            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            _btnPrestige.style.display = progress.PrestigeAvailable
                ? DisplayStyle.Flex
                : DisplayStyle.None;
        }

        // ============================================================
        // Panel openers
        // ============================================================

        private void OpenRecipePanel()
        {
            CloseAllPanels();
            BuildRecipeList();
            SetElementVisible(_recipePanel, true);
        }

        private void OpenBuildingsPanel()
        {
            CloseAllPanels();
            BuildBuildingsList();
            SetElementVisible(_buildingsPanel, true);
        }

        private void OpenCodexPanel()
        {
            CloseAllPanels();
            // Content is populated externally as codex entries are discovered
            SetElementVisible(_codexPanel, true);
        }

        private void OpenResearchPanel()
        {
            CloseAllPanels();
            // Content is populated when ResearchSO data is wired in
            SetElementVisible(_researchPanel, true);
        }

        private void OpenUpgradesPanel()
        {
            CloseAllPanels();
            // Content is populated when SpecialUpgradeSO data is wired in
            SetElementVisible(_upgradesPanel, true);
        }

        private void OpenPrestigePanel()
        {
            if (!_ecsReady || _progressQuery.IsEmpty || _prestigeQuery.IsEmpty) return;

            CloseAllPanels();

            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            var prestige = _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity());

            // Mirror the rate used in PrestigeSystem
            long preview = CalculatePrestigePreview(progress.NetWorth);
            long held    = prestige.PrestigeCurrency - prestige.PrestigeCurrencySpent;

            if (_prestigeSummary  != null) _prestigeSummary.text  = $"Net worth: {progress.NetWorth:N0}";
            if (_prestigeCurrency != null) _prestigeCurrency.text =
                $"You will receive: {preview} ✦   (current: {held} ✦)";

            SetElementVisible(_prestigePanel, true);
        }

        private void OnPrestigeConfirmed()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return;

            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            progress.PrestigeRequested = true;
            _em.SetComponentData(entity, progress);

            SetElementVisible(_prestigePanel, false);
            ShowNotification("✦", "Prestige complete! Starting a new run.");
        }

        // ============================================================
        // Recipe list
        // ============================================================

        private void BuildRecipeList()
        {
            _recipeList.Clear();

            var recipes = RecipeDatabase.Instance?.Recipes;
            if (recipes == null) return;

            var manual = recipes.Where(r => !r.requiresBuilding).ToList();
            if (manual.Count == 0)
            {
                _recipeList.Add(new Label("No manual recipes available."));
                return;
            }

            foreach (var recipe in manual)
            {
                var row = new VisualElement();
                row.AddToClassList("recipe-row");

                var info = new VisualElement();
                info.AddToClassList("recipe-info");

                var nameLabel = new Label(recipe.name);
                nameLabel.AddToClassList("recipe-name");

                var inputsLabel = new Label(BuildInputsText(recipe));
                inputsLabel.AddToClassList("recipe-inputs");

                info.Add(nameLabel);
                info.Add(inputsLabel);

                var craftBtn = new Button { text = "Craft" };
                craftBtn.AddToClassList("craft-btn");
                var captured = recipe;
                craftBtn.clicked += () => OnCraftPressed(captured);

                row.Add(info);
                row.Add(craftBtn);
                _recipeList.Add(row);
            }
        }

        private void OnCraftPressed(RecipeJson recipe)
        {
            if (!craftService.TryCraft(recipe))
                Debug.Log($"[HUD] Can't craft {recipe.name} — not enough inputs.");
        }

        // ============================================================
        // Buildings list
        // ============================================================

        private void BuildBuildingsList()
        {
            _buildingsList.Clear();
            if (placementController == null) return;

            foreach (var entry in placementController.availableBuildings)
            {
                var card = new VisualElement();
                card.AddToClassList("building-card");

                var nameLabel = new Label(entry.building?.displayName ?? "Building");
                nameLabel.AddToClassList("building-card-name");

                var recipeLabel = new Label(entry.defaultRecipe != null
                    ? $"Produces: {entry.defaultRecipe.displayName}"
                    : "No recipe");
                recipeLabel.AddToClassList("building-card-recipe");

                var placeBtn = new Button { text = "Place" };
                placeBtn.AddToClassList("craft-btn");

                var captured = entry;
                placeBtn.clicked += () =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    placementController.BeginPlacement(captured);
                };

                card.Add(nameLabel);
                card.Add(recipeLabel);
                card.Add(placeBtn);
                _buildingsList.Add(card);
            }
        }

        // ============================================================
        // Placement overlay
        // ============================================================

        private void OnPlacingChanged(bool isPlacing)
        {
            SetElementVisible(_placementOverlay, isPlacing);
            if (isPlacing && _placementLabel != null)
                _placementLabel.text = "Tap an empty tile to place  ·  Esc to cancel";
        }

        // ============================================================
        // Notification banner
        // ============================================================

        /// <summary>
        /// Shows a transient notification banner that auto-hides after
        /// <see cref="notificationDuration"/> seconds.
        /// <paramref name="modifier"/> can be null (success/green), "warning", or "danger".
        /// </summary>
        public void ShowNotification(string icon, string message, string modifier = null)
        {
            if (_notificationBanner == null) return;

            if (_notificationIcon    != null) _notificationIcon.text    = icon;
            if (_notificationMessage != null) _notificationMessage.text = message;

            _notificationBanner.RemoveFromClassList("notification-banner--warning");
            _notificationBanner.RemoveFromClassList("notification-banner--danger");
            if (modifier != null)
                _notificationBanner.AddToClassList($"notification-banner--{modifier}");

            SetElementVisible(_notificationBanner, true);

            if (_hideNotificationCoroutine != null)
                StopCoroutine(_hideNotificationCoroutine);
            _hideNotificationCoroutine = StartCoroutine(HideNotificationAfterDelay());
        }

        private IEnumerator HideNotificationAfterDelay()
        {
            yield return new WaitForSeconds(notificationDuration);
            SetElementVisible(_notificationBanner, false);
        }

        // ============================================================
        // Tooltip
        // ============================================================

        /// <summary>
        /// Shows the tooltip positioned near <paramref name="anchor"/>.
        /// Prefers rendering above; falls back to below if there is insufficient space.
        /// </summary>
        public void ShowTooltip(string title, string body, VisualElement anchor)
        {
            if (_tooltipPopup == null) return;

            if (_tooltipTitle != null) _tooltipTitle.text = title;
            if (_tooltipBody  != null) _tooltipBody.text  = body;

            // Layout isn't known until after first render — position once geometry resolves
            _tooltipPopup.RegisterCallbackOnce<GeometryChangedEvent>(_ => PositionTooltip(anchor));
            SetElementVisible(_tooltipPopup, true);
        }

        public void HideTooltip() => SetElementVisible(_tooltipPopup, false);

        private void PositionTooltip(VisualElement anchor)
        {
            if (anchor == null || _tooltipPopup == null) return;

            var anchorBounds  = anchor.worldBound;
            var tooltipHeight = _tooltipPopup.layout.height;

            float top = anchorBounds.yMin - tooltipHeight - 8f;
            if (top < 0f) top = anchorBounds.yMax + 8f; // not enough room above — go below

            _tooltipPopup.style.left = anchorBounds.xMin;
            _tooltipPopup.style.top  = top;
        }

        // ============================================================
        // Helpers
        // ============================================================

        private void CloseAllPanels()
        {
            foreach (var panel in _allPanels)
                SetElementVisible(panel, false);
        }

        // ============================================================
        // Internal helpers (internal for testability)
        // ============================================================

        /// <summary>
        /// Calculates how much prestige currency a prestige would award.
        /// Must stay in sync with the rate constant in <see cref="PrestigeSystem"/>.
        /// </summary>
        internal static long CalculatePrestigePreview(float netWorth) => (long)(netWorth * 1f);

        internal static void SetElementVisible(VisualElement el, bool visible)
        {
            if (el == null) return;
            if (visible) el.RemoveFromClassList("hidden");
            else         el.AddToClassList("hidden");
        }

        internal static string BuildInputsText(RecipeJson recipe)
        {
            if (recipe.inputs == null || recipe.inputs.Count == 0) return "";
            var parts = recipe.inputs.Select(i =>
            {
                var item = ItemDatabase.Instance?.Get(i.id);
                string sym = item?.symbol ?? item?.displayName ?? i.id;
                return $"{i.quantity}× {sym}";
            });
            return string.Join("  +  ", parts);
        }
    }
}
