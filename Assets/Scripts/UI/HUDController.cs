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
        [SerializeField] private ResearchService              researchService;

        // Sub-controllers (siblings on same GameObject)
        private HUDStatusBarController              _statusBar;
        private HUDBuildingInspectorSubController   _inspector;
        private HUDBannerController                 _banner;
        private PrestigeShopSubController           _prestigeShop;
        private ManagersSubController               _managers;
        private StatsSubController                  _stats;
        private EntropySurgeOfferController         _surgeOffer;
        private PlayerNamePromptController          _namePrompt;
        private TournamentPanelController           _tournament;
        private TournamentIntroController           _tournamentIntro;
        private MenuReminderController              _reminders;
        private TestAdOverlayController             _testAd;
        private CyclotronController                 _cyclotron;
        private MegastructureSubController          _megastructure;
        private DailyEventsSubController            _daily;
        private RewardedAdsSubController            _rewards;
        private IdleReturnSubController             _idleReturn;
        private GameUpdateNoticeSubController       _gameUpdate;
        private HUDPremiumShopSubController         _shop;
        private HUDSettingsSubController            _settings;
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
                              _achievementsPanel, _tournamentPanel, _placementOverlay,
                              _shopPanel, _settingsPanel, _rewardsPanel, _worldsPanel, _managersPanel, _megastructurePanel, _statsPanel;
        private VisualElement[] _allPanels;

        // ---- Panel content ----
        private ScrollView _recipeList, _buildingsList, _codexList,
                           _researchList, _upgradesList, _achievementsList;
        private Label      _prestigeSummary, _prestigeCurrency, _placementLabel, _achievementsTitle;
        // Research timer UI (live countdown + skip on the in-progress card)
        private Label      _researchCountdownLabel;
        // Research tree (#337): shown instead of the list once the tutorial is done; the list stays one tap away.
        private ResearchTreeView _researchTree;
        private Button     _researchModeBtn;
        private bool       _researchTreeMode = true;
        private VisualElement _researchTabs;        // one tab per unlocked world (Physics / Chemistry / Biology)
        private string     _researchWorldTab;       // world id whose research is shown; set to the live world on open
        private string     _researchSelectedId;
        private bool       _researchFocusPending;
        private ResearchBlock[] _researchBlocks;
        private const string ResearchViewPref = "research_view_mode";
        private Button     _researchSkipBtn;
        private bool       _researchSkipConfirming;
        private UnityEngine.UIElements.IVisualElementScheduledItem _researchTicker;

        // Live affordability refresh: automation can raise entropy past a building's cost while
        // the Build panel is open. We re-enable buy buttons on entropy change instead of only at
        // panel-open. Rows captured in BuildBuildingsList; re-evaluated in Update.
        private readonly List<(Button btn, Label costLabel, long cost)> _buildingAffordRows = new();
        private long _lastAffordEntropy = long.MinValue;
        private FieldGenerator _fieldGenerator; // source of the fields sold in the Build menu

        // Same problem in the Research panel, which had no live refresh at all: a Start button that
        // was unaffordable at panel-open stayed dead until the panel was reopened. Rows captured in
        // BuildResearchList; re-evaluated by the 1 Hz research ticker. The research is stored rather
        // than a precomputed bool because CanPurchase() is the authority on unlocked + prereqs +
        // affordability, and labBusy is tracked too so buttons revive when the lab frees up.
        private readonly List<(Button btn, Label costLabel, ResearchSO research)> _researchAffordRows = new();
        private long _lastResearchAffordEntropy = long.MinValue;
        private bool _lastResearchAffordLabBusy;

        // ---- HUD chrome ----
        private VisualElement _drawerPanel;
        private VisualElement _drawerBackdrop;
        private bool          _drawerOpen;
        private readonly DrawerTapGuard _drawerTapGuard = new DrawerTapGuard(DrawerSlideMs / 1000.0);

        // ---- Back button (#205) ----
        // Mode flags mirror the controllers' OnPlacingChanged / OnConveyorPlacingChanged /
        // OnDeconstructingChanged events. _modeEndedFrame lets the back handler yield on the frame a
        // controller cancelled its mode on the same Escape press, so one press never closes two layers.
        private bool _placingMode, _conveyorMode, _deconstructMode;
        private int  _modeEndedFrame = -1;
        private DialogueController _dialogue;

        // ---- Placement controls ----
        private Button        _btnRotateOutput;
        private Button        _btnFlipBuilding;
        private Button        _btnRemoveBuilding; // edit mode only
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
        private Button        _btnConveyorUp;
        private Button        _btnConveyorMode;

        // ---- Deconstruct mode ----
        private VisualElement _deconstructOverlay;

        // ---- Recipe-menu craft queue (#310) ----
        private CraftQueueView _craftQueueView;
        private bool IsRecipePanelOpen => _recipePanel != null && !_recipePanel.ClassListContains("hidden");

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
        private UnityEngine.UIElements.IVisualElementScheduledItem _achResetTicker;
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
            if (_inspector != null) _inspector.Hidden += () => BuildingInspectorHidden?.Invoke();
            _banner      = GetComponent<HUDBannerController>();
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
            _worlds       = GetComponent<WorldsSubController>();
            _stats        = GetComponent<StatsSubController>() ?? gameObject.AddComponent<StatsSubController>();
            if (GetComponent<IncomeRateTracker>() == null) gameObject.AddComponent<IncomeRateTracker>();
            _surgeOffer   = GetComponent<EntropySurgeOfferController>() ?? gameObject.AddComponent<EntropySurgeOfferController>();
            _reminders    = GetComponent<MenuReminderController>() ?? gameObject.AddComponent<MenuReminderController>();
            _testAd       = GetComponent<TestAdOverlayController>() ?? gameObject.AddComponent<TestAdOverlayController>();
            _cyclotron    = GetComponent<CyclotronController>() ?? gameObject.AddComponent<CyclotronController>();
            _namePrompt   = GetComponent<PlayerNamePromptController>() ?? gameObject.AddComponent<PlayerNamePromptController>();
            _tournament   = GetComponent<TournamentPanelController>() ?? gameObject.AddComponent<TournamentPanelController>();
            _tournamentIntro = GetComponent<TournamentIntroController>() ?? gameObject.AddComponent<TournamentIntroController>();
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
            _stats?.Init(root, this);
            _megastructure?.Init(root, this);
            _daily?.Init(root, this);
            _rewards?.Init(root, this);
            _idleReturn?.Init(root, this);
            _gameUpdate?.Init(root, this);
            _shop?.Initialize(root);
            _settings?.Initialize(root);
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
                conveyorController.OnTraceChanged     += UpdateConveyorBar;
                conveyorController.OnChainPlaced      += RaiseConveyorPlaced;
                conveyorController.IsPointerOverConveyorUI = IsPointerOverConveyorUI;
            }

            if (deconstructController != null)
                deconstructController.OnDeconstructingChanged += OnDeconstructingChanged;

            if (achievementService != null)
            {
                achievementService.OnAchievementUnlocked += OnAchievementUnlocked;
                achievementService.OnPeriodReset         += OnAchievementPeriodReset;
            }


            BindLiveServices();

            if (researchService != null)
                researchService.OnResearchUnlocked += OnResearchUnlocked;

            if (craftService != null)
            {
                _craftQueueView = new CraftQueueView(craftService);
                // Floats over the bottom of the panel (added last so it draws and picks on top) instead
                // of sitting above the list: above it, every show/hide shoved the rows ~230px, so a quick
                // second tap on a 1s Proton landed on a label and did nothing on a phone.
                _recipeList?.parent?.Add(_craftQueueView.Root);
                _craftQueueView.Root.RegisterCallback<GeometryChangedEvent>(_ => UpdateRecipeListClearance());
                craftService.QueueChanged += OnCraftQueueChanged;
            }
        }

        /// <summary>
        /// Points the serialized service fields at the live singletons. ManualCraftService and ResearchService
        /// sit on the persistent Bootstrap object, so after a scene reload (fresh start, new game, dev reload)
        /// the freshly loaded scene's copies are duplicates that SingletonMonoBehaviour destroys at the end of
        /// the frame. The HUD kept its serialized reference to the doomed copy, so every craft silently did
        /// nothing until Play Mode restarted (playtest S1, 2026-11-07). The kept instance is still Instance
        /// here; on a first load whose service hasn't woken yet, the serialized reference stays.
        /// </summary>
        private void BindLiveServices()
        {
            if (ManualCraftService.Instance != null) craftService    = ManualCraftService.Instance;
            if (ResearchService.Instance    != null) researchService = ResearchService.Instance;
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
                conveyorController.OnTraceChanged     -= UpdateConveyorBar;
                conveyorController.OnChainPlaced      -= RaiseConveyorPlaced;
                conveyorController.IsPointerOverConveyorUI = null;
            }

            if (deconstructController != null)
                deconstructController.OnDeconstructingChanged -= OnDeconstructingChanged;

            if (achievementService != null)
            {
                achievementService.OnAchievementUnlocked -= OnAchievementUnlocked;
                achievementService.OnPeriodReset         -= OnAchievementPeriodReset;
            }


            if (researchService != null)
                researchService.OnResearchUnlocked -= OnResearchUnlocked;

            if (craftService != null)
                craftService.QueueChanged -= OnCraftQueueChanged;
            _craftQueueView?.Root.RemoveFromHierarchy();
            _craftQueueView = null;
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
            _worlds?.SetECSContext(_em);
        }

        void Update()
        {
            _statusBar?.Tick();
            _inspector?.Tick();
            HandleBackButton();
            UpdatePlacementConfirmPopup();
            RefreshBuildingAffordabilityIfChanged();
            RefreshBannerClearance();
            if (_craftQueueView != null && IsRecipePanelOpen) _craftQueueView.Tick(GameClock.UtcNow);

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
                    ShowTutorialHint("🏆 Weekly, Monthly and Progress achievements unlocked! Open the drawer and tap Achievements.");
                    StartAchievementsHintPulse();
                }
            }
        }

        // ============================================================
        // Element queries & button binding
        // ============================================================

        private void QueryElements(VisualElement root)
        {
            // The element the slide panels, the hint pill and the overlay instances share; its shape
            // (wider than tall) switches the panels to their landscape layout (.hud--landscape, GameHUD.uss).
            _hudFrame    = root.Q("root") ?? root;
            _dialogueBox = root.Q("dialogue-box");
            _hintRestore = root.Q("tutorial-hint-restore");
            _hudFrame.RegisterCallback<GeometryChangedEvent>(_ =>
                _hudFrame.EnableInClassList("hud--landscape", IsLandscape(_hudFrame.layout.width, _hudFrame.layout.height)));

            // HUD chrome
            _drawerPanel    = root.Q("left-drawer");
            _drawerBackdrop = root.Q("drawer-backdrop");
            // Closed drawer is hidden, not just slid off-screen, so its ScrollView scroller
            // can never peek in at the left edge (#196). See OpenDrawer / CloseDrawer.
            if (_drawerPanel != null) _drawerPanel.style.visibility = Visibility.Hidden;
            _drawerTapGuard.Attach(_drawerPanel);

            // Panels
            _recipePanel       = root.Q("recipe-panel");
            _buildingsPanel    = root.Q("buildings-panel");
            _codexPanel        = root.Q("codex-panel");
            _researchPanel     = root.Q("research-panel");
            _upgradesPanel     = root.Q("upgrades-panel");
            _managersPanel     = root.Q("managers-panel");
            _statsPanel        = root.Q("stats-panel");
            _megastructurePanel = root.Q("megastructure-panel");
            _rewardsPanel      = root.Q("rewards-panel");
            _achievementsPanel = root.Q("achievements-panel");
            _tournamentPanel          = root.Q("pve-panel");
            _prestigePanel     = root.Q("prestige-panel");
            _shopPanel         = root.Q("shop-panel");
            _settingsPanel     = root.Q("settings-panel");
            _worldsPanel       = root.Q("worlds-panel");
            _placementOverlay  = root.Q("placement-overlay");

            _allPanels = new[]
            {
                _recipePanel, _buildingsPanel, _codexPanel,
                _researchPanel, _upgradesPanel, _managersPanel, _statsPanel, _megastructurePanel, _rewardsPanel, _achievementsPanel, _tournamentPanel, _prestigePanel,
                _shopPanel, _settingsPanel, _worldsPanel
            };

            // Panel content
            _recipeList       = root.Q<ScrollView>("recipe-list");
            _buildingsList    = root.Q<ScrollView>("buildings-list");
            _codexList        = root.Q<ScrollView>("codex-list");
            _researchList     = root.Q<ScrollView>("research-list");
            SetUpResearchTree();
            _upgradesList     = root.Q<ScrollView>("upgrades-list");
            _achievementsList    = root.Q<ScrollView>("achievements-list");

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


            _prestigeSummary  = root.Q<Label>("prestige-summary");
            _prestigeCurrency = root.Q<Label>("prestige-currency");
            _placementLabel   = root.Q<Label>("placement-label");
            _btnRotateOutput  = root.Q<Button>("btn-rotate-output");
            _btnFlipBuilding  = root.Q<Button>("btn-flip-building");
            _btnRemoveBuilding = root.Q<Button>("btn-remove-building");
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
            _btnConveyorUp              = root.Q<Button>("btn-conveyor-up");
            _btnConveyorMode            = root.Q<Button>("btn-conveyor-mode");

            // Deconstruct mode
            _deconstructOverlay = root.Q("deconstruct-overlay");
            ConfirmPrompt.Init(root); // remove confirmations for buildings and belts
            SaveConflictPrompt.Init(root); // "Two saves found": this device vs the cloud save
            _surgeOffer?.Init(root, this); // Entropy Surge side icon + offer card
            _testAd?.Init(root);            // ad test mode card (dev builds, ads.test_mode)
            _cyclotron?.Init(root, this);   // Cyclotron Kick minigame (drawer button from the 2nd prestige)
            _namePrompt?.Init(root, this);  // "Choose your name" card (PvE leaderboards): after the tutorial + Settings
            _tournament?.Init(root, this);  // Tournament panel (pve-panel): status, join, leaderboard, rewards
            _tournamentIntro?.Init(root, this); // "Tournament open" dialogue once per entry window + pulse to the button
            InitReminders(root);            // red dots: achievements to claim (drawer row, its tabs, the ≡ handle)

            // Tooltip — inner element inside "tooltip-instance" TemplateContainer
            _tooltipPopup = root.Q("tooltip-popup");
            _tooltipTitle = root.Q<Label>("tooltip-popup__title");
            _tooltipBody  = root.Q<Label>("tooltip-popup__body");

        }

        private void BindButtons(VisualElement root)
        {
            // Drawer toggle
            // A pan that starts on the handle also ends as a click; ignore clicks where the pointer moved.
            var drawerHandle = root.Q<Button>("btn-drawer-handle");
            Vector2 handleDownPos = Vector2.zero;
            drawerHandle.RegisterCallback<PointerDownEvent>(e => handleDownPos = e.position, TrickleDown.TrickleDown);
            drawerHandle.RegisterCallback<ClickEvent>(e =>
            {
                if (IsTap(handleDownPos, e.position, 12f)) ToggleDrawer();
            });
            if (_drawerBackdrop != null)
                _drawerBackdrop.RegisterCallback<ClickEvent>(_ => CloseDrawer());

            // Drawer nav buttons — non-research buttons respect the tutorial nav gate
            root.Q<Button>("btn-recipes").clicked      += () => TryOpenPanel(OpenRecipePanel);
            root.Q<Button>("btn-buildings").clicked    += () => TryOpenPanel(OpenBuildingsPanel);
            root.Q<Button>("btn-codex").clicked        += () => TryOpenPanel(OpenCodexPanel);
            root.Q<Button>("btn-research").clicked     += OpenResearchPanel;
            root.Q<Button>("btn-upgrades").clicked     += () => TryOpenPanel(OpenUpgradesPanel);
            root.Q<Button>("btn-managers")?.RegisterCallback<ClickEvent>(_ => TryOpenPanel(OpenManagersPanel));
            root.Q<Button>("btn-stats")?.RegisterCallback<ClickEvent>(_ => TryOpenPanel(OpenStatsPanel));
            root.Q<Button>("btn-megastructure")?.RegisterCallback<ClickEvent>(_ => TryOpenPanel(OpenMegastructurePanel));
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
            root.Q<Button>("btn-worlds")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => TryOpenPanel(OpenWorldsPanel));
            root.Q<Button>("btn-tournament").clicked   += () => TryOpenPanel(OpenTournamentPanel);
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
            root.Q<Button>("btn-close-stats")?.RegisterCallback<ClickEvent>(_ => SetElementVisible(_statsPanel, false));
            root.Q<Button>("btn-close-megastructure")?.RegisterCallback<ClickEvent>(_ => SetElementVisible(_megastructurePanel, false));
            var btnCloseRewards = root.Q<Button>("btn-close-rewards");
            if (btnCloseRewards != null) btnCloseRewards.clicked += () => SetElementVisible(_rewardsPanel, false);
            root.Q<Button>("btn-close-achievements").clicked += () => SetElementVisible(_achievementsPanel, false);
            root.Q<Button>("btn-close-pve").clicked              += () => SetElementVisible(_tournamentPanel,          false);
            root.Q<Button>("btn-close-prestige").clicked         += () => SetElementVisible(_prestigePanel,     false);
            root.Q<Button>("btn-close-worlds")?.RegisterCallback<UnityEngine.UIElements.ClickEvent>(_ => SetElementVisible(_worldsPanel, false));

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
            if (_btnRemoveBuilding != null)
                _btnRemoveBuilding.clicked += () => placementController?.RequestRemoveEditedBuilding();
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
                _btnConveyorCancelCandidate.clicked += () => conveyorController?.UndoLastNode();
            if (_btnConveyorNewStart != null)
                _btnConveyorNewStart.clicked += () => conveyorController?.ResetStart();
            if (_btnConveyorUp != null)
                _btnConveyorUp.clicked += () => conveyorController?.ToggleRaise();
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

        private void SetUpResearchTree()
        {
            if (_researchPanel == null || _researchList == null) return;
            _researchTreeMode = PlayerPrefs.GetInt(ResearchViewPref, 1) == 1;
            _researchBlocks   = Resources.Load<ResearchDatabaseSO>("ResearchDatabase")?.blocks ?? System.Array.Empty<ResearchBlock>();

            _researchTree = new ResearchTreeView();
            _researchTree.NodeSelected += r => { _researchSelectedId = r.id; BuildResearchList(); };
            int at = _researchPanel.IndexOf(_researchList);
            _researchPanel.Insert(at + 1, _researchTree);

            // Tree / List toggle in the panel header, left of the close button.
            var header = _researchPanel.Q("panel-header");
            _researchModeBtn = new Button { name = "btn-research-view" };
            _researchModeBtn.AddToClassList("research-view-btn");
            _researchModeBtn.clicked += () =>
            {
                _researchTreeMode = !_researchTreeMode;
                PlayerPrefs.SetInt(ResearchViewPref, _researchTreeMode ? 1 : 0);
                _researchFocusPending = _researchTreeMode;
                BuildResearchList();
            };
            var close = header?.Q<Button>("btn-close-research");
            if (header != null) header.Insert(close != null ? header.IndexOf(close) : header.childCount, _researchModeBtn);

            // World tabs under the header: each world's research on its own page.
            _researchTabs = new VisualElement { name = "research-world-tabs" };
            _researchTabs.AddToClassList("research-tabs");
            _researchPanel.Insert(_researchPanel.IndexOf(_researchList), _researchTabs);
        }

        /// <summary>World a research belongs to for the tabs (ResearchSO.world; empty = Physics).</summary>
        internal static string ResearchTabOf(ResearchSO r) =>
            string.IsNullOrEmpty(r?.world) ? WorldGate.Physics : r.world;

        /// <summary>Rebuilds the world tabs: one per unlocked world, hidden when only Physics is open.</summary>
        private void RefreshResearchTabs()
        {
            if (_researchTabs == null) return;
            _researchTabs.Clear();
            var ws = WorldService.Instance;
            var worlds = ws?.AllWorlds;
            int shown = 0;
            if (worlds != null)
                for (int i = 0; i < worlds.Count; i++)
                {
                    var w = worlds[i];
                    if (w == null || !ws.IsUnlocked(i)) continue;
                    shown++;
                    string id = w.id;
                    var tab = new Button(() =>
                    {
                        if (_researchWorldTab == id) return;
                        _researchWorldTab = id;
                        _researchSelectedId = null;
                        _researchFocusPending = true;
                        BuildResearchList();
                    }) { text = w.displayName, name = "research-tab-" + id };
                    tab.AddToClassList("research-tab");
                    if (id == _researchWorldTab) tab.AddToClassList("research-tab--selected");
                    _researchTabs.Add(tab);
                }
            SetElementVisible(_researchTabs, shown > 1);
        }

        private void OpenResearchPanel()
        {
            CloseAllPanels();
            _researchFocusPending = true;
            // Open on the world the player is in: its research first, the others one tap away.
            _researchWorldTab = WorldGate.ActiveWorldId() ?? WorldGate.Physics;
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

            // MUST run before the HasActiveResearch guard below: "no research running" is exactly
            // the state where Start buttons matter, so a refresh placed after that early return
            // would never fire in the case it exists to fix.
            RefreshResearchAffordability();

            if (researchService == null || !researchService.HasActiveResearch) return;

            double rem = researchService.ActiveRemainingSeconds;
            if (_researchCountdownLabel != null)
                _researchCountdownLabel.text = $"⏳ {FormatResearchTimer(rem)}";

            if (_researchSkipBtn != null && !_researchSkipConfirming)
            {
                long cost     = researchService.ActiveSkipCost;
                long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
                _researchSkipBtn.text = $"Skip  ♦{cost:N0}";
                _researchSkipBtn.SetEnabled(crystals >= cost);
            }
        }

        /// <summary>
        /// Re-evaluates Start-button state for the open Research panel. Without this a Start button
        /// that was unaffordable when the panel opened stayed dead forever, because BuildResearchList
        /// only runs on open / purchase / skip / cancel / research-unlock — never on an entropy
        /// change — so automation earning the cost had no visible effect until the panel was reopened.
        ///
        /// Driven by the existing 1 Hz research ticker rather than Update(): it is already scoped to
        /// the open panel and already live-refreshes the Skip button the same way.
        /// </summary>
        private void RefreshResearchAffordability()
        {
            if (researchService == null || _researchAffordRows.Count == 0) return;
            if (!_ecsReady || _progressQuery.IsEmpty) return;

            long entropy = _em.GetComponentData<PlayerProgressData>(
                _progressQuery.GetSingletonEntity()).BaseCurrency;
            bool labBusy = researchService.HasActiveResearch;

            // labBusy is part of the key, not just entropy: when a research finishes the lab frees
            // up and every other Start button becomes live again at unchanged entropy.
            if (entropy == _lastResearchAffordEntropy && labBusy == _lastResearchAffordLabBusy) return;
            _lastResearchAffordEntropy = entropy;
            _lastResearchAffordLabBusy = labBusy;

            foreach (var (btn, costLabel, research) in _researchAffordRows)
            {
                if (btn == null || research == null) continue;

                // CanPurchase is the authority — it covers already-unlocked, prerequisites and
                // affordability together, so this cannot drift from what Purchase() will accept.
                btn.SetEnabled(!labBusy && researchService.CanPurchase(research));
                btn.text = labBusy ? "Lab busy" : "Start";

                // Research cost has only an --unaffordable class; there is no --affordable pair.
                costLabel?.EnableInClassList("research-card-cost--unaffordable",
                                             entropy < researchService.EffectiveCost(research));
            }
        }

        /// <summary>"◈ 750", or "◈ 750 (-25%)" when Memory Resonance discounts it.</summary>
        internal static string ResearchCostText(long cost, long baseCost)
        {
            if (baseCost <= 0 || cost >= baseCost) return $"◈ {cost:N0}";
            int pct = (int)System.Math.Round(100d * (baseCost - cost) / baseCost);
            return pct > 0 ? $"◈ {cost:N0} (-{pct}%)" : $"◈ {cost:N0}";
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

            // The rows about to be discarded must not be refreshed after their elements are gone,
            // and the entropy cache is reset so the next tick re-evaluates rather than short-circuiting
            // on an unchanged value (same reason BuildBuildingsList resets _lastAffordEntropy).
            _researchAffordRows.Clear();
            _lastResearchAffordEntropy = long.MinValue;

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

            _researchWorldTab ??= WorldGate.ActiveWorldId() ?? WorldGate.Physics;
            RefreshResearchTabs();
            // Only the selected world's research (the tabs); the tutorial's target always shows.
            string tutorialTarget = tutorialActive ? TutorialResearchTargetId() : null;
            var shownResearch = researchService.AllResearch
                .Where(r => r != null && (ResearchTabOf(r) == _researchWorldTab || r.id == tutorialTarget)).ToList();

            bool treeMode = _researchTree != null && _researchTreeMode;
            SetElementVisible(_researchList, !treeMode);
            if (_researchTree != null) SetElementVisible(_researchTree, treeMode);
            if (_researchModeBtn != null)
            {
                SetElementVisible(_researchModeBtn, _researchTree != null);
                _researchModeBtn.text = treeMode ? "List" : "Tree";
            }

            if (treeMode)
            {
                BuildResearchTree(shownResearch, currentEntropy, tutorialTarget);
                return;
            }

            foreach (var research in shownResearch)
            {
                if (research == null) continue;
                bool unlocked   = researchService.IsUnlocked(research.id);
                bool prereqsMet = PrereqsMet(research);

                // During the tutorial, only show research that is already unlocked
                // or whose prerequisites are all satisfied (the immediate frontier).
                if (tutorialActive && !unlocked && !prereqsMet) continue;

                _researchList.Add(BuildResearchCard(research, currentEntropy));
            }
        }

        // Prerequisite research done AND its world (WorldGate) open.
        private bool PrereqsMet(ResearchSO research) =>
            researchService.WorldOpen(research) &&
            (research.prerequisites == null || research.prerequisites.Length == 0 ||
             System.Array.TrueForAll(research.prerequisites, p => p == null || researchService.IsUnlocked(p.id)));

        /// <summary>"Opens with Chemistry" while the research's world is still locked, else null.</summary>
        private string WorldLockText(ResearchSO research) =>
            researchService.WorldOpen(research) ? null : $"Opens with {WorldGate.DisplayName(research.requiredWorld)}";

        /// <summary>The research the current tutorial step is waiting for, or null when it waits for something else.</summary>
        private string TutorialResearchTargetId()
        {
            var steps = TutorialFlowSO.Current?.steps;
            int index = _tutorialQuery.GetSingleton<TutorialStateData>().CurrentStepIndex;
            if (steps == null || index < 0 || index >= steps.Length) return null;
            var condition = steps[index].advanceCondition;
            return condition.type == ConditionType.ResearchUnlocked ? condition.researchId : null;
        }

        private void BuildResearchTree(IReadOnlyList<ResearchSO> all, long currentEntropy, string tutorialTargetId)
        {
            ResearchTreeView.NodeState State(ResearchSO r) =>
                researchService.IsUnlocked(r.id) ? ResearchTreeView.NodeState.Done
                : researchService.HasActiveResearch && researchService.ActiveResearchId == r.id ? ResearchTreeView.NodeState.Active
                : PrereqsMet(r) ? ResearchTreeView.NodeState.Available
                : ResearchTreeView.NodeState.Locked;
            string Meta(ResearchSO r) => State(r) switch
            {
                ResearchTreeView.NodeState.Done   => "Researched",
                ResearchTreeView.NodeState.Active => $"In progress · {FormatResearchTimer(researchService.ActiveRemainingSeconds)}",
                ResearchTreeView.NodeState.Available => $"◈ {ShortNumber(researchService.EffectiveCost(r))}" + (currentEntropy >= researchService.EffectiveCost(r) ? "  Ready" : ""),
                _ => WorldLockText(r) ?? $"Locked · ◈ {ShortNumber(researchService.EffectiveCost(r))}",
            };

            // Open on the research the player is working toward: the one the tutorial is asking for (selected,
            // so its Start button is on screen), else the one in progress, else the first available.
            if (_researchFocusPending)
            {
                _researchFocusPending = false;
                var tutorialTarget = all.FirstOrDefault(r => r != null && r.id == tutorialTargetId);
                if (tutorialTarget != null) _researchSelectedId = tutorialTarget.id;
                var target = tutorialTarget
                          ?? all.FirstOrDefault(r => r != null && State(r) == ResearchTreeView.NodeState.Active)
                          ?? all.Where(r => r != null && State(r) == ResearchTreeView.NodeState.Available)
                                .OrderBy(r => System.Array.FindIndex(_researchBlocks, b => b.id == r.block)).ThenBy(r => r.costBaseCurrency)
                                .FirstOrDefault();
                if (target != null) { _researchSelectedId ??= target.id; _researchTree.FocusOn(target.id); }
            }

            _researchTree.Rebuild(all, _researchBlocks, State, Meta, _researchSelectedId);

            var detail = _researchTree.Detail;
            detail.Clear();
            var selected = all.FirstOrDefault(r => r != null && r.id == _researchSelectedId);
            if (selected == null)
            {
                var hint = new Label("Tap a research to see what it unlocks and start it. Drag to move around, pinch to zoom.");
                hint.AddToClassList("research-card-desc");
                detail.Add(hint);
                return;
            }
            var block = System.Array.Find(_researchBlocks, b => b.id == selected.block);
            if (block != null)
            {
                var phase = new Label(block.displayName);
                phase.AddToClassList("rt-detail-phase");
                detail.Add(phase);
            }
            detail.Add(BuildResearchCard(selected, currentEntropy));
        }

        internal static string ShortNumber(long v)
        {
            if (v >= 1_000_000_000_000) return (v / 1e12).ToString(v >= 100_000_000_000_000 ? "0" : "0.#") + "T";
            if (v >= 1_000_000_000)     return (v / 1e9).ToString(v >= 100_000_000_000 ? "0" : "0.#") + "B";
            if (v >= 1_000_000)         return (v / 1e6).ToString(v >= 100_000_000 ? "0" : "0.#") + "M";
            if (v >= 10_000)            return (v / 1e3).ToString(v >= 100_000 ? "0" : "0.#") + "k";
            return v.ToString("N0");
        }

        /// <summary>One research card: name, description, and cost/Start, the live timer + skip, or what it requires.
        /// The list shows one per research; the tree shows the selected node's.</summary>
        private VisualElement BuildResearchCard(ResearchSO research, long currentEntropy)
        {
            {
                bool unlocked   = researchService.IsUnlocked(research.id);
                bool prereqsMet = PrereqsMet(research);
                bool canPurchase = !unlocked && prereqsMet && researchService.CanPurchase(research);

                var card = new VisualElement();
                card.AddToClassList("research-card");
                if (unlocked) card.AddToClassList("research-card--unlocked");

                // Name
                var nameLabel = new Label(research.displayName);
                nameLabel.AddToClassList("research-card-name");
                card.Add(unlocked ? GlyphIcon.TitleRow(GlyphIcon.Kind.Check, nameLabel) : nameLabel);

                // Description (wrapping)
                if (!string.IsNullOrEmpty(research.description))
                {
                    var descLabel = new Label(research.description);
                    descLabel.AddToClassList("research-card-desc");
                    card.Add(descLabel);
                }

                if (research.effects != null)
                    foreach (var effect in research.effects)
                    {
                        var effectLabel = new Label(ResearchEffectText.Describe(effect));
                        effectLabel.AddToClassList("research-card-effect");
                        card.Add(effectLabel);
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
                        // Show what's blocking this research: its world first, else the missing prerequisites
                        var prereqNames = string.Join(", ",
                            System.Array.ConvertAll(research.prerequisites ?? System.Array.Empty<ResearchSO>(),
                                p => p != null ? p.displayName : "?"));
                        var prereqLabel = new Label(WorldLockText(research) ?? $"Requires: {prereqNames}");
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

                        long cost       = researchService.EffectiveCost(research);
                        bool affordable = currentEntropy >= cost;
                        var costLabel   = new Label(ResearchCostText(cost, research.costBaseCurrency));
                        costLabel.AddToClassList("research-card-cost");
                        if (!affordable) costLabel.AddToClassList("research-card-cost--unaffordable");
                        info.Add(costLabel);

                        int effSecs    = researchService.EffectiveDurationSeconds(research);
                        var timeLabel  = new Label(ResearchTimeText(effSecs, researchService.FreeResearchLeft));
                        timeLabel.AddToClassList("research-card-time");
                        info.Add(timeLabel);

                        footer.Add(info);

                        // Start button (disabled while the lab is busy with another research)
                        var startBtn = new Button { text = labBusy ? "Lab busy" : "Start" };
                        startBtn.AddToClassList("craft-btn");
                        startBtn.SetEnabled(!labBusy && canPurchase);

                        // Track for the live affordability refresh (see RefreshResearchAffordability).
                        _researchAffordRows.Add((startBtn, costLabel, research));

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

                return card;
            }
        }

        // Skip button for the in-progress research. First tap reveals a Confirm/Cancel row so
        // spending crystals is always a deliberate two-tap action (no generic confirm modal exists).
        private void BuildResearchSkipUi(VisualElement footer, ResearchSO research)
        {
            long cost     = researchService.ActiveSkipCost;
            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;

            _researchSkipBtn = new Button { text = $"Skip  ♦{cost:N0}" };
            _researchSkipBtn.AddToClassList("craft-btn");
            _researchSkipBtn.SetEnabled(crystals >= cost);
            _researchSkipBtn.clicked += () =>
            {
                _researchSkipConfirming = true;
                footer.Remove(_researchSkipBtn);

                var confirm = new Button { text = $"Confirm  ♦{researchService.ActiveSkipCost:N0}" };
                confirm.AddToClassList("craft-btn");
                confirm.clicked += () =>
                {
                    researchService.SkipActive(); // completion fires OnResearchUnlocked → rebuild
                    BuildResearchList();
                };

                var cancel = new Button();
                cancel.AddToClassList("craft-btn");
                GlyphIcon.SetOn(cancel, GlyphIcon.Kind.Close);
                cancel.clicked += () => BuildResearchList();

                footer.Add(confirm);
                footer.Add(cancel);
            };
            footer.Add(_researchSkipBtn);
        }

        private void OnResearchUnlocked(ResearchSO research)
        {
            // Refresh research panel if it is currently open. During the tutorial, close it instead: every
            // step after a tutorial research needs the world or another panel, and a Research panel left
            // open silently blocked field taps (2026-11-07 playtest: 18 turns lost at gather_for_hydrogen).
            if (_researchPanel != null && !_researchPanel.ClassListContains("hidden"))
            {
                bool tutorialActive = _ecsReady && !_tutorialQuery.IsEmpty
                                      && _tutorialQuery.GetSingleton<TutorialStateData>().IsActive;
                if (tutorialActive) SetElementVisible(_researchPanel, false);
                else                BuildResearchList();
            }

            // Refresh buildings panel if it is open (newly unlocked buildings will appear)
            if (_buildingsPanel != null && !_buildingsPanel.ClassListContains("hidden"))
                BuildBuildingsList();

            // Refresh recipe panel — newly learned recipes will appear
            if (_recipePanel != null && !_recipePanel.ClassListContains("hidden"))
                BuildRecipeList();

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

        private void OpenStatsPanel()
        {
            CloseAllPanels();
            _stats?.Refresh();
            SetElementVisible(_statsPanel, true);
            _stats?.StartTicker();
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
            if (!FeatureFlags.PveEnabled)         SetElementVisible(root.Q<Button>("btn-tournament"), false);
            if (!FeatureFlags.AdsEnabled)         SetElementVisible(root.Q<Button>("btn-rewards"), false);
        }

        internal void OpenWorldsPanel()
        {
            CloseAllPanels();
            _worlds?.Refresh();
            SetElementVisible(_worldsPanel, true);
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

        /// <summary>
        /// The menu is always open: its Daily tab (login calendar + daily achievements) works from the start.
        /// Weekly, Monthly and Progress unlock with the first prestige; their tabs show a lock until then.
        /// </summary>
        private void ApplyAchievementsGate()
        {
            bool unlocked = AchievementsUnlocked;
            _btnAchievements?.SetEnabled(true);
            SetElementVisible(_achievementsLockedHint, false);
            foreach (var tab in new[] { _achTabWeekly, _achTabMonthly, _achTabProgression })
                tab?.EnableInClassList("achievements-tab--locked", !unlocked);
        }

        private void OpenAchievementsPanel() => OpenAchievementsPanel(FirstClaimableCategory());

        /// <summary>The first tab with a reward waiting, so the menu opens where the red dot is; Daily otherwise.</summary>
        private AchievementCategory FirstClaimableCategory()
        {
            foreach (AchievementCategory c in new[] { AchievementCategory.Daily, AchievementCategory.Weekly,
                                                      AchievementCategory.Monthly, AchievementCategory.Progression })
                if (achievementService != null && achievementService.HasClaimable(c)) return c;
            return AchievementCategory.Daily;
        }

        private void OpenAchievementsPanel(AchievementCategory category)
        {
            DismissAchievementsHint();
            CloseAllPanels();
            _achActiveCategory = category;
            // A period may have ended since the last check (long session); a reset rebuilds via OnPeriodReset.
            achievementService?.RequestPeriodCheck("menu opened");
            BuildAchievementsList();
            SetElementVisible(_achievementsPanel, true);

            // Keep the reset countdown live while the panel is open (paused when it closes).
            if (_achResetLabel == null) return;
            if (_achResetTicker == null)
                _achResetTicker = _achResetLabel.schedule.Execute(TickAchievementResetLabel).Every(1000);
            else
                _achResetTicker.Resume();
        }

        private void TickAchievementResetLabel()
        {
            if (_achievementsPanel == null || _achievementsPanel.ClassListContains("hidden"))
            {
                _achResetTicker?.Pause();
                return;
            }
            achievementService?.CheckPeriodsIfDue();   // countdown hit zero: reset now, not on next launch
            UpdateAchievementResetLabel();
        }

        // Sets only the label, so the tick does not rebuild the list and reset its scroll position.
        private void UpdateAchievementResetLabel()
        {
            if (_achResetLabel == null) return;
            _achResetLabel.text = _achActiveCategory == AchievementCategory.Progression
                                  || !AchievementService.CategoryUnlocked(_achActiveCategory)
                ? ""
                : achievementService?.GetResetCountdown(_achActiveCategory) ?? "";
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

        private void OnAchievementPeriodReset() => OnAchievementUnlocked(null);

        // ============================================================
        // Tournament panel (PvE; TournamentPanelController owns pve-panel)
        // ============================================================

        private void OpenTournamentPanel()
        {
            CloseAllPanels();
            _tournament?.Open();
        }

        /// <summary>
        /// During a tournament run a prestige or Ascension ends the run early (#505): asks, retires on the server at
        /// the current net worth, then runs <paramref name="then"/>. Returns false (and does nothing) when no run is
        /// on, so the caller goes ahead. The tournament's own join prestige goes through
        /// <see cref="RequestTournamentPrestige"/> instead.
        /// </summary>
        private bool LeavesTournamentFirst(System.Action then)
        {
            var svc = TournamentService.Instance;
            if (svc == null || !svc.PrestigeLeavesRun) return false;
            double nw = _ecsReady && !_progressQuery.IsEmpty
                ? _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity()).NetWorth : 0d;
            ConfirmPrompt.Show("End your tournament run?", TournamentRules.PrestigeLeaveMessage(nw, svc.RunRemaining),
                "End run & prestige", onConfirm: async () =>
                {
                    string error = await svc.RetireAsync();
                    if (this == null) return;
                    if (error != null)
                    {
                        ShowNotification("⚠", TournamentRules.ErrorText(error) + " Prestige not done.", "warning");
                        return;
                    }
                    then?.Invoke();
                });
            return true;
        }

        /// <summary>The prestige that starts a tournament run: the normal prestige, exempt from the tournament lock.</summary>
        public void RequestTournamentPrestige()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return;
            var entity   = _progressQuery.GetSingletonEntity();
            var progress = _em.GetComponentData<PlayerProgressData>(entity);
            progress.PrestigeRequested = true;
            _em.SetComponentData(entity, progress);
            CloseAllPanels();
        }

        /// <summary>Prestige currency a prestige would pay now (the tournament join card shows it).</summary>
        public long CurrentPrestigePreview()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return 0;
            return CalculatePrestigePreview(_em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity()).NetWorth);
        }

        /// <summary>True once ECS is ready for a prestige request.</summary>
        public bool CanRequestPrestige => _ecsReady && !_progressQuery.IsEmpty;

        private void OpenPrestigePanel()
        {
            if (!_ecsReady || _progressQuery.IsEmpty || _prestigeQuery.IsEmpty) return;

            CloseAllPanels();

            var progress = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
            var prestige = _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity());

            // Mirror the rate used in PrestigeSystem
            long preview = CalculatePrestigePreview(progress.NetWorth);
            long held    = prestige.PrestigeCurrency - prestige.PrestigeCurrencySpent;

            if (_prestigeSummary  != null) _prestigeSummary.text  = $"Net worth: {progress.NetWorth:N0}" +
                (TournamentService.Instance != null && TournamentService.Instance.PrestigeLeavesRun
                    ? "\nThis ends your tournament run early." : "");
            if (_prestigeCurrency != null) _prestigeCurrency.text =
                $"You will receive: {preview} ✦   (current: {held} ✦)";

            SetElementVisible(_prestigePanel, true);
        }

        /// <summary>The Megastructure panel's Ascend button: a prestige that also applies <see cref="AscensionRules"/>.</summary>
        public void RequestAscension()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return;
            if (LeavesTournamentFirst(RequestAscension)) return;
            AscensionRules.Pending = true;
            OnPrestigeConfirmed();
            ShowNotification("◉", "Ascension! The worlds begin again; the megastructure grows.");
        }

        private void OnPrestigeConfirmed()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return;
            if (LeavesTournamentFirst(OnPrestigeConfirmed)) return;

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

        // Recipes (#488): item-type tabs over a horizontally scrolling row of cards (RecipeMenu, MenuTabs).
        private VisualElement _recipeTabs;
        private readonly Dictionary<string, VisualElement> _recipePages = new();
        private string _recipeTab;
        private string _openRecipeDropUp;   // output id of the card whose method drop-up is open
        private const string RecipeTabPref = "recipes_tab";

        private void EnsureRecipeTabs()
        {
            if (_recipeTabs != null || _recipePanel == null || _recipeList == null) return;
            _recipeTab = PlayerPrefs.GetString(RecipeTabPref, null);
            _recipeTabs = MenuTabs.MakeTabBar(RecipeMenu.Prefix, RecipeMenu.Tabs, RecipeMenu.TabLabel, tab =>
            {
                _recipeTab = tab;
                _openRecipeDropUp = null;
                PlayerPrefs.SetString(RecipeTabPref, tab);
                MenuTabs.ShowTab(RecipeMenu.Prefix, _recipeTabs, _recipePages, tab);
                _recipeList.scrollOffset = Vector2.zero;
            });
            _recipePanel.Insert(_recipePanel.IndexOf(_recipeList), _recipeTabs);
            _recipeList.mode = ScrollViewMode.Horizontal;
            _recipeList.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _recipeList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _recipeList.AddToClassList("recipe-cards");
        }

        private void BuildRecipeList()
        {
            EnsureRecipeTabs();
            _recipeList.Clear();
            _recipePages.Clear();

            var inventoryCounts = craftService?.GetInventoryCounts() ?? new Dictionary<int, int>();

            var recipes = RecipeDatabase.Instance?.Recipes;
            if (recipes == null) return;

            if (recipes.Count == 0)
            {
                _recipeList.Add(new Label("No recipes available."));
                return;
            }

            foreach (var tab in RecipeMenu.Tabs)
            {
                var page = MenuTabs.MakePage(RecipeMenu.Prefix, tab, "builder-page");
                _recipePages[tab] = page;
                _recipeList.Add(page);
            }

            // One card per output item (#336): hide recipes the player has never unlocked or that are made in
            // another world (Chemistry's molecules stay out of the Physics list), then group the rest.
            var ws = WorldService.Instance;
            string activeWorldId = ws?.GetWorld(ws.ActiveIndex)?.id;
            var known = new List<RecipeJson>();
            foreach (var recipe in recipes)
                if ((RecipeKnowledgeService.Instance?.IsKnown(recipe.id) ?? false)
                    && WorldGate.RecipeInWorld(recipe.crafted_in, BuildingById, activeWorldId))
                    known.Add(recipe);

            foreach (var group in RecipeGrouping.Group(known))
            {
                var chosen = RecipeGrouping.PickMethod(group,
                    _recipeMethodChoice.TryGetValue(group.outputId, out var rid) ? rid : null,
                    m => !IsRecipeLocked(m));
                var outputItem = ItemDatabase.Instance?.Get(group.outputId);
                string tab = RecipeMenu.TabOf(outputItem != null ? outputItem.category : (ItemCategory?)null);
                _recipePages[tab].Add(BuildRecipeCard(group, chosen, outputItem, inventoryCounts));
            }

            var withCards = new HashSet<string>();
            foreach (var kv in _recipePages) if (kv.Value.childCount > 0) withCards.Add(kv.Key);
            _recipeTab = MenuTabs.PickTab(_recipeTab, withCards, RecipeMenu.Tabs);
            MenuTabs.ShowTab(RecipeMenu.Prefix, _recipeTabs, _recipePages, _recipeTab);
        }

        // Session-only memory of which method the player last picked per output item, and which groups are open.
        private readonly Dictionary<string, string> _recipeMethodChoice = new();
        private Dictionary<string, string> _buildingNames, _researchNames;

        private bool IsRecipeLocked(RecipeJson recipe) =>
            !string.IsNullOrEmpty(recipe.requires_research)
            && (researchService == null || !researchService.IsUnlocked(recipe.requires_research));

        private Dictionary<string, BuildingSO> _buildingsById;

        private BuildingSO BuildingById(string buildingId)
        {
            if (_buildingsById == null)
            {
                _buildingsById = new Dictionary<string, BuildingSO>();
                var all = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings;
                if (all != null)
                    foreach (var b in all)
                        if (b != null && !string.IsNullOrEmpty(b.id)) _buildingsById[b.id] = b;
            }
            return buildingId != null && _buildingsById.TryGetValue(buildingId, out var so) ? so : null;
        }

        private string BuildingNameFor(string buildingId)
        {
            if (_buildingNames == null)
            {
                _buildingNames = new Dictionary<string, string>();
                var all = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings;
                if (all != null)
                    foreach (var b in all)
                        if (b != null && !string.IsNullOrEmpty(b.id)) _buildingNames[b.id] = b.displayName;
            }
            return _buildingNames.TryGetValue(buildingId, out var n) ? n : null;
        }

        /// <summary>A research card's time: "Instant", "Instant · 2 free left" (Free Research, #491) or "⏳ 1:30".</summary>
        internal static string ResearchTimeText(int effectiveSeconds, int freeLeft) =>
            effectiveSeconds <= 0 ? "Instant"
            : freeLeft > 0 ? $"Instant · {freeLeft} free left"
            : $"⏳ {FormatResearchTimer(effectiveSeconds)}";

        /// <summary>The lock line on a recipe known from an earlier run but gated by research now (#490).</summary>
        internal static string ResearchLockText(string researchName) => $"Research {researchName} to unlock";

        private string ResearchNameFor(string researchId)
        {
            if (_researchNames == null)
            {
                _researchNames = new Dictionary<string, string>();
                var all = Resources.Load<ResearchDatabaseSO>("ResearchDatabase")?.allResearch;
                if (all != null)
                    foreach (var r in all)
                        if (r != null && !string.IsNullOrEmpty(r.id)) _researchNames[r.id] = r.displayName;
            }
            return _researchNames.TryGetValue(researchId, out var n) && !string.IsNullOrEmpty(n) ? n : researchId;
        }

        // Rebuilds the recipe list in place, keeping the scroll position (used after expand / method changes).
        private void RebuildRecipeListKeepingScroll()
        {
            var scroll = _recipeList.scrollOffset;
            BuildRecipeList();
            _recipeList.scrollOffset = scroll;
        }

        // One card per item (#488): picture, name, inputs, hints, craft time and worth, then the x1/x5/x10/Max
        // buttons for the chosen method. An item with several methods shows the chosen one and a drop-up to pick
        // another (the choice is remembered for the session).
        private VisualElement BuildRecipeCard(RecipeGroup group, RecipeJson recipe, ItemSO outputItem,
                                              Dictionary<int, int> inventoryCounts)
        {
            bool isLocked = IsRecipeLocked(recipe);
            int  max      = isLocked ? 0 : (craftService?.MaxCraftable(recipe) ?? 0);

            var card = new VisualElement();
            card.AddToClassList("recipe-card");
            if (isLocked) card.AddToClassList("recipe-card--locked");   // fades the content, not the drop-up
            else if (max < 1) card.AddToClassList("recipe-card--unavailable");

            var pic = new VisualElement();
            pic.AddToClassList("recipe-card__icon");
            if (outputItem?.icon != null) pic.style.backgroundImage = new StyleBackground(outputItem.icon);
            else
            {
                var g = new GlyphIcon(GlyphIcon.Kind.Recipes);
                g.AddToClassList("building-tile__glyph");
                pic.Add(g);
            }
            card.Add(pic);

            var nameLabel = new Label(recipe.name);
            nameLabel.AddToClassList("recipe-name");
            nameLabel.AddToClassList("recipe-card__name");
            card.Add(nameLabel);

            var inputs = BuildIngredientsUI(recipe, inventoryCounts);
            inputs.AddToClassList("recipe-card__inputs");
            card.Add(inputs);

            // A building-only recipe needs one of its buildings placed (any recipe, busy or idle, #309).
            // Its speed sets the craft time, same as the building takes.
            float speed = 1f;
            string missingBuilding = null;
            bool hasBuilding = craftService == null
                || craftService.HasCraftingBuilding(recipe, out speed, out missingBuilding);
            if (isLocked)
                card.Add(CardHint(ResearchLockText(ResearchNameFor(recipe.requires_research)), "recipe-lock-hint"));
            else if (!hasBuilding)
                card.Add(CardHint($"Place {AOrAn(missingBuilding)} to craft this", "recipe-lock-hint"));

            string meta = $"{ManualCraftService.UnitSeconds(recipe, speed):0.#}s";
            if (outputItem != null)
                meta += " · " + WorthEachText(ItemValue.PricePerItem(outputItem), ItemStars.Count(outputItem.itemId));
            card.Add(CardHint(meta, "recipe-card__meta"));

            float bonus = recipe.EscapeEntropyPerCraft(ItemDatabase.Instance);
            if (bonus > 0f) card.Add(CardHint(EscapeBonusText(bonus), "recipe-bonus-hint"));

            var spacer = new VisualElement();
            spacer.AddToClassList("recipe-card__spacer");
            card.Add(spacer);

            bool several = group != null && group.methods.Count > 1;
            string methodTitle = RecipeMenu.MethodTitle(recipe, BuildingNameFor);
            if (several || recipe.requiresBuilding)
                card.Add(CardHint(methodTitle, "recipe-card__method"));

            if (several)
            {
                bool open = _openRecipeDropUp == group.outputId;
                var toggle = new Button { text = RecipeMenu.WaysText(group.methods.Count, open) };
                toggle.AddToClassList("recipe-methods-toggle");
                toggle.AddToClassList("recipe-card__ways");
                string outId = group.outputId;
                ListTapRouter.Register(_recipeList, toggle, () =>
                {
                    _openRecipeDropUp = _openRecipeDropUp == outId ? null : outId;
                    RebuildRecipeListKeepingScroll();
                });
                card.Add(toggle);
            }

            var craft = BuildCraftButtons(recipe, isLocked, hasBuilding, max);
            craft.AddToClassList("recipe-card__craft");
            card.Add(craft);

            if (several && _openRecipeDropUp == group.outputId)
                card.Add(BuildMethodDropUp(group, recipe));
            return card;
        }

        private static Label CardHint(string text, string ussClass)
        {
            var l = new Label(text);
            l.AddToClassList(ussClass);
            l.AddToClassList("recipe-card__hint");
            return l;
        }

        // The drop-up over a card: one button per method (where it is made, or locked behind what research).
        // Picking one makes it the card's method and closes the drop-up.
        private VisualElement BuildMethodDropUp(RecipeGroup group, RecipeJson chosen)
        {
            var menu = new VisualElement();
            menu.AddToClassList("recipe-dropup");
            foreach (var method in group.methods)
            {
                string text = RecipeMenu.ShortMethodTitle(method, BuildingNameFor);
                if (IsRecipeLocked(method)) text += $" · needs {ResearchNameFor(method.requires_research)}";
                var option = new Button { text = text };
                option.AddToClassList("recipe-dropup__option");
                if (method == chosen) option.AddToClassList("recipe-dropup__option--selected");
                var m = method;
                ListTapRouter.Register(_recipeList, option, () =>
                {
                    _recipeMethodChoice[group.outputId] = m.id;
                    _openRecipeDropUp = null;
                    RebuildRecipeListKeepingScroll();
                });
                menu.Add(option);
            }
            return menu;
        }

        /// <summary>Recipe-row / inspector line for what one item sells for, e.g. "Worth ◈ 2.5 each".</summary>
        internal static string WorthEachText(float price, int stars = 0) =>
            stars > 0
                ? $"Worth {NumberFormat.Entropy(price)} each · {StarBadge(stars)}"
                : $"Worth {NumberFormat.Entropy(price)} each";

        /// <summary>Tournament stars on an item, e.g. "★3 +60%" (PvE phase 2, <see cref="ItemStars"/>).</summary>
        internal static string StarBadge(int stars) =>
            stars <= 0 ? "" : $"★{stars} +{Mathf.RoundToInt(stars * ItemStars.BonusPerStar * 100f)}%";

        /// <summary>Recipe-row / inspector line for fusion byproducts that escape as entropy.</summary>
        internal static string EscapeBonusText(float entropyPerCraft) =>
            $"Releases particles: +{entropyPerCraft:0.#} entropy per craft";

        // x1 / x5 / x10 / Max (#310). One the inventory can't pay for (or with no building placed) is
        // styled unavailable but stays tappable: ListTapRouter swallows taps on disabled buttons, so a
        // disabled one looked pressed and did nothing. Tapping it says what is missing instead.
        private VisualElement BuildCraftButtons(RecipeJson recipe, bool isLocked, bool hasBuilding, int max, System.Action onPicked = null)
        {
            var group = new VisualElement();
            group.AddToClassList("craft-qty-group");

            if (isLocked)
            {
                var locked = new Button { text = "Locked" };
                locked.AddToClassList("craft-btn");
                locked.SetEnabled(false);
                group.Add(locked);
                return group;
            }

            foreach (int qty in CraftQuantities)
            {
                bool isMax = qty == CraftMax;
                var btn = new Button { text = isMax ? "Max" : $"x{qty}" };
                btn.AddToClassList("craft-btn");
                btn.AddToClassList("craft-qty-btn");
                btn.EnableInClassList("craft-btn--unavailable", !(hasBuilding && (isMax ? max >= 1 : max >= qty)));
                // Routed from the list, not the button: a Button's own Clickable consumes the
                // pointer-down before any manipulator added later. See ListTapRouter.
                ListTapRouter.Register(_recipeList, btn, () => { onPicked?.Invoke(); OnCraftPressed(recipe, qty); });
                group.Add(btn);
            }
            return group;
        }

        private const int CraftMax = -1;
        private static readonly int[] CraftQuantities = { 1, 5, 10, CraftMax };

        private void OnCraftPressed(RecipeJson recipe, int count)
        {
            if (craftService == null) BindLiveServices();   // a destroyed reload duplicate reads as null
            if (craftService == null) return;
            if (count == CraftMax) count = craftService.MaxCraftable(recipe);
            // Max with nothing affordable: report what one craft needs.
            int wanted = Mathf.Max(1, count);

            switch (craftService.TryEnqueue(recipe, wanted, out string missingBuilding))
            {
                case ManualCraftService.EnqueueResult.NoBuilding:
                    InventoryPopupController.NotifyWarning($"Place {AOrAn(missingBuilding)} first");
                    break;
                case ManualCraftService.EnqueueResult.NotEnoughMaterials:
                case ManualCraftService.EnqueueResult.NotReady:
                    string missing = craftService.DescribeMissing(recipe, wanted);
                    InventoryPopupController.NotifyWarning(missing != null
                        ? $"Need {missing} in your inventory"
                        : "Not enough materials");
                    break;
            }
        }

        // Rebuild the queue strip, and the recipe list (counts and button states moved) unless a
        // finger is down on it: replacing a button mid-press drops the tap (see ListTapRouter).
        private void OnCraftQueueChanged()
        {
            _craftQueueView?.Rebuild();
            UpdateRecipeListClearance();
            if (IsRecipePanelOpen && !ListTapRouter.GestureInProgress)
            {
                var scroll = _recipeList.scrollOffset;
                BuildRecipeList();
                _recipeList.scrollOffset = scroll;
            }
        }

        // While the queue strip floats over the bottom of the panel, lift the bottom of the card row by its
        // height so the craft buttons stay clear of it (#488: the cards scroll sideways, not down).
        private void UpdateRecipeListClearance()
        {
            var strip = _craftQueueView?.Root;
            if (_recipeList == null) return;
            bool shown = strip != null && !strip.ClassListContains("hidden") && !float.IsNaN(strip.layout.height);
            _recipeList.style.marginBottom = shown
                ? new StyleLength(strip.layout.height)
                : new StyleLength(StyleKeyword.Null);
        }

        private static string AOrAn(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a building";
            return ("AEIOU".IndexOf(char.ToUpperInvariant(name[0])) >= 0 ? "an " : "a ") + name;
        }

        // ============================================================
        // Buildings list
        // ============================================================

        // Builder (#487): role tabs over a horizontally scrolling row of cards (BuilderMenu).
        private VisualElement _builderTabs;
        private readonly Dictionary<string, VisualElement> _builderPages = new();
        private string _builderTab;
        private const string BuilderTabPref = "builder_tab";

        private void EnsureBuilderTabs()
        {
            if (_builderTabs != null || _buildingsPanel == null || _buildingsList == null) return;
            _builderTab = PlayerPrefs.GetString(BuilderTabPref, null);
            _builderTabs = BuilderMenu.MakeTabBar(tab =>
            {
                _builderTab = tab;
                PlayerPrefs.SetString(BuilderTabPref, tab);
                BuilderMenu.ShowTab(_builderTabs, _builderPages, tab);
                _buildingsList.scrollOffset = Vector2.zero;
            });
            _buildingsPanel.Insert(_buildingsPanel.IndexOf(_buildingsList), _builderTabs);
            _buildingsList.mode = ScrollViewMode.Horizontal;
            _buildingsList.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _buildingsList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _buildingsList.AddToClassList("builder-cards");
        }

        private void BuildBuildingsList()
        {
            EnsureBuilderTabs();
            _buildingsList.Clear();
            _builderPages.Clear();
            _buildingAffordRows.Clear();
            _lastAffordEntropy = long.MinValue; // force a re-evaluate next Update
            foreach (var tab in BuilderMenu.Tabs)
            {
                var page = new VisualElement { name = BuilderMenu.PageName(tab) };
                page.AddToClassList(BuilderMenu.PageClass);
                page.AddToClassList("builder-page");
                _builderPages[tab] = page;
                _buildingsList.Add(page);
            }

            long currentEntropy = 0;
            if (_ecsReady && !_progressQuery.IsEmpty)
                currentEntropy = _em.GetComponentData<PlayerProgressData>(
                    _progressQuery.GetSingletonEntity()).BaseCurrency;

            // ---- Conveyor Belt entry (always first in Logistics, always free) ----
            if (conveyorController != null)
            {
                // Named so tutorial steps can point at it (TutorialPulseResolver).
                var placeBtn = new Button { text = "Place", name = TutorialPulseResolver.PlaceButtonName("conveyor") };
                ListTapRouter.Register(_buildingsList, placeBtn, () =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    conveyorController.BeginConveyorMode();
                });

                // Stream worlds (Chemistry): the same tool draws water channels from a Spring.
                bool streams = WorldRules.StreamTransport(SaveManager.Instance?.Current);
                string beltName = streams ? "Channel" : "Conveyor Belt";
                string beltInfo = streams
                    ? "Draw a stream from a Spring past your buildings; each takes what its reaction needs"
                    : "Draw paths to move items between buildings. Hold a belt to split it into 2 or 3 directions.";
                _builderPages["logistics"].Add(BuilderMenu.MakeCard(beltName, null, GlyphIcon.Kind.Flip,
                    MakeCostLabel(0, true), placeBtn, InfoButton(beltName, beltInfo)));
            }

            if (placementController != null)
            {
                AddFieldPurchaseRows(currentEntropy);

                var ws = WorldService.Instance;
                string activeWorldId = ws?.GetWorld(ws.ActiveIndex)?.id;
                foreach (var entry in placementController.availableBuildings)
                {
                    // Skip buildings that belong to another world (the Molecular Synthesizer is built in Chemistry)
                    var bso = entry.building;
                    if (bso != null && !WorldGate.BuildingInWorld(bso, activeWorldId)) continue;

                    // Skip buildings whose required research has not been purchased yet
                    if (bso != null && !bso.availableFromStart && bso.requiredResearch != null)
                    {
                        if (researchService == null || !researchService.IsUnlocked(bso.requiredResearch.id))
                            continue;
                    }

                    string detail = entry.defaultRecipe != null
                        ? $"Produces: {entry.defaultRecipe.displayName}"
                        : "";
                    if (!string.IsNullOrEmpty(bso?.description))
                        detail = string.IsNullOrEmpty(detail) ? bso.description : $"{detail}\n\n{bso.description}";
                    if (IsObsolete(bso))
                        detail = $"Obsolete. {bso.obsoleteCondition}\n\n{detail}";

                    int rawCost = entry.building?.entropyCost ?? 0;
                    float reduction = !_prestigeQuery.IsEmpty
                        ? _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity()).CostReduction
                        : 0f;
                    long cost = EntropyWallet.ReducedCost(rawCost, reduction);
                    bool canAfford = currentEntropy >= cost;

                    var costLabel = MakeCostLabel(cost, canAfford);

                    var placeBtn = new Button { text = "Place" };
                    if (bso != null) placeBtn.name = TutorialPulseResolver.PlaceButtonName(bso.id);
                    placeBtn.SetEnabled(canAfford);

                    // Track cost rows so Update() can re-enable them the moment entropy catches up.
                    if (cost > 0)
                        _buildingAffordRows.Add((placeBtn, costLabel, cost));

                    var captured = entry;
                    ListTapRouter.Register(_buildingsList, placeBtn, () =>
                    {
                        SetElementVisible(_buildingsPanel, false);
                        placementController.BeginPlacement(captured);
                    });

                    string name = entry.building?.displayName ?? "Building";
                    _builderPages[BuilderMenu.TabOf(bso)].Add(BuilderMenu.MakeCard(name,
                        BuilderMenu.CardSprite(bso, entry.defaultRecipe), GlyphIcon.Kind.Build,
                        costLabel, placeBtn, InfoButton(name, detail)));
                }
            }

            var withCards = new HashSet<string>();
            foreach (var kv in _builderPages) if (kv.Value.childCount > 0) withCards.Add(kv.Key);
            _builderTab = BuilderMenu.PickTab(_builderTab, withCards);
            BuilderMenu.ShowTab(_builderTabs, _builderPages, _builderTab);
        }

        // The (i) on a Builder card: the building's details in a card (#487). Routed through ListTapRouter like
        // the Place button, since the ScrollView swallows a Button's own click on touch.
        private Button InfoButton(string title, string detail)
        {
            if (string.IsNullOrEmpty(detail)) return null;
            var info = new Button();
            ListTapRouter.Register(_buildingsList, info, () =>
                ConfirmPrompt.Show(title, detail, "OK", onConfirm: null, cancelLabel: "Close", destructive: false));
            return info;
        }

        /// <summary>
        /// Fields for sale after Field Synthesis (see <see cref="FieldPurchaseService"/>). Quark fields
        /// sell as a pair. The price shown is the next one; it climbs steeply with every purchase.
        /// </summary>
        private void AddFieldPurchaseRows(long currentEntropy)
        {
            _fieldGenerator ??= FindAnyObjectByType<FieldGenerator>();
            if (_fieldGenerator == null) return;

            var ws = WorldService.Instance;
            string activeWorldId = ws?.GetWorld(ws.ActiveIndex)?.id;
            foreach (var field in _fieldGenerator.KnownFields)
            {
                // Graphite and nitrogen are bought in Chemistry, quark pairs in Physics.
                if (!WorldGate.FieldInWorld(field, activeWorldId)) continue;
                if (!FieldPurchaseService.IsAvailable(field)) continue;

                long cost      = FieldPurchaseService.NextPrice(field);
                bool canAfford = currentEntropy >= cost;
                var costLabel  = MakeCostLabel(cost, canAfford);

                var placeBtn = new Button { text = "Place", name = TutorialPulseResolver.PlaceButtonName(field.id) };
                placeBtn.SetEnabled(canAfford);
                _buildingAffordRows.Add((placeBtn, costLabel, cost));

                var captured = field;
                ListTapRouter.Register(_buildingsList, placeBtn, () =>
                {
                    SetElementVisible(_buildingsPanel, false);
                    placementController.BeginFieldPlacement(captured);
                });

                string name = FieldPurchaseName(field);
                var drops = field.outputItems;
                var picture = field.fieldIcon != null ? field.fieldIcon
                            : drops.Count > 0 && drops[0] != null ? drops[0].icon : null;
                _builderPages["harvest"].Add(BuilderMenu.MakeCard(name, picture, GlyphIcon.Kind.Domains,
                    costLabel, placeBtn, InfoButton(name, FieldPurchaseDetail(field))));
            }
        }

        internal static string FieldPurchaseName(FieldSO field)
            => field.purchaseGroupSize > 1 ? $"{field.displayName} Pair" : field.displayName;

        internal static string FieldPurchaseDetail(FieldSO field)
        {
            string what = field.purchaseGroupSize > 1
                ? $"{field.purchaseGroupSize} fields side by side"
                : "One field on an empty tile";
            return $"{what}. Each one costs {field.purchaseCostMultiplier:0.#}x the last.";
        }

        // Zero-cost buildings (conveyors, Maxwell's Demon) say "Free" rather than leaving the
        // cost column blank (#197).
        /// <summary>True once the research that replaces <paramref name="bso"/> is done (e.g. Nucleon Harvesting
        /// for the Strong Force Combiner). The building stays placeable; the Build menu just says so.</summary>
        private bool IsObsolete(BuildingSO bso) =>
            bso != null && bso.isObsoleteable && !string.IsNullOrEmpty(bso.obsoletedByResearch)
            && researchService != null && researchService.IsUnlocked(bso.obsoletedByResearch);

        internal static string FormatBuildCost(long cost) => cost > 0 ? $"◈ {cost:N0}" : "Free";

        private static Label MakeCostLabel(long cost, bool canAfford)
        {
            var costLabel = new Label(FormatBuildCost(cost));
            costLabel.AddToClassList("building-card-cost");
            costLabel.AddToClassList(canAfford
                ? "building-card-recipe--affordable"
                : "building-card-recipe--unaffordable");
            return costLabel;
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
            long cost = EntropyWallet.ReducedCost(rawCost, reduction);

            // The building is already placed when this event fires, so charge what the player has
            // rather than refusing (refusing would make it free). See EntropyWallet.SpendUpTo.
            EntropyWallet.SpendUpTo(_progressQuery, cost, rawCost);
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

            UpdateAchievementResetLabel();

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

            // The Daily tab opens with the 28-day login calendar (the old Daily panel, merged in 2026-11-20).
            if (_achActiveCategory == AchievementCategory.Daily)
            {
                var card = _daily?.BuildLoginCard(() => OnAchievementUnlocked(null)); // rebuilds only while open
                if (card != null) _achievementsList.Add(card);
            }

            if (!AchievementService.CategoryUnlocked(_achActiveCategory))
            {
                var locked = new Label("Unlocks after your first prestige.");
                locked.AddToClassList("ach-locked-msg");
                _achievementsList.Add(locked);
                SetElementVisible(_achClaimAllBtn, achievementService.ClaimableCount > 0);
                return;
            }

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

                var checkLabel = new Label(isDone ? "" : "○");
                checkLabel.AddToClassList("ach-card-check");
                if (isDone) GlyphIcon.SetOn(checkLabel, GlyphIcon.Kind.Check);
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
                    if (achievement.paidCurrencyReward    > 0) parts.Add($"♦ {achievement.paidCurrencyReward}");
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
            TrackMode(ref _placingMode, isPlacing);
            SetElementVisible(_placementOverlay, isPlacing);
            if (!isPlacing)
            {
                SetElementVisible(_placementConfirmPopup, false);
                // The output selector only exists inside placement; a cancel from Escape/back used to
                // leave it on screen.
                SetElementVisible(_outputSelector, false);
            }

            bool canRotate = isPlacing && (placementController?.CanRotate ?? false);
            bool canFlip   = isPlacing && (placementController?.CanFlip   ?? false);
            bool editing   = isPlacing && (placementController?.IsEditing ?? false);
            SetElementVisible(_btnRotateOutput, canRotate);
            SetElementVisible(_btnFlipBuilding, canFlip);
            SetElementVisible(_btnRemoveBuilding, editing);

            if (isPlacing && _placementLabel != null)
            {
                string rotateHint = canRotate ? (canFlip ? "  ·  R rotate  ·  F flip" : "  ·  R rotate") : "";
                _placementLabel.text = editing
                    ? $"Tap a tile to move it  ·  confirm to keep it there{rotateHint}"
                    : $"Tap a tile to position  ·  drag to pan  ·  confirm to place{rotateHint}";
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

            // Centre horizontally over the cell and float above it (below it if that would clip the top).
            const float margin = 8f;
            float topAbove = panelPoint.y - h - 24f;
            float desiredTop = topAbove < margin ? panelPoint.y + 24f : topAbove;
            Vector2 panelSize = panel.visualTree.layout.size;
            Vector2 pos = (panelSize.x > 0f && panelSize.y > 0f)
                ? ClampPopup(new Vector2(panelPoint.x - w * 0.5f, desiredTop), new Vector2(w, h), panelSize, margin)
                : new Vector2(panelPoint.x - w * 0.5f, desiredTop);
            _placementConfirmPopup.style.left = pos.x;
            _placementConfirmPopup.style.top  = pos.y;

            _btnConfirmPlace?.SetEnabled(placementController.CandidateValid);
        }

        /// <summary>Clamps a popup's top-left so the whole popup stays inside the panel with a margin.</summary>
        public static Vector2 ClampPopup(Vector2 desired, Vector2 size, Vector2 panelSize, float margin)
        {
            float maxX = Mathf.Max(margin, panelSize.x - size.x - margin);
            float maxY = Mathf.Max(margin, panelSize.y - size.y - margin);
            return new Vector2(Mathf.Clamp(desired.x, margin, maxX), Mathf.Clamp(desired.y, margin, maxY));
        }

        /// <summary>True if the pointer moved no more than threshold between down and up (a tap, not a pan).</summary>
        public static bool IsTap(Vector2 down, Vector2 up, float threshold)
            => (up - down).sqrMagnitude <= threshold * threshold;

        /// <summary>True if a screen-space point is over the confirm popup or the placement bar. Used so
        /// a tap on placement UI is not also treated as a map tap by BuildingPlacementController.</summary>
        private bool IsPointerOverPlacementUI(Vector2 screenPos)
            => ScreenPointInElement(_placementConfirmPopup, screenPos)
            || ScreenPointInElement(_placementBar,          screenPos);  // the bar strip, NOT the full-screen overlay

        /// <summary>True if a screen-space point is over the conveyor toolbar strip. Used so a tap on
        /// the conveyor bar is not also treated as a grid tap by ConveyorPlacementController.</summary>
        private bool IsPointerOverConveyorUI(Vector2 screenPos)
        {
            return ScreenPointInElement(_conveyorBar, screenPos);  // the bar strip, NOT the full-screen overlay
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
            TrackMode(ref _conveyorMode, isPlacing);
            SetElementVisible(_conveyorOverlay, isPlacing);

            if (isPlacing)
            {
                // Mode always re-enters in Create with no pending trace (this also refreshes the bar).
                OnConveyorModeChanged(false);
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

            UpdateConveyorBar();
        }

        private void OnConveyorCandidateChanged(bool hasCandidate) => UpdateConveyorBar();
        private void OnConveyorStartChanged(bool hasStart)         => UpdateConveyorBar();

        /// <summary>
        /// The conveyor bar for the current trace: Bend and Place once there is a belt to place, Undo and New
        /// once a start is set, and a short instruction that never repeats the button names.
        /// </summary>
        private void UpdateConveyorBar()
        {
            var  cc        = conveyorController;
            bool destroy   = IsDestroyModeActive();
            bool hasStart  = cc != null && !destroy && cc.HasStart;
            bool candidate = cc != null && !destroy && cc.HasCandidate;

            SetElementVisible(_btnConveyorRotate,          candidate);
            SetElementVisible(_btnConveyorConfirm,         candidate);
            SetElementVisible(_btnConveyorCancelCandidate, hasStart);
            SetElementVisible(_btnConveyorNewStart,        hasStart);

            // Up (overpass): once researched, while the trace can go on. Lit while the next tap lays one.
            bool ended  = cc != null && cc.IsTraceEnded;
            bool raise  = hasStart && cc.RaiseNext;
            SetElementVisible(_btnConveyorUp, hasStart && !ended && cc.CanRaise);
            if (_btnConveyorUp != null)
            {
                if (raise) _btnConveyorUp.AddToClassList("conveyor-mode-btn--on");
                else       _btnConveyorUp.RemoveFromClassList("conveyor-mode-btn--on");
            }

            if (_conveyorLabel != null)
                _conveyorLabel.text = raise ? ConveyorRaiseLabel : ConveyorBarLabel(destroy, hasStart, candidate, ended);
        }

        /// <summary>The conveyor bar's instruction while Up is on.</summary>
        internal const string ConveyorRaiseLabel = "Tap where the overpass lands";

        /// <summary>The conveyor bar's instruction for a trace state. Pure (unit-tested); short so it fits the bar.</summary>
        internal static string ConveyorBarLabel(bool destroy, bool hasStart, bool canPlace, bool ended)
        {
            if (destroy)   return "Tap a belt to remove";
            if (!hasStart) return "Tap a start";
            if (ended)     return "Joins a belt: place it";
            return canPlace ? "Tap the next point, or place" : "Tap the next point";
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
            TrackMode(ref _deconstructMode, isDeconstructing);
            SetElementVisible(_deconstructOverlay, isDeconstructing);
        }

        // ============================================================
        // Back button (#205)
        // ============================================================

        private void TrackMode(ref bool flag, bool active)
        {
            if (flag && !active) _modeEndedFrame = Time.frameCount;
            flag = active;
        }

        /// <summary>
        /// Android back / Escape closes the top-most UI layer, one per press. Order and rules live in
        /// <see cref="BackNavigation"/>; this only reads the live state and applies the result.
        /// </summary>
        private void HandleBackButton()
        {
            if (!InputUtils.WasBackPressed()) return;

            if (_dialogue == null) _dialogue = FindAnyObjectByType<DialogueController>();

            bool panelOpen = false;
            if (_allPanels != null)
                foreach (var p in _allPanels)
                    if (p != null && !p.ClassListContains("hidden")) { panelOpen = true; break; }

            var state = new BackNavigation.State
            {
                ModeActiveOrJustEnded = _placingMode || _conveyorMode || _deconstructMode
                                        || _modeEndedFrame == Time.frameCount,
                DialoguePlaying  = _dialogue != null && _dialogue.IsPlaying,
                TestAdOpen       = _testAd != null && _testAd.IsOpen,
                // The name card is a centred question card like ConfirmPrompt, so it shares that layer.
                ConfirmOpen      = ConfirmPrompt.IsOpen || PlayerNamePromptController.IsOpen,
                // The Cyclotron card is a centred modal like the Surge offer, so it shares that layer.
                SurgeOfferOpen   = (_surgeOffer != null && _surgeOffer.IsOpen) || (_cyclotron != null && _cyclotron.IsOpen),
                IdleReturnOpen   = _idleReturn != null && _idleReturn.IsOpen,
                UpdateNoticeOpen = _gameUpdate != null && _gameUpdate.IsOpen,
                InspectorOpen    = _inspector != null && _inspector.IsOpen,
                PanelOpen        = panelOpen,
                DrawerOpen       = _drawerOpen,
            };

            switch (BackNavigation.TopLayer(state))
            {
                case BackNavigation.Layer.TestAd:        _testAd.Skip();                 break;
                case BackNavigation.Layer.ConfirmPrompt:
                    if (ConfirmPrompt.IsOpen) ConfirmPrompt.CancelFromBack();
                    else _namePrompt?.CancelFromBack();
                    break;
                case BackNavigation.Layer.SurgeOffer:
                    if (_cyclotron != null && _cyclotron.IsOpen) _cyclotron.Close();
                    else _surgeOffer.Minimize();
                    break;
                case BackNavigation.Layer.IdleReturn:    _idleReturn.Close();            break;
                case BackNavigation.Layer.UpdateNotice:  _gameUpdate.Close();            break;
                case BackNavigation.Layer.Inspector:     _inspector.HideBuildingInspector(); break;
                case BackNavigation.Layer.Panel:
                    foreach (var p in _allPanels)
                        if (p != null) SetElementVisible(p, false);   // same as each panel's close button
                    break;
                case BackNavigation.Layer.Drawer:        CloseDrawer();                  break;
                // YieldToMode / SwallowDialogue / None: nothing to do here.
            }
        }

        // ============================================================
        // Banner pass-throughs (delegated to HUDBannerController)
        // ============================================================

        public void ShowNotification(string icon, string message, string modifier = null) =>
            _banner?.ShowNotification(icon, message, modifier);

        /// <summary>
        /// "Achievement unlocked" banner. Once achievements are unlocked (first prestige), tapping it opens the
        /// achievements menu on that achievement's tab and its check button claims the reward right away.
        /// Before that the menu is locked, so the banner is a plain notice.
        /// </summary>
        public void ShowAchievementNotification(AchievementSO achievement, string message)
        {
            if (_banner == null) return;
            if (achievement == null || !AchievementService.CategoryUnlocked(achievement.category))
            { _banner.ShowNotification("🏆", message); return; }

            string id       = achievement.id;
            var    category = achievement.category;
            _banner.ShowNotification("🏆", message, null,
                onTap:    () => TryOpenPanel(() => OpenAchievementsPanel(category)),
                onAccept: () => ClaimAchievementFromBanner(id));
        }

        private bool AchievementsUnlocked => SaveManager.Instance?.Current?.tutorial.hasCompletedFirstRun ?? false;

        /// <summary>
        /// Registers the reminder dots. Menu rows also light the ≡ handle; add future ones (shop, daily) here
        /// with <c>menuRow: true</c>.
        /// </summary>
        private void InitReminders(VisualElement root)
        {
            if (_reminders == null) return;
            _reminders.Register("btn-achievements",
                () => !TutorialActive && ((achievementService != null && achievementService.ClaimableCount > 0)
                                          || LoginRewardWaiting),
                menuRow: true);
            RegisterAchTabReminder("ach-tab-daily",       AchievementCategory.Daily);
            RegisterAchTabReminder("ach-tab-weekly",      AchievementCategory.Weekly);
            RegisterAchTabReminder("ach-tab-monthly",     AchievementCategory.Monthly);
            RegisterAchTabReminder("ach-tab-progression", AchievementCategory.Progression);
            _reminders.Init(root);
        }

        private void RegisterAchTabReminder(string tab, AchievementCategory category) =>
            _reminders.Register(tab,
                () => !TutorialActive && ((achievementService != null && achievementService.HasClaimable(category))
                                          || (category == AchievementCategory.Daily && LoginRewardWaiting)),
                menuRow: false);

        private static bool TutorialActive => SaveManager.Instance?.Current?.tutorial?.isActive ?? false;

        private static bool LoginRewardWaiting =>
            FeatureFlags.DailyEventsEnabled && (DailyEventService.Instance?.CanClaimLoginReward() ?? false);

        private void ClaimAchievementFromBanner(string id)
        {
            var service = achievementService;
            if (service == null || !service.IsClaimable(id)) return;
            var a = service.GetAll()?.FirstOrDefault(x => x != null && x.id == id);
            service.ClaimReward(id);
            if (_achievementsPanel != null && !_achievementsPanel.ClassListContains("hidden"))
                BuildAchievementsList();
            if (a != null)
                _banner?.ShowNotification("♦", $"Claimed: {a.displayName}");   // not ✓: blank on Android
        }

        public void ShowFieldBanner(FieldSO field)   => _banner?.ShowFieldBanner(field);
        public void HideFieldBanner()                => _banner?.HideFieldBanner();
        public void ShowTutorialHint(string message) => _banner?.ShowTutorialHint(message);
        public void HideTutorialHint()               => _banner?.HideTutorialHint();
        public void SetTutorialHintProgress(string progress) => _banner?.SetTutorialHintProgress(progress);

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

        /// <summary>Fires when an open inspector closes, however that happened.</summary>
        public event System.Action BuildingInspectorHidden;

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

            // What's new in this build (#502): the release notes CI published, once per text.
            string notes = FeatureFlags.WhatsNewNotes, notesVersion = FeatureFlags.WhatsNewVersion;
            var whatsNew = WhatsNew.Decide(Application.version, notesVersion, notes, save?.lastSeenWhatsNewHash,
                                           newPlayer: !(save?.tutorial.hasCompletedFirstRun ?? false));
            bool dataChanged = GameUpdateNotice.ShouldShow(FeatureFlags.GameDataUpdatedUtc, save?.lastSeenGameDataUtc);

            if (whatsNew == WhatsNew.Decision.Show)
            {
                // One card: the notes, then the balance notice when values changed too.
                string message = notes.Trim();
                if (dataChanged) message += "\n\n" + FeatureFlags.GameDataNoticeMessage;
                _gameUpdate?.Show(WhatsNew.Title(notesVersion), message, leftAligned: true);   // bullet lists read left
            }
            else if (dataChanged)
            {
                _gameUpdate?.Show(FeatureFlags.GameDataNoticeTitle, FeatureFlags.GameDataNoticeMessage);
            }
            if (dataChanged) TelemetryService.Instance?.RecordGameUpdateNotice(FeatureFlags.GameDataVersion);

            if (save == null || (whatsNew == WhatsNew.Decision.Hide && !dataChanged)) return;
            if (whatsNew != WhatsNew.Decision.Hide) save.lastSeenWhatsNewHash = WhatsNew.Hash(notes);
            if (dataChanged) save.lastSeenGameDataUtc = FeatureFlags.GameDataUpdatedUtc;
            SaveManager.Instance.SaveLocal();
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

        /// <summary>True when the HUD is wider than it is tall (the phone is held sideways).</summary>
        internal static bool IsLandscape(float width, float height) => width > height && height > 0f;

        /// <summary>
        /// True while the left drawer, any slide panel or the building menu is open. Refreshed every frame by
        /// <see cref="RefreshBannerClearance"/>. The Entropy Surge icon hides while it is set so it never sits
        /// on a menu (it comes straight back when the menu closes).
        /// </summary>
        public bool MenuOpen { get; private set; }

        /// <summary>
        /// Keeps the tutorial hint clear of any open menu. The left drawer and every slide panel sit over the
        /// banner's bottom bar, which hides their bottom rows (the Place buttons). Derived from what is on
        /// screen each frame, so every open path is covered and no caller can leave the banner expanded.
        /// </summary>
        private void RefreshBannerClearance()
        {
            if (_allPanels == null) return;
            bool menuOpen = _drawerOpen;
            VisualElement openPanel = null;
            foreach (var panel in _allPanels)
                if (panel != null && !panel.ClassListContains("hidden")) { openPanel = panel; break; }
            menuOpen |= openPanel != null;
            MenuOpen = menuOpen || (_inspector != null && _inspector.IsOpen) || (_cyclotron != null && _cyclotron.IsOpen);
            RefreshPanelDocking(_drawerOpen ? null : openPanel);
            if (_banner == null) return;
            _banner.SetMenuOpen(menuOpen);
            _banner.RefreshNotificationClearance();
        }

        // Smallest free strip the tutorial dialogue / "Show hint" pill will move into (panel units).
        private const float DialogueDockMinHeight = 200f, DialogueDockMinWidth = 240f;
        private const float PillDockMinHeight     = 52f,  PillDockMinWidth     = 120f;
        private const float TopBarHeight          = 60f;  // --top-bar-height in tokens.uss

        private VisualElement _hudFrame, _dialogueBox, _hintRestore;
        private PanelDock _pillDock = PanelDock.None, _dialogueDock = PanelDock.None;

        /// <summary>
        /// Moves the "Show hint" pill and the tutorial dialogue onto the map beside an open slide panel (above
        /// it in portrait, left of it in landscape), so neither covers the panel's buttons. See <see cref="PanelDock"/>.
        /// </summary>
        private void RefreshPanelDocking(VisualElement openPanel)
        {
            if (_hudFrame == null) return;
            var frame = new Vector2(_hudFrame.layout.width, _hudFrame.layout.height);
            if (float.IsNaN(frame.x) || float.IsNaN(frame.y)) return;
            var rect = openPanel != null ? openPanel.layout : Rect.zero;
            bool sized = openPanel != null && !float.IsNaN(rect.width) && rect.height > 0f;

            var pill = sized ? PanelDock.For(frame, rect, TopBarHeight, PillDockMinHeight, PillDockMinWidth) : PanelDock.None;
            var dlg  = sized ? PanelDock.For(frame, rect, TopBarHeight, DialogueDockMinHeight, DialogueDockMinWidth) : PanelDock.None;

            if (pill.Where != _pillDock.Where || pill.Free != _pillDock.Free)
            {
                _pillDock = pill;
                pill.PlaceBottomRight(_hintRestore, frame);
            }
            if (dlg.Where != _dialogueDock.Where || dlg.Free != _dialogueDock.Free)
            {
                _dialogueDock = dlg;
                dlg.Fill(_dialogueBox, frame);
                _dialogueBox?.EnableInClassList("dialogue-box--docked", dlg.Where != PanelDock.Side.None);
            }
        }

        private void CloseAllPanels()
        {
            foreach (var panel in _allPanels)
                SetElementVisible(panel, false);
            CloseDrawer();
            CancelActiveModes();
        }

        // Matches .left-drawer transition-duration (0.2s) in GameHUD.uss, plus a frame of slack.
        private const long DrawerSlideMs = 250;

        private void ToggleDrawer()
        {
            if (_drawerOpen) CloseDrawer();
            else             OpenDrawer();
        }

        private void OpenDrawer()
        {
            if (_drawerPanel == null) return;
            _drawerOpen = true;
            _drawerTapGuard.MarkOpened();
            _drawerPanel.style.visibility = Visibility.Visible;
            _drawerPanel.AddToClassList("left-drawer--open");
            SetElementVisible(_drawerBackdrop, true);
            OnDrawerOpened?.Invoke();
            // Hint active: btn-achievements is now visible — pulse it instead of the drawer handle
            if (_achievementsHintPulse != null)
                SwapAchievementsHintPulse("btn-achievements");
        }

        /// <summary>Closes the left drawer (for sub-controllers that open a modal from a drawer button).</summary>
        public void CloseMenuDrawer() => CloseDrawer();

        private void CloseDrawer()
        {
            if (_drawerPanel == null) return;
            _drawerOpen = false;
            _drawerPanel.RemoveFromClassList("left-drawer--open");
            // Hide once the 0.2 s slide-out transition has finished (unless reopened meanwhile).
            _drawerPanel.schedule.Execute(() =>
            {
                if (!_drawerOpen) _drawerPanel.style.visibility = Visibility.Hidden;
            }).StartingIn(DrawerSlideMs);
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
        internal static long CalculatePrestigePreview(double netWorth)
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
