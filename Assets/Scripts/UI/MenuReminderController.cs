using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Red reminder dots. A source says "something here needs the player" (an achievement to claim, later a
    /// shop item, ...) and its button gets a dot. Sources registered as <b>menu rows</b> also light the dot on
    /// the ≡ drawer handle, so the main screen shows when anything inside the menu is waiting.
    ///
    /// Polled twice a second rather than event-driven, so any source is just a <c>Func&lt;bool&gt;</c> and no
    /// system needs to know about the HUD. Added to the HUD GameObject by <see cref="HUDController"/>
    /// (GetComponent ?? AddComponent), so no scene wiring is needed.
    ///
    /// To add one: <c>reminders.Register("btn-shop", () => ShopHasSomethingNew(), menuRow: true);</c>
    /// </summary>
    public class MenuReminderController : MonoBehaviour
    {
        public const string DotClass      = "reminder-dot";
        public const string HandleButton  = "btn-drawer-handle";
        const float RefreshSeconds = 0.5f;

        class Source
        {
            public string        ButtonName;
            public Func<bool>    Has;
            public bool          MenuRow;
            public VisualElement Dot;
        }

        readonly List<Source> _sources = new();
        VisualElement _root;
        VisualElement _handleDot;
        float         _nextRefresh;

        public void Init(VisualElement root)
        {
            _root      = root;
            _handleDot = AttachDot(root?.Q<Button>(HandleButton));
            foreach (var s in _sources) s.Dot ??= AttachDot(root?.Q<Button>(s.ButtonName));
            Refresh();
        }

        /// <summary>
        /// Puts a dot on <paramref name="buttonName"/> while <paramref name="hasReminder"/> is true. A
        /// <paramref name="menuRow"/> (a drawer row) also lights the drawer handle's dot; a button inside a
        /// panel (such as an achievements tab) does not.
        /// </summary>
        public void Register(string buttonName, Func<bool> hasReminder, bool menuRow)
        {
            if (string.IsNullOrEmpty(buttonName) || hasReminder == null) return;
            _sources.RemoveAll(x => x.ButtonName == buttonName);   // re-registering (HUD re-enabled) replaces
            var s = new Source { ButtonName = buttonName, Has = hasReminder, MenuRow = menuRow };
            if (_root != null) s.Dot = AttachDot(_root.Q<Button>(buttonName));
            _sources.Add(s);
            _nextRefresh = 0f;
        }

        /// <summary>True if any menu row has a reminder: the rule for the drawer handle's dot.</summary>
        public static bool AnyMenuReminder(IEnumerable<(bool menuRow, bool has)> sources)
        {
            foreach (var (menuRow, has) in sources)
                if (menuRow && has) return true;
            return false;
        }

        void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Refresh();
        }

        void Refresh()
        {
            bool any = false;
            foreach (var s in _sources)
            {
                bool has = SafeHas(s);
                SetDot(s.Dot, has);
                if (s.MenuRow && has) any = true;
            }
            SetDot(_handleDot, any);
        }

        static bool SafeHas(Source s)
        {
            try { return s.Has(); }
            catch (Exception e) { GameLogger.Warning($"[Reminders] {s.ButtonName} source threw: {e.Message}"); return false; }
        }

        static void SetDot(VisualElement dot, bool visible)
        {
            if (dot == null) return;
            HUDController.SetElementVisible(dot, visible);
        }

        /// <summary>Adds the (hidden) dot to a button's top-right corner, once.</summary>
        internal static VisualElement AttachDot(VisualElement button)
        {
            if (button == null) return null;
            var existing = button.Q(className: DotClass);
            if (existing != null) return existing;
            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList(DotClass);
            dot.AddToClassList("hidden");
            button.Add(dot);
            return dot;
        }
    }
}
