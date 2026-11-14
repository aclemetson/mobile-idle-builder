namespace MobileIdleBuilder
{
    /// <summary>
    /// Rolling per-second rate of a monotonically increasing total (e.g. AutomationIncome.Total).
    /// Pure class, no Unity dependencies. Keeps the most recent <c>capacity</c> samples in a ring buffer.
    /// </summary>
    public class RollingRate
    {
        private readonly double[] _times;
        private readonly double[] _totals;
        private int _start;   // index of the oldest sample
        private int _count;

        public RollingRate(int capacity = 90)
        {
            if (capacity < 2) capacity = 2;
            _times  = new double[capacity];
            _totals = new double[capacity];
        }

        public int Count => _count;

        /// <summary>Records a sample. A drop in total (entity recreated by a reload or prestige) restarts the buffer.</summary>
        public void Add(double time, double total)
        {
            if (_count > 0)
            {
                int newest = (_start + _count - 1) % _times.Length;
                if (total < _totals[newest]) Clear();
            }

            if (_count == _times.Length)
            {
                _start = (_start + 1) % _times.Length;   // overwrite the oldest
                _count--;
            }
            int idx = (_start + _count) % _times.Length;
            _times[idx]  = time;
            _totals[idx] = total;
            _count++;
        }

        /// <summary>
        /// (newest total - total of the oldest sample still inside the window) / time between them.
        /// 0 with fewer than 2 samples or no time span. Before a full window exists, uses the samples so far.
        /// </summary>
        public double Rate(double windowSeconds)
        {
            if (_count < 2) return 0;
            int newest = (_start + _count - 1) % _times.Length;
            int oldest = OldestInWindow(windowSeconds);
            double span = _times[newest] - _times[oldest];
            if (span <= 0) return 0;
            return (_totals[newest] - _totals[oldest]) / span;
        }

        /// <summary>Seconds of history behind <see cref="Rate"/> (capped at the window); 0 with fewer than 2 samples.</summary>
        public double SpanSeconds(double windowSeconds)
        {
            if (_count < 2) return 0;
            int newest = (_start + _count - 1) % _times.Length;
            return _times[newest] - _times[OldestInWindow(windowSeconds)];
        }

        int OldestInWindow(double windowSeconds)
        {
            int newest = (_start + _count - 1) % _times.Length;
            double newestTime = _times[newest];
            for (int i = 0; i < _count; i++)
            {
                int idx = (_start + i) % _times.Length;
                if (newestTime - _times[idx] <= windowSeconds) return idx;
            }
            return _start;
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
        }
    }
}
