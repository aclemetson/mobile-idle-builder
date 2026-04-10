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
    public class ResearchService : MonoBehaviour
    {
        [SerializeField] private ResearchSO[] _allResearch;

        public static ResearchService Instance { get; private set; }

        /// <summary>Fired whenever a research is successfully purchased.</summary>
        public event Action<ResearchSO> OnResearchUnlocked;

        public IReadOnlyList<ResearchSO> AllResearch => _allResearch;

        private readonly HashSet<string> _unlockedIds = new();

        void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        void Start()
        {
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

            // Check prerequisites
            if (research.prerequisites != null)
            {
                foreach (var prereq in research.prerequisites)
                {
                    if (prereq != null && !IsUnlocked(prereq.id))
                        return false;
                }
            }

            // Check entropy
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

            Debug.Log($"[ResearchService] Purchased: {research.displayName}");
            OnResearchUnlocked?.Invoke(research);
        }

        // ── ECS helpers ──────────────────────────────────────────────────────

        private long GetCurrentEntropy()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return 0;
            var query = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerProgressData>());
            if (query.IsEmpty) return 0;
            return query.GetSingleton<PlayerProgressData>().BaseCurrency;
        }

        private void DeductEntropy(int amount)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            var em    = world.EntityManager;
            var query = em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
            if (query.IsEmpty) return;
            var progress = query.GetSingleton<PlayerProgressData>();
            progress.BaseCurrency = Math.Max(0, progress.BaseCurrency - amount);
            query.SetSingleton(progress);
        }
    }
}
