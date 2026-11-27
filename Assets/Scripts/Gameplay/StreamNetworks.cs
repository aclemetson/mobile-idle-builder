using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure grouping of stream tiles into separate streams (unit-tested). Tiles linked by their downstream link are
    /// one stream, so branches that merge, each with its own Spring, become one stream whose flows add; streams that
    /// never join stay apart and are tracked apart. Union-find over any key type.
    /// </summary>
    public sealed class StreamNetworks<T>
    {
        readonly Dictionary<T, T> _parent = new();

        /// <summary>Registers <paramref name="tile"/> as its own stream if it is new.</summary>
        public void Add(T tile)
        {
            if (!_parent.ContainsKey(tile)) _parent[tile] = tile;
        }

        /// <summary>Joins the streams of <paramref name="a"/> and <paramref name="b"/> (a tile and the one it flows into).</summary>
        public void Link(T a, T b)
        {
            Add(a); Add(b);
            T ra = Find(a), rb = Find(b);
            if (!EqualityComparer<T>.Default.Equals(ra, rb)) _parent[ra] = rb;
        }

        /// <summary>The stream <paramref name="tile"/> belongs to (one representative tile per stream).</summary>
        public T Find(T tile)
        {
            Add(tile);
            T root = tile;
            while (!EqualityComparer<T>.Default.Equals(_parent[root], root)) root = _parent[root];
            // Path compression.
            while (!EqualityComparer<T>.Default.Equals(_parent[tile], root))
            {
                T next = _parent[tile];
                _parent[tile] = root;
                tile = next;
            }
            return root;
        }

        /// <summary>How many separate streams the registered tiles form.</summary>
        public int Count
        {
            get
            {
                var roots = new HashSet<T>();
                foreach (var t in new List<T>(_parent.Keys)) roots.Add(Find(t));
                return roots.Count;
            }
        }
    }

    /// <summary>One stream's totals: its Springs' flow, what its buildings draw, and how many Springs feed it.</summary>
    public struct StreamTotals
    {
        public float Supply;
        public float Draw;
        public int   Springs;

        /// <summary>True when its buildings want more water than its own Springs give.</summary>
        public bool Overdrawn => Draw > Supply + 1e-4f;
    }
}
