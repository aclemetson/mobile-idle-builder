using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Sibling MonoBehaviour on the HUD GameObject.
    /// Populates and shows the idle-return-modal when the player opens the game after offline time.
    /// Init() is called by HUDController.OnEnable() after the UIDocument is ready.
    /// Show() is called by HUDController.ShowIdleReturn() from ECSLoadBridge after IsLoaded=true.
    /// </summary>
    public class IdleReturnSubController : MonoBehaviour
    {
        private VisualElement _modal;
        private Label         _timeLabel;
        private Label         _maxLabel;
        private VisualElement _itemList;
        private Label         _entropyLabel;
        private Button        _claimBtn;

        public void Init(VisualElement root)
        {
            _modal        = root.Q("idle-return-modal");
            _timeLabel    = root.Q<Label>("idle-return-time");
            _maxLabel     = root.Q<Label>("idle-return-max");
            _itemList     = root.Q("idle-return-item-list");
            _entropyLabel = root.Q<Label>("idle-return-entropy");
            _claimBtn     = root.Q<Button>("btn-idle-return-claim");

            if (_claimBtn != null)
                _claimBtn.clicked += Hide;
        }

        public void Show(IdleCollectionResult result)
        {
            if (_modal == null || result == null) return;

            // Time-away header
            bool wasCapped = result.CappedSeconds < result.ElapsedSeconds - 1f;
            _timeLabel.text = $"Away: {result.FormatElapsed()}";

            if (_maxLabel != null)
            {
                _maxLabel.text = wasCapped
                    ? $"Max Idle: {result.FormatMax()} (cap reached)"
                    : $"Max Idle: {result.FormatMax()}";
            }

            // Clear previous item rows
            _itemList?.Clear();

            if (!result.HasAnyOutput)
            {
                // Nothing collected — show a single muted message
                var nothing = new Label("Nothing was collected while you were away.");
                nothing.AddToClassList("idle-return-nothing");
                _itemList?.Add(nothing);
                if (_entropyLabel != null) _entropyLabel.text = "";
            }
            else
            {
                // Item rows
                foreach (var kv in result.ItemsEarned)
                {
                    var item     = ItemDatabase.GetStatic(kv.Key);
                    string name  = item?.displayName ?? $"Item #{kv.Key}";
                    var row      = new Label($"{name}  ×{FormatLargeNumber(kv.Value)}");
                    row.AddToClassList("idle-return-value");
                    _itemList?.Add(row);
                }

                // Entropy row
                if (_entropyLabel != null)
                {
                    _entropyLabel.text = result.EntropyEarned > 0
                        ? $"◈ +{FormatLargeNumber(result.EntropyEarned)} entropy"
                        : "";
                    _entropyLabel.RemoveFromClassList("idle-return-nothing");
                    _entropyLabel.AddToClassList("idle-return-value");
                }
            }

            _modal.RemoveFromClassList("hidden");
        }

        private void Hide()
        {
            _modal?.AddToClassList("hidden");
        }

        // ── Number formatting ─────────────────────────────────────────────────

        /// <summary>
        /// Formats a non-negative integer for idle-game display.
        ///   &lt; 10 000            → "9,999"
        ///   10 000 – 999 999    → "12.3K"
        ///   1 M – 999 M         → "1.23M"
        ///   1 B – 999 B         → "1.23B"
        ///   1 T – 999 T         → "1.23T"
        ///   ≥ 1 000 T           → "1.23×10¹⁵" (scientific notation)
        /// </summary>
        public static string FormatLargeNumber(long value)
        {
            if (value < 10_000L)
                return value.ToString("N0");

            if (value < 1_000_000L)
                return $"{value / 1_000.0:F1}K";

            if (value < 1_000_000_000L)
                return $"{value / 1_000_000.0:F2}M";

            if (value < 1_000_000_000_000L)
                return $"{value / 1_000_000_000.0:F2}B";

            if (value < 1_000_000_000_000_000L)
                return $"{value / 1_000_000_000_000.0:F2}T";

            // Scientific notation for truly astronomical values
            double v    = value;
            int    exp  = (int)Math.Floor(Math.Log10(v));
            double coef = v / Math.Pow(10, exp);
            return $"{coef:F2}×10{ToSuperscript(exp)}";
        }

        private static string ToSuperscript(int n)
        {
            string digits = n.ToString();
            var sb = new System.Text.StringBuilder();
            foreach (char c in digits)
            {
                sb.Append(c switch
                {
                    '-' => '⁻',
                    '0' => '⁰',
                    '1' => '¹',
                    '2' => '²',
                    '3' => '³',
                    '4' => '⁴',
                    '5' => '⁵',
                    '6' => '⁶',
                    '7' => '⁷',
                    '8' => '⁸',
                    '9' => '⁹',
                    _   => c
                });
            }
            return sb.ToString();
        }
    }
}
