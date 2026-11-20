using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure rules for world delivery tasks (the Chemistry gate, 2026-11-19). State lives straight in the
    /// persistent part of <see cref="SaveData"/> (<c>worldTaskStage</c>, <c>worldTaskProgressKeys/Values</c>), so
    /// the live path (<see cref="WorldTaskService"/>, items reaching the Demon) and the offline path
    /// (<see cref="OfflineCollectionService"/>) credit the same numbers with no copy to keep in sync.
    /// <list type="bullet">
    /// <item>Only items the current stage still needs count; anything past its quantity is ignored (it was
    /// still sold for entropy).</item>
    /// <item>A finished stage clears its progress and the next one starts from zero.</item>
    /// <item>Nothing here unlocks the world: the service does that once <see cref="IsComplete"/> is true.</item>
    /// </list>
    /// </summary>
    public static class WorldTasks
    {
        /// <summary>Current save format; bumped saves have been migrated (<see cref="Migrate"/>).</summary>
        public const int Version = 1;

        static WorldTaskSO _data;
        static bool _loaded;

        /// <summary>The generated task data (Resources/WorldTasks), or null before Import Game Data.</summary>
        public static WorldTaskSO Data
        {
            get
            {
                if (!_loaded) { _data = Resources.Load<WorldTaskSO>("WorldTasks"); _loaded = true; }
                return _data;
            }
            internal set { _data = value; _loaded = true; } // tests
        }

        /// <summary>Bumped on every credit, whatever path made it, so the service can tell the UI progress moved.</summary>
        public static int Revision { get; private set; }

        public static int StageCount(WorldTaskSO data) => data?.stages?.Length ?? 0;

        /// <summary>Completed stage count, clamped to the data.</summary>
        public static int CompletedStages(SaveData save, WorldTaskSO data) =>
            Mathf.Clamp(save?.worldTaskStage ?? 0, 0, StageCount(data));

        public static bool IsComplete(SaveData save, WorldTaskSO data) =>
            data != null && StageCount(data) > 0 && CompletedStages(save, data) >= StageCount(data);

        /// <summary>The stage being worked on, or null when every stage is done.</summary>
        public static WorldTaskStage CurrentStage(SaveData save, WorldTaskSO data)
        {
            int i = CompletedStages(save, data);
            return i < StageCount(data) ? data.stages[i] : null;
        }

        /// <summary>How many of <paramref name="itemId"/> the current stage has received.</summary>
        public static int Delivered(SaveData save, int itemId)
        {
            var keys = save?.worldTaskProgressKeys;
            var vals = save?.worldTaskProgressValues;
            if (keys == null || vals == null) return 0;
            string key = itemId.ToString();
            for (int i = 0; i < keys.Count && i < vals.Count; i++)
                if (keys[i] == key) return Math.Max(0, vals[i]);
            return 0;
        }

        /// <summary>How many of <paramref name="itemId"/> <paramref name="stage"/> asks for (0 if it doesn't).</summary>
        public static int Required(WorldTaskStage stage, int itemId)
        {
            if (stage?.items == null) return 0;
            int total = 0;
            for (int i = 0; i < stage.items.Length; i++)
                if (stage.items[i] != null && stage.items[i].itemId == itemId)
                    total += stage.quantities != null && i < stage.quantities.Length ? Math.Max(0, stage.quantities[i]) : 0;
            return total;
        }

        /// <summary>Delivered over required for the whole stage, 0..1.</summary>
        public static float StageFraction(SaveData save, WorldTaskStage stage)
        {
            if (stage?.items == null) return 0f;
            long need = 0, have = 0;
            var seen = new HashSet<int>();
            foreach (var item in stage.items)
            {
                if (item == null || !seen.Add(item.itemId)) continue;
                int req = Required(stage, item.itemId);
                need += req;
                have += Math.Min(req, Delivered(save, item.itemId));
            }
            return need <= 0 ? 1f : (float)have / need;
        }

        static bool StageDone(SaveData save, WorldTaskStage stage)
        {
            if (stage?.items == null) return true;
            foreach (var item in stage.items)
                if (item != null && Delivered(save, item.itemId) < Required(stage, item.itemId)) return false;
            return true;
        }

        /// <summary>
        /// Credits <paramref name="quantity"/> of <paramref name="itemId"/> to the current stage, up to what it
        /// still needs. Returns how many counted; <paramref name="stageCompleted"/> is the stage this finished, if any.
        /// </summary>
        public static int Credit(SaveData save, WorldTaskSO data, int itemId, long quantity, out WorldTaskStage stageCompleted)
        {
            stageCompleted = null;
            if (save == null || quantity <= 0) return 0;
            var stage = CurrentStage(save, data);
            if (stage == null) return 0;

            int need = Required(stage, itemId) - Delivered(save, itemId);
            if (need <= 0) return 0;
            int add = (int)Math.Min(need, quantity);
            SetDelivered(save, itemId, Delivered(save, itemId) + add);
            Revision++;

            if (StageDone(save, stage))
            {
                save.worldTaskStage = CompletedStages(save, data) + 1;
                save.worldTaskProgressKeys?.Clear();
                save.worldTaskProgressValues?.Clear();
                stageCompleted = stage;
            }
            return add;
        }

        /// <summary>Credits every entry of <paramref name="deliveries"/> (item id → count). Returns the stages it finished.</summary>
        public static List<WorldTaskStage> CreditAll(SaveData save, WorldTaskSO data, IEnumerable<KeyValuePair<int, long>> deliveries)
            => CreditAll(save, data, deliveries, out _);

        /// <summary>
        /// Credits every entry of <paramref name="deliveries"/>; <paramref name="counted"/> is how many items counted.
        /// A batch can finish a stage and carry on into the next one (a long Time Warp), so it keeps crediting what
        /// is left of each entry until nothing more counts. Finished stages are also queued on
        /// <see cref="PendingAnnouncements"/> so the service announces them like a live completion.
        /// </summary>
        public static List<WorldTaskStage> CreditAll(SaveData save, WorldTaskSO data,
                                                     IEnumerable<KeyValuePair<int, long>> deliveries, out long counted)
        {
            counted = 0;
            var done = new List<WorldTaskStage>();
            if (deliveries == null || save == null) return done;
            var left = new Dictionary<int, long>();
            foreach (var kv in deliveries)
                if (kv.Value > 0) left[kv.Key] = (left.TryGetValue(kv.Key, out long v) ? v : 0) + kv.Value;

            bool finishedOne = true;
            while (finishedOne && left.Count > 0 && !IsComplete(save, data))
            {
                finishedOne = false;
                foreach (int id in new List<int>(left.Keys))
                {
                    int add = Credit(save, data, id, left[id], out var finished);
                    counted += add;
                    left[id] -= add;
                    if (left[id] <= 0) left.Remove(id);
                    if (finished == null) continue;
                    done.Add(finished);
                    PendingAnnouncements.Add(finished);
                    finishedOne = true; // the next stage starts from zero: offer it the rest
                    break;
                }
            }
            return done;
        }

        /// <summary>
        /// Stages finished outside the live Demon path (offline return, Time Warp and the other bonuses), waiting for
        /// <see cref="WorldTaskService"/> to play their toast and dialogue. Static so a credit made before the service
        /// exists (offline collection runs before ECS loads) is still announced once the game is up.
        /// </summary>
        public static readonly List<WorldTaskStage> PendingAnnouncements = new();

        /// <summary>
        /// One-time start point for saves from before the tasks existed: a save that already unlocked the world
        /// has finished them; otherwise each completed prestige counts as a finished stage, short of the last
        /// one (that run still has to deliver). New games start at stage 0.
        /// </summary>
        public static void Migrate(SaveData save, WorldTaskSO data, bool isNewGame)
        {
            if (save == null || save.worldTaskVersion >= Version) return;
            save.worldTaskVersion = Version;
            int count = StageCount(data);
            if (count == 0 || isNewGame) return;
            int stage = save.unlockedWorlds != null && data != null && save.unlockedWorlds.Contains(data.worldId)
                ? count
                : Math.Min(Math.Max(0, save.prestigeCount), count - 1);
            if (stage > save.worldTaskStage)
            {
                save.worldTaskStage = stage;
                save.worldTaskProgressKeys?.Clear();
                save.worldTaskProgressValues?.Clear();
            }
        }

        static void SetDelivered(SaveData save, int itemId, int value)
        {
            save.worldTaskProgressKeys   ??= new List<string>();
            save.worldTaskProgressValues ??= new List<int>();
            string key = itemId.ToString();
            for (int i = 0; i < save.worldTaskProgressKeys.Count; i++)
            {
                if (save.worldTaskProgressKeys[i] != key) continue;
                while (save.worldTaskProgressValues.Count <= i) save.worldTaskProgressValues.Add(0);
                save.worldTaskProgressValues[i] = value;
                return;
            }
            save.worldTaskProgressKeys.Add(key);
            while (save.worldTaskProgressValues.Count < save.worldTaskProgressKeys.Count - 1) save.worldTaskProgressValues.Add(0);
            save.worldTaskProgressValues.Add(value);
        }
    }
}
