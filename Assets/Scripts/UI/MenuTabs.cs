using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Tab bar + pages shared by the Builder (#487) and Recipes (#488) menus. For a menu with prefix "builder",
    /// buttons are named "builder-tab-{tab}" and pages "builder-page-{tab}" with class <see cref="PageClass"/>,
    /// so TutorialPulseResolver can guide to a hidden page's tab button.
    /// </summary>
    public static class MenuTabs
    {
        public const string PageClass = "tab-page";

        public static string PageName(string prefix, string tab) => $"{prefix}-page-{tab}";
        public static string TabButtonName(string prefix, string tab) => $"{prefix}-tab-{tab}";

        public static VisualElement MakeTabBar(string prefix, IEnumerable<string> tabs, Func<string, string> label,
                                               Action<string> onSelect)
        {
            var bar = new VisualElement { name = prefix + "-tabs" };
            bar.AddToClassList("research-tabs");
            bar.AddToClassList("menu-tabs");
            foreach (var tab in tabs)
            {
                string t = tab;
                var b = new Button(() => onSelect?.Invoke(t)) { text = label?.Invoke(t) ?? t, name = TabButtonName(prefix, t) };
                b.AddToClassList("research-tab");
                bar.Add(b);
            }
            return bar;
        }

        public static VisualElement MakePage(string prefix, string tab, string extraClass)
        {
            var page = new VisualElement { name = PageName(prefix, tab) };
            page.AddToClassList(PageClass);
            if (!string.IsNullOrEmpty(extraClass)) page.AddToClassList(extraClass);
            return page;
        }

        /// <summary>Shows <paramref name="tab"/>'s page, marks its button, hides tabs with no cards.</summary>
        public static void ShowTab(string prefix, VisualElement bar, IReadOnlyDictionary<string, VisualElement> pages, string tab)
        {
            foreach (var kv in pages)
            {
                HUDController.SetElementVisible(kv.Value, kv.Key == tab);
                var btn = bar?.Q<Button>(TabButtonName(prefix, kv.Key));
                if (btn == null) continue;
                btn.EnableInClassList("research-tab--selected", kv.Key == tab);
                HUDController.SetElementVisible(btn, kv.Value.childCount > 0);
            }
        }

        /// <summary>The tab to show: the remembered one if it has cards, else the first in order that has any.</summary>
        public static string PickTab(string remembered, ICollection<string> tabsWithCards, IList<string> order)
        {
            if (!string.IsNullOrEmpty(remembered) && tabsWithCards.Contains(remembered)) return remembered;
            foreach (var t in order) if (tabsWithCards.Contains(t)) return t;
            return order.Count > 0 ? order[0] : null;
        }
    }
}
