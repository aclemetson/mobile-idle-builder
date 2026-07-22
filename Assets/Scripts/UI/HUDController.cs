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
        private ManagersSubController               _managers;
        private MegastructureSubController          _megastructure;
        private DailyEventsSubController            _daily;
        private RewardedAdsSubController            _rewards;
        private IdleReturnSubController             _idleReturn;
        private GameUpdateNoticeSubController       _gameUpdate;
        private HUDPremiumShopSubController         _shop;
        private HUDSettingsSubController            _settings;
        private SitesSubController                  _sites;
        private WorldsSubController                 _worlds;

        // ---- Power-connection overlay toggle ----
        private Button                              _btnPowerToggle;
        private PowerConnectionOverlayController    _powerOverlay;
        private bool                                _powerConnectionsShown;

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
                              _achievementsPanel, _pvpPanel, _placementOverlay,
                              _shopPanel, _settingsPanel, _dailyPanel, _rewardsPanel, _sitesPanel, _worldsPanel, _managersPanel, _megastructurePanel;
        private VisualElement[] _allPanels;

        // ---- Panel content ----
        private ScrollView _recipeList, _buildingsList, _codexList,
                           _researchList, _upgradesList, _achievementsList, _pvpLeaderboardList;
        private Label      _prestigeSummary, _prestigeCurrency, _placementLabel, _achievementsTitle;
        // Research timer UI (live countdown + skip on the in-progress card)
        private Label      _researchCountdownLabel;
        private Button     _researchSkipBtn;
        private bool       _researchSkipConfirming;
        private UnityEngine.UIElements.IVisualElementScheduledItem _researchTicker;
        private Label      _pvpStateLabel, _pvpTimerLabel, _pvpLockLabel, _pvpCompletedLabel;

        // Live affordability refresh: automation can raise entropy past a building's cost while
        // the Build panel is open. We re-enable buy buttons on entropy change instead of only at
        // panel-open. Rows captured in BuildBuildingsList; re-evaluated in Update.
        private readonly List<(Button btn, Label costLabel, int cost)> _buildingAffordRows = new();
        private long _lastAffordEntropy = long.MinValue;
        private Button     _btnEnterPVP;

        // ---- HUD chrome ----
        private VisualElement _drawerPanel;
        private VisualElement _drawerBackdrop;
        private bool          _drawerOpen;

        // ---- Placement controls ----
        private Button        _btnRotateOutput;
        private Button        _btnFlipBuilding;
        private VisualElement _placementConfirmPopup;
        private VisualElement _placementBar;
        private Button        _btnConfirmPlace;
        private Button        _btnCancelCandidate;
        private Button        _btnRotateCandidate;

        // ---- Conveyor placement ----
        private VisualElement _conveyorOverlay;
        private VisualElement _conveyorBar;
        private Label         _conveyorLabel;
        private Button        _btnConveyorRotate;
        private Button        _btnConveyorConfirm;
        private Button        _btnConveyorCancelCandidate;
        private Button        _btnConveyorNewStart;
        private Button        _btnConveyorMode;

        // ---- Deconstruct mode ----
        private VisualElement _deconstructOverlay;

        // ---- Tutorial events ----
        public event System.Action OnDrawerOpened;
        public event System.Action OnResearchPanelOpened;
        public event System.Action OnRecipePanelOpened;
        public event System.Action OnConveyorPlaced;
        public event System.Action OnBuildingRecipeSet;

        // ---- Output selector ----
        private VisualElement _outputSelector;
        private VisualElement _outputSelectorOptions;
        private Label         _outputSelectorTitle;

        // ---- Tooltip ----
        private VisualElement _tooltipPopup;
        private Label         _tooltipTitle, _tooltipBody;

        // ---- Achievements category tabs ----
        private AchievementCategory _achActiveCategory = AchievementCategory.Daily;
        private Button _achTabDaily, _achTabWeekly, _achTabMonthly, _achTabProgression;
        private Label  _achResetLabel;
        private Button _achClaimAllBtn;

        // ---- Achievements gate ----
        private Button    _btnAchievements;
        private Label     _achievementsLockedHint;
        private bool      _achievementsUnlocked;

        // ---- Megastructure gate (nav button hidden until megastructure_theory is researched) ----
        private Button    _btnMegastructure;
        private Coroutine _achievementsHintPulse;
        private Button    _achievementsHintPulseTarget;


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
            _managers     = GetComponent<ManagersSubController>();
            _megastructure = GetComponent<MegastructureSubController>();
            _daily        = GetComponent<DailyEventsSubController>();
            // Added in code (not the scene) so the Free Rewards panel works without manual wiring.
            _rewards      = GetComponent<RewardedAdsSubController>() ?? gameObject.AddComponent<RewardedAdsSubController>();
            _idleReturn   = GetComponent<IdleReturnSubController>();
            _gameUpdate   = GetComponent<GameUpdateNoticeSubController>() ?? gameObject.AddComponent<GameUpdateNoticeSubController>();
            _shop         = GetComponent<HUDPremiumShopSubController>();
            _settings     = GetComponent<HUDSettingsSubController>();
            _sites        = GetComponent<SitesSubController>();
            _worlds       = GetComponent<WorldsSubController>();
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

            UIInputBlocker.Register(GetComponent<UIDocument>());

            QueryElements(root);
            _statusBar?.Init(root);
            _inspector?.Init(root, placementController, this);
            _banner?.Init(root);
            _prestigeShop?.Init(root, this);
            _managers?.Init(root, this);
            _megastructure?.Init(root, this);
            _daily?.Init(root, this);
            _rewards?.Init(root, this);
            _idleReturn?.Init(root, this);
            _gameUpdate?.Init(root, this);
            _shop?.Initialize(root);
            _settings?.Initialize(root);
            _sites?.Init(root, this);
            _worlds?.Init(root, this);
            BindButtons(root);
            ApplyAchievementsGate();
            ApplyMegastructureGate();
            ApplyFeatureFlagGates(root);
            _achievementsUnlocked = SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;
            GameLogger.Info($"[HUD] Achievements gate at scene start: unlocked={_achievementsUnlocked} (hasCompletedFirstRun={SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun})");

            CloseAllPanels();
            SetElementVisible(_placementOverlay,   false);
            SetElementVisible(_placementConfirmPopup, false);
            SetElementVisible(_conveyorOverlay,    false);
            SetElementVisible(_deconstructOverlay, false);
            // notification-banner starts hidden via CSS translate (no .hidden class needed)
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
                placementController.OnCandidateChanged       += OnCandidateChanged;
                placementController.IsPointerOverPlacementUI  = IsPointerOverPlacementUI;
            }

            if (conveyorController != null)
            {
                conveyorController.OnPlacingChanged   += OnConveyorPlacingChanged;
                conveyorController.OnModeChanged      += OnConveyorModeChanged;
                conveyorController.OnCandidateChanged += OnConveyorCandidateChanged;
                conveyorController.OnStartChanged     += OnConveyorStartChanged;
                conveyorController.OnChainPlaced      += RaiseConveyorPlaced;
                conveyorController.IsPointerOverConveyorUI = IsPointerOverConveyorUI;
            }

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
            UIInputBlocker.Unregister(GetComponent<UIDocument>());

            if (placementController != null)
            {
                placementController.OnPlacingChanged         -= OnPlacingChanged;
                placementController.OnOutputSelectionRequired -= ShowOutputSelector;
                placementController.OnBuildingPlaced         -= OnBuildingPlaced;
                placementController.OnCandidateChanged       -= OnCandidateChanged;
                placementController.IsPointerOverPlacementUI  = null;
            }

            if (conveyorController != null)
            {
                conveyorController.OnPlacingChanged   -= OnConveyorPlacingChanged;
                conveyorController.OnModeChanged      -= OnConveyorModeChanged;
                conveyorController.OnCandidateChanged -= OnConveyorCandidateChanged;
                conveyorController.OnStartChanged     -= OnConveyorStartChanged;
                conveyorController.OnChainPlaced      -= RaiseConveyorPlaced;
                conveyorController.IsPointerOverConveyorUI = null;
            }

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
            _powerQuery     = _em.CreateEntityQuery(ComponentType.ReadOnly<PowerGridState>());
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>()
            );
            _tutorialQuery  = _em.CreateEntityQuery(ComponentType.ReadOnly<TutorialStateData>());
            _ecsReady = true;

            _statusBar?.SetECSContext(_em, _inventoryQuery, _progressQuery, _powerQuery);
            _inspector?.SetECSContext(_em);
            _prestigeShop?.SetECSContext(_em);
            _sites?.SetECSContext(_em);
            _worlds?.SetECSContext(_em);
        }

        void Update()
        {
            _statusBar?.Tick();
            _inspector?.Tick();
            UpdatePlacementConfirmPopup();
            RefreshBuildingAffordabilityIfChanged();

            // One-shot: detect first prestige in the same session and re-apply the gate
            if (!_achievementsUnlocked)
            {
                bool firstRunNow = SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;
                if (firstRunNow)
                {
                    GameLogger.Info($"[HUD] Achievements gate flipped — showing unlock hint. _banner={_banner != null}");
                    _achievementsUnlocked = true;
                    ApplyAchievementsGate();
                    achievementService?.InitPostPrestige();
                    ShowTutorialHint("🏆 Achievements unlocked! Open the drawer and tap Achievements to get started.");
                    StartAchievementsHintPulse();
                }
            }
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
            _managersPanel     = root.Q("managers-panel");
            _megastructurePanel = root.Q("megastructure-panel");
            _dailyPanel        = root.Q("daily-panel");
            _rewardsPanel      = root.Q("rewards-panel");
            _achievementsPanel = root.Q("achievements-panel");
            _pvpPanel          = root.Q("pvp-panel");
            _prestigePanel     = root.Q("prestige-panel");
            _shopPanel         = root.Q("shop-panel");
            _settingsPanel     = root.Q("settings-panel");
            _sitesPanel        = root.Q("sites-panel");
            _worldsPanel       = root.Q("worlds-panel");
            _placementOverlay  = root.Q("placement-overlay");

            _allPanels = new[]
            {
                _recipePanel, _buildingsPanel, _codexPanel,
                _researchPanel, _upgradesPanel, _managersPanel, _megastructurePanel, _dailyPanel, _rewardsPanel, _achievementsPanel, _pvpPanel, _prestigePanel,
                _shopPanel, _settingsPanel, _sitesPanel, _worldsPanel
            };

            // Panel content
            _recipeList       = root.Q<ScrollView>("recipe-list");
            _buildingsList    = root.Q<ScrollView>("buildings-list");
            _codexList        = root.Q<ScrollView>("codex-list");
            _researchList     = root.Q<ScrollView>("research-list");
            _upgradesList     = root.Q<ScrollView>("upgrades-list");
            _achievementsList    = root.Q<ScrollView>("achievements-list");
            _pvpLeaderboardList = root.Q<ScrollView>("pvp-leaderboard-list");

            // Achievements category tabs
            _achTabDaily       = root.Q<Button>("ach-tab-daily");
            _achTabWeekly      = root.Q<Button>("ach-tab-weekly");
            _achTabMonthly     = root.Q<Button>("ach-tab-monthly");
            _achTabProgression = root.Q<Button>("ach-tab-progression");
            _achResetLabel     = root.Q<Label>("ach-reset-label");
            _achClaimAllBtn    = root.Q<Button>("ach-claim-all-btn");

            // Achievements gate
            _btnAchievements        = root.Q<Button>("btn-achievements");
            _achievementsLockedHint = root.Q<Label>("achievements-locked-hint");
            _btnMegastructure       = root.Q<Button>("btn-megastructure");

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
            _placementConfirmPopup = root.Q("placement-confirm-popup");
            _placementBar          = root.Q("placement-bar");
            _btnConfirmPlace       = root.Q<Button>("btn-confirm-place");
            _btnCancelCandidate    = root.Q<Button>("btn-cancel-candidate");
            _btnRotateCandidate    = root.Q<Button>("btn-rotate-candidate");
            _achievementsTitle = root.Q<Label>("achievements-title");

            // Output selector
            _outputSelector        = root.Q("output-selector");
            _outputSelectorOptions = root.Q("output-selector__options");
            _outputSelectorTitle   = root.Q<Label>("output-selector__title");

            // Conveyor placement
            _conveyorOverlay            = root.Q("conveyor-overlay");
            _conveyorBar                = root.Q("conveyor-bar");
            _conveyorLabel              = root.Q<Label>("conveyor-label");
            _btnConveyorRotate          = root.Q<Button>("btn-conveyor-rotate");
            _btnConveyorConfirm         = root.Q<Button>("btn-conveyor-confirm");
            _btnConveyorCancelCandidate = root.Q<Button>("btn-conveyor-cancel-candidate");
            _btnConveyorNewStart        = root.Q<Button>("btn-conveyor-new-start");
            _btnConveyorMode            = root.Q<Button>("btn-conveyor-mode");

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
            root.Q<Button>("btn-managers")?.RegisterCallback<ClickEvent>(_ => TryOpenPanel(OpenManagersPanel));
            root.Q<Button>("btn-megastructure")?.RegisterCallback<ClickEvent>(_ => TryOpenPanel(OpenMegastructurePanel));
            var btnDaily = root.Q<Button>("btn-daily");
            if (btnDaily != null) btnDaily.clicked     += () => TryOpenPanel(OpenDailyPanel);
            // Free Rewards (rewarded ads) — nav button hidden by ApplyFeatureFlagGates when ads.enabled is off.
            var btnRewards = root.Q<Button>("btn-rewards");
            if (btnRewards != null)
                btnRewards.clicked += () => TryOpenPanel(OpenRewardsPanel);
            root.Q<Button>("btn-achievements").clicked += () => TryOpenPanel(OpenAchievementsPanel);

            // Achievement category tabs
            if (_achTabDaily       != null) _achTabDaily.clicked       += () => SwitchAchCategory(AchievementCategory.Daily);
            if (_achTabWeekly      != null) _achTabWeekly.clicked      += () => SwitchAchCategory(AchievementCategory.Weekly);
            if (_achTabMonthly     != null) _achTabMonthly.clicked     += () => SwitchAchCategory(AchievementCategory.Monthly);
            if (_achTabProgression != null) _achTabProgression.clicked += () => SwitchAchCategory(AchievementCategory.Progression);
            if (_achClaimAllBtn    != null) _achClaimAllBtn.clicked    += () =>
            {
                achievementService?.ClaimAllRewards();
                BuildAchievementsList();
            };
            root.Q<Button>("btn-sites")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => TryOpenPanel(OpenSitesPanel));
            root.Q<Button>("btn-worlds")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => TryOpenPanel(OpenWorldsPanel));
            root.Q<Button>("btn-pvp").clicked          += () => TryOpenPanel(OpenPVPPanel);
            root.Q<Button>("btn-shop").clicked         += () => TryOpenPanel(OpenShopPanel);

            // Top bar
            root.Q<Button>("btn-prestige").clicked += OpenPrestigePanel;
            root.Q<Button>("btn-settings").clicked += () => TryOpenPanel(OpenSettingsPanel);

            // Power-connection overlay toggle (⚡ button). Restores its persisted on/off state.
            _btnPowerToggle = root.Q<Button>("btn-power-toggle");
            if (_btnPowerToggle != null)
            {
                _powerConnectionsShown = SettingsService.Instance?.Current?.showPowerConnections ?? false;
                ApplyPowerConnectionState();
                _btnPowerToggle.clicked += TogglePowerConnections;
            }

            // Panel close buttons
            root.Q<Button>("btn-close-recipes").clicked      += () => SetElementVisible(_recipePanel,       false);
            root.Q<Button>("btn-close-buildings").clicked    += () => SetElementVisible(_buildingsPanel,    false);
            root.Q<Button>("btn-close-codex").clicked        += () => SetElementVisible(_codexPanel,        false);
            root.Q<Button>("btn-close-research").clicked     += () => SetElementVisible(_researchPanel,     false);
            root.Q<Button>("btn-close-upgrades").clicked     += () => SetElementVisible(_upgradesPanel,     false);
            root.Q<Button>("btn-close-managers")?.RegisterCallback<ClickEvent>(_ => SetElementVisible(_managersPanel, false));
            root.Q<Button>("btn-close-megastructure")?.RegisterCallback<ClickEvent>(_ => SetElementVisible(_megastructurePanel, false));
            var btnCloseDaily = root.Q<Button>("btn-close-daily");
            if (btnCloseDaily != null) btnCloseDaily.clicked += () => SetElementVisible(_dailyPanel, false);
            var btnCloseRewards = root.Q<Button>("btn-close-rewards");
            if (btnCloseRewards != null) btnCloseRewards.clicked += () => SetElementVisible(_rewardsPanel, false);
            root.Q<Button>("btn-close-achievements").clicked += () => SetElementVisible(_achievementsPanel, false);
            root.Q<Button>("btn-close-pvp").clicked              += () => SetElementVisible(_pvpPanel,          false);
            root.Q<Button>("btn-close-prestige").clicked         += () => SetElementVisible(_prestigePanel,     false);
            root.Q<Button>("btn-close-sites")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => SetElementVisible(_sitesPanel, false));
            root.Q<Button>("btn-close-worlds")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => SetElementVisible(_worldsPanel, false));

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
            if (_btnConfirmPlace != null)
                _btnConfirmPlace.clicked += () => placementController?.ConfirmCandidate();
            if (_btnCancelCandidate != null)
                _btnCancelCandidate.clicked += () => placementController?.ClearCandidate();
            if (_btnRotateCandidate != null)
                _btnRotateCandidate.clicked += () => placementController?.Rotate();

            // Conveyor "Finished" (button inside the conveyor overlay)
            root.Q<Button>("btn-cancel-conveyor")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ =>
                conveyorController?.CancelConveyorMode());

            // Conveyor candidate controls + Create/Destroy toggle
            if (_btnConveyorRotate != null)
                _btnConveyorRotate.clicked += () => conveyorController?.RotatePath();
            if (_btnConveyorConfirm != null)
                _btnConveyorConfirm.clicked += () => conveyorController?.ConfirmPath();
            if (_btnConveyorCancelCandidate != null)
                _btnConveyorCancelCandidate.clicked += () => conveyorController?.ClearCandidate();
            if (_btnConveyorNewStart != null)
                _btnConveyorNewStart.clicked += () => conveyorController?.ResetStart();
            if (_btnConveyorMode != null)
                _btnConveyorMode.clicked += () => conveyorController?.ToggleMode();

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
            BuildCodexList();
            SetElementVisible(_codexPanel, true);
        }

        /// <summary>
        /// Populates the codex with an entry (tile icon + name + lore) for every item whose
        /// crafting recipe the player has discovered, using the same known-recipe gate as the
        /// recipe list. Rebuilt each time the panel opens.
        /// </summary>
        private void BuildCodexList()
        {
            if (_codexList == null) return;
            _codexList.Clear();

            var recipes = RecipeDatabase.Instance?.Recipes;
            if (recipes == null) return;

            var seen = new HashSet<int>();
            int shown = 0;
            foreach (var recipe in recipes)
            {
                if (!(RecipeKnowledgeService.Instance?.IsKnown(recipe.id) ?? false)) continue;

                var item = recipe.output != null ? ItemDatabase.Instance?.Get(recipe.output.id) : null;
                if (item == null || !seen.Add(item.itemId)) continue;

                var row = new VisualElement();
                row.AddToClassList("codex-row");

                var icon = MakeItemIcon(item.icon, "codex-icon");
                if (icon != null) row.Add(icon);

                var infoCol = new VisualElement();
                infoCol.AddToClassList("codex-info");

                var name = new Label(item.displayName ?? item.symbol ?? item.id);
                name.AddToClassList("codex-name");
                infoCol.Add(name);

                if (!string.IsNullOrEmpty(item.codexEntry))
                {
                    var entry = new Label(item.codexEntry);
                    entry.AddToClassList("codex-entry");
                    infoCol.Add(entry);
                }

                row.Add(infoCol);
                _codexList.Add(row);
                shown++;
            }

            if (shown == 0)
                _codexList.Add(new Label("Craft items to fill your codex."));
        }

        /// <summary>
        /// Builds a background-image VisualElement for an item's tile sprite, or null when the
        /// item has no icon yet (callers skip adding it so nothing breaks). Shared by the recipe
        /// list, ingredient chips, codex, and the building inspector's recipe picker.
        /// </summary>
        internal static VisualElement MakeItemIcon(Sprite icon, string ussClass)
        {
            if (icon == null) return null;
            var el = new VisualElement();
            el.AddToClassList(ussClass);
            el.style.backgroundImage = new StyleBackground(icon);
            return el;
        }

        private void OpenResearchPanel()
        {
            CloseAllPanels();
            BuildResearchList();
            SetElementVisible(_researchPanel, true);

            // Drive the live research countdown while the panel is open (paused when it closes).
            if (_researchTicker == null)
                _researchTicker = _researchList.schedule.Execute(UpdateResearchCountdown).Every(1000);
            else
                _researchTicker.Resume();

            OnResearchPanelOpened?.Invoke();
        }

        // Updates just the active card's countdown + skip cost each second, so an open inline
        // skip confirm is not blown away by a full rebuild.
        private void UpdateResearchCountdown()
        {
            if (_researchPanel == null || _researchPanel.ClassListContains("hidden"))
            {
                _researchTicker?.Pause();
                return;
            }
            if (researchService == null || !researchService.HasActiveResearch) return;

            double rem = researchService.ActiveRemainingSeconds;
            if (_researchCountdownLabel != null)
                _researchCountdownLabel.text = $"⏳ {FormatResearchTimer(rem)}";

            if (_researchSkipBtn != null && !_researchSkipConfirming)
            {
                long cost     = researchService.ActiveSkipCost;
                long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
                _researchSkipBtn.text = $"Skip  ◆{cost:N0}";
                _researchSkipBtn.SetEnabled(crystals >= cost);
            }
        }

        public static string FormatResearchTimer(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = System.TimeSpan.FromSeconds(System.Math.Ceiling(seconds));
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:D2}s";
            return $"{ts.Seconds}s";
        }

        private void BuildResearchList()
        {
            if (_researchList == null) return;
            _researchList.Clear();

            // Reset per-build references for the active-research card (reassigned below if present).
            _researchCountdownLabel = null;
            _researchSkipBtn        = null;
            _researchSkipConfirming = false;

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

                bool isActive    = researchService.HasActiveResearch &&
                                   researchService.ActiveResearchId == research.id;
                bool labBusy     = researchService.HasActiveResearch && !isActive;

                if (isActive) card.AddToClassList("research-card--in-progress");

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
                    else if (isActive)
                    {
                        // In-progress: live countdown + skip-for-crystals
                        _researchCountdownLabel = new Label($"⏳ {FormatResearchTimer(researchService.ActiveRemainingSeconds)}");
                        _researchCountdownLabel.AddToClassList("research-card-cost");
                        footer.Add(_researchCountdownLabel);
                        BuildResearchSkipUi(footer, research);
                    }
                    else
                    {
                        // Cost + effective research time (after the Research Overdrive discount),
                        // stacked so the Start button stays on the right.
                        var info = new VisualElement();
                        info.AddToClassList("research-card-info");

                        bool affordable = currentEntropy >= research.costBaseCurrency;
                        var costLabel   = new Label($"◈ {research.costBaseCurrency:N0}");
                        costLabel.AddToClassList("research-card-cost");
                        if (!affordable) costLabel.AddToClassList("research-card-cost--unaffordable");
                        info.Add(costLabel);

                        int effSecs    = researchService.EffectiveDurationSeconds(research);
                        var timeLabel  = new Label(effSecs <= 0 ? "Instant" : $"⏳ {FormatResearchTimer(effSecs)}");
                        timeLabel.AddToClassList("research-card-time");
                        info.Add(timeLabel);

                        footer.Add(info);

                        // Start button (disabled while the lab is busy with another research)
                        var startBtn = new Button { text = labBusy ? "Lab busy" : "Start" };
                        startBtn.AddToClassList("craft-btn");
                        startBtn.SetEnabled(!labBusy && canPurchase);

                        var captured = research;
                        startBtn.clicked += () =>
                        {
                            researchService.Purchase(captured);
                            BuildResearchList();
                        };
                        footer.Add(startBtn);
                    }

                    card.Add(footer);
                }

                _researchList.Add(card);
            }
        }

        // Skip button for the in-progress research. First tap reveals a Confirm/Cancel row so
        // spending crystals is always a deliberate two-tap action (no generic confirm modal exists).
        private void BuildResearchSkipUi(VisualElement footer, ResearchSO research)
        {
            long cost     = researchService.ActiveSkipCost;
            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;

            _researchSkipBtn = new Button { text = $"Skip  ◆{cost:N0}" };
            _researchSkipBtn.AddToClassList("craft-btn");
            _researchSkipBtn.SetEnabled(crystals >= cost);
            _researchSkipBtn.clicked += () =>
            {
                _researchSkipConfirming = true;
                footer.Remove(_researchSkipBtn);

                var confirm = new Button { text = $"Confirm  ◆{researchService.ActiveSkipCost:N0}" };
                confirm.AddToClassList("craft-btn");
                confirm.clicked += () =>
                {
                    researchService.SkipActive(); // completion fires OnResearchUnlocked → rebuild
                    BuildResearchList();
                };

                var cancel = new Button { text = "✕" };
                cancel.AddToClassList("craft-btn");
                cancel.clicked += () => BuildResearchList();

                footer.Add(confirm);
                footer.Add(cancel);
            };
            footer.Add(_researchSkipBtn);
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

            // One-shot Quantum Domains introduction when its gate research is unlocked.
            _sites?.NotifyResearchUnlocked(research);

            // Reveal the megastructure nav button when its gating research lands.
            ApplyMegastructureGate();
        }

        private void OpenUpgradesPanel()
        {
            CloseAllPanels();
            _prestigeShop?.Refresh();
            SetElementVisible(_upgradesPanel, true);
        }

        private void OpenManagersPanel()
        {
            CloseAllPanels();
            _managers?.Refresh();
            SetElementVisible(_managersPanel, true);
        }

        private void OpenMegastructurePanel()
        {
            CloseAllPanels();
            _megastructure?.Refresh();
            SetElementVisible(_megastructurePanel, true);
        }

        /// <summary>Shows the megastructure nav button only once its gating research is unlocked.</summary>
        private void ApplyMegastructureGate()
        {
            if (_btnMegastructure == null) return;
            bool unlocked = MegastructureService.Instance?.IsUnlocked() ?? false;
            SetElementVisible(_btnMegastructure, unlocked);
        }

        /// <summary>
        /// Hides nav buttons for features turned off by remote feature flags. Flags resolve from the
        /// cache at this point (last session's values); a change takes effect on the next launch.
        /// </summary>
        private void ApplyFeatureFlagGates(VisualElement root)
        {
            if (!FeatureFlags.PvpEnabled)         SetElementVisible(root.Q<Button>("btn-pvp"),   false);
            if (!FeatureFlags.DailyEventsEnabled) SetElementVisible(root.Q<Button>("btn-daily"), false);
            if (!FeatureFlags.AdsEnabled)         SetElementVisible(root.Q<Button>("btn-rewards"), false);
        }

        private void OpenSitesPanel()
        {
            CloseAllPanels();
            _sites?.ClearNavHighlight();
            _sites?.Refresh();
            SetElementVisible(_sitesPanel, true);
        }

        private void OpenWorldsPanel()
        {
            CloseAllPanels();
            _worlds?.Refresh();
            SetElementVisible(_worldsPanel, true);
        }

        private void OpenDailyPanel()
        {
            CloseAllPanels();
            _daily?.Refresh();
            SetElementVisible(_dailyPanel, true);
        }

        private void OpenRewardsPanel()
        {
            CloseAllPanels();
            _rewards?.Refresh();
            SetElementVisible(_rewardsPanel, true);
        }

        private void OpenShopPanel()
        {
            CloseAllPanels();
            _shop?.Open();
        }

        private void OpenSettingsPanel()
        {
            CloseAllPanels();
            _settings?.Open();
        }

        private void ApplyAchievementsGate()
        {
            bool unlocked = SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;
            if (_btnAchievements != null)
                _btnAchievements.SetEnabled(unlocked);
            SetElementVisible(_achievementsLockedHint, !unlocked);
        }

        private void OpenAchievementsPanel()
        {
            DismissAchievementsHint();
            CloseAllPanels();
            _achActiveCategory = AchievementCategory.Daily;
            BuildAchievementsList();
            SetElementVisible(_achievementsPanel, true);
        }

        private void StartAchievementsHintPulse() =>
            SwapAchievementsHintPulse("btn-drawer-handle");

        private void SwapAchievementsHintPulse(string buttonId)
        {
            if (_achievementsHintPulse != null) { StopCoroutine(_achievementsHintPulse); _achievementsHintPulse = null; }
            _achievementsHintPulseTarget?.RemoveFromClassList("panel-btn--highlight");
            _achievementsHintPulseTarget = null;
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            var btn  = root?.Q<Button>(buttonId);
            if (btn == null) return;
            _achievementsHintPulseTarget = btn;
            _achievementsHintPulse       = StartCoroutine(PulseAchievementsHint(btn));
        }

        private void DismissAchievementsHint()
        {
            HideTutorialHint();
            if (_achievementsHintPulse != null) { StopCoroutine(_achievementsHintPulse); _achievementsHintPulse = null; }
            _achievementsHintPulseTarget?.RemoveFromClassList("panel-btn--highlight");
            _achievementsHintPulseTarget = null;
        }

        private IEnumerator PulseAchievementsHint(Button btn)
        {
            while (true)
            {
                btn.AddToClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
                btn.RemoveFromClassList("panel-btn--highlight");
                yield return new WaitForSecondsRealtime(0.7f);
            }
        }

        private void SwitchAchCategory(AchievementCategory category)
        {
            _achActiveCategory = category;
            BuildAchievementsList();
        }

        private void OnAchievementUnlocked(AchievementSO _)
        {
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

                var outputItem = recipe.output != null ? ItemDatabase.Instance?.Get(recipe.output.id) : null;
                var outputIcon = MakeItemIcon(outputItem?.icon, "item-icon");
                if (outputIcon != null) info.Add(outputIcon);

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

                // Item-creation timer: how long this recipe takes to craft.
                if (recipe.base_craft_time > 0f)
                {
                    var timeLabel = new Label($"Craft time: {recipe.base_craft_time:0.#}s");
                    timeLabel.style.fontSize = 11;
                    timeLabel.style.opacity  = 0.7f;
                    info.Add(timeLabel);
                }

                var craftBtn = new Button { text = isLocked ? "Locked" : "Craft" };
                craftBtn.AddToClassList("craft-btn");
                craftBtn.SetEnabled(!isLocked && canCraft);
                var captured = recipe;
                if (!isLocked)
                    // Tap manipulator (not .clicked) so the tap survives the ScrollView's touch-scroll.
                    craftBtn.AddManipulator(new TapGestureManipulator(() => OnCraftPressed(captured)));

                row.Add(info);
                row.Add(craftBtn);
                _recipeList.Add(row);
            }
        }

        private void OnCraftPressed(RecipeJson recipe)
        {
            GameLogger.Develop($"[Craft] OnCraftPressed '{recipe?.id}' requiresBuilding={recipe?.requiresBuilding} " +
                               $"craftServiceNull={craftService == null} canCraft={craftService?.CanCraft(recipe)}");
            if (recipe.requiresBuilding)
            {
                bool triggered = craftService.TriggerBuildingCraft(recipe);
                GameLogger.Develop($"[Craft] TriggerBuildingCraft '{recipe?.id}' -> {triggered}");
                if (!triggered)
                    InventoryPopupController.NotifyWarning("No eligible building");
            }
            else
            {
                bool crafted = craftService.TryCraft(recipe);
                GameLogger.Develop($"[Craft] TryCraft '{recipe?.id}' -> {crafted}");
                if (!crafted)
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
            _buildingAffordRows.Clear();
            _lastAffordEntropy = long.MinValue; // force a re-evaluate next Update

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
                placeBtn.AddManipulator(new TapGestureManipulator(() =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    conveyorController.BeginConveyorMode();
                }));

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

                Label costLabel = null;
                if (cost > 0)
                {
                    costLabel = new Label($"◈ {cost:N0}");
                    costLabel.AddToClassList("building-card-recipe");
                    costLabel.AddToClassList(canAfford
                        ? "building-card-recipe--affordable"
                        : "building-card-recipe--unaffordable");
                    card.Add(costLabel);
                }

                var placeBtn = new Button { text = "Place" };
                placeBtn.AddToClassList("craft-btn");
                placeBtn.SetEnabled(canAfford);

                // Track cost rows so Update() can re-enable them the moment entropy catches up.
                if (cost > 0)
                    _buildingAffordRows.Add((placeBtn, costLabel, cost));

                var captured = entry;
                placeBtn.AddManipulator(new TapGestureManipulator(() =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    placementController.BeginPlacement(captured);
                }));

                card.Add(nameLabel);
                card.Add(recipeLabel);
                card.Add(placeBtn);
                _buildingsList.Add(card);
            }
        }

        /// <summary>
        /// Re-evaluates entropy affordability for the open Build panel whenever the player's
        /// entropy changes, so a button that was unaffordable at open becomes clickable the
        /// instant automation earns enough — no need to close and reopen the panel.
        /// </summary>
        private void RefreshBuildingAffordabilityIfChanged()
        {
            if (_buildingsPanel == null || _buildingsPanel.ClassListContains("hidden")) return;
            if (!_ecsReady || _progressQuery.IsEmpty || _buildingAffordRows.Count == 0) return;

            long entropy = _em.GetComponentData<PlayerProgressData>(
                _progressQuery.GetSingletonEntity()).BaseCurrency;
            if (entropy == _lastAffordEntropy) return;
            _lastAffordEntropy = entropy;

            foreach (var (btn, costLabel, cost) in _buildingAffordRows)
            {
                bool afford = entropy >= cost;
                btn.SetEnabled(afford);
                costLabel.EnableInClassList("building-card-recipe--affordable", afford);
                costLabel.EnableInClassList("building-card-recipe--unaffordable", !afford);
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

            // Update category tab highlights
            UpdateAchievementTabHighlights();

            // Update reset countdown
            if (_achResetLabel != null)
            {
                _achResetLabel.text = _achActiveCategory == AchievementCategory.Progression
                    ? ""
                    : achievementService?.GetResetCountdown(_achActiveCategory) ?? "";
            }

            var all = achievementService?.GetAll();
            if (all == null || all.Count == 0)
            {
                _achievementsList.Add(new Label("No achievements defined."));
                if (_achievementsTitle != null) _achievementsTitle.text = "Achievements (0 / 0)";
                return;
            }

            int completed = achievementService.CompletedCount;
            if (_achievementsTitle != null)
                _achievementsTitle.text = $"Achievements ({completed} / {all.Count})";

            foreach (var achievement in all)
            {
                if (achievement == null || achievement.category != _achActiveCategory) continue;

                bool isDone      = achievementService.IsCompleted(achievement.id);
                bool isClaimable = achievementService.IsClaimable(achievement.id);
                bool isHidden    = achievement.isHidden && !isDone;

                var row = new VisualElement();
                row.AddToClassList("ach-card");
                if (isDone && !isClaimable) row.AddToClassList("ach-card--completed");
                else if (isClaimable)       row.AddToClassList("ach-card--claimable");

                var innerRow = new VisualElement();
                innerRow.AddToClassList("ach-card-row");

                var checkLabel = new Label(isDone ? "✓" : "○");
                checkLabel.AddToClassList("ach-card-check");
                if (isDone) checkLabel.AddToClassList("ach-card-check--done");

                var body = new VisualElement();
                body.AddToClassList("ach-card-body");

                var nameLabel = new Label(isHidden ? "???" : achievement.displayName);
                nameLabel.AddToClassList("ach-card-name");

                var descLabel = new Label(isHidden ? "Complete a hidden objective to reveal." : achievement.description);
                descLabel.AddToClassList("ach-card-desc");

                body.Add(nameLabel);
                body.Add(descLabel);

                // Currency reward hint
                if (!isHidden && (achievement.paidCurrencyReward > 0 || achievement.prestigeCurrencyReward > 0))
                {
                    var parts = new System.Collections.Generic.List<string>();
                    if (achievement.paidCurrencyReward    > 0) parts.Add($"◆ {achievement.paidCurrencyReward}");
                    if (achievement.prestigeCurrencyReward > 0) parts.Add($"✦ {achievement.prestigeCurrencyReward}");
                    var rewardLabel = new Label(string.Join("  ", parts));
                    rewardLabel.AddToClassList("ach-card-reward");
                    body.Add(rewardLabel);
                }

                // Cosmetic rewards (legacy)
                if (!isHidden && achievement.rewards != null && achievement.rewards.Length > 0)
                {
                    var cosmeticNames = string.Join(", ", System.Array.ConvertAll(
                        achievement.rewards, r => r != null ? r.displayName : "?"));
                    var cosmeticLabel = new Label($"Unlocks: {cosmeticNames}");
                    cosmeticLabel.AddToClassList("ach-card-desc");
                    body.Add(cosmeticLabel);
                }

                innerRow.Add(checkLabel);
                innerRow.Add(body);

                // Claim button
                if (isClaimable)
                {
                    var claimBtn = new Button { text = "Claim" };
                    claimBtn.AddToClassList("ach-claim-btn");
                    string capturedId = achievement.id;
                    claimBtn.clicked += () =>
                    {
                        achievementService?.ClaimReward(capturedId);
                        BuildAchievementsList();
                    };
                    innerRow.Add(claimBtn);
                }

                row.Add(innerRow);

                // Progress bar (in-progress achievements with quantity > 1)
                if (!isHidden && !isDone && achievement.triggerQuantity > 1)
                {
                    int progress = achievementService.GetProgress(achievement.id);
                    float ratio  = Mathf.Clamp01((float)progress / achievement.triggerQuantity);

                    var barWrap = new VisualElement();
                    barWrap.AddToClassList("ach-progress-bar");
                    var fill = new VisualElement();
                    fill.AddToClassList("ach-progress-fill");
                    fill.style.width = new StyleLength(new Length(ratio * 100f, LengthUnit.Percent));
                    barWrap.Add(fill);
                    row.Add(barWrap);

                    var progLabel = new Label($"{progress:N0} / {achievement.triggerQuantity:N0}");
                    progLabel.AddToClassList("ach-card-desc");
                    row.Add(progLabel);
                }

                _achievementsList.Add(row);
            }

            // Claim All button visibility
            bool anyClaimable = achievementService?.ClaimableCount > 0;
            SetElementVisible(_achClaimAllBtn, anyClaimable);
        }

        private void UpdateAchievementTabHighlights()
        {
            AchievementTabActive(_achTabDaily,       _achActiveCategory == AchievementCategory.Daily);
            AchievementTabActive(_achTabWeekly,      _achActiveCategory == AchievementCategory.Weekly);
            AchievementTabActive(_achTabMonthly,     _achActiveCategory == AchievementCategory.Monthly);
            AchievementTabActive(_achTabProgression, _achActiveCategory == AchievementCategory.Progression);
        }

        private static void AchievementTabActive(Button tab, bool active)
        {
            if (tab == null) return;
            if (active) tab.AddToClassList("achievements-tab--active");
            else        tab.RemoveFromClassList("achievements-tab--active");
        }

        // ============================================================
        // Placement overlay
        // ============================================================

        private void OnPlacingChanged(bool isPlacing)
        {
            SetElementVisible(_placementOverlay, isPlacing);
            if (!isPlacing) SetElementVisible(_placementConfirmPopup, false);

            bool canRotate = isPlacing && (placementController?.CanRotate ?? false);
            bool canFlip   = isPlacing && (placementController?.CanFlip   ?? false);
            SetElementVisible(_btnRotateOutput, canRotate);
            SetElementVisible(_btnFlipBuilding, canFlip);

            if (isPlacing && _placementLabel != null)
            {
                string rotateHint = canRotate ? (canFlip ? "  ·  R rotate  ·  F flip" : "  ·  R rotate") : "";
                _placementLabel.text = $"Tap a tile to position  ·  drag to pan  ·  ✓ to place{rotateHint}";
            }
        }

        // ============================================================
        // Placement confirm popup (world-anchored ✓ / ✕ at the candidate cell)
        // ============================================================

        private void OnCandidateChanged(bool hasCandidate)
        {
            SetElementVisible(_placementConfirmPopup, hasCandidate);
            // Surface the rotate (↻) action on the confirm popup itself for rotatable buildings,
            // so the player can re-orient in-context next to ✓/✕ instead of reaching for the bottom bar.
            SetElementVisible(_btnRotateCandidate,
                hasCandidate && (placementController?.CanRotate ?? false));
            if (hasCandidate) UpdatePlacementConfirmPopup();
        }

        private void UpdatePlacementConfirmPopup()
        {
            if (_placementConfirmPopup == null || placementController == null) return;
            if (!placementController.HasCandidate) return;

            var panel = _placementConfirmPopup.panel;
            var cam   = Camera.main;
            if (panel == null || cam == null) return;

            Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(
                panel, placementController.CandidateWorldPosition, cam);

            float w = _placementConfirmPopup.resolvedStyle.width;
            float h = _placementConfirmPopup.resolvedStyle.height;
            if (float.IsNaN(w)) w = 0f;
            if (float.IsNaN(h)) h = 0f;

            // Centre horizontally over the cell and float above it.
            _placementConfirmPopup.style.left = panelPoint.x - w * 0.5f;
            _placementConfirmPopup.style.top  = panelPoint.y - h - 24f;

            _btnConfirmPlace?.SetEnabled(placementController.CandidateValid);
        }

        /// <summary>True if a screen-space point is over the confirm popup or the placement bar. Used so
        /// a tap on placement UI is not also treated as a map tap by BuildingPlacementController.</summary>
        private bool IsPointerOverPlacementUI(Vector2 screenPos)
            => ScreenPointInElement(_placementConfirmPopup, screenPos)
            || ScreenPointInElement(_placementBar,          screenPos);  // the bar strip, NOT the full-screen overlay

        /// <summary>True if a screen-space point is over the conveyor toolbar strip. Used so a tap on
        /// the conveyor bar is not also treated as a grid tap by ConveyorPlacementController.</summary>
        private bool IsPointerOverConveyorUI(Vector2 screenPos)
        {
            // Develop-tier dump of the bar geometry vs the converted pointer position.
            // Gated on VerboseLogging so it only logs during the press, not every hover frame.
            bool r = ScreenPointInElement(_conveyorBar, screenPos);
            if (UIInputBlocker.VerboseLogging)
            {
                var panel = _conveyorBar?.panel;
                Vector2 pp = panel != null ? RuntimePanelUtils.ScreenToPanel(panel, screenPos) : Vector2.zero;
                GameLogger.Develop($"[Conveyor] barCheck bar={(_conveyorBar == null ? "null" : _conveyorBar.name)} hidden={(_conveyorBar?.ClassListContains("hidden"))} panelNull={panel == null} worldBound={_conveyorBar?.worldBound} panelPos={pp} -> {r}");
            }
            return r;  // the bar strip, NOT the full-screen overlay
        }

        private static bool ScreenPointInElement(VisualElement el, Vector2 screenPos)
        {
            if (el == null || el.ClassListContains("hidden")) return false;
            var panel = el.panel;
            if (panel == null) return false;
            // Input System screen coords are bottom-left origin; panel coords are top-left. In this
            // project RuntimePanelUtils.ScreenToPanel scales but does NOT flip Y, so we flip here.
            // Without this, top-of-screen UI (placement/conveyor bars) maps to the bottom of the
            // panel and never registers as "over UI", leaking taps to the grid behind it.
            Vector2 flipped  = new Vector2(screenPos.x, Screen.height - screenPos.y);
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);
            return el.worldBound.Contains(panelPos);
        }

        private void OnConveyorPlacingChanged(bool isPlacing)
        {
            SetElementVisible(_conveyorOverlay, isPlacing);

            if (isPlacing)
            {
                // Mode always re-enters in Create with no pending start or candidate.
                OnConveyorModeChanged(false);
                OnConveyorCandidateChanged(false);
                OnConveyorStartChanged(false);
            }
        }

        /// <summary>Updates the Create/Destroy toggle look and the bar's instructional text.</summary>
        private void OnConveyorModeChanged(bool isDestroy)
        {
            if (_btnConveyorMode != null)
            {
                _btnConveyorMode.text = isDestroy ? "Destroy" : "Create";
                if (isDestroy) _btnConveyorMode.AddToClassList("conveyor-mode-btn--destroy");
                else           _btnConveyorMode.RemoveFromClassList("conveyor-mode-btn--destroy");
            }

            if (_conveyorLabel != null)
                _conveyorLabel.text = isDestroy
                    ? "Tap a belt to remove"
                    : "Tap a start, then an end";

            // Switching mode always clears any pending candidate.
            if (isDestroy) OnConveyorCandidateChanged(false);
        }

        /// <summary>Shows or hides the Bend / Place / Clear candidate controls.</summary>
        private void OnConveyorCandidateChanged(bool hasCandidate)
        {
            SetElementVisible(_btnConveyorRotate,          hasCandidate);
            SetElementVisible(_btnConveyorConfirm,         hasCandidate);
            SetElementVisible(_btnConveyorCancelCandidate, hasCandidate);

            // The label is a short instruction only — it must not repeat the button names.
            if (_conveyorLabel != null && !IsDestroyModeActive())
                _conveyorLabel.text = hasCandidate
                    ? "Place the belt, or adjust it"
                    : "Tap a start, then an end";
        }

        /// <summary>Shows the "start from a new cell" reset button whenever a start anchor is pending
        /// (e.g. after auto-chaining), so the player can begin a fresh run without leaving build mode.</summary>
        private void OnConveyorStartChanged(bool hasStart)
        {
            SetElementVisible(_btnConveyorNewStart, hasStart && !IsDestroyModeActive());
        }

        private bool IsDestroyModeActive() =>
            conveyorController != null && conveyorController.IsDestroyMode;

        /// <summary>Forwards the controller's chain-placed signal to tutorial listeners.</summary>
        private void RaiseConveyorPlaced() => OnConveyorPlaced?.Invoke();

        /// <summary>Raised by the building inspector when the player sets a building's recipe.
        /// Drives the tutorial's "set the Combiner to Proton" step.</summary>
        public void NotifyBuildingRecipeSet() => OnBuildingRecipeSet?.Invoke();

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
        // Power-connection overlay toggle (⚡ top-bar button)
        // ============================================================

        private void TogglePowerConnections()
        {
            _powerConnectionsShown = !_powerConnectionsShown;
            ApplyPowerConnectionState();
            SettingsService.Instance?.SetShowPowerConnections(_powerConnectionsShown);
        }

        /// <summary>Syncs the button's on/off look and the map overlay to the current toggle state.</summary>
        private void ApplyPowerConnectionState()
        {
            EnsurePowerOverlay()?.SetVisible(_powerConnectionsShown);

            if (_btnPowerToggle == null) return;
            if (_powerConnectionsShown) _btnPowerToggle.AddToClassList("power-toggle-btn--active");
            else                        _btnPowerToggle.RemoveFromClassList("power-toggle-btn--active");
        }

        /// <summary>
        /// Finds the map power-connection overlay, creating a runtime one if the scene has none (self-wires
        /// to the GridRenderer via FindAnyObjectByType), so no manual scene wiring is required.
        /// </summary>
        private PowerConnectionOverlayController EnsurePowerOverlay()
        {
            if (_powerOverlay != null) return _powerOverlay;
            _powerOverlay = FindAnyObjectByType<PowerConnectionOverlayController>();
            if (_powerOverlay == null)
                _powerOverlay = new GameObject("PowerConnectionOverlay").AddComponent<PowerConnectionOverlayController>();
            return _powerOverlay;
        }

        // ============================================================
        // Building inspector pass-throughs (delegated to HUDBuildingInspectorSubController)
        // ============================================================

        public void ShowBuildingInspector(Entity entity, string buildingName)
        {
            GameLogger.Develop($"[HUD] ShowBuildingInspector called: building='{buildingName}' inspector={((_inspector == null) ? "NULL" : "ok")}");
            _inspector?.ShowBuildingInspector(entity, buildingName);
        }

        public void HideBuildingInspector() => _inspector?.HideBuildingInspector();

        // Idle-return modal pass-through (delegated to IdleReturnSubController)
        public void ShowIdleReturn(IdleCollectionResult result) => _idleReturn?.Show(result);

        /// <summary>
        /// Shows the game-data update notice once if the remote <c>gamedata.updatedUtc</c> stamp is
        /// newer than the saved marker, then records the marker so it fires once per published stamp.
        /// Called by ECSLoadBridge after the save is applied. No-op (silent) on any blank/stale stamp.
        /// </summary>
        public void MaybeShowGameUpdateNotice()
        {
            var save = SaveManager.Instance?.Current;
            if (!GameUpdateNotice.ShouldShow(FeatureFlags.GameDataUpdatedUtc, save?.lastSeenGameDataUtc))
                return;

            _gameUpdate?.Show(FeatureFlags.GameDataNoticeTitle, FeatureFlags.GameDataNoticeMessage);
            TelemetryService.Instance?.RecordGameUpdateNotice(FeatureFlags.GameDataVersion);

            if (save != null)
            {
                save.lastSeenGameDataUtc = FeatureFlags.GameDataUpdatedUtc;
                SaveManager.Instance.SaveLocal();
            }
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
            // Hint active: btn-achievements is now visible — pulse it instead of the drawer handle
            if (_achievementsHintPulse != null)
                SwapAchievementsHintPulse("btn-achievements");
        }

        private void CloseDrawer()
        {
            if (_drawerPanel == null) return;
            _drawerOpen = false;
            _drawerPanel.RemoveFromClassList("left-drawer--open");
            SetElementVisible(_drawerBackdrop, false);
            // Hint still active but drawer hidden: swap back to pulsing the drawer handle
            if (_achievementsHintPulse != null)
                SwapAchievementsHintPulse("btn-drawer-handle");
        }

        /// <summary>
        /// Cancels any active building-placement, conveyor, or deconstruct mode (and their
        /// overlays). Public so sub-controllers (e.g. site travel) can clear placement before
        /// an action that swaps the grid out from under it.
        /// </summary>
        public void CancelActiveModes()
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

                var inputIcon = MakeItemIcon(item?.icon, "recipe-input-icon");
                if (inputIcon != null) row.Add(inputIcon);

                var lbl = new Label($"{count}/{input.quantity} {sym}");
                lbl.AddToClassList(count >= input.quantity ? "recipe-input-met" : "recipe-input-missing");
                row.Add(lbl);
            }

            return row;
        }
    }
}
