using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds a runtime lookup from string id → ItemSO and int itemId → ItemSO.
    /// Assign all ItemSO assets in the Inspector. Place on the same GameObject as GameBootstrap.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class ItemDatabase : MonoBehaviour
    {
        public static ItemDatabase Instance { get; private set; }

        [SerializeField] private ItemSO[] items;

        private readonly Dictionary<string, ItemSO> _byId     = new();
        private readonly Dictionary<int,    ItemSO> _byItemId = new();

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            foreach (var item in items)
            {
                if (item == null) continue;
                _byId[item.id]         = item;
                _byItemId[item.itemId] = item;
            }
        }

        public IReadOnlyList<ItemSO> All => items;

        public ItemSO Get(string id)   => _byId.TryGetValue(id, out var v)     ? v : null;
        public ItemSO Get(int itemId)  => _byItemId.TryGetValue(itemId, out var v) ? v : null;

        /// <summary>Returns -1 if the id is not registered.</summary>
        public int GetItemId(string id) => Get(id)?.itemId ?? -1;
    }
}
