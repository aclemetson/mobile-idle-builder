using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Swallows taps on the left drawer while it is still sliding in. The drawer handle sits on the left
    /// edge over the map, so a quick second tap on the same spot (a double tap, or tapping a field next to
    /// the handle) landed on whichever nav row had just slid under the finger and opened that panel
    /// (#256: a playtest opened Worlds this way). Once the slide finishes, rows react normally.
    /// </summary>
    public sealed class DrawerTapGuard
    {
        private readonly double       _settleSeconds;
        private readonly Func<double> _clock;
        private double                _openedAt = double.NegativeInfinity;

        public DrawerTapGuard(double settleSeconds, Func<double> clock = null)
        {
            _settleSeconds = settleSeconds;
            _clock         = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>Call when the drawer starts opening.</summary>
        public void MarkOpened() => _openedAt = _clock();

        /// <summary>True while the drawer is still settling, so a tap on it was not aimed at a row.</summary>
        public bool ShouldBlock => _clock() - _openedAt < _settleSeconds;

        /// <summary>
        /// Intercepts presses on <paramref name="drawer"/> in the trickle-down phase, before any row
        /// button sees them. Pointer up and click are caught too, so a press that slipped in can't fire.
        /// </summary>
        public void Attach(VisualElement drawer)
        {
            if (drawer == null) return;
            drawer.RegisterCallback<PointerDownEvent>(Block, TrickleDown.TrickleDown);
            drawer.RegisterCallback<PointerUpEvent>(Block, TrickleDown.TrickleDown);
            drawer.RegisterCallback<ClickEvent>(Block, TrickleDown.TrickleDown);
        }

        private void Block(EventBase evt)
        {
            if (ShouldBlock) evt.StopImmediatePropagation();
        }
    }
}
