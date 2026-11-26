using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure text and small-element helpers shared by the HUD and its panel sub-controllers (#286: moved out of
    /// HUDController so it is only the composition root). No state; unit-tested through HUDControllerTests and
    /// the panels' tests.
    /// </summary>
    public static class HudText
    {
        /// <summary>Formats the power readout for the top bar label.</summary>
        internal static string FormatPowerLabel(float current, float max) =>
            max > 0f ? $"⚡ {current:0.#} / {max:0.#} eV" : "⚡ No power";

        /// <summary>
        /// Builds a background-image VisualElement for an item's tile sprite, or null when the
        /// item has no icon yet (callers skip adding it so nothing breaks). Shared by the recipe
        /// list, ingredient chips, codex, and the building inspector's recipe picker.
        /// </summary>
        internal static VisualElement MakeItemIcon(Sprite icon, string ussClass)
        {
            if (icon == null) return null;
            var el = new VisualElement();
            el.AddToClassList(ussClass);
            el.style.backgroundImage = new StyleBackground(icon);
            return el;
        }

        public static string FormatResearchTimer(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = System.TimeSpan.FromSeconds(System.Math.Ceiling(seconds));
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:D2}s";
            return $"{ts.Seconds}s";
        }

        internal static string ShortNumber(long v)
        {
            if (v >= 1_000_000_000_000) return (v / 1e12).ToString(v >= 100_000_000_000_000 ? "0" : "0.#") + "T";
            if (v >= 1_000_000_000)     return (v / 1e9).ToString(v >= 100_000_000_000 ? "0" : "0.#") + "B";
            if (v >= 1_000_000)         return (v / 1e6).ToString(v >= 100_000_000 ? "0" : "0.#") + "M";
            if (v >= 10_000)            return (v / 1e3).ToString(v >= 100_000 ? "0" : "0.#") + "k";
            return v.ToString("N0");
        }

        /// <summary>A research card's time: "Instant", "Instant · 2 free left" (Free Research, #491) or "⏳ 1:30".</summary>
        internal static string ResearchTimeText(int effectiveSeconds, int freeLeft) =>
            effectiveSeconds <= 0 ? "Instant"
            : freeLeft > 0 ? $"Instant · {freeLeft} free left"
            : $"⏳ {FormatResearchTimer(effectiveSeconds)}";

        /// <summary>The lock line on a recipe known from an earlier run but gated by research now (#490).</summary>
        internal static string ResearchLockText(string researchName) => $"Research {researchName} to unlock";

        /// <summary>Recipe-row / inspector line for what one item sells for, e.g. "Worth ◈ 2.5 each".</summary>
        internal static string WorthEachText(float price, int stars = 0) =>
            stars > 0
                ? $"Worth {NumberFormat.Entropy(price)} each · {StarBadge(stars)}"
                : $"Worth {NumberFormat.Entropy(price)} each";

        /// <summary>Tournament stars on an item, e.g. "★3 +60%" (PvE phase 2, <see cref="ItemStars"/>).</summary>
        internal static string StarBadge(int stars) =>
            stars <= 0 ? "" : $"★{stars} +{Mathf.RoundToInt(stars * ItemStars.BonusPerStar * 100f)}%";

        /// <summary>Recipe-row / inspector line for fusion byproducts that escape as entropy.</summary>
        internal static string EscapeBonusText(float entropyPerCraft) =>
            $"Releases particles: +{entropyPerCraft:0.#} entropy per craft";

        internal static string FormatBuildCost(long cost) => cost > 0 ? $"◈ {cost:N0}" : "Free";

        /// <summary>Clamps a popup's top-left so the whole popup stays inside the panel with a margin.</summary>
        public static Vector2 ClampPopup(Vector2 desired, Vector2 size, Vector2 panelSize, float margin)
        {
            float maxX = Mathf.Max(margin, panelSize.x - size.x - margin);
            float maxY = Mathf.Max(margin, panelSize.y - size.y - margin);
            return new Vector2(Mathf.Clamp(desired.x, margin, maxX), Mathf.Clamp(desired.y, margin, maxY));
        }

        /// <summary>True if the pointer moved no more than threshold between down and up (a tap, not a pan).</summary>
        public static bool IsTap(Vector2 down, Vector2 up, float threshold)
            => (up - down).sqrMagnitude <= threshold * threshold;

        /// <summary>The conveyor bar's instruction while Up is on.</summary>
        internal const string ConveyorRaiseLabel = "Tap where the overpass lands";

        /// <summary>The conveyor bar's instruction for a trace state. Pure (unit-tested); short so it fits the bar.</summary>
        internal static string ConveyorBarLabel(bool destroy, bool hasStart, bool canPlace, bool ended)
        {
            if (destroy)   return "Tap a belt to remove";
            if (!hasStart) return "Tap a start";
            if (ended)     return "Joins a belt: place it";
            return canPlace ? "Tap the next point, or place" : "Tap the next point";
        }

        /// <summary>True when the HUD is wider than it is tall (the phone is held sideways).</summary>
        internal static bool IsLandscape(float width, float height) => width > height && height > 0f;

        /// <summary>
        /// Calculates how much prestige currency a prestige would award.
        /// Must stay in sync with the formula in <see cref="PrestigeSystem"/>.
        /// </summary>
        internal static long CalculatePrestigePreview(double netWorth)
        {
            var cfg    = GameBootstrap.Instance?.gameConfig;
            float pbase  = cfg != null ? cfg.prestigeBaseValue     : 5000f;
            float pscale = cfg != null ? cfg.prestigeCurrencyScale : 50f;
            if (pbase <= 0f || netWorth <= 0f) return 0L;
            long earned = (long)System.Math.Max(0, System.Math.Floor(System.Math.Log10(netWorth / pbase) * pscale));
            float gainBonus = PersistentUpgradeService.Instance?.GetEffect(UpgradeEffectType.PrestigeGainMultiplier) ?? 0f;
            if (gainBonus > 0f) earned = (long)(earned * (1f + gainBonus));
            return earned;
        }

        internal static string BuildInputsText(RecipeJson recipe)
        {
            if (recipe.inputs == null || recipe.inputs.Count == 0) return "";
            var parts = recipe.inputs.Select(i =>
            {
                var item = ItemDatabase.Instance?.Get(i.id);
                string sym = item?.symbol ?? item?.displayName ?? i.id;
                return $"{i.quantity}× {sym}";
            });
            return string.Join("  +  ", parts);
        }

        internal static VisualElement BuildIngredientsUI(
            RecipeJson recipe,
            Dictionary<int, int> inventoryCounts)
        {
            var row = new VisualElement();
            row.AddToClassList("recipe-inputs-row");

            if (recipe.inputs == null || recipe.inputs.Count == 0)
                return row;

            bool first = true;
            foreach (var input in recipe.inputs)
            {
                if (!first)
                {
                    var sep = new Label("+");
                    sep.AddToClassList("recipe-input-sep");
                    row.Add(sep);
                }
                first = false;

                var item   = ItemDatabase.Instance?.Get(input.id);
                string sym = item?.symbol ?? item?.displayName ?? input.id;

                int itemId = ItemDatabase.Instance?.GetItemId(input.id) ?? -1;
                int count  = (itemId >= 0 && inventoryCounts.TryGetValue(itemId, out int c)) ? c : 0;

                var inputIcon = MakeItemIcon(item?.icon, "recipe-input-icon");
                if (inputIcon != null) row.Add(inputIcon);

                var lbl = new Label($"{count}/{input.quantity} {sym}");
                lbl.AddToClassList(count >= input.quantity ? "recipe-input-met" : "recipe-input-missing");
                row.Add(lbl);
            }

            return row;
        }
    }
}
