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

        private EntityQuery _effectsQuery;
        private bool        _effectsDirty;
        private bool        _effectsReady;

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

            // Overlay any cached remote balance overrides onto the loaded research (no-op when none published).
            GameDataOverrides.ApplyResearch(_allResearch);
        }

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null)
            {
                _em = world.EntityManager;
                _progressQuery = _em.CreateEntityQuery(ComponentType.ReadWrite<PlayerProgressData>());
                _effectsQuery  = _em.CreateEntityQuery(ComponentType.ReadWrite<ResearchEffects>());
                _effectsReady  = true;
            }

            var save = SaveManager.Instance?.Current;
            long refund = MigrateMergedResearch(save);
            if (refund > 0) RefundRetired(save, refund);
            if (save?.unlockedResearch != null)
                foreach (var id in save.unlockedResearch) _unlockedIds.Add(id);

            // Backfill: a recipe whose research was finished before the recipe was gated on it
            // (helium..iron in recipes.json had no requires_research) was never marked known and
            // stayed out of the Recipes panel. MarkKnown is idempotent, so this is cheap on load.
            foreach (var id in _unlockedIds) MarkGatedRecipesKnown(id);

            // Restore an in-progress research timer; complete it immediately if it already elapsed
            // while the app was closed (offline completion, mirrors the speed-boost UtcNow pattern).
            RestoreActiveTimer(save);
            ProcessActiveTimer();

            _effectsDirty = true;
        }

        void Update()
        {
            TryApplyEffectsToECS();

            if (_activeResearch == null) return;
            if (GameClock.UtcNow >= _activeCompleteUtc)
                CompleteResearch(_activeResearch);
        }

        // ── Query ────────────────────────────────────────────────────────────

        public bool IsUnlocked(string id) =>
            !string.IsNullOrEmpty(id) && _unlockedIds.Contains(id);

        /// <summary>
        /// False while <paramref name="research"/> needs a world that isn't unlocked yet (WorldGate): Physics past
        /// silicon and the Chemistry tree wait for the Chemistry tasks.
        /// </summary>
        public bool WorldOpen(ResearchSO research) =>
            WorldGate.ResearchOpen(research, SaveManager.Instance?.Current?.unlockedWorlds);

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

        /// <summary>
        /// Sum of every effect of <paramref name="type"/> across the given (unlocked) research.
        /// Values of the same type add up; returns 0 when none apply.
        /// </summary>
        public static float SumEffect(IEnumerable<ResearchSO> unlocked, ResearchEffectType type)
        {
            float total = 0f;
            if (unlocked == null) return total;
            foreach (var r in unlocked)
            {
                if (r == null || r.effects == null) continue;
                foreach (var e in r.effects)
                    if (e.type == type) total += e.value;
            }
            return total;
        }

        /// <summary>Total of one effect type across all currently unlocked research.</summary>
        public float GetEffectTotal(ResearchEffectType type)
        {
            if (_allResearch == null) return 0f;
            var unlocked = new List<ResearchSO>();
            foreach (var r in _allResearch)
                if (r != null && IsUnlocked(r.id)) unlocked.Add(r);
            return SumEffect(unlocked, type);
        }

        /// <summary>
        /// Pushes the research effect totals into the ResearchEffects ECS singleton. The player entity
        /// comes from a SubScene and may not exist yet, so this retries each frame while dirty.
        /// </summary>
        private void TryApplyEffectsToECS()
        {
            if (!_effectsDirty || !_effectsReady) return;
            if (_effectsQuery.IsEmptyIgnoreFilter) return;
            _effectsQuery.SetSingleton(new ResearchEffects
            {
                ConveyorSpeedMult = 1f + GetEffectTotal(ResearchEffectType.ConveyorSpeed)
            });
            _effectsDirty = false;
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
                double rem = (_activeCompleteUtc - GameClock.UtcNow).TotalSeconds;
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
            if (!WorldOpen(research)) return false;

            if (research.prerequisites != null)
            {
                foreach (var prereq in research.prerequisites)
                {
                    if (prereq != null && !IsUnlocked(prereq.id))
                        return false;
                }
            }

            // Tutorial: while collection is paused only the tutorial's next research can be bought, and a
            // player already stuck on it gets it for what they hold (TutorialResearchPolicy).
            if (!TutorialResearchPolicy.CanBuy(research.id, IsUnlocked)) return false;

            long currentEntropy = GetCurrentEntropy();
            long cost           = EffectiveCost(research);
            return currentEntropy >= cost || TutorialResearchPolicy.IsRescue(research.id, currentEntropy, cost);
        }

        /// <summary>
        /// What this research costs now: the authored cost less the Memory Resonance upgrade discount.
        /// Use for every price shown and charged (<see cref="ResearchCost"/>).
        /// </summary>
        public long EffectiveCost(ResearchSO research)
        {
            if (research == null) return 0;
            float global  = PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.GlobalResearchDiscount) ?? 0f;
            return ResearchCost.Effective(research.costBaseCurrency, global);
        }

        /// <summary>Effective research time after the Research Overdrive prestige bonus (capped at -50%).</summary>
        public int EffectiveDurationSeconds(ResearchSO research)
        {
            if (research == null) return 0;
            float reduction = Mathf.Min(0.5f,
                PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.ResearchSpeed) ?? 0f);
            return Mathf.Max(0, Mathf.RoundToInt(research.durationSeconds * (1f - reduction)));
        }

        /// <summary>
        /// Research left this run that will finish the moment it starts: the Free Research levels (#491) less
        /// the ones already used this run (<see cref="CurrentRunData.freeResearchUsed"/>, reset by prestige).
        /// </summary>
        public int FreeResearchLeft =>
            FreeResearchRule.Remaining(PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.FreeResearch) ?? 0f,
                                       SaveManager.Instance?.Current?.currentRun?.freeResearchUsed ?? 0);

        // ── Start / complete ─────────────────────────────────────────────────

        /// <summary>
        /// Starts the research: deducts entropy up front and begins the timer. Research with a
        /// zero (or fully-reduced-to-zero) duration completes instantly. No-op if another research
        /// is already in progress.
        /// </summary>
        public void Purchase(ResearchSO research)
        {
            if (research == null || HasActiveResearch || !CanPurchase(research)) return;

            // Check-and-spend in one step; CanPurchase already confirmed the balance.
            long cost = EffectiveCost(research);
            if (TutorialResearchPolicy.IsRescue(research.id, GetCurrentEntropy(), cost))
                cost = EntropyWallet.SpendUpTo(_progressQuery, cost, research.costBaseCurrency);
            else if (!EntropyWallet.TrySpend(_progressQuery, cost, research.costBaseCurrency)) return;
            AchievementService.Instance?.NotifyEntropySpent(cost);

            int effective = EffectiveDurationSeconds(research);
            if (effective <= 0)
            {
                CompleteResearch(research);
                return;
            }

            // Free Research (#491): a timed research skips its timer while this run has free ones left.
            var run = SaveManager.Instance?.Current?.currentRun;
            if (run != null && FreeResearchLeft > 0)
            {
                run.freeResearchUsed++;
                GameLogger.Info($"[ResearchService] Free research: {research.displayName} ({FreeResearchLeft} left this run)");
                CompleteResearch(research);   // saves
                return;
            }

            _activeResearch    = research;
            _activeCompleteUtc = GameClock.UtcNow.AddSeconds(effective);

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
            _effectsDirty      = true;

            // Mark any JSON recipes gated behind this research as "known" (persists across prestige)
            MarkGatedRecipesKnown(research.id);

            GameLogger.Info($"[ResearchService] Completed: {research.displayName}");
            OnResearchUnlocked?.Invoke(research);

            AchievementService.Instance?.NotifyResearchCompleted(research.id);

            // Research is the only thing that unlocks codex entries (it is what marks recipes
            // known, just above), so this is the one place the total can change. NotifyCodexUnlocked
            // takes an absolute total rather than a delta, so recount instead of incrementing —
            // Evaluate does Max(current, total), making it safe to call with an unchanged value.
            AchievementService.Instance?.NotifyCodexUnlocked(RecipeKnowledgeService.CountCodexEntries());
        }

        private static void MarkGatedRecipesKnown(string researchId)
        {
            var knowledge = RecipeKnowledgeService.Instance;
            if (knowledge == null || RecipeDatabase.Instance?.Recipes == null) return;
            foreach (var id in RecipesGatedBy(RecipeDatabase.Instance.Recipes, researchId))
                knowledge.MarkKnown(id);
        }

        /// <summary>Ids of the JSON recipes that <paramref name="researchId"/> unlocks.</summary>
        internal static IEnumerable<string> RecipesGatedBy(IEnumerable<RecipeJson> recipes, string researchId)
        {
            foreach (var r in recipes)
                if (r != null && !string.IsNullOrEmpty(researchId) && r.requires_research == researchId)
                    yield return r.id;
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
            if (GameClock.UtcNow >= _activeCompleteUtc)
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

            MarkGatedRecipesKnown(researchId);
            _effectsDirty = true;

            var so = FindResearch(researchId);
            if (so != null)
                OnResearchUnlocked?.Invoke(so);

            GameLogger.Debug($"[TutorialSkip] Force-unlocked research: {researchId}");
        }
#endif

        // ── Prestige reset ───────────────────────────────────────────────────

        /// <summary>
        /// Clears all unlocked research and any in-progress timer for the new run.
        /// Called by PrestigeSystem. The caller clears the matching
        /// SaveData fields before saving.
        /// </summary>
        public void ResetAll()
        {
            _unlockedIds.Clear();
            _activeResearch    = null;
            _activeCompleteUtc = default;
            _effectsDirty      = true;
            GameLogger.Info("[ResearchService] Research reset for prestige.");
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Research merged into another node: removed id -> the node that now does its job. A save that bought
        /// (or is researching) the removed node gets the node it was merged into, so it keeps what it paid for.
        /// </summary>
        internal static readonly System.Collections.Generic.Dictionary<string, string> MergedResearch = new()
        {
            // 2026-11-19: Surplus Containment unlocks Input Capacity upgrades too.
            { "feedstock_buffers", "surplus_containment" },
            // 2026-11-23 Item Timeline (scripts/apply_item_timeline.py REMOVED_RESEARCH): the s/r-process capture
            // groups became the Spallation descent and Uranium is mined from the Uranium Vein.
            { "transition_metals",            "iron_convergence" },
            { "precious_metals",              "spallation_1" },
            { "exotic_metals",                "spallation_1" },
            { "lanthanides_and_heavy_metals", "spallation_3" },
            { "heavy_transition_metals",      "spallation_5" },
            { "rapid_neutron_capture",        "spallation_4" },
            { "slow_neutron_capture",         "spallation_7" },
            { "fissile_extraction",           "heavy_elements" },
            // Phase 2: Organic Chemistry became Hydrocarbons (and Alcohols and Acids after it).
            { "organic_chemistry",            "hydrocarbons" },
            // Phase 3: Biology's precursors are the life chemicals; metals and materials moved to Metallurgy.
            { "biochem_precursors",           "energy_currency" },
            { "reaction_engineering",         "ore_prospecting" },
            { "advanced_molecules",           "roasting" },
            { "materials_science",            "steelmaking" },
            { "advanced_materials",           "metamaterials" },
            // Phase 4: the components moved to the Engineering world.
            { "component_engineering",        "circuitry" },
            { "megastructure_theory",         "dyson_engineering" },
        };

        /// <summary>
        /// Research removed with no replacement: id -> its entropy price. A save that finished one just drops it
        /// (it already had the benefit); one whose timer was running on it gets the price back.
        /// </summary>
        internal static readonly System.Collections.Generic.Dictionary<string, long> RetiredResearch = new()
        {
            // 2026-11-24: field tap-cooldown research. Nobody taps fields once Automation I's harvesters arrive,
            // so these were dead weight in the tree; Transportation I now follows Automation I.
            { "rapid_extraction_i",  150 },
            { "rapid_extraction_ii", 48000 },
        };

        /// <summary>
        /// Rewrites merged research ids in <paramref name="save"/> (unlocked list and the running timer) and drops
        /// retired ones. Returns the entropy to refund for a retired research whose timer was running. Idempotent.
        /// </summary>
        internal static long MigrateMergedResearch(SaveData save)
        {
            if (save == null) return 0;
            long refund = 0;
            save.unlockedResearch?.RemoveAll(id => id != null && RetiredResearch.ContainsKey(id));
            if (!string.IsNullOrEmpty(save.activeResearchId)
                && RetiredResearch.TryGetValue(save.activeResearchId, out long price))
            {
                refund = price;
                save.activeResearchId          = null;
                save.activeResearchCompleteUtc = null;
            }
            if (save.unlockedResearch != null)
            {
                for (int i = save.unlockedResearch.Count - 1; i >= 0; i--)
                {
                    if (!MergedResearch.TryGetValue(save.unlockedResearch[i], out var into)) continue;
                    save.unlockedResearch.RemoveAt(i);
                    if (!save.unlockedResearch.Contains(into)) save.unlockedResearch.Add(into);
                }
            }
            if (!string.IsNullOrEmpty(save.activeResearchId)
                && MergedResearch.TryGetValue(save.activeResearchId, out var active))
            {
                if (save.unlockedResearch != null && save.unlockedResearch.Contains(active))
                {
                    // Already owns what the timer was buying: drop the timer rather than run it twice.
                    save.activeResearchId          = null;
                    save.activeResearchCompleteUtc = null;
                }
                else save.activeResearchId = active;
            }
            return refund;
        }

        // A retired research's running timer: give its price back. Into the live ECS balance when the save has
        // already been applied, otherwise into the save before it is.
        void RefundRetired(SaveData save, long amount)
        {
            bool live = _effectsReady && ECSLoadBridge.Instance != null && ECSLoadBridge.Instance.IsLoaded
                        && !_progressQuery.IsEmpty;
            if (live)
            {
                var e = _progressQuery.GetSingletonEntity();
                var p = _em.GetComponentData<PlayerProgressData>(e);
                p.BaseCurrency += amount;
                _em.SetComponentData(e, p);
            }
            else if (save?.currentRun != null) save.currentRun.baseCurrency += amount;
            GameLogger.Info($"[ResearchService] Refunded {amount} entropy for a retired research in progress.");
        }

        private ResearchSO FindResearch(string id) =>
            _allResearch == null ? null : System.Array.Find(_allResearch, r => r != null && r.id == id);

        private long GetCurrentEntropy() => EntropyWallet.Balance(_progressQuery);
    }
}
