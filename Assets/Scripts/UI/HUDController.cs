using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        // Sub-controllers (siblings on same GameObject)
        private HUDStatusBarController              _statusBar;
        private HUDBuildingInspectorSubController   _inspector;
        private HUDBannerController                 _banner;
        private PrestigeShopSubController           _prestigeShop;

        // ---- ECS (retained for panel content queries) ----
        private EntityManager _em;
        private EntityQuery   _progressQuery;
        private EntityQuery   _prestigeQuery;
        private EntityQuery   _powerQuery;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _tutorialQuery;
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
        private VisualElement _drawerPanel;
        private VisualElement _drawerBackdrop;
        private bool          _drawerOpen;

        // ---- Placement controls ----
        private Button _btnRotateOutput;
        private Button _btnFlipBuilding;

        // ---- Conveyor placement ----
        private VisualElement _conveyorOverlay;

        // ---- Deconstruct mode ----
        private VisualElement _deconstructOverlay;

        // ---- Tutorial events ----
        public event System.Action OnDrawerOpened;
        public event System.Action OnResearchPanelOpened;
        public event System.Action OnRecipePanelOpened;

        // ---- Output selector ----
        private VisualElement _outputSelector;
        private VisualElement _outputSelectorOptions;
        private Label         _outputSelectorTitle;

        // ---- Tooltip ----
        private VisualElement _tooltipPopup;
        private Label         _tooltipTitle, _tooltipBody;


        // ============================================================
        // Unity lifecycle
        // ============================================================

        void Awake()
        {
            if (deconstructController == null)
                deconstructController = FindAnyObjectByType<DeconstructController>();

            _statusBar    = GetComponent<HUDStatusBarController>();
            _inspector    = GetComponent<HUDBuildingInspectorSubController>();
            _banner       = GetComponent<HUDBannerController>();
            _prestigeShop = GetComponent<PrestigeShopSubController>();
        }

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            if (root == null)
            {
                GameLogger.Error("[HUDController] rootVisualElement is null in OnEnable — UIDocument not ready yet.");
                return;
            }

            if (SceneLoader.IsTransitioning)
            {
                root.style.display = DisplayStyle.None;
                // Re-query at callback time; 'root' may be stale if the UIDocument was
                // recycled between OnEnable and the transition completing.
                var doc = GetComponent<UIDocument>();
                SceneLoader.OnTransitionComplete += () =>
                {
                    var liveRoot = doc != null ? doc.rootVisualElement : null;
                    if (liveRoot != null)
                        liveRoot.style.display = DisplayStyle.Flex;
                    else
                        GameLogger.Warning("[HUDController] rootVisualElement null on transition complete — HUD may stay hidden.");
                };
            }

            QueryElements(root);
            _statusBar?.Init(root);
            _inspector?.Init(root, placementController);
            _banner?.Init(root);
            _prestigeShop?.Init(root, this);
            BindButtons(root);

            CloseAllPanels();
            SetElementVisible(_placementOverlay,   false);
            SetElementVisible(_conveyorOverlay,    false);
            SetElementVisible(_deconstructOverlay, false);
            SetElementVisible(root.Q("notification-banner"),   false);
            SetElementVisible(root.Q("tutorial-hint-banner"),  false);
            SetElementVisible(root.Q("field-proximity-banner"), false);
            SetElementVisible(_outputSelector, false);
            SetElementVisible(_tooltipPopup, false);
            SetElementVisible(root.Q("building-inspector-panel"), false);

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
            _tutorialQuery  = _em.CreateEntityQuery(ComponentType.ReadOnly<TutorialStateData>());
            _ecsReady = true;

            _statusBar?.SetECSContext(_em, _inventoryQuery, _progressQuery, _powerQuery);
            _inspector?.SetECSContext(_em);
            _prestigeShop?.SetECSContext(_em);
        }

        void Update()
        {
            _statusBar?.Tick();
            _inspector?.Tick();
        }

        // ============================================================
        // Element queries & button binding
        // ============================================================

        private void QueryElements(VisualElement root)
        {
            // HUD chrome
            _drawerPanel    = root.Q("left-drawer");
            _drawerBackdrop = root.Q("drawer-backdrop");

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

        }

        private void BindButtons(VisualElement root)
        {
            // Drawer toggle
            root.Q<Button>("btn-drawer-handle").clicked += ToggleDrawer;
            if (_drawerBackdrop != null)
                _drawerBackdrop.RegisterCallback<ClickEvent>(_ => CloseDrawer());

            // Drawer nav buttons — non-research buttons respect the tutorial nav gate
            root.Q<Button>("btn-recipes").clicked      += () => TryOpenPanel(OpenRecipePanel);
            root.Q<Button>("btn-buildings").clicked    += () => TryOpenPanel(OpenBuildingsPanel);
            root.Q<Button>("btn-codex").clicked        += () => TryOpenPanel(OpenCodexPanel);
            root.Q<Button>("btn-research").clicked     += OpenResearchPanel;
            root.Q<Button>("btn-upgrades").clicked     += () => TryOpenPanel(OpenUpgradesPanel);
            root.Q<Button>("btn-achievements").clicked += () => TryOpenPanel(OpenAchievementsPanel);
            root.Q<Button>("btn-pvp").clicked          += () => TryOpenPanel(OpenPVPPanel);

            // Top bar
            root.Q<Button>("btn-prestige").clicked += OpenPrestigePanel;
            root.Q<Button>("btn-settings").clicked += () => GameLogger.Debug("[HUD] Settings — coming soon");

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
        // Per-frame refresh (delegated to HUDStatusBarController)
        // ============================================================

        /// <summary>Formats the power readout for the top bar label.</summary>
        internal static string FormatPowerLabel(float current, float max) =>
            max > 0f ? $"⚡ {current:0.#} / {max:0.#} eV" : "⚡ No power";

        // ============================================================
        // Panel openers
        // ============================================================

        private void OpenRecipePanel()
        {
            CloseAllPanels();
            BuildRecipeList();
            SetElementVisible(_recipePanel, true);
            OnRecipePanelOpened?.Invoke();
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
            OnResearchPanelOpened?.Invoke();
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

            bool tutorialActive = false;
            if (_ecsReady && !_tutorialQuery.IsEmpty)
                tutorialActive = _tutorialQuery.GetSingleton<TutorialStateData>().IsActive;

            foreach (var research in researchService.AllResearch)
            {
                if (research == null) continue;

                bool unlocked   = researchService.IsUnlocked(research.id);
                bool prereqsMet = research.prerequisites == null || research.prerequisites.Length == 0 ||
                                  System.Array.TrueForAll(research.prerequisites,
                                      p => p == null || researchService.IsUnlocked(p.id));

                // During the tutorial, only show research that is already unlocked
                // or whose prerequisites are all satisfied (the immediate frontier).
                if (tutorialActive && !unlocked && !prereqsMet) continue;

                bool canPurchase = !unlocked && prereqsMet && researchService.CanPurchase(research);

                var card = new VisualElement();
                card.AddToClassList("research-card");
                if (unlocked) card.AddToClassList("research-card--unlocked");

                // Name
                var nameLabel = new Label(unlocked ? $"✓  {research.displayName}" : research.displayName);
                nameLabel.AddToClassList("research-card-name");
                card.Add(nameLabel);

                // Description (wrapping)
                if (!string.IsNullOrEmpty(research.description))
                {
                    var descLabel = new Label(research.description);
                    descLabel.AddToClassList("research-card-desc");
                    card.Add(descLabel);
                }

                if (!unlocked)
                {
                    var footer = new VisualElement();
                    footer.AddToClassList("research-card-footer");

                    if (!prereqsMet)
                    {
                        // Show what's blocking this research
                        var prereqNames = string.Join(", ",
                            System.Array.ConvertAll(research.prerequisites,
                                p => p != null ? p.displayName : "?"));
                        var prereqLabel = new Label($"Requires: {prereqNames}");
                        prereqLabel.AddToClassList("research-card-prereq");
                        footer.Add(prereqLabel);
                    }
                    else
                    {
                        // Cost badge
                        bool affordable = currentEntropy >= research.costBaseCurrency;
                        var costLabel   = new Label($"◈ {research.costBaseCurrency:N0}");
                        costLabel.AddToClassList("research-card-cost");
                        if (!affordable) costLabel.AddToClassList("research-card-cost--unaffordable");
                        footer.Add(costLabel);

                        // Purchase button
                        var purchaseBtn = new Button { text = "Unlock" };
                        purchaseBtn.AddToClassList("craft-btn");
                        purchaseBtn.SetEnabled(canPurchase);

                        var captured = research;
                        purchaseBtn.clicked += () =>
                        {
                            researchService.Purchase(captured);
                            BuildResearchList();
                        };
                        footer.Add(purchaseBtn);
                    }

                    card.Add(footer);
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
            _prestigeShop?.Refresh();
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

            var inventoryCounts = craftService?.GetInventoryCounts() ?? new Dictionary<int, int>();

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
                bool canCraft = !isLocked && (craftService?.CanCraft(recipe) ?? false);

                var row = new VisualElement();
                row.AddToClassList("recipe-row");
                if (isLocked) row.AddToClassList("recipe-row--locked");
                else if (!canCraft) row.AddToClassList("recipe-row--unavailable");

                var info = new VisualElement();
                info.AddToClassList("recipe-info");

                var nameLabel = new Label(recipe.name);
                nameLabel.AddToClassList("recipe-name");

                var inputsContainer = BuildIngredientsUI(recipe, inventoryCounts);

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
                info.Add(inputsContainer);

                var craftBtn = new Button { text = isLocked ? "Locked" : "Craft" };
                craftBtn.AddToClassList("craft-btn");
                craftBtn.SetEnabled(!isLocked && canCraft);
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
                    InventoryPopupController.NotifyWarning("No eligible building");
            }
            else
            {
                if (!craftService.TryCraft(recipe))
                    InventoryPopupController.NotifyWarning("Not enough materials");
                else
                    BuildRecipeList();
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

                int rawCost = entry.building?.entropyCost ?? 0;
                float reduction = !_prestigeQuery.IsEmpty
                    ? _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity()).CostReduction
                    : 0f;
                int cost = rawCost > 0 ? (int)(rawCost * (1f - reduction)) : 0;
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
            int rawCost = entry.building?.entropyCost ?? 0;
            if (rawCost <= 0 || !_ecsReady || _progressQuery.IsEmpty) return;

            float reduction = !_prestigeQuery.IsEmpty
                ? _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity()).CostReduction
                : 0f;
            int cost = (int)(rawCost * (1f - reduction));

            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            long actual = System.Math.Min(cost, progress.BaseCurrency);
            progress.BaseCurrency      -= actual;
            progress.TotalEntropySpent += actual;
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
        // Banner pass-throughs (delegated to HUDBannerController)
        // ============================================================

        public void ShowNotification(string icon, string message, string modifier = null) =>
            _banner?.ShowNotification(icon, message, modifier);

        public void ShowFieldBanner(FieldSO field)   => _banner?.ShowFieldBanner(field);
        public void HideFieldBanner()                => _banner?.HideFieldBanner();
        public void ShowTutorialHint(string message) => _banner?.ShowTutorialHint(message);
        public void HideTutorialHint()               => _banner?.HideTutorialHint();

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
        // Building inspector pass-throughs (delegated to HUDBuildingInspectorSubController)
        // ============================================================

        public void ShowBuildingInspector(Entity entity, string buildingName) =>
            _inspector?.ShowBuildingInspector(entity, buildingName);

        public void HideBuildingInspector() => _inspector?.HideBuildingInspector();

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

        private void TryOpenPanel(System.Action open)
        {
            if (ToastService.Instance != null && ToastService.Instance.IsLocked("nav_other"))
            {
                ToastService.Instance.Post("nav_other");
                return;
            }
            open();
        }

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
            OnDrawerOpened?.Invoke();
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
        /// Must stay in sync with the formula in <see cref="PrestigeSystem"/>.
        /// </summary>
        internal static long CalculatePrestigePreview(float netWorth)
        {
            var cfg    = GameBootstrap.Instance?.gameConfig;
            float pbase  = cfg != null ? cfg.prestigeBaseValue     : 5000f;
            float pscale = cfg != null ? cfg.prestigeCurrencyScale : 50f;
            if (pbase <= 0f || netWorth <= 0f) return 0L;
            long earned = (long)System.Math.Max(0, System.Math.Floor(System.Math.Log10(netWorth / pbase) * pscale));
            float gainBonus = PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.PrestigeGainMultiplier) ?? 0f;
            if (gainBonus > 0f) earned = (long)(earned * (1f + gainBonus));
            return earned;
        }

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

        internal static VisualElement BuildIngredientsUI(
            RecipeJson recipe,
            Dictionary<int, int> inventoryCounts)
        {
            var row = new VisualElement();
            row.AddToClassList("recipe-inputs-row");

            if (recipe.inputs == null || recipe.inputs.Count == 0)
                return row;

            bool first = true;
            foreach (var input in recipe.inputs)
            {
                if (!first)
                {
                    var sep = new Label("+");
                    sep.AddToClassList("recipe-input-sep");
                    row.Add(sep);
                }
                first = false;

                var item   = ItemDatabase.Instance?.Get(input.id);
                string sym = item?.symbol ?? item?.displayName ?? input.id;

                int itemId = ItemDatabase.Instance?.GetItemId(input.id) ?? -1;
                int count  = (itemId >= 0 && inventoryCounts.TryGetValue(itemId, out int c)) ? c : 0;

                var lbl = new Label($"{count}/{input.quantity} {sym}");
                lbl.AddToClassList(count >= input.quantity ? "recipe-input-met" : "recipe-input-missing");
                row.Add(lbl);
            }

            return row;
        }
    }
}
