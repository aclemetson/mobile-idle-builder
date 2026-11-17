using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Managed-side bridge between the UI and the ECS inventory.
    /// Runs the recipe-menu craft queue (#310): inputs are taken when items are queued, each item takes
    /// the recipe's craft time (scaled by the fastest placed building that makes it), and the output
    /// lands in the inventory when it finishes. Also provides a snapshot of inventory counts for the HUD.
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public class ManualCraftService : SingletonMonoBehaviour<ManualCraftService>
    {
        /// <summary>crafted_in entry for recipes the player can craft without a building.</summary>
        public const string InventoryMenuSource = "inventory_menu";

        private const float MinUnitSeconds = 0.1f;

        private EntityManager _em;
        private EntityQuery   _inventoryQuery;
        private EntityQuery   _buildingQuery;
        private World         _boundWorld;

        public bool IsReady { get; private set; }

        /// <summary>Fired after the queue changes: items queued, cancelled or finished.</summary>
        public event Action QueueChanged;

        // Test seams: production reads RecipeDatabase / BuildingDatabase / SaveManager.
        internal static Func<string, RecipeJson> RecipeLookupOverride;
        internal static BuildingSO[]             BuildingsOverride;
        internal static CraftQueueSaveData       QueueOverride;

        private BuildingSO[] _buildings;

        void Start()
        {
            if (Instance != this) return; // a duplicate destroyed by SingletonMonoBehaviour.Awake
            EnsureBound();
        }

        void Update()
        {
            if (Instance != this) return;
            if (ECSLoadBridge.Instance == null || !ECSLoadBridge.Instance.IsLoaded) return;
            TickQueue(GameClock.UtcNow);
        }

        // This is a persistent (DontDestroyOnLoad) singleton, but the DOTS world is rebuilt on every
        // scene reload (e.g. starting a new game in-session — see DevConsoleController). A query cached
        // against the old world reads empty, so the HUD recipe panel shows 0/x and craft is greyed out
        // even though the inventory holds the items. Re-acquire the EntityManager + queries lazily
        // whenever the default world changes. Checking at call time (rather than on sceneLoaded) is
        // immune to the ordering of world recreation vs. the scene-loaded event.
        private void EnsureBound()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) { IsReady = false; return; }
            if (ReferenceEquals(world, _boundWorld) && IsReady) return;

            _boundWorld     = world;
            _em             = world.EntityManager;
            _inventoryQuery = _em.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerInventoryTag>(),
                ComponentType.ReadWrite<InventorySlot>());
            _buildingQuery  = _em.CreateEntityQuery(ComponentType.ReadOnly<BuildingData>());
            IsReady = true;
            GameLogger.Debug("[ManualCraftService] (Re)bound to the active ECS world.");
        }

        /// <summary>Returns true if the player's ECS inventory has enough inputs for one craft of this recipe.</summary>
        public bool CanCraft(RecipeJson recipe) => MaxCraftable(recipe) >= 1;

        /// <summary>How many of this recipe the current inventory can pay for.</summary>
        public int MaxCraftable(RecipeJson recipe)
        {
            EnsureBound();
            if (!IsReady || _inventoryQuery.IsEmpty || recipe?.inputs == null || recipe.inputs.Count == 0) return 0;

            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);
            int max = int.MaxValue;
            foreach (var input in recipe.inputs)
            {
                if (input.quantity <= 0) continue;
                int itemId = ItemDatabase.Instance.GetItemId(input.id);
                if (itemId < 0) return 0;
                max = Math.Min(max, SlotBufferUtils.CountInInventory(buffer, itemId) / input.quantity);
            }
            return max == int.MaxValue ? 0 : max;
        }

        /// <summary>
        /// What the inventory is short of for <paramref name="count"/> crafts, e.g. "Electron ×1, Down
        /// Quark ×2". Null when nothing is missing. Only the player's inventory counts: items on belts
        /// or inside buildings do not, which is why the Recipes panel says "in your inventory".
        /// </summary>
        public string DescribeMissing(RecipeJson recipe, int count)
        {
            if (recipe?.inputs == null || count <= 0) return null;
            var have = GetInventoryCounts();
            var parts = new List<string>();
            foreach (var input in recipe.inputs)
            {
                if (input.quantity <= 0) continue;
                int itemId = ItemDatabase.Instance.GetItemId(input.id);
                int held   = itemId >= 0 && have.TryGetValue(itemId, out int q) ? q : 0;
                int shortBy = input.quantity * count - held;
                if (shortBy <= 0) continue;
                var so = ItemDatabase.Instance.Get(input.id);
                string name = so != null && !string.IsNullOrEmpty(so.displayName) ? so.displayName : input.id;
                parts.Add($"{name} ×{shortBy}");
            }
            return parts.Count == 0 ? null : string.Join(", ", parts);
        }

        /// <summary>
        /// Consumes inputs and deposits the recipe output into the ECS inventory at once.
        /// Returns false and makes no changes if inputs are insufficient.
        /// </summary>
        public bool TryCraft(RecipeJson recipe)
        {
            if (!CanCraft(recipe)) return false;
            ChangeInputs(recipe, -1);
            DepositOutput(recipe);
            return true;
        }

        /// <summary>Snapshot of current inventory: itemId → quantity.</summary>
        public Dictionary<int, int> GetInventoryCounts()
        {
            EnsureBound();
            var result = new Dictionary<int, int>();
            if (!IsReady || _inventoryQuery.IsEmpty) return result;

            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity(), isReadOnly: true);
            for (int i = 0; i < buffer.Length; i++)
                result[buffer[i].ItemID] = buffer[i].Quantity;
            return result;
        }

        // ---- Building requirement (#309) ----

        /// <summary>
        /// Whether a recipe can be crafted from the menu right now. Recipes made only in a building need
        /// one of those buildings placed; its current recipe and whether it is busy do not matter.
        /// <paramref name="speed"/> is the fastest placed building's ProductionSpeed (1 for menu recipes).
        /// <paramref name="missingBuildingName"/> names the building to place when none is.
        /// </summary>
        public bool HasCraftingBuilding(RecipeJson recipe, out float speed, out string missingBuildingName)
        {
            speed = 1f;
            missingBuildingName = null;
            if (recipe == null || !recipe.requiresBuilding) return true;

            var types = CraftingBuildingTypes(recipe, out missingBuildingName);
            missingBuildingName ??= "the right building";

            EnsureBound();
            if (!IsReady || types.Count == 0) return false;

            bool found = false;
            float best = 0f;
            var data = _buildingQuery.ToComponentDataArray<BuildingData>(Allocator.Temp);
            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    if (!types.Contains(data[i].BuildingType)) continue;
                    found = true;
                    best = Mathf.Max(best, data[i].ProductionSpeed);
                }
            }
            finally { data.Dispose(); }

            if (!found) return false;
            speed = best > 0f ? best : 1f;
            missingBuildingName = null;
            return true;
        }

        /// <summary>Seconds one item takes: the recipe's craft time divided by the building speed.</summary>
        public static float UnitSeconds(RecipeJson recipe, float speed)
        {
            float baseTime = recipe != null && recipe.base_craft_time > 0f ? recipe.base_craft_time : 1f;
            return Mathf.Max(MinUnitSeconds, baseTime / Mathf.Max(0.01f, speed));
        }

        // Numeric building types (BuildingSO.buildingId) that make this recipe: those named in
        // the recipe's crafted_in, plus any whose supportedRecipes output the same item.
        private HashSet<int> CraftingBuildingTypes(RecipeJson recipe, out string firstName)
        {
            var types = new HashSet<int>();
            firstName = null;
            foreach (var b in Buildings())
            {
                if (b == null) continue;
                bool makes = recipe.crafted_in != null && recipe.crafted_in.Contains(b.id);
                if (!makes && b.supportedRecipes != null && recipe.output != null)
                {
                    foreach (var r in b.supportedRecipes)
                        if (r != null && r.outputItem != null && r.outputItem.id == recipe.output.id) { makes = true; break; }
                }
                if (!makes) continue;
                types.Add(b.buildingId);
                firstName ??= b.displayName;
            }
            return types;
        }

        private BuildingSO[] Buildings()
        {
            if (BuildingsOverride != null) return BuildingsOverride;
            if (_buildings == null)
                _buildings = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings ?? Array.Empty<BuildingSO>();
            return _buildings;
        }

        // ---- Craft queue (#310) ----

        /// <summary>The live queue, stored in the save so it survives a reload. Null before a save exists.</summary>
        public CraftQueueSaveData Queue
        {
            get
            {
                if (QueueOverride != null) return QueueOverride;
                var run = SaveManager.Instance?.Current?.currentRun;
                if (run == null) return null;
                run.craftQueue ??= new CraftQueueSaveData();
                // A queue saved before recipes.json was retired may hold legacy recipe ids (#336); it is cheap
                // and idempotent, and the first read after a load is what fixes them.
                LegacyRecipeIds.Migrate(run.craftQueue);
                return run.craftQueue;
            }
        }

        public enum EnqueueResult { Queued, NotEnoughMaterials, NoBuilding, NotReady }

        /// <summary>
        /// Queues <paramref name="count"/> crafts. Takes all their inputs now, so the queue can never
        /// stall for lack of materials and cancelling can refund exactly what was taken.
        /// </summary>
        public EnqueueResult TryEnqueue(RecipeJson recipe, int count, out string missingBuildingName)
        {
            missingBuildingName = null;
            var queue = Queue;
            if (recipe == null || queue == null || count <= 0) return EnqueueResult.NotReady;
            if (!HasCraftingBuilding(recipe, out float speed, out missingBuildingName)) return EnqueueResult.NoBuilding;
            if (MaxCraftable(recipe) < count) return EnqueueResult.NotEnoughMaterials;

            ChangeInputs(recipe, -count);
            CraftQueue.Enqueue(queue, recipe.id, count, UnitSeconds(recipe, speed), GameClock.UtcNow);
            QueueChanged?.Invoke();
            return EnqueueResult.Queued;
        }

        /// <summary>Cancels a queue entry and gives back the inputs of every item it still held.</summary>
        public void CancelQueued(int index)
        {
            var queue = Queue;
            if (CraftQueue.IsEmpty(queue) || index < 0 || index >= queue.entries.Count) return;

            var recipe = FindRecipe(queue.entries[index].recipeId);
            int removed = CraftQueue.Cancel(queue, index, GameClock.UtcNow);
            if (recipe != null && removed > 0) ChangeInputs(recipe, removed);
            QueueChanged?.Invoke();
        }

        /// <summary>Finishes due items. Called every frame once the save is applied to ECS.</summary>
        public void TickQueue(DateTime now)
        {
            var queue = Queue;
            if (CraftQueue.IsEmpty(queue)) return;
            EnsureBound();
            if (!IsReady || _inventoryQuery.IsEmpty) return;

            int done = CraftQueue.Tick(queue, now, id =>
            {
                var recipe = FindRecipe(id);
                if (recipe != null) DepositOutput(recipe);
            });
            if (done > 0) QueueChanged?.Invoke();
        }

        public static RecipeJson FindRecipe(string id) =>
            RecipeLookupOverride != null ? RecipeLookupOverride(id) : RecipeDatabase.Instance?.GetRecipe(id);

        // ---- Inventory helpers ----

        // times < 0 removes that many crafts' inputs; times > 0 gives them back.
        private void ChangeInputs(RecipeJson recipe, int times)
        {
            EnsureBound();
            if (!IsReady || _inventoryQuery.IsEmpty) return;
            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity());
            foreach (var input in recipe.inputs)
            {
                int itemId = ItemDatabase.Instance.GetItemId(input.id);
                if (itemId < 0 || input.quantity <= 0) continue;
                int qty = input.quantity * Math.Abs(times);
                if (times < 0) SlotBufferUtils.RemoveFromInventory(ref buffer, itemId, qty);
                else           SlotBufferUtils.AddToInventory(ref buffer, itemId, qty);
            }
        }

        private void DepositOutput(RecipeJson recipe)
        {
            int outputId = ItemDatabase.Instance.GetItemId(recipe.output.id);
            if (outputId < 0) return;

            var buffer = _em.GetBuffer<InventorySlot>(_inventoryQuery.GetSingletonEntity());
            SlotBufferUtils.AddToInventory(ref buffer, outputId, recipe.output.quantity);

            long escaped = (long)recipe.EscapeEntropyPerCraft(ItemDatabase.Instance);
            if (escaped > 0) ECSLoadBridge.Instance?.AddEntropy(escaped);

            AchievementService.Instance?.NotifyCraft(recipe.output.id, recipe.output.quantity);
            TierProgress.NotifyItemProduced(outputId);
            CraftLog.Record(outputId, recipe.output.quantity);
        }
    }
}
