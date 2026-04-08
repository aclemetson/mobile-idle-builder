using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton service that evaluates achievement triggers, tracks per-achievement
    /// progress, unlocks cosmetics on completion, stubs platform SDK calls, and
    /// keeps SaveManager in sync.
    ///
    /// Call the Notify* methods from gameplay code whenever the relevant event occurs.
    /// </summary>
    public class AchievementService : MonoBehaviour
    {
        [SerializeField] AchievementDatabase database;
        [SerializeField] HUDController       hudController;

        public static AchievementService Instance { get; private set; }

        /// <summary>Fired whenever a new achievement is completed.</summary>
        public event Action<AchievementSO> OnAchievementUnlocked;

        // Runtime state — rebuilt from save on Start
        readonly Dictionary<string, int>  _progress  = new();
        readonly HashSet<string>          _completed = new();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start() => LoadFromSave();

        // ── Load / flush ──────────────────────────────────────────────────────

        void LoadFromSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            _completed.Clear();
            _progress.Clear();

            foreach (var id in save.achievements)
                _completed.Add(id);

            foreach (var entry in save.achievementProgress)
                _progress[entry.id] = entry.count;
        }

        void FlushToSave()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;

            save.achievements        = new List<string>(_completed);
            save.achievementProgress = _progress
                .Select(kvp => new AchievementProgressEntry { id = kvp.Key, count = kvp.Value })
                .ToList();
        }

        // ── Public trigger API ────────────────────────────────────────────────

        /// <summary>Call when the player crafts an item (manual or automated output).</summary>
        public void NotifyCraft(string itemId, int quantity = 1) =>
            Evaluate(AchievementTrigger.CraftItem, itemId, delta: quantity);

        /// <summary>Call when the player places a building.</summary>
        public void NotifyBuildingPlaced(string buildingType) =>
            Evaluate(AchievementTrigger.PlaceBuilding, buildingType, delta: 1);

        /// <summary>Call when the player completes a research project.</summary>
        public void NotifyResearchCompleted(string researchId) =>
            Evaluate(AchievementTrigger.CompleteResearch, researchId, delta: 1);

        /// <summary>Call when the player reaches a new tier (pass the absolute tier number).</summary>
        public void NotifyTierReached(int tier) =>
            Evaluate(AchievementTrigger.ReachTier, tier.ToString(), delta: tier, absolute: true);

        /// <summary>Call when the player completes a prestige.</summary>
        public void NotifyPrestige() =>
            Evaluate(AchievementTrigger.Prestige, targetId: "", delta: 1);

        /// <summary>Call when the player wins a PVP match.</summary>
        public void NotifyPVPWin() =>
            Evaluate(AchievementTrigger.WinPVP, targetId: "", delta: 1);

        /// <summary>Call when the player unlocks a codex entry; pass the running total.</summary>
        public void NotifyCodexUnlocked(int totalCount) =>
            Evaluate(AchievementTrigger.UnlockCodex, targetId: "", delta: totalCount, absolute: true);

        // ── Evaluation ────────────────────────────────────────────────────────

        /// <param name="absolute">
        /// When true, <paramref name="delta"/> replaces the stored count rather than adding to it
        /// (used for tier and codex totals which are inherently monotonically increasing values).
        /// </param>
        void Evaluate(AchievementTrigger trigger, string targetId, int delta, bool absolute = false)
        {
            if (database == null) return;

            bool dirty = false;
            foreach (var achievement in database.All)
            {
                if (achievement == null)                             continue;
                if (_completed.Contains(achievement.id))            continue;
                if (achievement.triggerType != trigger)             continue;
                if (!string.IsNullOrEmpty(achievement.triggerTargetId) &&
                    achievement.triggerTargetId != targetId)        continue;

                _progress.TryGetValue(achievement.id, out int current);
                int updated = absolute ? Mathf.Max(current, delta) : current + delta;
                _progress[achievement.id] = updated;
                dirty = true;

                if (updated >= achievement.triggerQuantity)
                    Complete(achievement);
            }

            if (dirty) FlushToSave();
        }

        void Complete(AchievementSO achievement)
        {
            _completed.Add(achievement.id);
            _progress.Remove(achievement.id);

            UnlockCosmetics(achievement);
            ReportToPlatform(achievement);

            hudController?.ShowNotification("🏆", $"Achievement unlocked: {achievement.displayName}");
            OnAchievementUnlocked?.Invoke(achievement);

            Debug.Log($"[Achievements] Unlocked: {achievement.id}");
        }

        static void UnlockCosmetics(AchievementSO achievement)
        {
            if (achievement.rewards == null) return;
            foreach (var cosmetic in achievement.rewards)
            {
                if (cosmetic == null) continue;
                // TODO: notify CosmeticService to mark this cosmetic available
                Debug.Log($"[Achievements] Cosmetic unlocked: {cosmetic.id} ({cosmetic.type})");
            }
        }

        static void ReportToPlatform(AchievementSO achievement)
        {
            if (string.IsNullOrEmpty(achievement.platformAchievementId)) return;
            // TODO: Google Play Games: PlayGamesPlatform.Instance.ReportProgress(...)
            // TODO: Apple Game Center: Social.ReportProgress(...)
            Debug.Log($"[Achievements] Platform report stub: {achievement.platformAchievementId}");
        }

        // ── Query API (for panel) ─────────────────────────────────────────────

        public IReadOnlyList<AchievementSO> GetAll()            => database?.All;
        public bool IsCompleted(string id)                      => _completed.Contains(id);
        public int  GetProgress(string id)                      => _progress.TryGetValue(id, out var c) ? c : 0;
        public int  CompletedCount                              => _completed.Count;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // ── Dev console helpers ───────────────────────────────────────────────

        public void ForceComplete(string id)
        {
            var achievement = database?.Get(id);
            if (achievement == null || _completed.Contains(id)) return;
            Complete(achievement);
            FlushToSave();
        }

        public void ForceCompleteAll()
        {
            if (database == null) return;
            foreach (var a in database.All)
                if (a != null && !_completed.Contains(a.id))
                    Complete(a);
            FlushToSave();
        }
#endif
    }
}
