using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages the research tree: tracks unlock state, checks affordability, and runs the
    /// active research timer. Starting a research deducts entropy up front and begins a real-time
    /// timer (one research at a time); the node unlocks when the timer elapses, on a later tick,
    /// or offline on the next load. A running timer can be skipped for crystals.
    /// </summary>
    public class ResearchService : SingletonMonoBehaviour<ResearchService>
    {
        private ResearchSO[] _allResearch;

        /// <summary>Fired when a research timer is started.</summary>
        public event Action<ResearchSO> OnResearchStarted;
        /// <summary>Fired whenever a research finishes (instantly, on timer, or via skip).</summary>
        public event Action<ResearchSO> OnResearchUnlocked;

        public IReadOnlyList<ResearchSO> AllResearch => _allResearch;

        private readonly HashSet<string> _unlockedIds = new();

        // Active timer (one at a time). Cached so Update() avoids per-frame save lookups/parsing.
        private ResearchSO _activeResearch;
        private DateTime    _activeCompleteUtc;

        private EntityManager _em;
        private EntityQuery _progressQuery;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;
            var db = Resources.Load<ResearchDatabaseSO>("ResearchDatabase");
            _allResearch = db != null ? db.allResearch : System.Array.Empty<ResearchSO>();
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _em = world.EntityManager;
                _progressQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            }

            var save = SaveManager.Instance?.Current;
            if (save?.unlockedResearch != null)
                foreach (var id in save.unlockedResearch) _unlockedIds.Add(id);

            // Restore an in-progress research timer; complete it immediately if it already elapsed
            // while the app was closed (offline completion, mirrors the speed-boost UtcNow pattern).
            RestoreActiveTimer(save);
            ProcessActiveTimer();
        }

        void Update()
        {
            if (_activeResearch == null) return;
            if (DateTime.UtcNow >= _activeCompleteUtc)
                CompleteResearch(_activeResearch);
        }

        // ── Query ────────────────────────────────────────────────────────────

        public bool IsUnlocked(string id) =>
            !string.IsNullOrEmpty(id) && _unlockedIds.Contains(id);

        /// <summary>
        /// Product of <see cref="ResearchSO.fieldCooldownMult"/> across every unlocked research node.
        /// Returns 1 when nothing relevant is unlocked. Used to shorten the field tap cooldown
        /// (combines multiplicatively with the permanent prestige reduction).
        /// </summary>
        public float GetFieldCooldownMultiplier()
        {
            if (_allResearch == null) return 1f;
            float mult = 1f;
            foreach (var r in _allResearch)
            {
                if (r == null || r.fieldCooldownMult >= 1f) continue;
                if (IsUnlocked(r.id)) mult *= r.fieldCooldownMult;
            }
            return mult;
        }

        /// <summary>True while a research timer is running (one research at a time).</summary>
        public bool HasActiveResearch => _activeResearch != null;

        /// <summary>The research currently in progress, or null.</summary>
        public ResearchSO ActiveResearch => _activeResearch;

        public string ActiveResearchId => _activeResearch?.id;

        /// <summary>Seconds left on the active timer (0 if none / already elapsed).</summary>
        public double ActiveRemainingSeconds
        {
            get
            {
                if (_activeResearch == null) return 0;
                double rem = (_activeCompleteUtc - DateTime.UtcNow).TotalSeconds;
                return rem > 0 ? rem : 0;
            }
        }

        /// <summary>Crystal cost to skip the active timer right now (0 if none).</summary>
        public long ActiveSkipCost =>
            _activeResearch == null ? 0 : PremiumShopCalculator.CalcResearchSkipCost(ActiveRemainingSeconds);

        /// <summary>
        /// Returns true if the player can afford this research and all prerequisites
        /// are already unlocked. (Does NOT consider the one-at-a-time gate — see <see cref="HasActiveResearch"/>.)
        /// </summary>
        public bool CanPurchase(ResearchSO research)
        {
            if (research == null) return false;
            if (IsUnlocked(research.id)) return false;

            if (research.prerequisites != null)
            {
                foreach (var prereq in research.prerequisites)
                {
                    if (prereq != null && !IsUnlocked(prereq.id))
                        return false;
                }
            }

            long currentEntropy = GetCurrentEntropy();
            return currentEntropy >= research.costBaseCurrency;
        }

        /// <summary>Effective research time after the Research Overdrive prestige bonus (capped at -50%).</summary>
        public int EffectiveDurationSeconds(ResearchSO research)
        {
            if (research == null) return 0;
            float reduction = Mathf.Min(0.5f,
                PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.ResearchSpeed) ?? 0f);
            return Mathf.Max(0, Mathf.RoundToInt(research.durationSeconds * (1f - reduction)));
        }

        // ── Start / complete ─────────────────────────────────────────────────

        /// <summary>
        /// Starts the research: deducts entropy up front and begins the timer. Research with a
        /// zero (or fully-reduced-to-zero) duration completes instantly. No-op if another research
        /// is already in progress.
        /// </summary>
        public void Purchase(ResearchSO research)
        {
            if (research == null || HasActiveResearch || !CanPurchase(research)) return;

            DeductEntropy(research.costBaseCurrency);
            AchievementService.Instance?.NotifyEntropySpent(research.costBaseCurrency);

            int effective = EffectiveDurationSeconds(research);
            if (effective <= 0)
            {
                CompleteResearch(research);
                return;
            }

            _activeResearch    = research;
            _activeCompleteUtc = DateTime.UtcNow.AddSeconds(effective);

            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.activeResearchId          = research.id;
                save.activeResearchCompleteUtc = _activeCompleteUtc.ToString("O");
                SaveManager.Instance.SaveLocal();
            }

            GameLogger.Info($"[ResearchService] Started: {research.displayName} ({effective}s)");
            OnResearchStarted?.Invoke(research);
        }

        /// <summary>
        /// Instantly finishes the active research by spending crystals scaled to the time remaining.
        /// Returns false if there is nothing to skip or the player cannot afford it.
        /// </summary>
        public bool SkipActive()
        {
            if (_activeResearch == null) return false;
            var save = SaveManager.Instance?.Current;
            if (save == null) return false;

            long cost = ActiveSkipCost;
            if (save.paidCurrency < cost) return false;

            save.paidCurrency -= cost;
            GameLogger.Info($"[ResearchService] Skipped {_activeResearch.displayName} for {cost}◆");
            CompleteResearch(_activeResearch); // clears the timer and saves
            return true;
        }

        /// <summary>
        /// Marks the research unlocked, clears the active timer, persists, and fires events.
        /// </summary>
        private void CompleteResearch(ResearchSO research)
        {
            _unlockedIds.Add(research.id);

            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.unlockedResearch ??= new();
                if (!save.unlockedResearch.Contains(research.id))
                    save.unlockedResearch.Add(research.id);

                if (save.activeResearchId == research.id)
                {
                    save.activeResearchId          = null;
                    save.activeResearchCompleteUtc = null;
                }
                SaveManager.Instance.SaveLocal();
            }

            _activeResearch    = null;
            _activeCompleteUtc = default;

            // Mark any JSON recipes gated behind this research as "known" (persists across prestige)
            if (RecipeDatabase.Instance != null)
            {
                foreach (var r in RecipeDatabase.Instance.Recipes)
                {
                    if (r.requires_research == research.id)
                        RecipeKnowledgeService.Instance?.MarkKnown(r.id);
                }
            }

            GameLogger.Info($"[ResearchService] Completed: {research.displayName}");
            OnResearchUnlocked?.Invoke(research);

            AchievementService.Instance?.NotifyResearchCompleted(research.id);
        }

        /// <summary>Loads the persisted active-timer state into the cached fields (no completion).</summary>
        private void RestoreActiveTimer(SaveData save)
        {
            _activeResearch    = null;
            _activeCompleteUtc = default;
            if (save == null || string.IsNullOrEmpty(save.activeResearchId)) return;

            var so = FindResearch(save.activeResearchId);
            if (so == null) return;
            if (!DateTime.TryParse(save.activeResearchCompleteUtc, null,
                    DateTimeStyles.RoundtripKind, out var done))
                return;

            _activeResearch    = so;
            _activeCompleteUtc = done;
        }

        /// <summary>Completes the active research if its timer has elapsed. Safe to call any time.</summary>
        public void ProcessActiveTimer()
        {
            if (_activeResearch == null) return;
            if (DateTime.UtcNow >= _activeCompleteUtc)
                CompleteResearch(_activeResearch);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Unlocks research by ID without checking prerequisites, cost, or timer.
        /// Updates the live in-memory set, SaveData, and marks gated recipes as known.
        /// Safe to call any time after ResearchService.Start() has run.
        /// </summary>
        public void ForceUnlock(string researchId)
        {
            if (string.IsNullOrEmpty(researchId) || _unlockedIds.Contains(researchId)) return;

            _unlockedIds.Add(researchId);

            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.unlockedResearch ??= new();
                if (!save.unlockedResearch.Contains(researchId))
                    save.unlockedResearch.Add(researchId);

                if (save.activeResearchId == researchId)
                {
                    save.activeResearchId          = null;
                    save.activeResearchCompleteUtc = null;
                }
            }

            if (_activeResearch != null && _activeResearch.id == researchId)
            {
                _activeResearch    = null;
                _activeCompleteUtc = default;
            }

            if (RecipeDatabase.Instance != null)
                foreach (var r in RecipeDatabase.Instance.Recipes)
                    if (r.requires_research == researchId)
                        RecipeKnowledgeService.Instance?.MarkKnown(r.id);

            var so = FindResearch(researchId);
            if (so != null)
                OnResearchUnlocked?.Invoke(so);

            GameLogger.Debug($"[TutorialSkip] Force-unlocked research: {researchId}");
        }
#endif

        // ── Prestige reset ───────────────────────────────────────────────────

        /// <summary>
        /// Clears all unlocked research and any in-progress timer for the new run.
        /// Called by PrestigeSaveWatcher / PrestigeSystem. The caller clears the matching
        /// SaveData fields before saving.
        /// </summary>
        public void ResetAll()
        {
            _unlockedIds.Clear();
            _activeResearch    = null;
            _activeCompleteUtc = default;
            GameLogger.Info("[ResearchService] Research reset for prestige.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private ResearchSO FindResearch(string id) =>
            _allResearch == null ? null : System.Array.Find(_allResearch, r => r != null && r.id == id);

        private long GetCurrentEntropy()
        {
            if (_progressQuery.IsEmpty) return 0;
            return _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }

        private void DeductEntropy(int amount)
        {
            if (_progressQuery.IsEmpty) return;
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            long actual = Math.Min(amount, progress.BaseCurrency);
            progress.BaseCurrency      -= actual;
            progress.TotalEntropySpent += actual;
            _progressQuery.SetSingleton(progress);
        }
    }
}
