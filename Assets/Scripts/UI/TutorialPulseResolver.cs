using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Decides which element the tutorial should pulse so the player can actually see it (#202).
    ///
    /// A tutorial step names the button it ultimately wants tapped (e.g. "btn-place-conveyor", the
    /// Place button on the Builder's Conveyor Belt card). That button is often not visible yet: it
    /// lives in a closed slide panel, which is opened from the side drawer, which may be closed too.
    /// Pulsing it there shows nothing. Instead, pulse the first step of the path the player has not
    /// taken yet:
    ///   drawer closed                  -> the drawer handle
    ///   drawer open, panel closed      -> the panel's drawer button (e.g. "btn-buildings")
    ///   panel open                     -> the target itself
    /// Re-resolved every pulse cycle, so the pulse follows the player along the path.
    ///
    /// Build-menu Place buttons are named "btn-place-&lt;building id&gt;" (and "btn-place-conveyor");
    /// they only exist once the Builder list has been built, so a missing one resolves to the
    /// Builder's own button.
    /// </summary>
    public static class TutorialPulseResolver
    {
        public const string DrawerHandle    = "btn-drawer-handle";
        public const string DrawerName      = "left-drawer";
        public const string DrawerOpenClass = "left-drawer--open";
        public const string PlacePrefix     = "btn-place-";
        public const string BuilderButton   = "btn-buildings";

        // Slide panel name -> the drawer button that opens it.
        private static readonly Dictionary<string, string> PanelOpeners = new()
        {
            ["buildings-panel"]     = BuilderButton,
            ["recipe-panel"]        = "btn-recipes",
            ["codex-panel"]         = "btn-codex",
            ["research-panel"]      = "btn-research",
            ["upgrades-panel"]      = "btn-upgrades",
            ["managers-panel"]      = "btn-managers",
            ["daily-panel"]         = "btn-daily",
            ["rewards-panel"]       = "btn-rewards",
            ["achievements-panel"]  = "btn-achievements",
            ["worlds-panel"]        = "btn-worlds",
            ["sites-panel"]         = "btn-sites",
            ["shop-panel"]          = "btn-shop",
            ["megastructure-panel"] = "btn-megastructure",
            ["pvp-panel"]           = "btn-pvp",
            ["settings-panel"]      = "btn-settings", // top-bar button, not in the drawer
        };

        /// <summary>Name of the Build-menu Place button for a building id (or "conveyor").</summary>
        public static string PlaceButtonName(string buildingId) => PlacePrefix + buildingId;

        /// <summary>
        /// The element to pulse for <paramref name="targetId"/> under <paramref name="root"/>, or null
        /// when nothing on the path exists.
        /// </summary>
        public static VisualElement Resolve(VisualElement root, string targetId)
        {
            if (root == null || string.IsNullOrEmpty(targetId)) return null;
            return Resolve(root, targetId, depth: 0);
        }

        private static VisualElement Resolve(VisualElement root, string targetId, int depth)
        {
            if (depth > 4) return null; // opener chains are short; guards a bad map entry

            var target = root.Q(targetId);
            if (target == null)
            {
                // Place buttons are built when the Builder opens: guide to the Builder first.
                if (targetId.StartsWith(PlacePrefix, System.StringComparison.Ordinal))
                    return Resolve(root, BuilderButton, depth + 1);
                return null;
            }

            // Inside a closed slide panel: guide to the panel's opener instead.
            for (var e = target.parent; e != null; e = e.parent)
            {
                if (!e.ClassListContains("slide-panel")) continue;
                if (IsHidden(e) && PanelOpeners.TryGetValue(e.name, out var opener))
                    return Resolve(root, opener, depth + 1);
                break;
            }

            // Inside the closed side drawer: guide to the drawer handle.
            for (var e = target.parent; e != null; e = e.parent)
            {
                if (e.name != DrawerName) continue;
                if (!e.ClassListContains(DrawerOpenClass))
                    return root.Q(DrawerHandle) ?? target;
                break;
            }

            return target;
        }

        private static bool IsHidden(VisualElement e) =>
            e.ClassListContains("hidden") || e.style.display == DisplayStyle.None;
    }
}
