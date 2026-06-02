using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds a runtime lookup from string id → ItemSO and int itemId → ItemSO.
    /// Items are loaded automatically from Assets/Resources/Items/ — no Inspector wiring needed.
    /// Re-run MobileIdleBuilder > Import Game Data to pick up new items from game_data.json.
    ///
    /// The lookup dictionaries are static so they survive even if Unity destroys the
    /// MonoBehaviour (e.g. when Bootstrap lives inside a SubScene that bakes at runtime).
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class ItemDatabase : SingletonMonoBehaviour<ItemDatabase>
    {
        // Static so the data outlives the MonoBehaviour if it gets destroyed.
        private static readonly Dictionary<string, ItemSO> _byId     = new();
        private static readonly Dictionary<int,    ItemSO> _byItemId = new();
        private static ItemSO[] _allItems;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this) return;

            _byId.Clear();
            _byItemId.Clear();
            _allItems = Resources.LoadAll<ItemSO>("Items");

            foreach (var item in _allItems)
            {
                if (item == null) continue;
                if (string.IsNullOrEmpty(item.id))
                {
                    GameLogger.Warning($"[ItemDatabase] '{item.name}' has a null/empty id — skipped. Fix the ItemSO asset.");
                    continue;
                }
                _byId[item.id]         = item;
                _byItemId[item.itemId] = item;
            }

            GameLogger.Info($"[ItemDatabase] Registered on '{gameObject.name}' with {_byItemId.Count} items.");
        }

        public IReadOnlyList<ItemSO> All => _allItems;

        // Instance methods kept for backwards compatibility — delegate to static lookups.
        public ItemSO Get(string id)   => GetStatic(id);
        public ItemSO Get(int itemId)  => GetStatic(itemId);
        public int GetItemId(string id) => GetStatic(id)?.itemId ?? -1;

        // Static accessors — work even after the MonoBehaviour is destroyed.
        public static ItemSO GetStatic(string id)   => _byId.TryGetValue(id, out var v)     ? v : null;
        public static ItemSO GetStatic(int itemId)  => _byItemId.TryGetValue(itemId, out var v) ? v : null;
    }
}
