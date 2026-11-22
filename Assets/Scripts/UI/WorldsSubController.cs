using System.Collections.Generic;
using System.Text;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Populates and manages the Worlds panel (worlds-panel): one row per World (track) with its
    /// lock/unlock state, prerequisite research, and a Travel/Unlock action. Worlds are the layer
    /// ABOVE Sites — the economic gate is a prerequisite research node (the world's own unlock_cost
    /// is 0), so a locked row shows what research is still required. On first unlock, plays the
    /// world's one-shot introduction dialogue. Sits as a component alongside HUDController.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class WorldsSubController : MonoBehaviour
    {
        private VisualElement _panel;
        private Label         _entropyLabel;
        private ScrollView    _list;
        private Button        _navButton;
        private HUDController _hud;

        private EntityManager _em;
        private EntityQuery   _progressQuery;
        private bool          _ecsReady;

        private DialogueDatabaseSO _dialogueDb;
        private ResearchDatabaseSO _researchDb;

        // Two-tap unlock confirm: the row whose Unlock button is armed, and the timer that disarms it.
        private int _pendingUnlockIndex = -1;
        private IVisualElementScheduledItem _pendingReset;
        private const long ConfirmWindowMs = 3000;

        /// <summary>What action a world row should present.</summary>
        public enum WorldRowAction { Active, Travel, Unlock, Locked }

        /// <summary>
        /// Pure classification of a world row's action from its state. A world unlocks only when its
        /// prerequisite research is met AND it is affordable (world unlock_cost is 0 today, so
        /// affordability is normally trivially true). Testable without ECS/UI.
        /// </summary>
        public static WorldRowAction ClassifyRow(bool isActive, bool isUnlocked, bool prereqsMet, bool canAfford)
        {
            if (isActive)               return WorldRowAction.Active;
            if (isUnlocked)             return WorldRowAction.Travel;
            if (prereqsMet && canAfford) return WorldRowAction.Unlock;
            return WorldRowAction.Locked;
        }

        /// <summary>The one-shot intro dialogue id for a world (e.g. "world_chemistry" -> "intro_world_chemistry").</summary>
        public static string IntroDialogueId(string worldId) =>
            string.IsNullOrEmpty(worldId) ? null : $"intro_{worldId}";

        /// <summary>Pure rule for a world's one-shot intro: fire once, the first time it is unlocked.</summary>
        public static bool ShouldShowWorldIntro(string worldId, bool alreadySeen) =>
            !alreadySeen && !string.IsNullOrEmpty(worldId);

        public void Init(VisualElement root, HUDController hud)
        {
            _hud          = hud;
            _panel        = root.Q("worlds-panel");
            _entropyLabel = root.Q<Label>("worlds-entropy");
            _list         = root.Q<ScrollView>("worlds-list");
            _navButton    = root.Q<Button>("btn-worlds");
            _taskStrip    = root.Q("task-status");
            _taskLabel    = root.Q<Label>("task-status-label");
            _taskStrip?.RegisterCallback<ClickEvent>(_ => _hud?.OpenWorldsPanel());
            if (_list == null)
                GameLogger.Error("[WorldsSubController] 'worlds-list' not found in GameHUD.uxml.");
        }

        // ── Chemistry task strip + live progress ─────────────────────────────

        private VisualElement _taskStrip;
        private Label         _taskLabel;
        private WorldTaskService _taskService;
        private float _taskStripTimer;
        private const float TaskStripInterval = 0.5f;

        void Update()
        {
            if (_taskService == null && WorldTaskService.Instance != null)
            {
                _taskService = WorldTaskService.Instance;
                _taskService.OnChanged        += OnTasksChanged;
                _taskService.OnStageCompleted += OnTaskStageCompleted;
            }

            _taskStripTimer -= Time.unscaledDeltaTime;
            if (_taskStripTimer > 0f) return;
            _taskStripTimer = TaskStripInterval;
            RefreshTaskStrip();
        }

        void OnDestroy()
        {
            if (_taskService == null) return;
            _taskService.OnChanged        -= OnTasksChanged;
            _taskService.OnStageCompleted -= OnTaskStageCompleted;
        }

        private void OnTasksChanged()
        {
            if (_panel != null && !_panel.ClassListContains("hidden")) Refresh();
        }

        private void OnTaskStageCompleted(WorldTaskStage stage)
        {
            var tasks = WorldTasks.Data;
            var save  = SaveManager.Instance?.Current;
            string text = WorldTasks.IsComplete(save, tasks)
                ? "Chemistry is open! Travel there from Worlds."
                : $"{stage.displayName} delivered. Next: {WorldTasks.CurrentStage(save, tasks)?.displayName}.";
            _hud?.ShowNotification("⚗", text);
            OnTasksChanged();
        }

        /// <summary>
        /// The strip under the top bar: "⚗ Chemistry  Stage 2/5: Light Elements  43%". Hidden in the tutorial, once
        /// the tasks are done, without task data, or in any world other than the one they are delivered in.
        /// </summary>
        private void RefreshTaskStrip()
        {
            if (_taskStrip == null) return;
            var tasks = WorldTasks.Data;
            var save  = SaveManager.Instance?.Current;
            var stage = WorldTasks.CurrentStage(save, tasks);
            bool loaded = ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded;
            bool show = loaded && stage != null && save != null && !(save.tutorial?.isActive ?? false)
                        && WorldTasks.ShownIn(tasks, WorldGate.ActiveWorldId());   // only where they are delivered
            HUDController.SetElementVisible(_taskStrip, show);
            if (!show || _taskLabel == null) return;
            int pct = Mathf.FloorToInt(WorldTasks.StageFraction(save, stage) * 100f);
            _taskLabel.text = $"⚗ {WorldGate.DisplayName(tasks.worldId)}  " +
                              $"{TaskStageTitle(WorldTasks.CompletedStages(save, tasks), WorldTasks.StageCount(tasks), stage.displayName)}  {pct}%";
        }

        public void SetECSContext(EntityManager em)
        {
            _em            = em;
            _progressQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
            _ecsReady      = true;
        }

        /// <summary>Rebuilds the panel contents with current world/entropy state.</summary>
        public void Refresh()
        {
            if (_list == null) return;
            _list.Clear();

            long entropy = GetCurrentEntropy();
            if (_entropyLabel != null)
                _entropyLabel.text = $"Entropy: {entropy:N0}";

            var svc = WorldService.Instance;
            if (svc?.AllWorlds == null || svc.AllWorlds.Count == 0)
            {
                _list.Add(new Label("Worlds not available."));
                return;
            }

            var save = SaveManager.Instance?.Current;
            int activeIndex = svc.ActiveIndex;
            for (int i = 0; i < svc.AllWorlds.Count; i++)
            {
                var world = svc.AllWorlds[i];
                if (world == null) continue;

                bool isActive   = i == activeIndex;
                bool isUnlocked = svc.IsUnlocked(i);
                bool prereqsMet = WorldService.PrereqsMet(save, world);
                bool canAfford  = entropy >= world.unlockCost;
                var  action     = ClassifyRow(isActive, isUnlocked, prereqsMet, canAfford);

                _list.Add(BuildRow(i, world, action));
            }
        }

        private VisualElement BuildRow(int index, WorldSO world, WorldRowAction action)
        {
            var row = new VisualElement();
            row.AddToClassList("upgrade-row");
            if (action == WorldRowAction.Locked) row.AddToClassList("upgrade-row--locked");
            else if (action == WorldRowAction.Active) row.AddToClassList("upgrade-row--maxed");

            // Header: name + status badge
            var header = new VisualElement();
            header.AddToClassList("upgrade-row-header");

            var nameLabel = new Label(string.IsNullOrEmpty(world.displayName) ? world.id : world.displayName);
            nameLabel.AddToClassList("upgrade-row-name");

            string badge = action switch
            {
                WorldRowAction.Active => "Active",
                WorldRowAction.Travel => "Unlocked",
                _                     => "Locked",
            };
            var badgeLabel = new Label(badge);
            badgeLabel.AddToClassList("upgrade-row-level");

            header.Add(nameLabel);
            header.Add(badgeLabel);
            row.Add(header);

            // A task-gated world (Chemistry) shows its delivery stage instead of research.
            if (action == WorldRowAction.Locked && world.unlockByTasks)
            {
                row.Add(BuildTaskProgress(world));
            }
            // Locked rows explain what research is still required.
            else if (action == WorldRowAction.Locked)
            {
                string need = DescribeMissingPrereqs(world);
                if (!string.IsNullOrEmpty(need))
                {
                    var hint = new Label($"Requires: {need}");
                    hint.AddToClassList("upgrade-row-desc");
                    row.Add(hint);
                }
            }

            // Footer: unlock cost (if any) + action button
            var footer = new VisualElement();
            footer.AddToClassList("upgrade-row-footer");

            if ((action == WorldRowAction.Unlock || action == WorldRowAction.Locked) && world.unlockCost > 0)
            {
                var costLabel = new Label($"◈ {world.unlockCost:N0}");
                costLabel.AddToClassList("upgrade-row-cost");
                if (action == WorldRowAction.Locked) costLabel.AddToClassList("upgrade-row-cost--unaffordable");
                footer.Add(costLabel);
            }

            switch (action)
            {
                case WorldRowAction.Active:
                {
                    var badgeBtn = new Button { text = "Current" };
                    badgeBtn.AddToClassList("craft-btn");
                    badgeBtn.SetEnabled(false);
                    footer.Add(badgeBtn);
                    break;
                }
                case WorldRowAction.Travel:
                {
                    var travelBtn = new Button { text = "Travel" };
                    travelBtn.AddToClassList("craft-btn");
                    int captured = index;
                    travelBtn.clicked += () => OnTravelPressed(captured);
                    footer.Add(travelBtn);
                    break;
                }
                case WorldRowAction.Unlock:
                {
                    bool armed = index == _pendingUnlockIndex;
                    var unlockBtn = new Button { text = armed ? "Confirm?" : "Unlock" };
                    unlockBtn.AddToClassList("craft-btn");
                    int captured = index;
                    unlockBtn.clicked += () => OnUnlockPressed(captured);
                    footer.Add(unlockBtn);
                    break;
                }
                case WorldRowAction.Locked:
                {
                    var lockedBtn = new Button { text = "Unlock" };
                    lockedBtn.AddToClassList("craft-btn");
                    lockedBtn.SetEnabled(false);
                    footer.Add(lockedBtn);
                    break;
                }
            }

            row.Add(footer);
            return row;
        }

        /// <summary>
        /// The locked row's task block: "Stage 2/5: Light Elements", what to do, one progress bar per item
        /// ("Lithium 120 / 300"). Items reaching Maxwell's Demon count while the stage is active (WorldTasks).
        /// </summary>
        private VisualElement BuildTaskProgress(WorldSO world)
        {
            var box = new VisualElement();
            var tasks = WorldTasks.Data;
            var save  = SaveManager.Instance?.Current;
            var stage = tasks != null && tasks.worldId == world.id ? WorldTasks.CurrentStage(save, tasks) : null;
            if (stage == null)
            {
                var none = new Label("Opens when its delivery tasks are done.");
                none.AddToClassList("upgrade-row-desc");
                box.Add(none);
                return box;
            }

            var title = new Label(TaskStageTitle(WorldTasks.CompletedStages(save, tasks), WorldTasks.StageCount(tasks), stage.displayName));
            title.AddToClassList("upgrade-row-desc");
            box.Add(title);
            var how = new Label("Send these to Maxwell's Demon. They still pay entropy.");
            how.AddToClassList("upgrade-row-desc");
            box.Add(how);

            var seen = new HashSet<int>();
            foreach (var item in stage.items ?? new ItemSO[0])
            {
                if (item == null || !seen.Add(item.itemId)) continue;
                int need = WorldTasks.Required(stage, item.itemId);
                int have = Mathf.Min(need, WorldTasks.Delivered(save, item.itemId));
                box.Add(ProgressBar($"{(string.IsNullOrEmpty(item.displayName) ? item.id : item.displayName)}  {have:N0} / {need:N0}",
                                    need <= 0 ? 1f : (float)have / need));
            }
            return box;
        }

        /// <summary>"Stage 2/5: Light Elements" for the stage after <paramref name="completed"/> finished ones.</summary>
        public static string TaskStageTitle(int completed, int count, string stageName) =>
            $"Stage {Mathf.Min(completed + 1, count)}/{count}: {stageName}";

        private static VisualElement ProgressBar(string text, float fraction)
        {
            var bar = new VisualElement();
            bar.AddToClassList("progress-bar");
            var label = new Label(text);
            label.AddToClassList("progress-bar__label");
            var track = new VisualElement();
            track.AddToClassList("progress-bar__track");
            var fill = new VisualElement();
            fill.AddToClassList("progress-bar__fill");
            if (fraction >= 1f) fill.AddToClassList("progress-bar__fill--success");
            fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            track.Add(fill);
            bar.Add(label);
            bar.Add(track);
            return bar;
        }

        /// <summary>Human-readable list of the world's still-unmet prerequisite research (display names).</summary>
        private string DescribeMissingPrereqs(WorldSO world)
        {
            if (world?.prereqUnlockIds == null || world.prereqUnlockIds.Count == 0) return null;
            var save = SaveManager.Instance?.Current;
            var sb = new StringBuilder();
            foreach (var id in world.prereqUnlockIds)
            {
                if (string.IsNullOrEmpty(id)) continue;
                bool have = save?.unlockedResearch != null && save.unlockedResearch.Contains(id);
                if (have) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(ResearchDisplayName(id));
            }
            return sb.ToString();
        }

        private string ResearchDisplayName(string id)
        {
            _researchDb ??= Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            if (_researchDb?.allResearch != null)
                foreach (var r in _researchDb.allResearch)
                    if (r != null && r.id == id)
                        return string.IsNullOrEmpty(r.displayName) ? id : r.displayName;
            return id;
        }

        // ── Actions ──────────────────────────────────────────────────────────

        private void OnTravelPressed(int index)
        {
            var svc = WorldService.Instance;
            if (svc == null) return;

            // Cancel any active placement/conveyor/deconstruct overlay before swapping grids.
            _hud?.CancelActiveModes();

            if (svc.SwitchTo(index))
            {
                ClearPending();
                _hud?.ShowNotification("◈", $"Traveled to {WorldName(svc, index)}.");
                Refresh();
            }
        }

        private void OnUnlockPressed(int index)
        {
            var svc = WorldService.Instance;
            if (svc == null) return;

            // First tap arms the confirm; second tap within the window commits the unlock.
            if (_pendingUnlockIndex != index)
            {
                ArmConfirm(index);
                Refresh();
                return;
            }

            ClearPending();
            var world = svc.GetWorld(index);
            if (world != null && svc.UnlockWorld(world.id))
            {
                _hud?.ShowNotification("◈", $"Unlocked {WorldName(svc, index)}.");
                PlayWorldIntro(world.id);
            }
            Refresh();
        }

        // ── One-shot world introduction ──────────────────────────────────────

        /// <summary>Plays a world's intro dialogue the first time it is unlocked, then records it as seen.</summary>
        public void PlayWorldIntro(string worldId)
        {
            var save = SaveManager.Instance?.Current;
            if (save != null) save.worldsIntroSeen ??= new List<string>();
            bool seen = save?.worldsIntroSeen != null && save.worldsIntroSeen.Contains(worldId);
            if (!ShouldShowWorldIntro(worldId, seen)) return;

            if (save != null)
            {
                save.worldsIntroSeen.Add(worldId);
                SaveManager.Instance.SaveLocal();
            }

            _dialogueDb ??= Resources.Load<DialogueDatabaseSO>("DialogueDatabase");
            var dialogue   = _dialogueDb != null ? _dialogueDb.Get(IntroDialogueId(worldId)) : null;
            var controller = FindAnyObjectByType<DialogueController>();

            if (dialogue != null && controller != null)
                controller.PlayDialogue(dialogue);
        }

        private void ArmConfirm(int index)
        {
            _pendingUnlockIndex = index;
            _pendingReset?.Pause();
            if (_panel != null)
                _pendingReset = _panel.schedule.Execute(() =>
                {
                    if (_pendingUnlockIndex < 0) return;
                    ClearPending();
                    Refresh();
                }).StartingIn(ConfirmWindowMs);
        }

        private void ClearPending()
        {
            _pendingUnlockIndex = -1;
            _pendingReset?.Pause();
            _pendingReset = null;
        }

        private static string WorldName(WorldService svc, int index)
        {
            var w = svc.GetWorld(index);
            return w == null ? $"world {index}" : (string.IsNullOrEmpty(w.displayName) ? w.id : w.displayName);
        }

        private long GetCurrentEntropy()
        {
            if (!_ecsReady || _progressQuery.IsEmpty) return 0L;
            return _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }
    }
}
