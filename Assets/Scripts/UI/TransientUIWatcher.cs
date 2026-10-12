using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// TEMP (#238): finds the message that flashes up from the bottom edge and vanishes before it can
    /// be read. Every frame it notes which NAMED elements of every UIDocument are showing and logs
    /// each change at Info (kept in release builds) as "[TransientUI] shown/hidden", with the
    /// element's text, where it sits on screen and, on hide, how long it was up. Unnamed elements
    /// (rebuilt list rows) are skipped so open lists don't flood the log.
    ///
    /// Filter the device log with: adb logcat -d | findstr TransientUI
    /// Created by HUDController; remove with #238.
    /// </summary>
    public class TransientUIWatcher : MonoBehaviour
    {
        const int   MaxDepth           = 6;
        const float DocumentRefreshSec = 2f;

        readonly Dictionary<VisualElement, float> _shownAt = new();
        readonly HashSet<VisualElement>           _seen    = new();
        readonly List<VisualElement>              _gone    = new();
        UIDocument[] _documents = System.Array.Empty<UIDocument>();
        float        _nextDocumentRefresh;

        void Update()
        {
            float now = Time.unscaledTime;
            if (now >= _nextDocumentRefresh)
            {
                _documents           = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
                _nextDocumentRefresh = now + DocumentRefreshSec;
            }

            _seen.Clear();
            foreach (var doc in _documents)
                if (doc != null && doc.isActiveAndEnabled && doc.rootVisualElement != null)
                    Scan(doc.rootVisualElement, 0, now);

            _gone.Clear();
            foreach (var kv in _shownAt)
                if (!_seen.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var el in _gone)
            {
                GameLogger.Info($"[TransientUI] hidden '{el.name}' after {now - _shownAt[el]:F2}s");
                _shownAt.Remove(el);
            }
        }

        void Scan(VisualElement el, int depth, float now)
        {
            if (!IsShowing(el)) return; // nothing under a hidden element can show
            if (!string.IsNullOrEmpty(el.name))
            {
                _seen.Add(el);
                if (!_shownAt.ContainsKey(el))
                {
                    _shownAt[el] = now;
                    if (depth > 0) // the document roots are always up
                        GameLogger.Info($"[TransientUI] shown '{el.name}' text='{TextOf(el)}' " +
                                        $"rect={el.worldBound} panel={el.panel?.visualTree.layout.size}");
                }
            }
            if (depth >= MaxDepth) return;
            for (int i = 0; i < el.childCount; i++) Scan(el[i], depth + 1, now);
        }

        static bool IsShowing(VisualElement el) =>
            el.resolvedStyle.display != DisplayStyle.None &&
            el.resolvedStyle.visibility == Visibility.Visible &&
            el.resolvedStyle.opacity > 0.01f;

        /// <summary>First few labels' text inside the element, so the log names the message itself.</summary>
        static string TextOf(VisualElement el)
        {
            var sb = new StringBuilder();
            el.Query<TextElement>().ForEach(t =>
            {
                if (sb.Length > 120 || string.IsNullOrEmpty(t.text)) return;
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(t.text);
            });
            return sb.ToString();
        }
    }
}
