using System;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages the research tree: tracks unlock state, checks affordability,
    /// and processes purchases by deducting entropy and updating SaveData.
    /// </summary>
    public class ResearchService : SingletonMonoBehaviour<ResearchService>
    {
        private ResearchSO[] _allResearch;

        /// <summary>Fired whenever a research is successfully purchased.</summary>
        public event Action<ResearchSO> OnResearchUnlocked;

        public IReadOnlyList<ResearchSO> AllResearch => _allResearch;

        private readonly HashSet<string> _unlockedIds = new();

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

            var saved = SaveManager.Instance?.Current?.unlockedResearch;
            if (saved != null)
                foreach (var id in saved) _unlockedIds.Add(id);
        }

        // ── Query ────────────────────────────────────────────────────────────

        public bool IsUnlocked(string id) =>
            !string.IsNullOrEmpty(id) && _unlockedIds.Contains(id);

        /// <summary>
        /// Returns true if the player can afford this research and all prerequisites
        /// are already unlocked.
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

        // ── Purchase ─────────────────────────────────────────────────────────

        /// <summary>
        /// Purchases the research: deducts entropy via ECS, records in SaveData,
        /// then fires OnResearchUnlocked.
        /// </summary>
        public void Purchase(ResearchSO research)
        {
            if (research == null || !CanPurchase(research)) return;

            DeductEntropy(research.costBaseCurrency);
            _unlockedIds.Add(research.id);

            // Sync to persistent save
            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.unlockedResearch ??= new();
                if (!save.unlockedResearch.Contains(research.id))
                    save.unlockedResearch.Add(research.id);
                SaveManager.Instance.SaveLocal();
            }

            // Mark any JSON recipes gated behind this research as "known" (persists across prestige)
            if (RecipeDatabase.Instance != null)
            {
                foreach (var r in RecipeDatabase.Instance.Recipes)
                {
                    if (r.requires_research == research.id)
                        RecipeKnowledgeService.Instance?.MarkKnown(r.id);
                }
            }

            GameLogger.Info($"[ResearchService] Purchased: {research.displayName}");
            OnResearchUnlocked?.Invoke(research);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Unlocks research by ID without checking prerequisites or cost.
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
            }

            if (RecipeDatabase.Instance != null)
                foreach (var r in RecipeDatabase.Instance.Recipes)
                    if (r.requires_research == researchId)
                        RecipeKnowledgeService.Instance?.MarkKnown(r.id);

            var so = System.Array.Find(_allResearch, r => r.id == researchId);
            if (so != null)
                OnResearchUnlocked?.Invoke(so);

            GameLogger.Debug($"[TutorialSkip] Force-unlocked research: {researchId}");
        }
#endif

        // ── ECS helpers ──────────────────────────────────────────────────────

        private long GetCurrentEntropy()
        {
            if (_progressQuery.IsEmpty) return 0;
            return _progressQuery.GetSingleton<PlayerProgressData>().BaseCurrency;
        }

        private void DeductEntropy(int amount)
        {
            if (_progressQuery.IsEmpty) return;
            var progress = _progressQuery.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency = Math.Max(0, progress.BaseCurrency - amount);
            _progressQuery.SetSingleton(progress);
        }
    }
}
