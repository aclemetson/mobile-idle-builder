using System;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The craft queue strip at the top of the Recipes panel (#310): one row per queued recipe with a
    /// count and a cancel button, and a progress bar on the item being made. Rows are rebuilt when
    /// <see cref="ManualCraftService.QueueChanged"/> fires; only the bar and the time label move per frame.
    /// It sits outside the recipe ScrollView, so plain <c>clicked</c> handlers are safe here.
    /// </summary>
    public class CraftQueueView
    {
        private const int MaxRows = 5;

        private readonly ManualCraftService _service;
        private readonly VisualElement      _root;
        private readonly Label              _header;
        private readonly VisualElement      _rows;
        private VisualElement               _activeFill;

        public VisualElement Root => _root;

        public CraftQueueView(ManualCraftService service)
        {
            _service = service;

            _root = new VisualElement { name = "craft-queue" };
            _root.AddToClassList("craft-queue");

            _header = new Label();
            _header.AddToClassList("craft-queue__header");
            _root.Add(_header);

            _rows = new VisualElement();
            _root.Add(_rows);

            Rebuild();
        }

        public void Rebuild()
        {
            _rows.Clear();
            _activeFill = null;

            var queue = _service != null ? _service.Queue : null;
            bool empty = CraftQueue.IsEmpty(queue);
            _root.EnableInClassList("hidden", empty);
            if (empty) return;

            int shown = Math.Min(queue.entries.Count, MaxRows);
            for (int i = 0; i < shown; i++)
            {
                var entry = queue.entries[i];
                var recipe = ManualCraftService.FindRecipe(entry.recipeId);

                var row = new VisualElement();
                row.AddToClassList("craft-queue__row");

                var text = new VisualElement();
                text.AddToClassList("craft-queue__text");
                var name = new Label($"{recipe?.name ?? entry.recipeId}  ×{entry.count}");
                name.AddToClassList("recipe-inputs");
                text.Add(name);

                if (i == 0)
                {
                    var bar = new VisualElement();
                    bar.AddToClassList("craft-queue__bar");
                    _activeFill = new VisualElement();
                    _activeFill.AddToClassList("craft-queue__fill");
                    bar.Add(_activeFill);
                    text.Add(bar);
                }
                row.Add(text);

                int index = i;
                var cancel = new Button(() => _service.CancelQueued(index)) { text = "Cancel" };
                cancel.AddToClassList("craft-btn");
                cancel.AddToClassList("craft-queue__cancel");
                row.Add(cancel);

                _rows.Add(row);
            }

            if (queue.entries.Count > shown)
            {
                var more = new Label($"+{queue.entries.Count - shown} more");
                more.AddToClassList("recipe-lock-hint");
                _rows.Add(more);
            }

            Tick(GameClock.UtcNow);
        }

        /// <summary>Moves the progress bar and the time left. Call every frame while the panel is open.</summary>
        public void Tick(DateTime now)
        {
            var queue = _service != null ? _service.Queue : null;
            if (CraftQueue.IsEmpty(queue)) return;

            if (_activeFill != null)
                _activeFill.style.width = Length.Percent(CraftQueue.ActiveProgress01(queue, now) * 100f);

            int items = CraftQueue.TotalItems(queue);
            _header.text = $"Crafting {items} item{(items == 1 ? "" : "s")} · {FormatSeconds(CraftQueue.SecondsRemaining(queue, now))} left";
        }

        internal static string FormatSeconds(double seconds)
        {
            int s = (int)Math.Ceiling(Math.Max(0, seconds));
            if (s < 60)   return $"{s}s";
            if (s < 3600) return $"{s / 60}m {s % 60:00}s";
            return $"{s / 3600}h {s / 60 % 60:00}m";
        }
    }
}
