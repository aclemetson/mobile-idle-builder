#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Dev
{
    /// <summary>
    /// Drives the dev console UI and dispatches commands.
    /// Activated via backtick/tilde (`) key on desktop, or device shake on mobile.
    /// Attach to a GameObject that also has a UIDocument (Sort Order 10).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(-80)]
    public sealed class DevConsoleController : MonoBehaviour
    {
        // ── Activation ────────────────────────────────────────────────────────

        private const float ShakeThreshold    = 2.5f;
        private const float ShakeWindow       = 1.5f;
        private const int   ShakePeaksRequired = 3;

        private readonly ShakePeakDetector _shakeDetector =
            new ShakePeakDetector(ShakeThreshold, ShakeWindow, ShakePeaksRequired);

        private bool _isVisible;
        private bool _clearFieldNextFrame;
        private bool _refocusFieldNextFrame;

        // ── UI ────────────────────────────────────────────────────────────────

        private VisualElement _consoleRoot;
        private ScrollView    _logView;
        private TextField     _inputField;

        private const int MaxLogLines = 10;
        private readonly List<Label> _logLabels = new();

        // ── ECS ───────────────────────────────────────────────────────────────

        private EntityManager _em;
        private EntityQuery   _progressQuery;
        private EntityQuery   _prestigeQuery;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _tutorialQuery;

        // ── Commands ──────────────────────────────────────────────────────────

        private DevCommandRegistry _registry;

        // ── Singleton ─────────────────────────────────────────────────────────

        private static DevConsoleController s_Instance;

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);   // destroy the whole GO so the UIDocument goes with it
                return;
            }
            s_Instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnEnable()
        {
            BindUI();
            SetVisible(false);
        }

        private void BindUI()
        {
            GameLogger.Info("[DevConsole] BindUI — start");
            var root = GetComponent<UIDocument>().rootVisualElement;

            _consoleRoot = root.Q("dev-console-root");
            _logView     = root.Q<ScrollView>("dev-console-log");
            _inputField  = root.Q<TextField>("dev-console-input");

            // Stale label references belong to the old visual tree.
            _logLabels.Clear();

            var closeBtn  = root.Q<Button>("btn-close-console");
            var submitBtn = root.Q<Button>("btn-submit-console");

            // Unregister first so repeated BindUI calls don't stack duplicates.
            closeBtn.clicked  -= OnCloseButtonClicked;
            submitBtn.clicked -= SubmitCommand;
            _inputField?.UnregisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);
            root.UnregisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            closeBtn.clicked  += OnCloseButtonClicked;
            submitBtn.clicked += SubmitCommand;
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);

            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);
            GameLogger.Info("[DevConsole] BindUI — done");

            // Block world input (camera pan + gameplay taps) over the console overlay.
            UIInputBlocker.Register(GetComponent<UIDocument>());
        }

        private void OnCloseButtonClicked() => SetVisible(false);

        void OnDisable()
        {
            UIInputBlocker.Unregister(GetComponent<UIDocument>());
            UIInputBlocker.SetModal(this, false);
        }

        void Start()
        {
            TryEnableAccelerometer();
            InputSystem.onDeviceChange += OnInputDeviceChange;

            _registry = new DevCommandRegistry();
            RegisterCommands();

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;

            _em             = world.EntityManager;
            _progressQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            _prestigeQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>());
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>());
            _tutorialQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<TutorialStateData>());
        }

        void OnDestroy()
        {
            InputSystem.onDeviceChange -= OnInputDeviceChange;
            if (s_Instance == this)
            {
                s_Instance = null;
                SceneManager.sceneLoaded -= OnSceneLoaded;
            }
            if (Accelerometer.current != null)
                InputSystem.DisableDevice(Accelerometer.current);
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            GameLogger.Info($"[DevConsole] OnSceneLoaded — scene='{scene.name}'  _isVisible={_isVisible}");

            // Re-acquire ECS queries — the world is recreated on each scene reload.
            var world = World.DefaultGameObjectInjectionWorld;
            GameLogger.Info($"[DevConsole] OnSceneLoaded — world={(world != null ? "found" : "null")}");
            if (world != null)
            {
                _em             = world.EntityManager;
                _progressQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
                _prestigeQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<PrestigeData>());
                _inventoryQuery = _em.CreateEntityQuery(
                    ComponentType.ReadOnly<PlayerInventoryTag>(),
                    ComponentType.ReadWrite<InventorySlot>());
                _tutorialQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<TutorialStateData>());
            }

            // Re-bind UI — UIDocument rebuilds its visual tree on each scene reload.
            // Without this, _consoleRoot and button handlers point to the old detached tree.
            BindUI();
            // Close the console only when we arrive at the destination scene, not during the
            // intermediate loading screen.  Calling SetVisible(false) while LoadingScreen is
            // active dismisses the soft keyboard mid-LoadSceneAsync and deadlocks Vulkan.
            if (scene.name != SceneLoader.LoadingSceneName)
            {
                GameLogger.Info($"[DevConsole] OnSceneLoaded — destination scene, calling SetVisible(false)");
                SetVisible(false);
            }
            else
            {
                GameLogger.Info($"[DevConsole] OnSceneLoaded — loading screen, keeping console state _isVisible={_isVisible}");
            }
            GameLogger.Info($"[DevConsole] OnSceneLoaded — done for scene='{scene.name}'");
        }

        void Update()
        {
            if (_clearFieldNextFrame)
            {
                _inputField?.SetValueWithoutNotify(string.Empty);
                _clearFieldNextFrame = false;
            }
            if (_refocusFieldNextFrame)
            {
                _inputField?.Focus();
                _refocusFieldNextFrame = false;
            }
            DetectKeyboard();
            DetectShake();
        }

        // ── Activation detection ──────────────────────────────────────────────

        private void DetectKeyboard()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.backquoteKey.wasPressedThisFrame)
                SetVisible(!_isVisible);
        }

        private void OnRootKeyDown(KeyDownEvent e) { /* reserved for future use */ }

        private static void TryEnableAccelerometer()
        {
            if (Accelerometer.current != null)
                InputSystem.EnableDevice(Accelerometer.current);
        }

        private static void OnInputDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (device is Accelerometer && change == InputDeviceChange.Added)
                InputSystem.EnableDevice(device);
        }

        private void DetectShake()
        {
            var accel = Accelerometer.current;
            if (accel == null) return;

            // On Android the device may arrive after Start(); enable it lazily.
            if (!accel.enabled)
                InputSystem.EnableDevice(accel);

            float magnitude = accel.acceleration.ReadValue().magnitude;
            if (_shakeDetector.Feed(magnitude, Time.realtimeSinceStartup))
                SetVisible(!_isVisible);
        }

        // ── Visibility ────────────────────────────────────────────────────────

        private void SetVisible(bool visible)
        {
            GameLogger.Info($"[DevConsole] SetVisible({visible}) — consoleRoot={((_consoleRoot == null) ? "null" : "ok")}");
            _isVisible = visible;
            // The console is a debug overlay sharing the HUD's panel; block ALL world input
            // (camera pan + gameplay taps) while it is open rather than relying on per-element
            // hit-testing across the shared panel.
            UIInputBlocker.SetModal(this, visible);
            if (_consoleRoot == null) return;

            if (visible)
            {
                _consoleRoot.RemoveFromClassList("hidden");
                _inputField?.Focus();
                _clearFieldNextFrame = true;
                if (_logLabels.Count == 0)
                    AppendLog("Dev console ready. Type 'help' for commands.", "log-entry--info");
            }
            else
            {
                _consoleRoot.AddToClassList("hidden");
            }
            GameLogger.Info($"[DevConsole] SetVisible({visible}) — done");
        }

        // ── Input ─────────────────────────────────────────────────────────────

        private void OnInputKeyDown(KeyDownEvent e)
        {
            if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
            {
                SubmitCommand();
                e.StopPropagation();
            }
        }

        private void SubmitCommand()
        {
            if (_inputField == null || _registry == null) return;
            var input = _inputField.value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(input)) return;

            AppendLog($"> {input}", "log-entry--info");
            _inputField.SetValueWithoutNotify(string.Empty);

            var result = _registry.Execute(input);
            if (!string.IsNullOrEmpty(result))
            {
                bool isError = result.StartsWith("Error") || result.StartsWith("Unknown");
                AppendLog(result, isError ? "log-entry--error" : "log-entry--success");
            }

            // Don't re-focus if a scene transition is already underway — keeping the input
            // field focused leaves UI Toolkit's keyboard-poll timer running into the loading
            // screen where it fires CloseTouchScreenKeyboard() on the same frame as
            // LoadSceneAsync, triggering a Vulkan swapchain race on Android.
            if (!SceneLoader.IsTransitioning)
            {
                _refocusFieldNextFrame = true;
                _inputField.Focus();
            }
            else
            {
                _inputField.Blur();
            }
        }

        // ── Log ───────────────────────────────────────────────────────────────

        private void AppendLog(string text, string cssClass = null)
        {
            if (_logView == null) return;

            var label = new Label(text);
            label.AddToClassList("log-entry");
            if (!string.IsNullOrEmpty(cssClass))
                label.AddToClassList(cssClass);

            _logView.Add(label);
            _logLabels.Add(label);

            while (_logLabels.Count > MaxLogLines)
            {
                _logView.Remove(_logLabels[0]);
                _logLabels.RemoveAt(0);
            }

            _logView.schedule.Execute(() => _logView.ScrollTo(_logLabels[^1]));
        }

        // ── Command registration ──────────────────────────────────────────────

        private void RegisterCommands()
        {
            // ── help ─────────────────────────────────────────────────────────
            _registry.Register("help", "List all commands",
                _ => _registry.GetHelpText());

            // ── item listing ─────────────────────────────────────────────────
            _registry.Register("list items", "List all items with their string IDs",
                _ =>
                {
                    var db = ItemDatabase.Instance;
                    if (db?.All == null || db.All.Count == 0)
                        return "Error: ItemDatabase not ready or empty.";
                    var sb = new System.Text.StringBuilder();
                    foreach (var item in db.All)
                        if (item != null)
                            sb.AppendLine($"  {item.id,-24} {item.symbol} {item.displayName}");
                    return sb.ToString().TrimEnd();
                });

            // ── currency & prestige currency ─────────────────────────────────
            _registry.Register("add currency <amount>", "Add BaseCurrency",
                args =>
                {
                    if (!long.TryParse(args[0], out long amount) || amount <= 0)
                        return "Error: <amount> must be a positive integer.";
                    if (_progressQuery.IsEmpty) return "Error: PlayerProgressData not found.";
                    var entity = _progressQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PlayerProgressData>(entity);
                    data.BaseCurrency += amount;
                    _em.SetComponentData(entity, data);
                    return $"Added {amount} currency. Total: {data.BaseCurrency}";
                });

            _registry.Register("add prestige <amount>", "Add PrestigeCurrency",
                args =>
                {
                    if (!long.TryParse(args[0], out long amount) || amount <= 0)
                        return "Error: <amount> must be a positive integer.";
                    if (_prestigeQuery.IsEmpty) return "Error: PrestigeData not found.";
                    var entity = _prestigeQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PrestigeData>(entity);
                    data.PrestigeCurrency += amount;
                    _em.SetComponentData(entity, data);
                    return $"Added {amount} prestige currency. Total: {data.PrestigeCurrency}";
                });

            // ── inventory item ────────────────────────────────────────────────
            _registry.Register("add item <itemId> <qty>", "Add inventory item by string ID",
                args =>
                {
                    var itemId = args[0];
                    if (!int.TryParse(args[1], out int qty) || qty <= 0)
                        return "Error: <qty> must be a positive integer.";

                    int numericId = ItemDatabase.Instance?.GetItemId(itemId) ?? -1;
                    if (numericId == -1)
                        return $"Error: Item '{itemId}' not found in ItemDatabase.";
                    if (_inventoryQuery.IsEmpty)
                        return "Error: Player inventory entity not found.";

                    var entity = _inventoryQuery.GetSingletonEntity();
                    var buffer = _em.GetBuffer<InventorySlot>(entity);

                    for (int i = 0; i < buffer.Length; i++)
                    {
                        if (buffer[i].ItemID == numericId)
                        {
                            var slot = buffer[i];
                            slot.Quantity += qty;
                            buffer[i] = slot;
                            return $"Added {qty}x {itemId}. New total: {slot.Quantity}";
                        }
                    }

                    buffer.Add(new InventorySlot { ItemID = numericId, Quantity = qty });
                    return $"Added {qty}x {itemId} (new slot).";
                });

            // ── research ──────────────────────────────────────────────────────
            _registry.Register("unlock research <id>", "Force-unlock a research node by string ID (ignores cost/prereqs)",
                args =>
                {
                    var rs = ResearchService.Instance;
                    if (rs == null) return "Error: ResearchService not found.";

                    string id    = args[0];
                    bool   exists = false;
                    if (rs.AllResearch != null)
                        foreach (var r in rs.AllResearch)
                            if (r != null && r.id == id) { exists = true; break; }
                    if (!exists)
                        return $"Error: research '{id}' not found. Use lowercase IDs from game_data.json (e.g. megastructure_theory).";
                    if (rs.IsUnlocked(id))
                        return $"Research '{id}' already unlocked.";

                    rs.ForceUnlock(id);
                    return $"Unlocked research '{id}'.";
                });

            // ── show prestige ─────────────────────────────────────────────────
            _registry.Register("show prestige", "Dump PrestigeData (run count, currency, multipliers)",
                _ =>
                {
                    if (_prestigeQuery.IsEmpty) return "Error: PrestigeData not found.";
                    var data = _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity());
                    return $"RunCount={data.RunCount}  Currency={data.PrestigeCurrency}  Speed={data.SpeedMultiplier}  Output={data.OutputMultiplier}";
                });

            // ── show progress ─────────────────────────────────────────────────
            _registry.Register("show progress", "Dump PlayerProgressData (entropy, tier, research, NetWorth, prestige)",
                _ =>
                {
                    if (_progressQuery.IsEmpty) return "Error: PlayerProgressData not found.";
                    var data = _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity());
                    int researchCount = -1;
                    var rs2 = ResearchService.Instance;
                    if (rs2?.AllResearch != null)
                    {
                        researchCount = 0;
                        foreach (var r in rs2.AllResearch)
                            if (rs2.IsUnlocked(r.id)) researchCount++;
                    }
                    string research = researchCount >= 0 ? researchCount.ToString() : "?";
                    return $"Entropy={data.BaseCurrency}  Tier={data.CurrentTier}  Research={research}  NetWorth={data.NetWorth:F0}  Base={data.BaseNetWorth:F0}  Wall={data.PrestigeWallValue:F0}  Available={data.PrestigeAvailable}";
                });

            // ── set prestige available ────────────────────────────────────────
            _registry.Register("set prestige available", "Force PrestigeAvailable = true",
                _ =>
                {
                    if (_progressQuery.IsEmpty) return "Error: PlayerProgressData not found.";
                    var entity = _progressQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PlayerProgressData>(entity);
                    data.PrestigeAvailable = true;
                    _em.SetComponentData(entity, data);
                    return "PrestigeAvailable = true. Prestige button should now appear.";
                });

            // ── set tier ──────────────────────────────────────────────────────
            _registry.Register("set tier <n>", "Set CurrentTier",
                args =>
                {
                    if (!int.TryParse(args[0], out int n) || n < 0)
                        return "Error: <n> must be a non-negative integer.";
                    if (_progressQuery.IsEmpty) return "Error: PlayerProgressData not found.";
                    var entity = _progressQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PlayerProgressData>(entity);
                    data.CurrentTier = n;
                    _em.SetComponentData(entity, data);
                    return $"Tier set to {n}.";
                });

            // ── set prestige_count ────────────────────────────────────────────
            _registry.Register("set prestige_count <n>", "Set prestige run count",
                args =>
                {
                    if (!int.TryParse(args[0], out int n) || n < 0)
                        return "Error: <n> must be a non-negative integer.";
                    if (_prestigeQuery.IsEmpty) return "Error: PrestigeData not found.";
                    var entity = _prestigeQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PrestigeData>(entity);
                    data.RunCount = n;
                    _em.SetComponentData(entity, data);
                    if (SaveManager.Instance?.Current != null)
                        SaveManager.Instance.Current.prestigeCount = n;
                    return $"Prestige count set to {n}.";
                });

            // ── set multiplier ────────────────────────────────────────────────
            _registry.Register("set multiplier speed <n>", "Set SpeedMultiplier",
                args =>
                {
                    if (!float.TryParse(args[0], out float n) || n <= 0)
                        return "Error: <n> must be a positive number.";
                    if (_prestigeQuery.IsEmpty) return "Error: PrestigeData not found.";
                    var entity = _prestigeQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PrestigeData>(entity);
                    data.SpeedMultiplier = n;
                    _em.SetComponentData(entity, data);
                    return $"SpeedMultiplier set to {n}.";
                });

            _registry.Register("set multiplier output <n>", "Set OutputMultiplier",
                args =>
                {
                    if (!float.TryParse(args[0], out float n) || n <= 0)
                        return "Error: <n> must be a positive number.";
                    if (_prestigeQuery.IsEmpty) return "Error: PrestigeData not found.";
                    var entity = _prestigeQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PrestigeData>(entity);
                    data.OutputMultiplier = n;
                    _em.SetComponentData(entity, data);
                    return $"OutputMultiplier set to {n}.";
                });

            // ── force prestige ────────────────────────────────────────────────
            _registry.Register("force prestige", "Trigger prestige (sets PrestigeRequested flag)",
                _ =>
                {
                    if (_progressQuery.IsEmpty) return "Error: PlayerProgressData not found.";
                    var entity = _progressQuery.GetSingletonEntity();
                    var data   = _em.GetComponentData<PlayerProgressData>(entity);
                    data.PrestigeRequested = true;
                    _em.SetComponentData(entity, data);
                    return "Prestige requested. PrestigeSystem will process on next frame.";
                });

            // ── PVP ───────────────────────────────────────────────────────────
            _registry.Register("unlock pvp", "Override PVP prestige lock",
                _ =>
                {
                    if (SaveManager.Instance?.Current == null)
                        return "Error: SaveManager not ready.";
                    SaveManager.Instance.Current.prestigeCount = PVPService.PvpPrestigeUnlockCount;
                    PVPService.Instance?.ResetForNewWeek();
                    return $"PVP unlocked (prestigeCount set to {PVPService.PvpPrestigeUnlockCount}).";
                });

            _registry.Register("reset pvp", "Reset PVP run state",
                _ =>
                {
                    PVPService.Instance?.ResetForNewWeek();
                    return "PVP run reset.";
                });

            _registry.Register("win pvp", "Notify achievement service of a PVP win",
                _ =>
                {
                    AchievementService.Instance?.NotifyPVPWin();
                    return "PVP win notified.";
                });

            // ── achievements ──────────────────────────────────────────────────
            _registry.Register("complete achievement <id>", "Force-complete one achievement by ID",
                args =>
                {
                    if (AchievementService.Instance == null)
                        return "Error: AchievementService not found.";
                    AchievementService.Instance.ForceComplete(args[0]);
                    return $"Force-completed achievement '{args[0]}'.";
                });

            _registry.Register("complete all achievements", "Force-complete all achievements",
                _ =>
                {
                    if (AchievementService.Instance == null)
                        return "Error: AchievementService not found.";
                    AchievementService.Instance.ForceCompleteAll();
                    return "All achievements force-completed.";
                });

            // ── sites (multi-grids) ──────────────────────────────────────────
            _registry.Register("site list", "List all build sites (index, id, cost, unlocked, active)",
                _ =>
                {
                    var svc = SiteService.Instance;
                    if (svc?.AllSites == null || svc.AllSites.Count == 0)
                        return "Error: SiteService not ready or SiteDatabase empty.";
                    var sb = new StringBuilder($"Sites ({svc.AllSites.Count}):\n");
                    for (int i = 0; i < svc.AllSites.Count; i++)
                    {
                        var s = svc.AllSites[i];
                        if (s == null) continue;
                        string flags = (i == svc.ActiveIndex ? "ACTIVE " : "")
                                     + (svc.IsUnlocked(i) ? "unlocked" : "locked");
                        sb.AppendLine($"  [{i}] {s.id,-18} {s.unlockCost,12}e  {flags}");
                    }
                    return sb.ToString().TrimEnd();
                });

            _registry.Register("site switch <n>", "Switch the live grid to site index n",
                args =>
                {
                    if (!int.TryParse(args[0], out int n) || n < 0)
                        return "Error: <n> must be a non-negative integer.";
                    var svc = SiteService.Instance;
                    if (svc == null) return "Error: SiteService not ready.";
                    if (svc.GetSite(n) == null) return $"Error: no site at index {n}. Try 'site list'.";
                    if (!svc.IsUnlocked(n)) return $"Error: site [{n}] is locked. Unlock it first.";
                    if (n == svc.ActiveIndex) return $"Already on site [{n}].";
                    return svc.SwitchTo(n)
                        ? $"Switched to site [{n}] '{svc.GetSite(n).id}'."
                        : $"Error: switch to [{n}] failed.";
                });

            _registry.Register("domains intro", "Replay the one-shot Quantum Domains intro dialogue",
                _ =>
                {
                    var sites = FindAnyObjectByType<SitesSubController>();
                    if (sites == null) return "Error: SitesSubController not in scene (wire it on the HUD GameObject).";
                    sites.ReplayDomainsIntroForTesting();
                    return "Replaying Quantum Domains intro.";
                });

            _registry.Register("site unlock <id>", "Unlock a site by id (deducts entropy)",
                args =>
                {
                    var svc = SiteService.Instance;
                    if (svc == null) return "Error: SiteService not ready.";
                    int idx = svc.IndexOf(args[0]);
                    var site = svc.GetSite(idx);
                    if (site == null) return $"Error: unknown site '{args[0]}'. Try 'site list'.";
                    if (svc.IsUnlocked(idx)) return $"Site '{site.id}' already unlocked.";
                    if (!svc.CanUnlock(idx)) return $"Error: cannot afford '{site.id}' ({site.unlockCost}e).";
                    return svc.UnlockSite(site.id)
                        ? $"Unlocked '{site.id}' for {site.unlockCost}e."
                        : $"Error: unlock of '{site.id}' failed.";
                });

            // ── tutorial ─────────────────────────────────────────────────────
            _registry.Register("skip tutorial", "Complete tutorial immediately and unlock all tutorial research",
                _ =>
                {
                    // ECS — mark tutorial finished
                    if (!_tutorialQuery.IsEmpty)
                    {
                        var entity = _tutorialQuery.GetSingletonEntity();
                        var ts     = _em.GetComponentData<TutorialStateData>(entity);
                        var flow   = TutorialFlowSO.Current;
                        ts.CurrentStepIndex = flow?.steps?.Length ?? int.MaxValue;
                        ts.IsActive         = false;
                        ts.FirstRunComplete = true;
                        _em.SetComponentData(entity, ts);
                    }

                    // Save data — persist so tutorial doesn't restart on reload
                    if (SaveManager.Instance?.Current != null)
                    {
                        SaveManager.Instance.Current.tutorial.hasCompletedFirstRun = true;
                        SaveManager.Instance.Current.tutorial.isActive             = false;
                    }

                    // Research — force-unlock every research gate the tutorial would have awarded
                    var rs = ResearchService.Instance;
                    var tutFlow = TutorialFlowSO.Current;
                    int unlocked = 0;
                    if (rs != null && tutFlow?.steps != null)
                    {
                        foreach (var step in tutFlow.steps)
                        {
                            var cond = step.advanceCondition;
                            if (cond?.type == ConditionType.ResearchUnlocked &&
                                !string.IsNullOrEmpty(cond.researchId))
                            {
                                rs.ForceUnlock(cond.researchId);
                                unlocked++;
                            }
                        }
                    }

                    return $"Tutorial skipped. {unlocked} research node(s) unlocked.";
                });

            _registry.Register("tutorial list", "List all tutorial step IDs with their indices",
                _ =>
                {
                    var flow = TutorialFlowSO.Current;
                    if (flow?.steps == null || flow.steps.Length == 0)
                        return "Error: TutorialFlowSO not loaded. Run the GameData importer.";

                    var sb = new StringBuilder($"Tutorial steps ({flow.steps.Length} total):\n");
                    for (int i = 0; i < flow.steps.Length; i++)
                        sb.AppendLine($"  [{i,2}]  {flow.steps[i].id}");
                    return sb.ToString().TrimEnd();
                });

            _registry.Register("tutorial skip <id>", "Skip to a tutorial step by ID (use 'tutorial list' to find IDs)",
                args =>
                {
                    var flow = TutorialFlowSO.Current;
                    if (flow?.steps == null || flow.steps.Length == 0)
                        return "Error: TutorialFlowSO not loaded. Run the GameData importer.";

                    // Find step index — args[0] is already lowercased by registry
                    int idx = -1;
                    string targetId = args[0];
                    for (int i = 0; i < flow.steps.Length; i++)
                    {
                        if (flow.steps[i].id.ToLowerInvariant() == targetId)
                        {
                            idx = i;
                            break;
                        }
                    }
                    if (idx < 0)
                        return $"Unknown step '{targetId}'. Type 'tutorial list' to see all IDs.";

                    string canonicalId = flow.steps[idx].id;

                    // ── ECS: set tutorial step, entropy, and net worth ────────
                    long  entropy    = TutorialStepPresets.GetEntropy(flow, idx);
                    float netWorth   = TutorialStepPresets.GetNetWorth(flow, idx);
                    long  totalSpent = TutorialStepPresets.GetTotalEntropySpent(flow, idx);
                    float totalEarned = (float)(entropy + totalSpent);

                    if (!_tutorialQuery.IsEmpty)
                    {
                        var entity = _tutorialQuery.GetSingletonEntity();
                        var ts     = _em.GetComponentData<TutorialStateData>(entity);
                        ts.CurrentStepIndex = idx;
                        ts.IsActive         = true;
                        _em.SetComponentData(entity, ts);
                    }

                    if (!_progressQuery.IsEmpty)
                    {
                        var pp = _progressQuery.GetSingleton<PlayerProgressData>();
                        pp.BaseCurrency      = entropy;
                        pp.TotalEntropySpent = totalSpent;
                        pp.BaseNetWorth      = System.Math.Max(0f, netWorth - totalEarned);
                        pp.NetWorth          = System.Math.Max(netWorth, totalEarned);
                        _progressQuery.SetSingleton(pp);
                    }

                    // ── Research: unlock all gates for steps 0..(idx-1) ──────
                    var rs           = ResearchService.Instance;
                    int  unlocked    = 0;
                    long researchCost = 0;
                    if (rs != null)
                    {
                        for (int i = 0; i < idx; i++)
                        {
                            var cond = flow.steps[i].advanceCondition;
                            if (cond?.type == ConditionType.ResearchUnlocked &&
                                !string.IsNullOrEmpty(cond.researchId))
                            {
                                rs.ForceUnlock(cond.researchId);
                                unlocked++;
                                foreach (var r in rs.AllResearch)
                                    if (r != null && r.id == cond.researchId)
                                        { researchCost += r.costBaseCurrency; break; }
                            }
                        }
                    }

                    // ── SaveData: persist tutorial and currency state ─────────
                    var save = SaveManager.Instance?.Current;
                    if (save != null)
                    {
                        save.tutorial.currentStepId           = canonicalId;
                        save.tutorial.isActive                 = true;
                        save.currentRun.baseCurrency           = entropy;
                        save.currentRun.totalEntropySpent      = totalSpent;
                        save.currentRun.baseNetWorth           = System.Math.Max(0f, netWorth - totalEarned);
                    }

                    // ── Grid: apply building/conveyor preset if defined ───────
                    var buildings = TutorialStepPresets.GetBuildings(canonicalId);
                    var conveyors = TutorialStepPresets.GetConveyors(canonicalId);

                    if (buildings != null && save != null)
                    {
                        save.currentRun.grid.buildings = new List<BuildingSaveData>(buildings);
                        save.currentRun.grid.conveyors = conveyors != null
                            ? new List<ConveyorSaveData>(conveyors)
                            : new List<ConveyorSaveData>();
                        var presetFields = TutorialStepPresets.GetFields(canonicalId);
                        save.currentRun.grid.fields = presetFields != null
                            ? new List<FieldSaveData>(presetFields)
                            : new List<FieldSaveData>();
                        GridSaveService.Instance?.ClearGrid();
                        GridSaveService.Instance?.LoadGrid(forceApply: true);

                        // Set TotalEntropySpent = actual research costs + actual building costs
                        // so net worth (BaseCurrency + TotalEntropySpent) correctly reflects
                        // the full investment at this tutorial checkpoint.
                        long buildingCost  = GridSaveService.Instance?.ComputeGridBuildingCost() ?? 0;
                        long totalInvested = researchCost + buildingCost;
                        if (!_progressQuery.IsEmpty)
                        {
                            var pp = _progressQuery.GetSingleton<PlayerProgressData>();
                            pp.BaseCurrency      = entropy;
                            pp.TotalEntropySpent = totalInvested;
                            pp.BaseNetWorth      = 0f;
                            pp.NetWorth          = entropy + totalInvested;
                            _progressQuery.SetSingleton(pp);
                        }

                        SaveManager.Instance.SaveLocal(skipGridFlush: true);
                        return $"Skipped to '{canonicalId}' [{idx}]. Entropy: {entropy}e. " +
                               $"{unlocked} research node(s) unlocked. Grid applied.";
                    }

                    SaveManager.Instance?.SaveLocal();

                    string gridNote = idx >= 31
                        ? " (grid preset not yet captured — place buildings manually if needed)"
                        : string.Empty;

                    return $"Skipped to '{canonicalId}' [{idx}]. Entropy: {entropy}e. " +
                           $"{unlocked} research node(s) unlocked.{gridNote}";
                });

            // ── testing helpers ───────────────────────────────────────────────
            _registry.Register("reset firstrun", "Reset hasCompletedFirstRun=false so the prestige unlock notification can re-trigger",
                _ =>
                {
                    var save = SaveManager.Instance?.Current;
                    if (save == null) return "Error: SaveManager not ready.";
                    save.tutorial.hasCompletedFirstRun = false;
                    SaveManager.Instance.SaveLocal();
                    return "hasCompletedFirstRun reset to false and saved. Run 'reload', then 'tutorial skip <id>' + 'set prestige available' + prestige to re-test.";
                });

            // ── analytics / telemetry ────────────────────────────────────────
            _registry.Register("analytics status", "Show whether telemetry is collecting + the phase",
                _ =>
                {
                    var t = TelemetryService.Instance;
                    return t == null ? "Error: TelemetryService not found." : t.DevStatus();
                });

            _registry.Register("analytics fire", "Fire all 6 telemetry events with sample data and flush to UGS",
                _ =>
                {
                    var t = TelemetryService.Instance;
                    if (t == null) return "Error: TelemetryService not found.";

                    // Force-start with the live UGS sink so this works even if analytics.enabled is off.
                    // (If the flag already started collection, this is a no-op.)
                    t.DevForceStart();

                    t.RecordBuildingPlaced("dev_test_building");
                    t.RecordResearch("dev_test_research");
                    t.RecordTier(3);
                    t.RecordMegastructureStage(1);
                    // Sends prestige_completed AND an internal player_snapshot:
                    t.RecordPrestige(runCount: 1, netWorthBefore: 12345f, prestigeCurrencyEarned: 42,
                                     buildingCount: 7, highestTier: 3);
                    t.CaptureSnapshot();   // explicit snapshot in case the prestige one couldn't read ECS
                    t.Flush();             // push immediately instead of waiting for the batch interval

                    return "Fired: building_placed, research_completed, tier_reached, megastructure_stage, " +
                           "prestige_completed, player_snapshot. Flushed. Check Event Manager (dev env) shortly.";
                });

            // ── save / reload ─────────────────────────────────────────────────
            _registry.Register("save", "Force local save",
                _ =>
                {
                    SaveManager.Instance?.SaveLocal();
                    return "Saved.";
                });

            _registry.Register("clear save", "Delete local save file and reload scene",
                _ =>
                {
                    GridSaveService.Instance?.ClearGrid();
                    SaveWipe.WipeFilesAndPrefs(scheduleCloudWipe: false);
                    SaveManager.Instance?.ResetToFreshSave();
                    PersistentUpgradeService.Instance?.LoadFromSave(new System.Collections.Generic.List<string>());
                    AchievementService.Instance?.ResetInMemory();
                    SceneLoader.GoTo(SceneManager.GetActiveScene().name);
                    return "Save cleared. Reloading...";
                });

            _registry.Register("clear cloud save", "Delete cloud + local save and restart from tutorial (debug only)",
                _ =>
                {
                    var sm = SaveManager.Instance;
                    if (sm == null)
                    {
                        // SaveManager not ready yet (running before GameScene initializes).
                        // Wipe local files/prefs now and defer the cloud delete to next boot.
                        SaveWipe.WipeFilesAndPrefs(scheduleCloudWipe: true);
                        SceneLoader.GoTo("GameScene");
                        return "SaveManager not ready — local cleared, cloud wipe deferred to next GameScene load.";
                    }
                    GameLogger.Info("[DevConsole] clear cloud save — starting coroutine");
                    StartCoroutine(sm.DeleteCloudSave(success =>
                    {
                        GameLogger.Info($"[DevConsole] clear cloud save callback — success={success}  activeScene='{SceneManager.GetActiveScene().name}'");
                        if (!success)
                            AppendLog("Warning: cloud delete failed (offline?). Scheduling cloud wipe for next boot.", "log-entry--error");
                        GridSaveService.Instance?.ClearGrid();
                        // On an offline/failed cloud delete, schedule the wipe so the cloud key is
                        // removed on the next boot instead of silently leaving stale cloud data.
                        SaveWipe.WipeFilesAndPrefs(scheduleCloudWipe: !success);
                        sm.ResetToFreshSave();
                        PersistentUpgradeService.Instance?.LoadFromSave(new System.Collections.Generic.List<string>());
                        AchievementService.Instance?.ResetInMemory();
                        // Blur the input field before transitioning so UI Toolkit's keyboard-poll
                        // timer is cancelled before LoadSceneAsync runs (async path: field was
                        // re-focused by SubmitCommand after this coroutine started).
                        _inputField?.Blur();
                        GameLogger.Info("[DevConsole] clear cloud save — calling SceneLoader.GoTo");
                        SceneLoader.GoTo(SceneManager.GetActiveScene().name);
                        GameLogger.Info("[DevConsole] clear cloud save — SceneLoader.GoTo returned");
                    }));
                    return "Deleting cloud + local save. Reloading...";
                });

            _registry.Register("reload", "Reload the active scene",
                _ =>
                {
                    SceneLoader.GoTo(SceneManager.GetActiveScene().name);
                    return "Reloading...";
                });
        }
    }
}
#endif
