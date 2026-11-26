using System;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Runs the world delivery tasks (the Chemistry gate, 2026-11-19). Each frame it drains the
    /// <see cref="DemonDelivery"/> log that <see cref="EntropySinkSystem"/> fills and credits the current stage
    /// (<see cref="WorldTasks"/> holds the rules and keeps the state in SaveData). When the last stage is done it
    /// unlocks the world through <see cref="WorldService.UnlockWorld"/> (cost 0) and plays its intro. Offline
    /// deliveries are credited by <see cref="OfflineCollectionService"/>; this service notices the result on load.
    /// <para>
    /// Self-bootstrapped like <see cref="WorldService"/> (no scene placement, nothing serialized). Persists across
    /// scenes; ECS access is lazy because it is created before GameScene's ECS world.
    /// </para>
    /// </summary>
    public class WorldTaskService : SingletonMonoBehaviour<WorldTaskService>
    {
        protected override bool PersistAcrossScenes => true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null) return;
            new GameObject(nameof(WorldTaskService)).AddComponent<WorldTaskService>();
        }

        /// <summary>Fired when progress changes (at most a few times a second) or a stage completes.</summary>
        public event Action OnChanged;
        /// <summary>Fired with each stage as it completes.</summary>
        public event Action<WorldTaskStage> OnStageCompleted;

        const float NotifyInterval = 0.5f;

        EntityQuery _logQuery;
        bool _ecsReady;
        bool _migrated;
        bool _dirty;
        float _notifyTimer;
        int _seenRevision;

        /// <summary>The tasks in focus: the unfinished set delivered in the live world (else the Chemistry gate).</summary>
        public WorldTaskSO Data => WorldTasks.Focus(SaveManager.Instance?.Current, WorldGate.ActiveWorldId());
        public bool IsComplete => WorldTasks.IsComplete(SaveManager.Instance?.Current, Data);
        public WorldTaskStage CurrentStage => WorldTasks.CurrentStage(SaveManager.Instance?.Current, Data);
        public int CompletedStages => WorldTasks.CompletedStages(SaveManager.Instance?.Current, Data);
        public int StageCount => WorldTasks.StageCount(Data);

        void Update()
        {
            if (!Ready(out var save)) return;

            if (!_migrated)
            {
                WorldTasks.Migrate(save, Data, SaveManager.Instance.IsNewGame);
                _migrated = true;
                _dirty = true;
            }

            DrainDeliveries(save);
            AnnounceBonusCredits();
            UnlockWorldIfComplete(save);

            _notifyTimer -= Time.unscaledDeltaTime;
            if (_dirty && _notifyTimer <= 0f)
            {
                _dirty = false;
                _notifyTimer = NotifyInterval;
                OnChanged?.Invoke();
            }
        }

        bool Ready(out SaveData save)
        {
            save = SaveManager.Instance?.Current;
            if (save == null || Data == null) return false;
            var bridge = ECSLoadBridge.Instance;
            return bridge != null && bridge.IsLoaded && bridge.SaveApplied;
        }

        bool EnsureEcs()
        {
            if (_ecsReady) return true;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            _logQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<DemonDeliveryLog>(),
                                                              ComponentType.ReadWrite<DemonDelivery>());
            _ecsReady = true;
            return true;
        }

        void DrainDeliveries(SaveData save)
        {
            if (!EnsureEcs() || _logQuery.IsEmptyIgnoreFilter) return;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var buffer = em.GetBuffer<DemonDelivery>(_logQuery.GetSingletonEntity());
            if (buffer.Length == 0) return;

            // Every unfinished task set delivered in this world counts what reaches its Demon (Physics feeds the
            // Chemistry gate, Biology feeds Metallurgy's, Metallurgy feeds Engineering's).
            string here = WorldGate.ActiveWorldId();
            foreach (var tasks in WorldTasks.All)
            {
                if (tasks == null || !WorldTasks.ShownIn(tasks, here) || WorldTasks.IsComplete(save, tasks)) continue;
                for (int i = 0; i < buffer.Length; i++)
                {
                    int counted = WorldTasks.Credit(save, tasks, buffer[i].ItemID, buffer[i].Quantity, out var finished);
                    if (counted > 0) _dirty = true;
                    if (finished != null) StageFinished(finished);
                }
            }
            buffer.Clear();
        }

        /// <summary>
        /// Picks up credits made off the live path (offline return, Time Warp, Double Offline, Entropy Surge): marks
        /// the UI dirty and announces each stage they finished, exactly as a live completion is announced.
        /// </summary>
        void AnnounceBonusCredits()
        {
            if (_seenRevision != WorldTasks.Revision) { _seenRevision = WorldTasks.Revision; _dirty = true; }
            if (WorldTasks.PendingAnnouncements.Count == 0) return;
            var stages = WorldTasks.PendingAnnouncements.ToArray();
            WorldTasks.PendingAnnouncements.Clear();
            foreach (var stage in stages) StageFinished(stage);
        }

        void StageFinished(WorldTaskStage stage)
        {
            GameLogger.Info($"[WorldTasks] Stage complete: {stage.displayName} ({CompletedStages}/{StageCount})");
            SaveManager.Instance?.SaveLocal();
            _dirty = true;
            OnStageCompleted?.Invoke(stage);
            PlayDialogue(stage.dialogueId);
        }

        void UnlockWorldIfComplete(SaveData save)
        {
            foreach (var tasks in WorldTasks.All)
            {
                if (tasks == null || !WorldTasks.IsComplete(save, tasks)) continue;
                if (save.unlockedWorlds != null && save.unlockedWorlds.Contains(tasks.worldId)) continue;
                var ws = WorldService.Instance;
                if (ws == null || !ws.UnlockWorld(tasks.worldId)) continue;
                _dirty = true;
                FindAnyObjectByType<WorldsSubController>()?.PlayWorldIntro(tasks.worldId);
            }
        }

        static void PlayDialogue(string dialogueId)
        {
            if (string.IsNullOrEmpty(dialogueId)) return;
            var db = Resources.Load<DialogueDatabaseSO>("DialogueDatabase");
            var dialogue = db != null ? db.Get(dialogueId) : null;
            var controller = FindAnyObjectByType<DialogueController>();
            if (dialogue != null && controller != null) controller.PlayDialogue(dialogue);
        }

        // ── Dev console ───────────────────────────────────────────────────────

        /// <summary>Dev: credits <paramref name="quantity"/> of an item as if the Demon had received it.</summary>
        public int DevCredit(int itemId, long quantity)
        {
            var save = SaveManager.Instance?.Current;
            int counted = WorldTasks.Credit(save, Data, itemId, quantity, out var finished);
            if (finished != null) StageFinished(finished);
            _dirty = true;
            return counted;
        }

        /// <summary>Dev: jumps to <paramref name="completedStages"/> finished stages with no progress on the next.</summary>
        public void DevSetStage(int completedStages)
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;
            WorldTasks.SetStage(save, Data, completedStages);
            SaveManager.Instance.SaveLocal();
            _dirty = true;
        }
    }
}
