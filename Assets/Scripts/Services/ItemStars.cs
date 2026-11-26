using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Tournament item stars (PvE phase 2). Each star on an item adds <see cref="BonusPerStar"/> (20%) to its
    /// value, additively: 5 stars double it, 10 triple it. Stars are permanent (they survive prestige and
    /// Ascension) and are kept in <see cref="SaveData.itemStarIds"/> / <see cref="SaveData.itemStarCounts"/>
    /// by item string id, so a renumbered numeric id never moves them.
    ///
    /// Every runtime price goes through <see cref="Multiplier"/>: the Demon and Calorimeter payouts
    /// (<c>EntropySinkSystem</c>), surplus sales and stored-item value (<see cref="SurplusSale.DemonPrice"/>),
    /// inventory net worth (<c>NetWorthSystem</c>), offline chains (<c>GridSaveService</c>) and the Demon
    /// panel. Escaping byproduct entropy is baked into ECS at bake time and stays unstarred.
    /// </summary>
    public static class ItemStars
    {
        /// <summary>Value added per star before the tournament track, as a fraction of the item's base price.</summary>
        public const float BaseBonusPerStar = 0.2f;

        /// <summary>
        /// Value added per star now: 20% plus Star Resonance (+2% a level, <see cref="TournamentTrack"/>, #500).
        /// Cached with the star lookup; a track purchase calls <see cref="Invalidate"/>.
        /// </summary>
        public static float BonusPerStar
        {
            get { Sync(LiveSave()); return s_bonusPerStar; }
        }

        static float s_bonusPerStar = BaseBonusPerStar;

        /// <summary>Raised after stars are granted or cleared (main thread).</summary>
        public static event Action OnChanged;

        /// <summary>Value multiplier for <paramref name="stars"/> stars: 1 + bonus x stars (0.2 without Star Resonance).</summary>
        public static float MultiplierFor(int stars) => 1f + BonusPerStar * Math.Max(0, stars);

        /// <summary>The same for a given per-star bonus (pure, for tests and previews).</summary>
        public static float MultiplierFor(int stars, float bonusPerStar) => 1f + bonusPerStar * Math.Max(0, stars);

        // ── Live lookups (hot path: every sink payout, every net worth tick) ─────

        // numeric item id -> stars, rebuilt when the save object or its lists change.
        static readonly Dictionary<int, int> s_byItemId = new();
        static SaveData     s_source;
        static List<string> s_sourceIds;
        static int          s_sourceCount = -1;
        static int          s_syncedVersion = -1;
        static int          s_version;      // bumped by every write through this class
        static int          s_rebuilds;     // bumped whenever the lookup below is rebuilt (Stamp)

        /// <summary>Stars on the item with numeric id <paramref name="itemId"/> in the live save.</summary>
        public static int Count(int itemId)
        {
            Sync(LiveSave());
            return s_byItemId.Count == 0 ? 0 : s_byItemId.TryGetValue(itemId, out int n) ? n : 0;
        }

        /// <summary>Value multiplier for the item with numeric id <paramref name="itemId"/> (1 without stars or a save).</summary>
        /// <summary>
        /// Changes whenever any item's star multiplier may have changed (the live save, its star lists or a grant),
        /// so ItemValueTableSystem rebuilds its price table only then (#284). Cheap: reference and count compares.
        /// </summary>
        public static int Stamp
        {
            get { Sync(LiveSave()); return s_rebuilds; }
        }

        public static float Multiplier(int itemId)
        {
            Sync(LiveSave());
            if (s_byItemId.Count == 0) return 1f;
            return s_byItemId.TryGetValue(itemId, out int n) ? MultiplierFor(n, s_bonusPerStar) : 1f;
        }

        /// <summary>
        /// <paramref name="item"/>'s <c>baseSellValue</c> with its stars applied (0 stays 0; callers keep their
        /// own "no value sells for 1" fallback). 0 for a null item.
        /// </summary>
        public static float StarredBase(ItemSO item) =>
            item == null ? 0f : item.baseSellValue * Multiplier(item.itemId);

        /// <summary>Stars on <paramref name="itemStringId"/> in <paramref name="save"/>.</summary>
        public static int Count(SaveData save, string itemStringId)
        {
            if (save?.itemStarIds == null || string.IsNullOrEmpty(itemStringId)) return 0;
            int i = save.itemStarIds.IndexOf(itemStringId);
            return i >= 0 && i < save.itemStarCounts.Count ? save.itemStarCounts[i] : 0;
        }

        /// <summary>Total stars the player holds.</summary>
        public static int Total(SaveData save)
        {
            if (save?.itemStarCounts == null) return 0;
            int t = 0;
            foreach (int c in save.itemStarCounts) t += Math.Max(0, c);
            return t;
        }

        // Unity null check (a destroyed SaveManager is "fake null"; ?. would read through it).
        static SaveData LiveSave()
        {
            var sm = SaveManager.Instance;
            return sm != null ? sm.Current : null;
        }

        static void Sync(SaveData save)
        {
            var ids = save?.itemStarIds;
            int count = ids?.Count ?? 0;
            if (ReferenceEquals(save, s_source) && ReferenceEquals(ids, s_sourceIds)
                && count == s_sourceCount && s_version == s_syncedVersion) return;

            s_source = save; s_sourceIds = ids; s_sourceCount = count; s_syncedVersion = s_version;
            s_rebuilds++;
            s_bonusPerStar = BaseBonusPerStar + TournamentTrack.StarBonus(save);
            s_byItemId.Clear();
            if (save == null || ids == null) return;
            bool unresolved = false;
            for (int i = 0; i < ids.Count && i < save.itemStarCounts.Count; i++)
            {
                if (save.itemStarCounts[i] <= 0) continue;
                var item = ItemDatabase.GetStatic(ids[i]);
                if (item != null) s_byItemId[item.itemId] = save.itemStarCounts[i];
                else unresolved = true;
            }
            // The item database may not be loaded yet (early in boot): look again next call. A retired id
            // keeps this retrying, which only costs a walk over the (short) star list.
            if (unresolved) s_syncedVersion = -1;
        }

        /// <summary>Drops the cached lookup (tests, or after the item database reloads).</summary>
        public static void Invalidate()
        {
            s_rebuilds++;
            s_source = null; s_sourceIds = null; s_sourceCount = -1; s_syncedVersion = -1;
            s_bonusPerStar = BaseBonusPerStar;
            s_byItemId.Clear();
        }

        // ── Granting ─────────────────────────────────────────────────────

        /// <summary>Adds <paramref name="stars"/> to one item in <paramref name="save"/>.</summary>
        public static void Add(SaveData save, string itemStringId, int stars)
        {
            if (save == null || string.IsNullOrEmpty(itemStringId) || stars <= 0) return;
            s_version++;
            save.itemStarIds    ??= new List<string>();
            save.itemStarCounts ??= new List<int>();
            int i = save.itemStarIds.IndexOf(itemStringId);
            if (i >= 0 && i < save.itemStarCounts.Count) save.itemStarCounts[i] += stars;
            else
            {
                // Repair a length mismatch before appending so the lists stay parallel.
                while (save.itemStarCounts.Count < save.itemStarIds.Count) save.itemStarCounts.Add(0);
                save.itemStarIds.Add(itemStringId);
                save.itemStarCounts.Add(stars);
            }
        }

        /// <summary>
        /// Draws <paramref name="stars"/> stars one at a time, uniformly from <paramref name="pool"/>
        /// (repeats allowed), adds them to <paramref name="save"/> and returns the item each star landed on.
        /// </summary>
        public static List<string> Grant(SaveData save, int stars, IReadOnlyList<string> pool, Random rng)
        {
            var landed = new List<string>();
            if (save == null || stars <= 0 || pool == null || pool.Count == 0) return landed;
            rng ??= new Random();
            for (int s = 0; s < stars; s++)
            {
                string id = pool[rng.Next(pool.Count)];
                Add(save, id, 1);
                landed.Add(id);
            }
            return landed;
        }

        /// <summary>
        /// Grants stars on the live save from the player's discovered items, saves, and raises
        /// <see cref="OnChanged"/>. Returns where each star landed (empty when nothing could be granted).
        /// </summary>
        public static List<string> GrantLive(int stars, Random rng = null)
        {
            var save = SaveManager.Instance?.Current;
            var pool = DiscoveredItemIds(RecipeDatabase.Instance?.Recipes, RecipeKnowledgeService.Current);
            var landed = Grant(save, stars, pool, rng);
            if (landed.Count > 0)
            {
                SaveManager.Instance.SaveLocal();
                OnChanged?.Invoke();
            }
            return landed;
        }

        /// <summary>Removes every star from the live save (dev console).</summary>
        public static void ClearLive()
        {
            var save = SaveManager.Instance?.Current;
            if (save == null) return;
            save.itemStarIds    = new List<string>();
            save.itemStarCounts = new List<int>();
            s_version++;
            SaveManager.Instance.SaveLocal();
            OnChanged?.Invoke();
        }

        /// <summary>
        /// Items a star can land on: the inputs and output of every recipe the player knows (recipe
        /// knowledge survives prestige), so harvested raw items count once a recipe uses them. Distinct, in
        /// recipe order; only ids the item database knows.
        /// </summary>
        public static List<string> DiscoveredItemIds(IReadOnlyList<RecipeJson> recipes, IRecipeKnowledgeService knowledge)
        {
            var result = new List<string>();
            if (recipes == null || knowledge == null) return result;
            var seen = new HashSet<string>();
            void Consider(string id)
            {
                if (string.IsNullOrEmpty(id) || seen.Contains(id)) return;
                if (ItemDatabase.GetStatic(id) == null) return;
                seen.Add(id);
                result.Add(id);
            }
            foreach (var r in recipes)
            {
                if (r == null || !knowledge.IsKnown(r.id)) continue;
                if (r.inputs != null) foreach (var input in r.inputs) Consider(input?.id);
                Consider(r.output?.id);
            }
            return result;
        }
    }
}
