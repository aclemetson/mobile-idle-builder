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
        [SerializeField] private ManualCraftService           craftService;
        [SerializeField] private BuildingPlacementController  placementController;
        [SerializeField] private ConveyorPlacementController  conveyorController;
        [SerializeField] private DeconstructController        deconstructController;
        [SerializeField] private AchievementService           achievementService;
        [SerializeField] private PVPService                   pvpService;
        [SerializeField] private ResearchService              researchService;
        [SerializeField] private float                        notificationDuration = 3f;

        // ---- ECS ----
        private EntityManager _em;
        private EntityQuery   _progressQuery;
        private EntityQuery   _prestigeQuery;
        private EntityQuery   _powerQuery;
        private EntityQuery   _inventoryQuery;
        private bool          _ecsReady;

        // ---- Panels ----
        private VisualElement _recipePanel, _buildingsPanel, _codexPanel,
                              _researchPanel, _upgradesPanel, _prestigePanel,
                              _achievementsPanel, _pvpPanel, _placementOverlay;
        private VisualElement[] _allPanels;

        // ---- Panel content ----
        private ScrollView _recipeList, _buildingsList, _codexList,
                           _researchList, _upgradesList, _achievementsList, _pvpLeaderboardList;
        private Label      _prestigeSummary, _prestigeCurrency, _placementLabel, _achievementsTitle;
        private Label      _pvpStateLabel, _pvpTimerLabel, _pvpLockLabel, _pvpCompletedLabel;
        private Button     _btnEnterPVP;

        // ---- HUD chrome ----
        private VisualElement _drawerInventory;
        private VisualElement _drawerPanel;
        private VisualElement _drawerBackdrop;
        private bool          _drawerOpen;
        private Label         _entropyLabel;
        private Label         _powerLabel;
        private Button        _btnPrestige;

        // ---- Notification banner ----
        private VisualElement _notificationBanner;
        private Label         _notificationIcon, _notificationMessage;
        private Coroutine     _hideNotificationCoroutine;

        // ---- Field proximity banner ----
        private VisualElement _fieldBanner;
        private Label         _fieldBannerName;
        private Label         _fieldBannerType;

        // ---- Placement controls ----
        private Button _btnRotateOutput;
        private Button _btnFlipBuilding;

        // ---- Conveyor placement ----
        private VisualElement _conveyorOverlay;

        // ---- Deconstruct mode ----
        private VisualElement _deconstructOverlay;

        // ---- Output selector ----
        private VisualElement _outputSelector;
        private VisualElement _outputSelectorOptions;
        private Label         _outputSelectorTitle;

        // ---- Tooltip ----
        private VisualElement _tooltipPopup;
        private Label         _tooltipTitle, _tooltipBody;

        // ---- Building inspector ----
        private VisualElement _buildingInspectorPanel;
        private ScrollView    _inspectorContent;
        private Label         _inspectorBuildingName;
        private Entity        _inspectorEntity = Entity.Null;
        private float         _inspectorRefreshTimer;

        // ---- Inventory label cache ----
        private readonly Dictionary<int, Label> _inventoryLabels = new();

        // ============================================================
        // Unity lifecycle
        // ============================================================

        void Awake()
        {
            if (deconstructController == null)
                deconstructController = FindAnyObjectByType<DeconstructController>();
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            QueryElements(root);
            BindButtons(root);

            CloseAllPanels();
            SetElementVisible(_placementOverlay,   false);
            SetElementVisible(_conveyorOverlay,    false);
            SetElementVisible(_deconstructOverlay, false);
            SetElementVisible(_notificationBanner, false);
            SetElementVisible(_fieldBanner, false);
            SetElementVisible(_outputSelector, false);
            SetElementVisible(_tooltipPopup, false);
            SetElementVisible(_buildingInspectorPanel, false);

            if (placementController != null)
            {
                placementController.OnPlacingChanged         += OnPlacingChanged;
                placementController.OnOutputSelectionRequired += ShowOutputSelector;
                placementController.OnBuildingPlaced         += OnBuildingPlaced;
            }

            if (conveyorController != null)
                conveyorController.OnPlacingChanged += OnConveyorPlacingChanged;

            if (deconstructController != null)
                deconstructController.OnDeconstructingChanged += OnDeconstructingChanged;

            if (achievementService != null)
                achievementService.OnAchievementUnlocked += OnAchievementUnlocked;

            if (pvpService != null)
                pvpService.OnStateChanged += OnPVPStateChanged;

            if (researchService != null)
                researchService.OnResearchUnlocked += OnResearchUnlocked;
        }

        void OnDisable()
        {
            if (placementController != null)
            {
                placementController.OnPlacingChanged         -= OnPlacingChanged;
                placementController.OnOutputSelectionRequired -= ShowOutputSelector;
                placementController.OnBuildingPlaced         -= OnBuildingPlaced;
            }

            if (conveyorController != null)
                conveyorController.OnPlacingChanged -= OnConveyorPlacingChanged;

            if (deconstructController != null)
                deconstructController.OnDeconstructingChanged -= OnDeconstructingChanged;

            if (achievementService != null)
                achievementService.OnAchievementUnlocked -= OnAchievementUnlocked;

            if (pvpService != null)
                pvpService.OnStateChanged -= OnPVPStateChanged;

            if (researchService != null)
                researchService.OnResearchUnlocked -= OnResearchUnlocked;
        }

        void Start()
        {
            if (placementController == null)
                placementController = FindAnyObjectByType<BuildingPlacementController>();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em             = world.EntityManager;
            _progressQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery  = _em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
            _powerQuery     = _em.CreateEntityQuery(ComponentType.ReadOnly<PowerNodeData>());
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>()
            );
            _ecsReady       = true;
        }

        void Update()
        {
            RefreshInventoryBar();
            RefreshEntropyLabel();
            RefreshPowerLabel();
            RefreshPrestigeButton();
            TickBuildingInspector();
        }

        // ============================================================
        // Element queries & button binding
        // ============================================================

        private void QueryElements(VisualElement root)
        {
            // HUD chrome
            _drawerInventory = root.Q("drawer-inventory");
            _drawerPanel     = root.Q("left-drawer");
            _drawerBackdrop  = root.Q("drawer-backdrop");
            _entropyLabel    = root.Q<Label>("entropy-label");
            _powerLabel   = root.Q<Label>("power-label");
            _btnPrestige  = root.Q<Button>("btn-prestige");

            // Panels
            _recipePanel       = root.Q("recipe-panel");
            _buildingsPanel    = root.Q("buildings-panel");
            _codexPanel        = root.Q("codex-panel");
            _researchPanel     = root.Q("research-panel");
            _upgradesPanel     = root.Q("upgrades-panel");
            _achievementsPanel = root.Q("achievements-panel");
            _pvpPanel          = root.Q("pvp-panel");
            _prestigePanel     = root.Q("prestige-panel");
            _placementOverlay  = root.Q("placement-overlay");

            _allPanels = new[]
            {
                _recipePanel, _buildingsPanel, _codexPanel,
                _researchPanel, _upgradesPanel, _achievementsPanel, _pvpPanel, _prestigePanel
            };

            // Panel content
            _recipeList       = root.Q<ScrollView>("recipe-list");
            _buildingsList    = root.Q<ScrollView>("buildings-list");
            _codexList        = root.Q<ScrollView>("codex-list");
            _researchList     = root.Q<ScrollView>("research-list");
            _upgradesList     = root.Q<ScrollView>("upgrades-list");
            _achievementsList    = root.Q<ScrollView>("achievements-list");
            _pvpLeaderboardList = root.Q<ScrollView>("pvp-leaderboard-list");

            _pvpStateLabel     = root.Q<Label>("pvp-state-label");
            _pvpTimerLabel     = root.Q<Label>("pvp-timer-label");
            _pvpLockLabel      = root.Q<Label>("pvp-lock-label");
            _pvpCompletedLabel = root.Q<Label>("pvp-completed-label");
            _btnEnterPVP       = root.Q<Button>("btn-enter-pvp");

            _prestigeSummary  = root.Q<Label>("prestige-summary");
            _prestigeCurrency = root.Q<Label>("prestige-currency");
            _placementLabel   = root.Q<Label>("placement-label");
            _btnRotateOutput  = root.Q<Button>("btn-rotate-output");
            _btnFlipBuilding  = root.Q<Button>("btn-flip-building");
            _achievementsTitle = root.Q<Label>("achievements-title");

            // Notification banner — inner element inside "notification-instance" TemplateContainer
            _notificationBanner  = root.Q("notification-banner");
            _notificationIcon    = root.Q<Label>("notification-banner__icon");
            _notificationMessage = root.Q<Label>("notification-banner__message");

            // Field proximity banner — inner element inside "field-proximity-instance" TemplateContainer
            _fieldBanner     = root.Q("field-proximity-banner");
            _fieldBannerName = root.Q<Label>("field-proximity-banner__name");
            _fieldBannerType = root.Q<Label>("field-proximity-banner__type");

            // Output selector
            _outputSelector        = root.Q("output-selector");
            _outputSelectorOptions = root.Q("output-selector__options");
            _outputSelectorTitle   = root.Q<Label>("output-selector__title");

            // Conveyor placement
            _conveyorOverlay = root.Q("conveyor-overlay");

            // Deconstruct mode
            _deconstructOverlay = root.Q("deconstruct-overlay");

            // Tooltip — inner element inside "tooltip-instance" TemplateContainer
            _tooltipPopup = root.Q("tooltip-popup");
            _tooltipTitle = root.Q<Label>("tooltip-popup__title");
            _tooltipBody  = root.Q<Label>("tooltip-popup__body");

            // Building inspector
            _buildingInspectorPanel = root.Q("building-inspector-panel");
            _inspectorContent       = root.Q<ScrollView>("inspector-content");
            _inspectorBuildingName  = root.Q<Label>("inspector-building-name");
        }

        private void BindButtons(VisualElement root)
        {
            // Drawer toggle
            root.Q<Button>("btn-drawer-handle").clicked += ToggleDrawer;
            if (_drawerBackdrop != null)
                _drawerBackdrop.RegisterCallback<ClickEvent>(_ => CloseDrawer());

            // Drawer nav buttons
            root.Q<Button>("btn-recipes").clicked      += OpenRecipePanel;
            root.Q<Button>("btn-buildings").clicked    += OpenBuildingsPanel;
            root.Q<Button>("btn-codex").clicked        += OpenCodexPanel;
            root.Q<Button>("btn-research").clicked     += OpenResearchPanel;
            root.Q<Button>("btn-upgrades").clicked     += OpenUpgradesPanel;
            root.Q<Button>("btn-achievements").clicked += OpenAchievementsPanel;
            root.Q<Button>("btn-pvp").clicked          += OpenPVPPanel;

            // Top bar
            root.Q<Button>("btn-prestige").clicked += OpenPrestigePanel;
            root.Q<Button>("btn-settings").clicked += () => Debug.Log("[HUD] Settings — coming soon");

            // Panel close buttons
            root.Q<Button>("btn-close-recipes").clicked      += () => SetElementVisible(_recipePanel,       false);
            root.Q<Button>("btn-close-buildings").clicked    += () => SetElementVisible(_buildingsPanel,    false);
            root.Q<Button>("btn-close-codex").clicked        += () => SetElementVisible(_codexPanel,        false);
            root.Q<Button>("btn-close-research").clicked     += () => SetElementVisible(_researchPanel,     false);
            root.Q<Button>("btn-close-upgrades").clicked     += () => SetElementVisible(_upgradesPanel,     false);
            root.Q<Button>("btn-close-achievements").clicked += () => SetElementVisible(_achievementsPanel, false);
            root.Q<Button>("btn-close-pvp").clicked              += () => SetElementVisible(_pvpPanel,          false);
            root.Q<Button>("btn-close-prestige").clicked         += () => SetElementVisible(_prestigePanel,     false);

            // PVP actions (queried after _btnEnterPVP is set in QueryElements)
            if (_btnEnterPVP != null)
                _btnEnterPVP.clicked += OnEnterPVPPressed;
            root.Q<Button>("btn-refresh-leaderboard").clicked += () =>
            {
                if (pvpService != null)
                    StartCoroutine(pvpService.FetchLeaderboardAsync(PopulatePVPLeaderboard));
            };

            // Deconstruct (header button inside buildings panel)
            var btnDeconstruct = root.Q<Button>("btn-deconstruct");
            if (btnDeconstruct != null)
                btnDeconstruct.clicked += () =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    deconstructController?.BeginDeconstructMode();
                };

            // Building placement
            root.Q<Button>("btn-cancel-placement").clicked += () => placementController?.CancelPlacement();
            if (_btnRotateOutput != null)
                _btnRotateOutput.clicked += () => placementController?.Rotate();
            if (_btnFlipBuilding != null)
                _btnFlipBuilding.clicked += () => placementController?.Flip();

            // Conveyor cancel (button inside the conveyor overlay)
            root.Q<Button>("btn-cancel-conveyor")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ =>
                conveyorController?.CancelConveyorMode());

            // Deconstruct cancel
            root.Q<Button>("btn-cancel-deconstruct")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ =>
                deconstructController?.CancelDeconstructMode());

            // Building inspector close
            var btnCloseInspector = root.Q<Button>("btn-close-inspector");
            if (btnCloseInspector != null) btnCloseInspector.clicked += HideBuildingInspector;

            // Prestige confirm
            root.Q<Button>("btn-confirm-prestige").clicked += OnPrestigeConfirmed;
        }

        // ============================================================
        // Per-frame refresh
        // ============================================================

        private void RefreshInventoryBar()
        {
            if (!_ecsReady || _drawerInventory == null || _inventoryQuery.IsEmpty) return;

            var buffer = _em.GetBuffer<InventorySlot>(
                _inventoryQuery.GetSingletonEntity(), isReadOnly: true);

            var seen = new HashSet<int>();
            for (int i = 0; i < buffer.Length; i++)
            {
                var slot = buffer[i];
                if (slot.Quantity <= 0) continue;

                seen.Add(slot.ItemID);
                var item = ItemDatabase.GetStatic(slot.ItemID);
                string label = item != null
                    ? $"{item.symbol ?? item.displayName}  ×{slot.Quantity}"
                    : $"#{slot.ItemID}  ×{slot.Quantity}";

                if (!_inventoryLabels.TryGetValue(slot.ItemID, out var lbl))
                {
                    lbl = new Label();
                    lbl.AddToClassList("drawer-inventory-item");
                    _drawerInventory.Add(lbl);
                    _inventoryLabels[slot.ItemID] = lbl;
                }

                lbl.text = label;
                lbl.style.display = DisplayStyle.Flex;
            }

            foreach (var (itemId, lbl) in _inventoryLabels)
            {
                if (!seen.Contains(itemId))
                    lbl.style.display = DisplayStyle.None;
            }
        }

        private void RefreshEntropyLabel()
        {
            if (!_ecsReady || _entropyLabel == null || _progressQuery.IsEmpty) return;

            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            _entropyLabel.text = $"◈ {progress.BaseCurrency:N0}";
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
            BuildResearchList();
            SetElementVisible(_researchPanel, true);
        }

        private void BuildResearchList()
        {
            if (_researchList == null) return;
            _researchList.Clear();

            if (researchService == null || researchService.AllResearch == null ||
                researchService.AllResearch.Count == 0)
            {
                _researchList.Add(new Label("No research available."));
                return;
            }

            long currentEntropy = 0;
            if (_ecsReady && !_progressQuery.IsEmpty)
                currentEntropy = _em.GetComponentData<PlayerProgressData>(
                    _progressQuery.GetSingletonEntity()).BaseCurrency;

            foreach (var research in researchService.AllResearch)
            {
                if (research == null) continue;

                bool unlocked    = researchService.IsUnlocked(research.id);
                bool canPurchase = !unlocked && researchService.CanPurchase(research);

                var card = new VisualElement();
                card.AddToClassList("building-card");
                if (unlocked) card.AddToClassList("research-card--unlocked");

                // Name row
                var nameLabel = new Label(unlocked ? $"✓  {research.displayName}" : research.displayName);
                nameLabel.AddToClassList("building-card-name");
                card.Add(nameLabel);

                // Description
                if (!string.IsNullOrEmpty(research.description))
                {
                    var descLabel = new Label(research.description);
                    descLabel.AddToClassList("building-card-recipe");
                    card.Add(descLabel);
                }

                if (!unlocked)
                {
                    // Cost
                    bool affordable = currentEntropy >= research.costBaseCurrency;
                    var costLabel = new Label($"◈ {research.costBaseCurrency:N0}");
                    costLabel.AddToClassList("building-card-recipe");
                    if (!affordable)
                        costLabel.style.color = new UnityEngine.Color(1f, 0.3f, 0.3f);
                    card.Add(costLabel);

                    // Prerequisites (if locked)
                    if (research.prerequisites != null && research.prerequisites.Length > 0)
                    {
                        bool prereqsMet = canPurchase || unlocked;
                        if (!prereqsMet)
                        {
                            var prereqNames = string.Join(", ",
                                System.Array.ConvertAll(research.prerequisites,
                                    p => p != null ? p.displayName : "?"));
                            var prereqLabel = new Label($"Requires: {prereqNames}");
                            prereqLabel.AddToClassList("building-card-recipe");
                            prereqLabel.style.color = new UnityEngine.Color(0.6f, 0.6f, 0.6f);
                            card.Add(prereqLabel);
                        }
                    }

                    // Purchase button
                    var purchaseBtn = new Button { text = "Research" };
                    purchaseBtn.AddToClassList("craft-btn");
                    purchaseBtn.SetEnabled(canPurchase);

                    var captured = research;
                    purchaseBtn.clicked += () =>
                    {
                        researchService.Purchase(captured);
                        BuildResearchList(); // refresh after purchase
                    };
                    card.Add(purchaseBtn);
                }

                _researchList.Add(card);
            }
        }

        private void OnResearchUnlocked(ResearchSO research)
        {
            // Refresh research panel if it is currently open
            if (_researchPanel != null && !_researchPanel.ClassListContains("hidden"))
                BuildResearchList();

            // Refresh buildings panel if it is open (newly unlocked buildings will appear)
            if (_buildingsPanel != null && !_buildingsPanel.ClassListContains("hidden"))
                BuildBuildingsList();

            // Refresh recipe panel — newly learned recipes will appear
            if (_recipePanel != null && !_recipePanel.ClassListContains("hidden"))
                BuildRecipeList();
        }

        private void OpenUpgradesPanel()
        {
            CloseAllPanels();
            // Content is populated when SpecialUpgradeSO data is wired in
            SetElementVisible(_upgradesPanel, true);
        }

        private void OpenAchievementsPanel()
        {
            CloseAllPanels();
            BuildAchievementsList();
            SetElementVisible(_achievementsPanel, true);
        }

        private void OnAchievementUnlocked(AchievementSO _)
        {
            // Refresh the title counter whenever a new achievement completes
            if (_achievementsPanel != null && !_achievementsPanel.ClassListContains("hidden"))
                BuildAchievementsList();
        }

        // ============================================================
        // PVP panel
        // ============================================================

        private void OpenPVPPanel()
        {
            CloseAllPanels();
            BuildPVPPanel();
            SetElementVisible(_pvpPanel, true);

            // Kick off leaderboard fetch whenever the panel opens
            if (pvpService != null)
                StartCoroutine(pvpService.FetchLeaderboardAsync(PopulatePVPLeaderboard));
        }

        private void OnPVPStateChanged()
        {
            if (_pvpPanel != null && !_pvpPanel.ClassListContains("hidden"))
                BuildPVPPanel();
        }

        private void BuildPVPPanel()
        {
            if (pvpService == null) return;

            var state = pvpService.State;

            // --- state label ---
            if (_pvpStateLabel != null)
                _pvpStateLabel.text = state switch
                {
                    PVPState.Locked    => "Locked",
                    PVPState.Available => "Available — this week's competition is open!",
                    PVPState.InRun     => $"In Run — {pvpService.FormatTimeRemaining()} remaining",
                    PVPState.Completed => "Run complete — score submitted",
                    _                  => "—"
                };

            // --- timer (only visible during a run) ---
            if (_pvpTimerLabel != null)
                _pvpTimerLabel.text = state == PVPState.InRun ? pvpService.FormatTimeRemaining() : "";

            // --- lock label ---
            if (_pvpLockLabel != null)
            {
                bool showLock = state == PVPState.Locked;
                _pvpLockLabel.text = showLock
                    ? $"Complete {PVPService.PvpPrestigeUnlockCount} prestiges to unlock Weekly Competition."
                    : "";
                SetElementVisible(_pvpLockLabel, showLock);
            }

            // --- enter button (only when available) ---
            if (_btnEnterPVP != null)
                SetElementVisible(_btnEnterPVP, state == PVPState.Available);

            // --- completed label ---
            if (_pvpCompletedLabel != null)
            {
                bool showCompleted = state == PVPState.Completed;
                _pvpCompletedLabel.text = showCompleted
                    ? "Your score has been submitted. Rewards are distributed after the competition window closes."
                    : "";
                SetElementVisible(_pvpCompletedLabel, showCompleted);
            }
        }

        private void OnEnterPVPPressed()
        {
            if (pvpService == null || !pvpService.CanEnterCompetition) return;
            if (!_ecsReady || _progressQuery.IsEmpty) return;

            // Reset the grid/inventory via ECS (same mechanism as prestige)
            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            progress.PVPRunRequested = true;
            _em.SetComponentData(entity, progress);

            // Record run start in save data
            pvpService.MarkRunStarted();

            SetElementVisible(_pvpPanel, false);
            ShowNotification("⚔", "Competition run started! 48 hours on the clock.");
        }

        private void PopulatePVPLeaderboard(System.Collections.Generic.List<LeaderboardEntry> entries)
        {
            if (_pvpLeaderboardList == null) return;
            _pvpLeaderboardList.Clear();

            if (entries == null || entries.Count == 0)
            {
                _pvpLeaderboardList.Add(new Label("No leaderboard data yet."));
                return;
            }

            foreach (var entry in entries)
            {
                var row = new VisualElement();
                row.AddToClassList("leaderboard-row");

                var rankLabel   = new Label($"#{entry.rank}");
                rankLabel.AddToClassList("leaderboard-row__rank");

                var playerLabel = new Label(entry.playerId);
                playerLabel.AddToClassList("leaderboard-row__player");

                var scoreLabel  = new Label(entry.score.ToString("N0"));
                scoreLabel.AddToClassList("leaderboard-row__score");

                row.Add(rankLabel);
                row.Add(playerLabel);
                row.Add(scoreLabel);
                _pvpLeaderboardList.Add(row);
            }
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

            if (recipes.Count == 0)
            {
                _recipeList.Add(new Label("No recipes available."));
                return;
            }

            foreach (var recipe in recipes)
            {
                // Hide recipes the player has never unlocked
                if (!(RecipeKnowledgeService.Instance?.IsKnown(recipe.id) ?? false))
                    continue;

                bool isGated = !string.IsNullOrEmpty(recipe.requires_research);
                bool isLocked = isGated && (researchService == null || !researchService.IsUnlocked(recipe.requires_research));

                var row = new VisualElement();
                row.AddToClassList("recipe-row");
                if (isLocked) row.AddToClassList("recipe-row--locked");

                var info = new VisualElement();
                info.AddToClassList("recipe-info");

                var nameLabel = new Label(recipe.name);
                nameLabel.AddToClassList("recipe-name");

                var inputsLabel = new Label(BuildInputsText(recipe));
                inputsLabel.AddToClassList("recipe-inputs");

                if (recipe.requiresBuilding)
                {
                    var buildingTag = new Label("[Building]");
                    buildingTag.AddToClassList("recipe-building-tag");
                    info.Add(buildingTag);
                }

                if (isLocked)
                {
                    var lockLabel = new Label($"[Requires research]");
                    lockLabel.AddToClassList("recipe-lock-hint");
                    info.Add(lockLabel);
                }

                info.Add(nameLabel);
                info.Add(inputsLabel);

                var craftBtn = new Button { text = isLocked ? "Locked" : "Craft" };
                craftBtn.AddToClassList("craft-btn");
                craftBtn.SetEnabled(!isLocked);
                var captured = recipe;
                if (!isLocked)
                    craftBtn.clicked += () => OnCraftPressed(captured);

                row.Add(info);
                row.Add(craftBtn);
                _recipeList.Add(row);
            }
        }

        private void OnCraftPressed(RecipeJson recipe)
        {
            if (recipe.requiresBuilding)
            {
                if (!craftService.TriggerBuildingCraft(recipe))
                    Debug.Log($"[HUD] Can't craft {recipe.name} — no eligible building or missing inputs.");
            }
            else
            {
                if (!craftService.TryCraft(recipe))
                    Debug.Log($"[HUD] Can't craft {recipe.name} — not enough inputs.");
            }
        }

        // ============================================================
        // Buildings list
        // ============================================================

        private void BuildBuildingsList()
        {
            _buildingsList.Clear();

            long currentEntropy = 0;
            if (_ecsReady && !_progressQuery.IsEmpty)
                currentEntropy = _em.GetComponentData<PlayerProgressData>(
                    _progressQuery.GetSingletonEntity()).BaseCurrency;

            // ---- Conveyor Belt entry (always first, always free) ----
            if (conveyorController != null)
            {
                var conveyorCard = new VisualElement();
                conveyorCard.AddToClassList("building-card");

                var nameLabel = new Label("Conveyor Belt");
                nameLabel.AddToClassList("building-card-name");

                var descLabel = new Label("Draw paths to move items between buildings");
                descLabel.AddToClassList("building-card-recipe");

                var placeBtn = new Button { text = "Place" };
                placeBtn.AddToClassList("craft-btn");
                placeBtn.clicked += () =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    conveyorController.BeginConveyorMode();
                };

                conveyorCard.Add(nameLabel);
                conveyorCard.Add(descLabel);
                conveyorCard.Add(placeBtn);
                _buildingsList.Add(conveyorCard);
            }

            if (placementController == null) return;

            foreach (var entry in placementController.availableBuildings)
            {
                // Skip buildings whose required research has not been purchased yet
                var bso = entry.building;
                if (bso != null && !bso.availableFromStart && bso.requiredResearch != null)
                {
                    if (researchService == null || !researchService.IsUnlocked(bso.requiredResearch.id))
                        continue;
                }

                var card = new VisualElement();
                card.AddToClassList("building-card");

                var nameLabel = new Label(entry.building?.displayName ?? "Building");
                nameLabel.AddToClassList("building-card-name");

                var recipeLabel = new Label(entry.defaultRecipe != null
                    ? $"Produces: {entry.defaultRecipe.displayName}"
                    : "No recipe");
                recipeLabel.AddToClassList("building-card-recipe");

                int cost = entry.building?.entropyCost ?? 0;
                bool canAfford = currentEntropy >= cost;

                if (cost > 0)
                {
                    var costLabel = new Label($"◈ {cost:N0}");
                    costLabel.AddToClassList("building-card-recipe");
                    if (!canAfford)
                        costLabel.style.color = new UnityEngine.Color(1f, 0.3f, 0.3f);
                    card.Add(costLabel);
                }

                var placeBtn = new Button { text = "Place" };
                placeBtn.AddToClassList("craft-btn");
                placeBtn.SetEnabled(canAfford);

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

        private void OnBuildingPlaced(BuildingPlacementController.BuildingEntry entry)
        {
            int cost = entry.building?.entropyCost ?? 0;
            if (cost <= 0 || !_ecsReady || _progressQuery.IsEmpty) return;

            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            progress.BaseCurrency = System.Math.Max(0, progress.BaseCurrency - cost);
            _em.SetComponentData(entity, progress);
        }

        // ============================================================
        // Achievements list
        // ============================================================

        private void BuildAchievementsList()
        {
            if (_achievementsList == null) return;
            _achievementsList.Clear();

            var all = achievementService?.GetAll();
            if (all == null || all.Count == 0)
            {
                _achievementsList.Add(new Label("No achievements defined."));
                return;
            }

            int completed = achievementService.CompletedCount;
            if (_achievementsTitle != null)
                _achievementsTitle.text = $"Achievements ({completed} / {all.Count})";

            foreach (var achievement in all)
            {
                bool isDone   = achievementService.IsCompleted(achievement.id);
                bool isHidden = achievement.isHidden && !isDone;

                var row = new VisualElement();
                row.AddToClassList("achievement-row");
                if (isDone) row.AddToClassList("achievement-row--completed");

                // Check / lock indicator
                var checkLabel = new Label(isDone ? "✓" : "○");
                checkLabel.AddToClassList("achievement-row__check");

                // Body
                var body = new VisualElement();
                body.AddToClassList("achievement-row__body");

                var nameLabel = new Label(isHidden ? "???" : achievement.displayName);
                nameLabel.AddToClassList("achievement-row__name");

                var descLabel = new Label(isHidden ? "Complete a hidden objective to reveal." : achievement.description);
                descLabel.AddToClassList("achievement-row__description");

                body.Add(nameLabel);
                body.Add(descLabel);

                // Progress (only for quantity > 1 and not hidden)
                if (!isHidden && !isDone && achievement.triggerQuantity > 1)
                {
                    int progress = achievementService.GetProgress(achievement.id);
                    var progLabel = new Label($"{progress} / {achievement.triggerQuantity}");
                    progLabel.AddToClassList("achievement-row__progress");
                    body.Add(progLabel);
                }

                // Rewards (only if not hidden)
                if (!isHidden && achievement.rewards != null && achievement.rewards.Length > 0)
                {
                    var rewardNames = string.Join(", ", System.Array.ConvertAll(
                        achievement.rewards, r => r != null ? r.displayName : "?"));
                    var rewardLabel = new Label($"Reward: {rewardNames}");
                    rewardLabel.AddToClassList("achievement-row__rewards");
                    body.Add(rewardLabel);
                }

                row.Add(checkLabel);
                row.Add(body);
                _achievementsList.Add(row);
            }
        }

        // ============================================================
        // Placement overlay
        // ============================================================

        private void OnPlacingChanged(bool isPlacing)
        {
            SetElementVisible(_placementOverlay, isPlacing);

            bool canRotate = isPlacing && (placementController?.CanRotate ?? false);
            bool canFlip   = isPlacing && (placementController?.CanFlip   ?? false);
            SetElementVisible(_btnRotateOutput, canRotate);
            SetElementVisible(_btnFlipBuilding, canFlip);

            if (isPlacing && _placementLabel != null)
            {
                bool isField = placementController?.RequiresOutputDirection ?? false;
                _placementLabel.text = isField
                    ? "Tap a field tile to place  ·  R rotate  ·  Esc cancel"
                    : canRotate
                        ? "Tap a tile to place  ·  R rotate  ·  F flip  ·  Esc cancel"
                        : "Tap an empty tile to place  ·  Esc to cancel";
            }
        }

        private void OnConveyorPlacingChanged(bool isPlacing)
        {
            SetElementVisible(_conveyorOverlay, isPlacing);
        }

        private void OnDeconstructingChanged(bool isDeconstructing)
        {
            SetElementVisible(_deconstructOverlay, isDeconstructing);
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
        // Field proximity banner
        // ============================================================

        /// <summary>
        /// Shows the persistent field proximity banner for the given field.
        /// Remains visible until <see cref="HideFieldBanner"/> is called.
        /// </summary>
        public void ShowFieldBanner(FieldSO field)
        {
            if (field == null) return;

            if (_fieldBannerName != null) _fieldBannerName.text = field.displayName;
            if (_fieldBannerType != null) _fieldBannerType.text = field.fieldType.ToString();

            if (_fieldBanner != null)
            {
                // Accent strip color driven by field's identity color
                var accent = _fieldBanner.Q("field-proximity-banner__accent");
                if (accent != null)
                    accent.style.backgroundColor = new StyleColor(field.fieldColor);
            }

            SetElementVisible(_fieldBanner, true);
        }

        /// <summary>Hides the field proximity banner.</summary>
        public void HideFieldBanner() => SetElementVisible(_fieldBanner, false);

        // ============================================================
        // Output selector
        // ============================================================

        private void ShowOutputSelector(List<RecipeSO> options)
        {
            if (_outputSelector == null || _outputSelectorOptions == null) return;

            _outputSelectorOptions.Clear();

            foreach (var recipe in options)
            {
                var captured = recipe;
                var item     = recipe.outputItem;
                string label = item != null
                    ? (item.displayName ?? item.name)
                    : recipe.displayName;

                var btn = new Button { text = label };
                btn.AddToClassList("output-option-btn");
                btn.clicked += () =>
                {
                    SetElementVisible(_outputSelector, false);
                    placementController?.SelectOutput(captured);
                };
                _outputSelectorOptions.Add(btn);
            }

            // Wire cancel button once (clear existing callbacks by replacing via query each time)
            var cancelBtn = _outputSelector.Q<Button>("btn-cancel-output");
            if (cancelBtn != null)
            {
                cancelBtn.clicked -= OnCancelOutputSelector;
                cancelBtn.clicked += OnCancelOutputSelector;
            }

            SetElementVisible(_outputSelector, true);
        }

        private void OnCancelOutputSelector()
        {
            SetElementVisible(_outputSelector, false);
            placementController?.CancelPlacement();
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

        // ============================================================
        // Building inspector
        // ============================================================

        /// <summary>
        /// Opens the building inspector panel for <paramref name="entity"/>.
        /// Called by <see cref="BuildingInspectorController"/> when the player taps a building.
        /// </summary>
        public void ShowBuildingInspector(Entity entity, string buildingName)
        {
            if (!_ecsReady) return;
            _inspectorEntity = entity;
            if (_inspectorBuildingName != null) _inspectorBuildingName.text = buildingName;
            RefreshInspectorContent();
            SetElementVisible(_buildingInspectorPanel, true);
        }

        public void HideBuildingInspector()
        {
            SetElementVisible(_buildingInspectorPanel, false);
            _inspectorEntity = Entity.Null;
        }

        private void TickBuildingInspector()
        {
            if (_inspectorEntity == Entity.Null) return;
            _inspectorRefreshTimer += Time.deltaTime;
            if (_inspectorRefreshTimer < 0.5f) return;
            _inspectorRefreshTimer = 0f;
            RefreshInspectorContent();
        }

        private void RefreshInspectorContent()
        {
            if (_inspectorContent == null || !_ecsReady) return;
            if (_inspectorEntity == Entity.Null) return;
            if (!_em.Exists(_inspectorEntity)) { HideBuildingInspector(); return; }

            _inspectorContent.Clear();

            // Active / type
            if (_em.HasComponent<BuildingData>(_inspectorEntity))
            {
                var d = _em.GetComponentData<BuildingData>(_inspectorEntity);
                AddInspectorRow($"Active: {(d.IsActive ? "Yes" : "No")}");
            }

            // Grid position
            if (_em.HasComponent<GridPosition>(_inspectorEntity))
            {
                var p = _em.GetComponentData<GridPosition>(_inspectorEntity);
                AddInspectorRow($"Cell: ({p.Cell.x}, {p.Cell.y})");
            }

            // Collector rate & timer
            if (_em.HasComponent<CollectorData>(_inspectorEntity))
            {
                var col      = _em.GetComponentData<CollectorData>(_inspectorEntity);
                float rate   = col.OutputRate > 0f ? col.OutputRate : 1f;
                float next   = Mathf.Max(0f, (1f / rate) - col.Timer);
                AddInspectorRow("—— Collector ——");
                AddInspectorRow($"Output rate: {col.OutputRate:F1} /s");
                AddInspectorRow($"Next item in: {next:F2}s");
            }

            // Output inventory
            if (_em.HasBuffer<BuildingOutputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<BuildingOutputSlot>(_inspectorEntity, isReadOnly: true);
                AddInspectorRow("—— Output Buffer ——");
                if (buf.Length == 0)
                {
                    AddInspectorRow("  (empty)");
                }
                else
                {
                    for (int i = 0; i < buf.Length; i++)
                    {
                        string name = ItemName(buf[i].ItemID);
                        AddInspectorRow($"  {name}  ×  {buf[i].Quantity}");
                    }
                }
            }

            // Input inventory
            if (_em.HasBuffer<BuildingInputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<BuildingInputSlot>(_inspectorEntity, isReadOnly: true);
                if (buf.Length > 0)
                {
                    AddInspectorRow("—— Input Buffer ——");
                    for (int i = 0; i < buf.Length; i++)
                    {
                        string name = ItemName(buf[i].ItemID);
                        AddInspectorRow($"  {name}  ×  {buf[i].Quantity}");
                    }
                }
            }

            // What this building produces (recipe output)
            if (_em.HasBuffer<RecipeOutputSlot>(_inspectorEntity))
            {
                var buf = _em.GetBuffer<RecipeOutputSlot>(_inspectorEntity, isReadOnly: true);
                if (buf.Length > 0)
                {
                    AddInspectorRow("—— Produces ——");
                    for (int i = 0; i < buf.Length; i++)
                    {
                        string name = ItemName(buf[i].ItemID);
                        AddInspectorRow($"  {name}  ×  {buf[i].Quantity}");
                    }
                }
            }

            // Recipe picker — shown for buildings with multiple supported recipes
            var buildingSO = GetBuildingSO(_inspectorEntity);
            if (buildingSO?.supportedRecipes != null && buildingSO.supportedRecipes.Length > 1)
            {
                AddInspectorRow("—— Set Recipe ——");
                foreach (var r in buildingSO.supportedRecipes)
                {
                    if (r == null) continue;
                    var captured = r;
                    var btn = new Button { text = r.displayName ?? r.name };
                    btn.AddToClassList("craft-btn");
                    btn.clicked += () =>
                    {
                        SetBuildingRecipe(_inspectorEntity, captured);
                        RefreshInspectorContent();
                    };
                    _inspectorContent?.Add(btn);
                }
            }
        }

        private BuildingSO GetBuildingSO(Entity entity)
        {
            if (!_ecsReady || !_em.HasComponent<BuildingData>(entity)) return null;
            int buildingType = _em.GetComponentData<BuildingData>(entity).BuildingType;
            if (placementController?.availableBuildings == null) return null;
            foreach (var entry in placementController.availableBuildings)
            {
                if (entry.building != null && entry.building.buildingId == buildingType)
                    return entry.building;
            }
            return null;
        }

        private void SetBuildingRecipe(Entity entity, RecipeSO recipe)
        {
            if (!_ecsReady || !_em.Exists(entity) || recipe == null) return;

            _em.SetComponentData(entity, new RecipeProcessData
            {
                RecipeID        = recipe.recipeId,
                CraftTime       = recipe.baseCraftTime,
                Progress        = 0f,
                InputsSatisfied = false,
                IsCrafting      = false
            });

            var inputBuf = _em.GetBuffer<RecipeInputSlot>(entity);
            inputBuf.Clear();
            if (recipe.inputs != null)
            {
                foreach (var input in recipe.inputs)
                {
                    if (input.item == null) continue;
                    inputBuf.Add(new RecipeInputSlot { ItemID = input.item.itemId, Quantity = input.quantity });
                }
            }

            var outputBuf = _em.GetBuffer<RecipeOutputSlot>(entity);
            outputBuf.Clear();
            if (recipe.outputItem != null)
                outputBuf.Add(new RecipeOutputSlot
                {
                    ItemID   = recipe.outputItem.itemId,
                    Quantity = recipe.outputQuantity
                });
        }

        private void AddInspectorRow(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("recipe-inputs");
            _inspectorContent?.Add(lbl);
        }

        private static string ItemName(int itemID)
        {
            var item = ItemDatabase.Instance?.Get(itemID);
            return item?.displayName ?? item?.symbol ?? $"Item {itemID}";
        }

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
            CloseDrawer();
            CancelActiveModes();
        }

        private void ToggleDrawer()
        {
            if (_drawerOpen) CloseDrawer();
            else             OpenDrawer();
        }

        private void OpenDrawer()
        {
            if (_drawerPanel == null) return;
            _drawerOpen = true;
            _drawerPanel.AddToClassList("left-drawer--open");
            SetElementVisible(_drawerBackdrop, true);
        }

        private void CloseDrawer()
        {
            if (_drawerPanel == null) return;
            _drawerOpen = false;
            _drawerPanel.RemoveFromClassList("left-drawer--open");
            SetElementVisible(_drawerBackdrop, false);
        }

        private void CancelActiveModes()
        {
            placementController?.CancelPlacement();
            conveyorController?.CancelConveyorMode();
            deconstructController?.CancelDeconstructMode();
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
