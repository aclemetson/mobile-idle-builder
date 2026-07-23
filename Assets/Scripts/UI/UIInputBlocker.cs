using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Single source of truth for "is this screen-space pointer over a UI surface that should
    /// swallow world input?" Used by the camera (so a drag that starts on a menu does not pan
    /// the map) and by every world-tap router (so tapping a menu never activates the world
    /// behind it).
    ///
    /// Panels register themselves (HUD + dev console live on separate UIDocuments / panels),
    /// and the check walks every registered panel. A surface blocks when the picked element or
    /// any ancestor carries one of <see cref="BlockingClasses"/> or the universal marker class
    /// <see cref="MarkerClass"/>, or is an interactive control (Button / Toggle / Slider /
    /// TextField). Transparent HUD chrome (root, grid-area, placement overlays) is
    /// picking-mode=Ignore or carries none of these classes, so the world stays interactive
    /// beneath it.
    ///
    /// Coordinate convention matches HUDController.ScreenPointInElement (the proven placement
    /// hit-test): pass the raw Input System pointer position to RuntimePanelUtils.ScreenToPanel
    /// with NO manual Screen.height - y flip — the conversion handles the Y inversion itself.
    /// </summary>
    public static class UIInputBlocker
    {
        /// <summary>Add this class to any new menu/modal/overlay that should block world input.</summary>
        public const string MarkerClass = "blocks-world-input";

        // Structural classes already worn by every current menu surface, so existing panels work
        // with no per-element edits. New surfaces that don't reuse one of these should add MarkerClass.
        private static readonly string[] BlockingClasses =
        {
            MarkerClass,
            "slide-panel",        // every left-drawer panel (recipes, research, shop, settings, ...)
            "left-drawer",        // the side menu itself
            "drawer-backdrop",    // tap-outside scrim behind the drawer
            "top-bar",            // persistent top status/action bar
            "output-selector",    // field output picker + building-inspector panel
            "demon-panel",        // Maxwell's Demon panel
            "idle-return-modal",  // offline-earnings modal
            "placement-bar",      // solid action strip (placement / conveyor / deconstruct overlays)
            "dev-console",        // dev console overlay (separate panel)
        };

        // When true, IsPointerOverUI emits per-panel hit-test details at GameLogger.Develop tier.
        // Callers flip it on around a single check (e.g. a press) so the dump isn't per-frame spam.
        public static bool VerboseLogging;

        private static readonly List<UIDocument> s_documents = new();
        private static readonly HashSet<object>  s_modals    = new();

        // Buttons that must be driven by an Input-System tap instead of UI Toolkit pointer events.
        // A UI Toolkit ScrollView captures touch for scroll recognition and drops/defers the
        // pointer-up for buttons inside it, so Button.clicked / pointer-event manipulators never
        // fire on device. PlayerInputRouter detects the tap via the Input System (the same reliable
        // path the grid uses) and routes it here; we hit-test the panel and invoke the button under
        // the finger. Registered by TapGestureManipulator on attach; cleared on detach.
        private static readonly Dictionary<VisualElement, Action> s_tapHandlers = new();

        public static void Register(UIDocument document)
        {
            if (document != null && !s_documents.Contains(document))
                s_documents.Add(document);
        }

        public static void Unregister(UIDocument document) => s_documents.Remove(document);

        /// <summary>
        /// Marks a full-screen / debug overlay as open. While any modal is open, ALL world input
        /// is blocked, regardless of where the pointer is. Used by overlays where per-element
        /// hit-testing is unreliable or undesirable (e.g. the dev console, which lives on a panel
        /// shared with the HUD and whose precise bounds we don't want to depend on). Pass a stable
        /// owner (usually `this`) and call again with active=false to clear.
        /// </summary>
        public static void SetModal(object owner, bool active)
        {
            if (owner == null) return;
            if (active) s_modals.Add(owner);
            else        s_modals.Remove(owner);
        }

        // ── Input-System-driven UI taps ──────────────────────────────────────────

        /// <summary>Registers an action to fire when a confirmed Input-System tap lands on this element.</summary>
        public static void RegisterTap(VisualElement element, Action onTap)
        {
            if (element != null && onTap != null) s_tapHandlers[element] = onTap;
        }

        /// <summary>Removes a tap handler registered via <see cref="RegisterTap"/>.</summary>
        public static void UnregisterTap(VisualElement element)
        {
            if (element != null) s_tapHandlers.Remove(element);
        }

        /// <summary>
        /// Routes a confirmed Input-System tap (screen-space, bottom-left origin) to the registered
        /// button under the finger, bypassing UI Toolkit's touch pipeline. Returns true if a handler
        /// was found and invoked (or found-but-disabled), so the caller knows the tap was consumed by
        /// UI. Uses the same panel hit-test and coordinate convention as <see cref="IsPointerOverUI"/>.
        /// </summary>
        public static bool TryHandleUITap(Vector2 screenPos)
        {
            if (s_tapHandlers.Count == 0) return false;

            for (int i = 0; i < s_documents.Count; i++)
            {
                var doc   = s_documents[i];
                var panel = doc != null ? doc.rootVisualElement?.panel : null;
                if (panel == null) continue;
                if (AlreadyTested(panel, i)) continue;

                Vector2 flipped  = new Vector2(screenPos.x, Screen.height - screenPos.y);
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);

                var picked = panel.Pick(panelPos);
                for (var e = picked; e != null; e = e.parent)
                {
                    if (!s_tapHandlers.TryGetValue(e, out var action)) continue;
                    if (e.enabledInHierarchy)
                    {
                        GameLogger.Develop($"[Tap] Input-System tap -> invoking '{e.name}'");
                        action.Invoke();
                    }
                    return true; // consumed: found the target (invoked, or skipped because disabled)
                }
            }
            return false;
        }

        /// <summary>
        /// True if the screen point is over a blocking UI surface on any registered panel, or if
        /// any modal overlay is open. Safe to call before any panel exists (returns false).
        ///
        /// Primary check is IPanel.Pick (UI Toolkit's own hit-test, which honours pickingMode,
        /// clipping, transforms and z-order) classified through IsBlockingElement; a resolved-bounds
        /// tree-walk (HitTestBlocking) is kept as a fallback. Documents that share a PanelSettings
        /// share one runtime panel, so we test each panel once via its visualTree (covering every
        /// document on it).
        /// </summary>
        public static bool IsPointerOverUI(Vector2 screenPos)
        {
            if (s_modals.Count > 0) return true;

            for (int i = 0; i < s_documents.Count; i++)
            {
                var doc   = s_documents[i];
                var panel = doc != null ? doc.rootVisualElement?.panel : null;
                if (panel == null) continue;

                if (AlreadyTested(panel, i)) continue;

                // Input System screen coords are bottom-left origin; panel coords are top-left.
                // RuntimePanelUtils.ScreenToPanel scales but does NOT flip Y in this project, so we
                // flip here. Without this, top-of-screen UI maps to the bottom of the panel and
                // never blocks, leaking taps to the world behind it.
                Vector2 flipped  = new Vector2(screenPos.x, Screen.height - screenPos.y);
                Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);

                // UI Toolkit's own hit-test: respects pickingMode, clipping, transforms and
                // z-order, so it catches taps the manual worldBound walk misses (e.g. the conveyor
                // toolbar). picking-mode=Ignore chrome (root, grid-area, world-anchored popups,
                // transparent overlays) returns null/non-blocking here, so the world stays
                // interactive beneath transparent overlays.
                var picked = panel.Pick(panelPos);
                bool blockingPick = picked != null && IsBlockingElement(picked);

                // Fallback: resolved-bounds tree-walk (belt-and-suspenders).
                bool walkBlocked = HitTestBlocking(panel.visualTree, panelPos);

                if (VerboseLogging)
                {
                    string pickDesc = picked == null
                        ? "null"
                        : $"{picked.GetType().Name}:'{picked.name}' classes=[{string.Join(",", picked.GetClasses())}] pm={picked.pickingMode}";
                    GameLogger.Develop($"[UIBlock] doc#{i} '{(doc != null ? doc.gameObject.name : "?")}' screen={screenPos} panelPos={panelPos} picked={pickDesc} blockingPick={blockingPick} walk={walkBlocked}");
                }

                if (blockingPick || walkBlocked)
                    return true;
            }
            return false;
        }

        private static bool AlreadyTested(IPanel panel, int upToIndex)
        {
            for (int j = 0; j < upToIndex; j++)
            {
                var pj = s_documents[j] != null ? s_documents[j].rootVisualElement?.panel : null;
                if (pj == panel) return true;
            }
            return false;
        }

        /// <summary>
        /// Recursively tests whether any visible blocking surface in this subtree contains the
        /// (panel-space) point. Hidden subtrees (display:none, e.g. closed slide-panels) are
        /// pruned so a collapsed panel never blocks.
        /// </summary>
        private static bool HitTestBlocking(VisualElement el, Vector2 panelPos)
        {
            if (el == null || el.resolvedStyle.display == DisplayStyle.None)
                return false;

            if (HasBlockingSelf(el) && el.worldBound.Contains(panelPos))
                return true;

            for (int i = 0; i < el.childCount; i++)
                if (HitTestBlocking(el[i], panelPos))
                    return true;

            return false;
        }

        /// <summary>
        /// Pure decision: does this picked element (or any ancestor) block world input?
        /// Exposed for testing — no panel required.
        /// </summary>
        public static bool IsBlockingElement(VisualElement picked)
        {
            for (var e = picked; e != null; e = e.parent)
                if (HasBlockingSelf(e))
                    return true;
            return false;
        }

        /// <summary>Does this single element (ignoring ancestors) carry a blocking class or interactive type?</summary>
        private static bool HasBlockingSelf(VisualElement e)
        {
            if (e is Button || e is Toggle || e is Slider || e is TextField)
                return true;
            for (int i = 0; i < BlockingClasses.Length; i++)
                if (e.ClassListContains(BlockingClasses[i]))
                    return true;
            return false;
        }
    }
}
