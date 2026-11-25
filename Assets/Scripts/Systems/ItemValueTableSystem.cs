using Unity.Collections;
using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>Per-item prices by numeric item id, built by <see cref="ItemValueTableSystem"/> (#284).</summary>
    public struct ItemValueBlob
    {
        /// <summary><c>baseSellValue</c> x tournament stars; 0 when the item has no value (inventory net worth).</summary>
        public BlobArray<float> Starred;
        /// <summary>What the Demon pays for one: <c>baseSellValue</c> (1 when it has none) x stars (SurplusSale.DemonPrice).</summary>
        public BlobArray<float> Demon;
    }

    /// <summary>
    /// Singleton: item prices that Burst systems can read (NetWorthSystem, EntropySinkSystem), instead of the managed
    /// ItemDatabase and ItemStars lookups. An id outside the table is priced as an unknown item: 0 starred, 1 Demon.
    /// Without the singleton (isolated test worlds) <see cref="IsCreated"/> is false and the same defaults apply.
    /// </summary>
    public struct ItemValueTable : IComponentData
    {
        public BlobAssetReference<ItemValueBlob> Blob;

        public bool IsCreated => Blob.IsCreated;

        public float Starred(int itemId)
        {
            if (!Blob.IsCreated || itemId < 0) return 0f;
            ref var a = ref Blob.Value.Starred;
            return itemId < a.Length ? a[itemId] : 0f;
        }

        public float Demon(int itemId)
        {
            if (!Blob.IsCreated || itemId < 0) return 1f;
            ref var a = ref Blob.Value.Demon;
            return itemId < a.Length ? a[itemId] : 1f;
        }
    }

    /// <summary>
    /// Builds <see cref="ItemValueTable"/> from ItemDatabase (remote overrides included) and ItemStars, and rebuilds
    /// it only when either changes (<see cref="ItemDatabase.Version"/>, <see cref="ItemStars.Stamp"/>), so the
    /// per-frame cost is two integer compares. Runs first so the Burst systems that price items see this frame's table.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup), OrderFirst = true)]
    public partial class ItemValueTableSystem : SystemBase
    {
        BlobAssetReference<ItemValueBlob> _blob;
        int _dbVersion = -1, _starStamp = -1;

        protected override void OnCreate()
        {
            var e = EntityManager.CreateEntity(typeof(ItemValueTable));
            EntityManager.SetName(e, "ItemValueTable");
        }

        protected override void OnUpdate()
        {
            int db = ItemDatabase.Version, stars = ItemStars.Stamp;
            if (_blob.IsCreated && db == _dbVersion && stars == _starStamp) return;
            _dbVersion = db; _starStamp = stars;

            var old = _blob;
            _blob = Build();
            SystemAPI.SetSingleton(new ItemValueTable { Blob = _blob });
            if (old.IsCreated) old.Dispose();
        }

        protected override void OnDestroy()
        {
            if (_blob.IsCreated) _blob.Dispose();
        }

        /// <summary>The table for the items ItemDatabase holds now.</summary>
        internal static BlobAssetReference<ItemValueBlob> Build()
        {
            var items = ItemDatabase.AllStatic;
            int n = 0;
            if (items != null)
                foreach (var it in items)
                    if (it != null && it.itemId + 1 > n) n = it.itemId + 1;

            var builder = new BlobBuilder(Allocator.Temp);
            ref var root = ref builder.ConstructRoot<ItemValueBlob>();
            var starred = builder.Allocate(ref root.Starred, n);
            var demon   = builder.Allocate(ref root.Demon, n);
            for (int i = 0; i < n; i++) { starred[i] = 0f; demon[i] = 1f; }
            if (items != null)
                foreach (var it in items)
                {
                    if (it == null || it.itemId < 0) continue;
                    starred[it.itemId] = ItemStars.StarredBase(it);
                    demon[it.itemId]   = SurplusSale.DemonPrice(it.itemId);
                }
            var blob = builder.CreateBlobAssetReference<ItemValueBlob>(Allocator.Persistent);
            builder.Dispose();
            return blob;
        }
    }
}
