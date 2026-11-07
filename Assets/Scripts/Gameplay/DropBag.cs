using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Deals drop indices from a shuffled bag so manual-tap yields keep their ratio over short runs
    /// (0.5/0.5 gives one of each every 2 taps). Pure logic; the RNG is injectable for tests.
    /// </summary>
    public class DropBag
    {
        public const int MaxBagSize = 20;

        private readonly List<int> _template = new List<int>();
        private readonly List<int> _bag = new List<int>();
        private readonly Func<int, int> _nextInt; // returns [0, maxExclusive)

        /// <summary>False when the weights can't be expressed as a small integer ratio; caller should fall back to a plain roll.</summary>
        public bool IsUsable => _template.Count > 0;

        public DropBag(IList<float> weights, Func<int, int> nextInt)
        {
            _nextInt = nextInt;
            if (weights == null || weights.Count == 0) return;

            float min = float.MaxValue;
            for (int i = 0; i < weights.Count; i++)
                if (weights[i] > 0f && weights[i] < min) min = weights[i];
            if (min == float.MaxValue) return;

            var counts = new int[weights.Count];
            int total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] <= 0f) continue;
                float ratio = weights[i] / min;
                int n = (int)Math.Round(ratio);
                if (n < 1 || Math.Abs(ratio - n) > 0.01f) return; // not an integer ratio
                counts[i] = n;
                total += n;
            }
            if (total > MaxBagSize) return;

            for (int i = 0; i < counts.Length; i++)
                for (int k = 0; k < counts[i]; k++)
                    _template.Add(i);
        }

        /// <summary>Next drop index from the bag, refilling and shuffling (Fisher-Yates) when empty. -1 if not usable.</summary>
        public int Next()
        {
            if (_template.Count == 0) return -1;
            if (_bag.Count == 0)
            {
                _bag.AddRange(_template);
                for (int i = _bag.Count - 1; i > 0; i--)
                {
                    int j = _nextInt(i + 1);
                    (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
                }
            }
            int last = _bag.Count - 1;
            int result = _bag[last];
            _bag.RemoveAt(last);
            return result;
        }
    }
}
