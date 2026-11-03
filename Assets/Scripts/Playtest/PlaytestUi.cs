#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Playtest
{
    /// <summary>
    /// Reads what a player can see on the UI Toolkit HUD: visible buttons and text with their
    /// on-screen rectangles (pixels, top-left origin, same as Game View screenshots). Hidden
    /// panels (display:none), invisible or transparent elements, and rows scrolled out of a
    /// ScrollView's viewport are left out, so the list matches the screen, not the UXML.
    /// </summary>
    public static class PlaytestUi
    {
        private const int MaxTextLength = 120;
        private const string ScrollViewportClass = "unity-scroll-view__content-viewport";

        // Overlays and modals worth naming in the state snapshot when shown.
        private static readonly string[] OverlayNames =
        {
            "left-drawer", "placement-overlay", "placement-confirm-popup", "conveyor-overlay",
            "deconstruct-overlay", "building-inspector-panel", "idle-return-modal", "confirm-prompt",
            "tutorial-hint-banner", "demon-panel",
        };

        [Serializable]
        public sealed class UiElementInfo
        {
            public string kind;       // button | toggle | field | slider | text
            public string name;
            public string text;
            public bool   enabled;
            public float  x, y, width, height;    // screen px, top-left origin
            public float  centerX, centerY;
            public string panel;      // nearest named ancestor panel, if any
        }

        [Serializable]
        public sealed class UiListing
        {
            public int screenWidth;
            public int screenHeight;
            public List<UiElementInfo> elements = new();
            public int scrolledOutOfView;   // interactive elements hidden inside a ScrollView
        }

        public static UiListing List(string filter = null, bool interactiveOnly = false)
        {
            var listing = new UiListing { screenWidth = Screen.width, screenHeight = Screen.height };
            foreach (var root in VisibleRoots())
                Walk(root, root, null, null, listing, filter, interactiveOnly);
            return listing;
        }

        /// <summary>
        /// Finds a visible element by exact name, else by text (exact, then contains), ignoring
        /// case. <paramref name="index"/> picks among several matches in listing order.
        /// </summary>
        public static UiElementInfo Find(string nameOrText, int index, out string error)
        {
            error = null;
            var all = List().elements;
            var byName = all.FindAll(e => string.Equals(e.name, nameOrText, StringComparison.OrdinalIgnoreCase));
            var matches = byName.Count > 0 ? byName
                : all.FindAll(e => string.Equals(e.text, nameOrText, StringComparison.OrdinalIgnoreCase));
            if (matches.Count == 0)
                matches = all.FindAll(e => e.text != null &&
                                           e.text.IndexOf(nameOrText, StringComparison.OrdinalIgnoreCase) >= 0);

            // Prefer something tappable over a label with the same text.
            var interactive = matches.FindAll(e => e.kind != "text");
            if (interactive.Count > 0) matches = interactive;

            if (matches.Count == 0) { error = $"No visible element named or reading '{nameOrText}'."; return null; }
            if (index < 0 || index >= matches.Count)
            {
                error = $"'{nameOrText}' matched {matches.Count} element(s); index {index} is out of range.";
                return null;
            }
            return matches[index];
        }

        /// <summary>Named slide panels and overlays that are currently shown.</summary>
        public static List<string> OpenPanels()
        {
            var open = new List<string>();
            foreach (var root in VisibleRoots())
            {
                root.Query<VisualElement>(className: "slide-panel").ForEach(p =>
                {
                    if (IsShown(p) && !string.IsNullOrEmpty(p.name)) open.Add(p.name);
                });
                foreach (var name in OverlayNames)
                {
                    var el = root.Q<VisualElement>(name);
                    if (el != null && IsShown(el) && !open.Contains(name)) open.Add(name);
                }
            }
            return open;
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private static IEnumerable<VisualElement> VisibleRoots()
        {
            var docs = UnityEngine.Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
            Array.Sort(docs, (a, b) => b.sortingOrder.CompareTo(a.sortingOrder));
            foreach (var doc in docs)
            {
                if (!doc.isActiveAndEnabled) continue;
                var root = doc.rootVisualElement;
                if (root?.panel != null && IsSelfVisible(root)) yield return root;
            }
        }

        private static void Walk(VisualElement el, VisualElement root, string panel, Rect? clip,
                                 UiListing listing, string filter, bool interactiveOnly)
        {
            if (!IsSelfVisible(el)) return;

            if (el.ClassListContains("slide-panel") && !string.IsNullOrEmpty(el.name)) panel = el.name;
            if (el.ClassListContains(ScrollViewportClass))
                clip = clip.HasValue ? Intersect(clip.Value, el.worldBound) : el.worldBound;

            string kind = KindOf(el);
            if (kind != null)
            {
                var bound = clip.HasValue ? Intersect(clip.Value, el.worldBound) : el.worldBound;
                if (bound.width < 1f || bound.height < 1f)
                {
                    if (kind != "text") listing.scrolledOutOfView++;
                }
                else if (!interactiveOnly || kind != "text")
                {
                    var info = Describe(el, kind, bound, root, panel);
                    if (string.IsNullOrEmpty(filter) || Matches(info, filter)) listing.elements.Add(info);
                }
                // A button's own label is already reported as the button's text.
                if (kind != "text") return;
            }

            foreach (var child in el.Children())
                Walk(child, root, panel, clip, listing, filter, interactiveOnly);
        }

        private static string KindOf(VisualElement el) => el switch
        {
            Button                     => "button",
            Toggle                     => "toggle",
            TextField                  => "field",
            Slider or SliderInt        => "slider",
            TextElement t when !string.IsNullOrWhiteSpace(t.text) => "text",
            _                          => null,
        };

        private static UiElementInfo Describe(VisualElement el, string kind, Rect panelRect, VisualElement root, string panel)
        {
            var screen = PanelToScreen(root, panelRect);
            return new UiElementInfo
            {
                kind    = kind,
                name    = string.IsNullOrEmpty(el.name) ? null : el.name,
                text    = Truncate(TextOf(el)),
                enabled = el.enabledInHierarchy,
                x       = Mathf.Round(screen.x),     y      = Mathf.Round(screen.y),
                width   = Mathf.Round(screen.width), height = Mathf.Round(screen.height),
                centerX = Mathf.Round(screen.center.x), centerY = Mathf.Round(screen.center.y),
                panel   = panel,
            };
        }

        private static string TextOf(VisualElement el)
        {
            if (el is TextElement t && !string.IsNullOrWhiteSpace(t.text)) return t.text;
            if (el is BaseField<bool> toggle) return $"{toggle.label} = {toggle.value}";
            if (el is TextField field) return $"{field.label} = {field.value}";
            // Buttons built in C# often hold a child label (or a GlyphIcon) instead of text.
            var parts = new List<string>();
            el.Query<TextElement>().ForEach(c => { if (!string.IsNullOrWhiteSpace(c.text) && IsSelfVisible(c)) parts.Add(c.text); });
            if (parts.Count > 0) return string.Join(" | ", parts);
            var glyph = el.Q<GlyphIcon>();
            return glyph != null ? $"[{glyph.Icon} icon]" : null;
        }

        /// <summary>Converts a rect in panel space to screen pixels with a top-left origin.</summary>
        public static Rect PanelToScreen(VisualElement root, Rect panelRect)
        {
            var size = root.panel.visualTree.layout.size;
            float sx = size.x > 0 ? Screen.width / size.x : 1f;
            float sy = size.y > 0 ? Screen.height / size.y : 1f;
            return new Rect(panelRect.x * sx, panelRect.y * sy, panelRect.width * sx, panelRect.height * sy);
        }

        private static bool IsShown(VisualElement el)
        {
            for (var e = el; e != null; e = e.parent)
                if (!IsSelfVisible(e)) return false;
            return true;
        }

        private static bool IsSelfVisible(VisualElement el) =>
            el.resolvedStyle.display != DisplayStyle.None &&
            el.resolvedStyle.visibility != Visibility.Hidden &&
            el.resolvedStyle.opacity > 0.01f;

        private static bool Matches(UiElementInfo info, string filter) =>
            (info.name != null && info.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (info.text != null && info.text.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
            (info.panel != null && info.panel.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0);

        private static Rect Intersect(Rect a, Rect b)
        {
            float xMin = Mathf.Max(a.xMin, b.xMin), yMin = Mathf.Max(a.yMin, b.yMin);
            float xMax = Mathf.Min(a.xMax, b.xMax), yMax = Mathf.Min(a.yMax, b.yMax);
            return xMax > xMin && yMax > yMin ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : Rect.zero;
        }

        private static string Truncate(string s) =>
            s == null ? null : (s.Length <= MaxTextLength ? s : s.Substring(0, MaxTextLength) + "…");
    }
}
#endif
