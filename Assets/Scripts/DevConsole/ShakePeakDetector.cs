#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;

namespace MobileIdleBuilder.Dev
{
    // Pure shake-detection algorithm: counts acceleration rising-edges within a rolling
    // time window. Extracted so the logic can be exercised by EditMode tests.
    internal sealed class ShakePeakDetector
    {
        private readonly Queue<float> _peakTimes = new();
        private bool _wasAbove;

        internal float Threshold { get; }
        internal float Window { get; }
        internal int PeaksRequired { get; }

        internal ShakePeakDetector(float threshold, float window, int peaksRequired)
        {
            Threshold     = threshold;
            Window        = window;
            PeaksRequired = peaksRequired;
        }

        // Feed one accelerometer magnitude sample at the given timestamp.
        // Returns true only on the frame the peak count crosses PeaksRequired
        // (queue is cleared so it must accumulate fresh peaks again to re-trigger).
        internal bool Feed(float magnitude, float timestamp)
        {
            bool isAbove = magnitude > Threshold;
            bool triggered = false;

            if (isAbove && !_wasAbove)
            {
                _peakTimes.Enqueue(timestamp);
                while (_peakTimes.Count > 0 && timestamp - _peakTimes.Peek() > Window)
                    _peakTimes.Dequeue();

                if (_peakTimes.Count >= PeaksRequired)
                {
                    _peakTimes.Clear();
                    triggered = true;
                }
            }

            _wasAbove = isAbove;
            return triggered;
        }

        internal void Reset()
        {
            _peakTimes.Clear();
            _wasAbove = false;
        }
    }
}
#endif
