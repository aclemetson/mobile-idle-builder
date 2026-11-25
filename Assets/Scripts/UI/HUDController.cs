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

        // For the panel sub-controllers (#286).
        internal BuildingPlacementController PlacementController => placementController;
        internal ConveyorPlacementController ConveyorController  => conveyorController;
        // Live singletons first: after a scene reload the serialized copies are duplicates about to be destroyed.
        internal ResearchService             Research            => ResearchService.Instance != null ? ResearchService.Instance : researchService;
        internal ManualCraftService          Craft               => ManualCraftService.Instance != null ? ManualCraftService.Instance : craftService;

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
                              _achievementsPanel, _tournamentPanel,
                              _shopPanel, _settingsPanel, _rewardsPanel, _worldsPanel, _managersPanel, _megastructurePanel, _statsPanel;
        private VisualElement[] _allPanels;

        // ---- Panel content ----
        private ScrollView _upgradesList;
        // ---- Codex + Prestige panels (#286): CodexPanelSubController, PrestigePanelSubController ----
        private CodexPanelSubController    _codex;
        private PrestigePanelSubController _prestigeUi;
        // ---- Research panel (list/tree, tabs, cards, skip: ResearchPanelSubController, #286) ----
        private ResearchPanelSubController _research;

        // ---- Build menu (tabs, cards, field purchases, affordability: BuildingsPanelSubController, #286) ----
        private BuildingsPanelSubController _buildings;

        // ---- HUD chrome ----
        // Left drawer + panel docking (DrawerSubController, #286)
        private DrawerSubController _drawer;
        private bool DrawerIsOpen => _drawer != null && _drawer.IsOpen;

        private DialogueController _dialogue;

        // ---- Mode bars (placement / conveyor / deconstruct, confirm popup, output selector: ModeBarsSubController, #286) ----
        private ModeBarsSubController _modeBars;

        // ---- Recipe-menu craft queue (#310) ----
        // ---- Recipes panel (tabs, cards, craft buttons, queue strip: RecipesPanelSubController, #286) ----
        private RecipesPanelSubController _recipes;

        // ---- Tutorial events ----
        public event System.Action OnDrawerOpened;
        public event System.Action OnResearchPanelOpened;
        public event System.Action OnRecipePanelOpened;
        public event System.Action OnConveyorPlaced;
        public event System.Action OnBuildingRecipeSet;

        // ---- Output selector ----

        // ---- Tooltip ----
        private VisualElement _tooltipPopup;
        private Label         _tooltipTitle, _tooltipBody;

        // ---- Achievements panel (tabs, list, claim all: AchievementsSubController, #286) ----
        private AchievementsSubController _achievements;

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
            _achievements = GetComponent<AchievementsSubController>() ?? gameObject.AddComponent<AchievementsSubController>();
            _research     = GetComponent<ResearchPanelSubController>() ?? gameObject.AddComponent<ResearchPanelSubController>();
            _buildings    = GetComponent<BuildingsPanelSubController>() ?? gameObject.AddComponent<BuildingsPanelSubController>();
            _recipes      = GetComponent<RecipesPanelSubController>() ?? gameObject.AddComponent<RecipesPanelSubController>();
            _modeBars     = GetComponent<ModeBarsSubController>() ?? gameObject.AddComponent<ModeBarsSubController>();
            _codex        = GetComponent<CodexPanelSubController>() ?? gameObject.AddComponent<CodexPanelSubController>();
            _prestigeUi   = GetComponent<PrestigePanelSubController>() ?? gameObject.AddComponent<PrestigePanelSubController>();
            _drawer       = GetComponent<DrawerSubController>() ?? gameObject.AddComponent<DrawerSubController>();
            _drawer.Opened += () =>
            {
                OnDrawerOpened?.Invoke();
                // Hint active: btn-achievements is now visible, so pulse it instead of the drawer handle.
                if (_achievementsHintPulse != null) SwapAchievementsHintPulse("btn-achievements");
            };
            _drawer.Closed += () =>
            {
                // Hint still active but the drawer is hidden: back to pulsing the drawer handle.
                if (_achievementsHintPulse != null) SwapAchievementsHintPulse("btn-drawer-handle");
            };
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
            // notification-banner starts hidden via CSS translate (no .hidden class needed)
            SetElementVisible(root.Q("tutorial-hint-banner"),  false);
            SetElementVisible(root.Q("field-proximity-banner"), false);
            SetElementVisible(_tooltipPopup, false);
            SetElementVisible(root.Q("building-inspector-panel"), false);

            if (placementController != null)
                placementController.OnBuildingPlaced += OnBuildingPlaced;
            if (conveyorController != null)
                conveyorController.OnChainPlaced     += RaiseConveyorPlaced;
            // Overlays, bars, confirm popup, output selector and the back button's mode flags.
            _modeBars?.Attach(placementController, conveyorController, deconstructController);



            BindLiveServices();

            if (researchService != null)
                researchService.OnResearchUnlocked += OnResearchUnlocked;

            _recipes?.AttachCraftService(craftService);   // craft-queue strip + queue refreshes
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
                placementController.OnBuildingPlaced -= OnBuildingPlaced;
            if (conveyorController != null)
                conveyorController.OnChainPlaced     -= RaiseConveyorPlaced;
            _modeBars?.Detach();



            if (researchService != null)
                researchService.OnResearchUnlocked -= OnResearchUnlocked;

            _recipes?.DetachCraftService();
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
            _research?.SetECSContext(_em);
            _buildings?.SetECSContext(_em);
            _prestigeUi?.SetECSContext(_em);
        }

        void Update()
        {
            _statusBar?.Tick();
            _inspector?.Tick();
            HandleBackButton();
            _modeBars?.Tick();
            _buildings?.Tick();
            RefreshBannerClearance();
            _recipes?.Tick(GameClock.UtcNow);

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
            var hudFrame = root.Q("root") ?? root;
            hudFrame.RegisterCallback<GeometryChangedEvent>(_ =>
                hudFrame.EnableInClassList("hud--landscape", IsLandscape(hudFrame.layout.width, hudFrame.layout.height)));

            // HUD chrome: the left drawer (handle, backdrop, slide, tap guard) and panel docking
            _drawer?.Initialize(root);

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

            _allPanels = new[]
            {
                _recipePanel, _buildingsPanel, _codexPanel,
                _researchPanel, _upgradesPanel, _managersPanel, _statsPanel, _megastructurePanel, _rewardsPanel, _achievementsPanel, _tournamentPanel, _prestigePanel,
                _shopPanel, _settingsPanel, _worldsPanel
            };

            // Panel content
            _recipes?.Initialize(root, this);   // recipe tabs + cards
            _buildings?.Initialize(root, this);   // Build menu tabs + cards
            _codex?.Initialize(root);
            _research?.Initialize(root, this);   // tree/list toggle, world tabs, cards
            _upgradesList     = root.Q<ScrollView>("upgrades-list");
            _achievements?.Initialize(root, achievementService);   // tabs, list, claim all, close

            // Achievements gate
            _btnAchievements        = root.Q<Button>("btn-achievements");
            _achievementsLockedHint = root.Q<Label>("achievements-locked-hint");
            _btnMegastructure       = root.Q<Button>("btn-megastructure");


            _prestigeUi?.Initialize(root, this);   // summary, preview, Confirm
            _modeBars?.Initialize(root);   // placement / conveyor / deconstruct bars, confirm popup, output selector
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

            // Building inspector close
            var btnCloseInspector = root.Q<Button>("btn-close-inspector");
            if (btnCloseInspector != null) btnCloseInspector.clicked += HideBuildingInspector;
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
            _recipes?.Open();
            OnRecipePanelOpened?.Invoke();
        }

        private void OpenBuildingsPanel()
        {
            CloseAllPanels();
            _buildings?.Open();
        }

        private void OpenCodexPanel()
        {
            CloseAllPanels();
            _codex?.Open();
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
            _research?.Open();
            OnResearchPanelOpened?.Invoke();
        }

        public static string FormatResearchTimer(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = System.TimeSpan.FromSeconds(System.Math.Ceiling(seconds));
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:D2}s";
            return $"{ts.Seconds}s";
        }

        internal static string ShortNumber(long v)
        {
            if (v >= 1_000_000_000_000) return (v / 1e12).ToString(v >= 100_000_000_000_000 ? "0" : "0.#") + "T";
            if (v >= 1_000_000_000)     return (v / 1e9).ToString(v >= 100_000_000_000 ? "0" : "0.#") + "B";
            if (v >= 1_000_000)         return (v / 1e6).ToString(v >= 100_000_000 ? "0" : "0.#") + "M";
            if (v >= 10_000)            return (v / 1e3).ToString(v >= 100_000 ? "0" : "0.#") + "k";
            return v.ToString("N0");
        }

        private void OnResearchUnlocked(ResearchSO research)
        {
            // An open Research panel rebuilds, or closes during the tutorial (ResearchPanelSubController).
            _research?.OnResearchUnlocked();

            // Refresh buildings panel if it is open (newly unlocked buildings will appear)
            _buildings?.RefreshIfOpen();

            // Refresh recipe panel — newly learned recipes will appear
            _recipes?.RefreshIfOpen();

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
            _btnAchievements?.SetEnabled(true);
            SetElementVisible(_achievementsLockedHint, false);
            _achievements?.ApplyGate(AchievementsUnlocked);
        }

        private void OpenAchievementsPanel() =>
            OpenAchievementsPanel(_achievements != null ? _achievements.FirstClaimableCategory() : AchievementCategory.Daily);

        private void OpenAchievementsPanel(AchievementCategory category)
        {
            DismissAchievementsHint();
            CloseAllPanels();
            _achievements?.Open(category);
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

        // ============================================================
        // Tournament panel (PvE; TournamentPanelController owns pve-panel)
        // ============================================================

        private void OpenTournamentPanel()
        {
            CloseAllPanels();
            _tournament?.Open();
        }

        // Prestige requests (PrestigePanelSubController; other controllers call these on the HUD).

        /// <summary>The prestige that starts a tournament run: the normal prestige, exempt from the tournament lock.</summary>
        public void RequestTournamentPrestige()
        {
            if (_prestigeUi != null && _prestigeUi.RequestTournamentPrestige()) CloseAllPanels();
        }

        /// <summary>Prestige currency a prestige would pay now (the tournament join card shows it).</summary>
        public long CurrentPrestigePreview() => _prestigeUi != null ? _prestigeUi.Preview() : 0;

        /// <summary>True once ECS is ready for a prestige request.</summary>
        public bool CanRequestPrestige => _prestigeUi != null && _prestigeUi.CanRequest;

        private void OpenPrestigePanel()
        {
            if (!CanRequestPrestige) return;
            CloseAllPanels();
            _prestigeUi.Open();
        }

        /// <summary>The Megastructure panel's Ascend button: a prestige that also applies <see cref="AscensionRules"/>.</summary>
        public void RequestAscension() => _prestigeUi?.RequestAscension();

        // ============================================================
        // Shared text helpers (the panel sub-controllers and tests use these)
        // ============================================================

        /// <summary>A research card's time: "Instant", "Instant · 2 free left" (Free Research, #491) or "⏳ 1:30".</summary>
        internal static string ResearchTimeText(int effectiveSeconds, int freeLeft) =>
            effectiveSeconds <= 0 ? "Instant"
            : freeLeft > 0 ? $"Instant · {freeLeft} free left"
            : $"⏳ {FormatResearchTimer(effectiveSeconds)}";

        /// <summary>The lock line on a recipe known from an earlier run but gated by research now (#490).</summary>
        internal static string ResearchLockText(string researchName) => $"Research {researchName} to unlock";

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

        // ============================================================
        // Build costs
        // ============================================================

        // Zero-cost buildings (conveyors, Maxwell's Demon) say "Free" rather than leaving the cost blank (#197).
        internal static string FormatBuildCost(long cost) => cost > 0 ? $"◈ {cost:N0}" : "Free";

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
        // Mode-bar helpers (pure; ModeBarsSubController and tests use these) and tutorial signals
        // ============================================================

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

        /// <summary>Forwards the controller's chain-placed signal to tutorial listeners.</summary>
        private void RaiseConveyorPlaced() => OnConveyorPlaced?.Invoke();

        /// <summary>Raised by the building inspector when the player sets a building's recipe.
        /// Drives the tutorial's "set the Combiner to Proton" step.</summary>
        public void NotifyBuildingRecipeSet() => OnBuildingRecipeSet?.Invoke();

        // ============================================================
        // Back button (#205)
        // ============================================================

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
                ModeActiveOrJustEnded = _modeBars != null && _modeBars.ModeActiveOrJustEnded,
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
                DrawerOpen       = DrawerIsOpen,
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
            _achievements?.RefreshIfOpen();
            if (a != null)
                _banner?.ShowNotification("♦", $"Claimed: {a.displayName}");   // not ✓: blank on Android
        }

        public void ShowFieldBanner(FieldSO field)   => _banner?.ShowFieldBanner(field);
        public void HideFieldBanner()                => _banner?.HideFieldBanner();
        public void ShowTutorialHint(string message) => _banner?.ShowTutorialHint(message);
        public void HideTutorialHint()               => _banner?.HideTutorialHint();
        public void SetTutorialHintProgress(string progress) => _banner?.SetTutorialHintProgress(progress);

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
            bool menuOpen = DrawerIsOpen;
            VisualElement openPanel = null;
            foreach (var panel in _allPanels)
                if (panel != null && !panel.ClassListContains("hidden")) { openPanel = panel; break; }
            menuOpen |= openPanel != null;
            MenuOpen = menuOpen || (_inspector != null && _inspector.IsOpen) || (_cyclotron != null && _cyclotron.IsOpen);
            _drawer?.RefreshPanelDocking(DrawerIsOpen ? null : openPanel);
            if (_banner == null) return;
            _banner.SetMenuOpen(menuOpen);
            _banner.RefreshNotificationClearance();
        }

        private void CloseAllPanels()
        {
            foreach (var panel in _allPanels)
                SetElementVisible(panel, false);
            CloseDrawer();
            CancelActiveModes();
        }

        /// <summary>Closes the left drawer (for sub-controllers that open a modal from a drawer button).</summary>
        public void CloseMenuDrawer() => CloseDrawer();

        private void CloseDrawer() => _drawer?.Close();

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
