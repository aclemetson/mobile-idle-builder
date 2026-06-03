#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
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

        private const float ShakeThreshold    = 2.5f;   // g-force above which a peak is counted
        private const float ShakeWindow       = 1.5f;   // seconds the peaks must fall within
        private const int   ShakePeaksRequired = 3;     // number of threshold crossings to trigger

        private readonly Queue<float> _shakePeakTimes = new();
        private bool _wasAboveShakeThreshold;
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

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _consoleRoot = root.Q("dev-console-root");
            _logView     = root.Q<ScrollView>("dev-console-log");
            _inputField  = root.Q<TextField>("dev-console-input");

            root.Q<Button>("btn-close-console").clicked += () => SetVisible(false);
            root.Q<Button>("btn-submit-console").clicked += SubmitCommand;
            _inputField.RegisterCallback<KeyDownEvent>(OnInputKeyDown, TrickleDown.TrickleDown);

            root.focusable = true;
            root.RegisterCallback<KeyDownEvent>(OnRootKeyDown, TrickleDown.TrickleDown);

            SetVisible(false);
        }

        void Start()
        {
            if (Accelerometer.current != null)
                InputSystem.EnableDevice(Accelerometer.current);

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
            if (Accelerometer.current != null)
                InputSystem.DisableDevice(Accelerometer.current);
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

        private void DetectShake()
        {
            var accel = Accelerometer.current;
            if (accel == null) return;

            float magnitude = accel.acceleration.ReadValue().magnitude;
            bool isAbove = magnitude > ShakeThreshold;

            // Count rising edges (transitions from below to above threshold)
            if (isAbove && !_wasAboveShakeThreshold)
            {
                float now = Time.realtimeSinceStartup;
                _shakePeakTimes.Enqueue(now);
                while (_shakePeakTimes.Count > 0 && now - _shakePeakTimes.Peek() > ShakeWindow)
                    _shakePeakTimes.Dequeue();

                if (_shakePeakTimes.Count >= ShakePeaksRequired)
                {
                    _shakePeakTimes.Clear();
                    SetVisible(!_isVisible);
                }
            }

            _wasAboveShakeThreshold = isAbove;
        }

        // ── Visibility ────────────────────────────────────────────────────────

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
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

            _refocusFieldNextFrame = true;

            _inputField.Focus();
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
                    new LocalSaveService().Delete();
                    SceneLoader.GoTo(SceneManager.GetActiveScene().name);
                    return "Save cleared. Reloading...";
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
