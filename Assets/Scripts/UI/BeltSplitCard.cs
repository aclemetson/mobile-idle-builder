using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The card that press-and-hold on a belt opens (2026-11-24): which sides the belt sends items to, and the
    /// weight of each (1-5, default 1 = even). Every change applies at once through
    /// <see cref="ConveyorPlacer.SetSplit"/>, so the fork's arms appear on the belt under the card as the player
    /// toggles sides. Turning sides off until one is left makes it a plain belt leaving that way.
    /// Built in code on the HUD root (the confirm-prompt card styles); the backdrop or Done closes it.
    /// </summary>
    public sealed class BeltSplitCard
    {
        static readonly string[] SideNames = { "North", "East", "South", "West" };

        readonly VisualElement _root;
        readonly ConveyorPlacer _placer;
        readonly Vector2Int _cell;
        readonly object _modalOwner = new();
        VisualElement _modal, _rows;
        Label _summary;
        int _inbound, _mask;
        int[] _weights;

        static BeltSplitCard s_open;

        /// <summary>True while a split card is open.</summary>
        public static bool IsOpen => s_open != null;

        BeltSplitCard(VisualElement root, ConveyorPlacer placer, Vector2Int cell)
        {
            _root = root; _placer = placer; _cell = cell;
        }

        /// <summary>Opens the card for the belt at <paramref name="cell"/>. False when there is no belt there.</summary>
        public static bool Show(VisualElement root, ConveyorPlacer placer, Vector2Int cell)
        {
            if (root == null || placer == null) return false;
            if (!placer.TryGetSplitInfo(cell, out int inbound, out int mask, out int[] weights, out _)) return false;
            s_open?.Close();
            var card = new BeltSplitCard(root, placer, cell) { _inbound = inbound, _mask = mask, _weights = weights };
            card.Build();
            s_open = card;
            return true;
        }

        /// <summary>Closes the open card (Android back).</summary>
        public static void CloseOpen() => s_open?.Close();

        void Build()
        {
            _modal = new VisualElement { name = "belt-split-modal" };
            _modal.AddToClassList("idle-return-modal");
            _modal.AddToClassList("blocks-world-input");
            _modal.style.position = Position.Absolute;
            _modal.style.left = 0; _modal.style.right = 0; _modal.style.top = 0; _modal.style.bottom = 0;

            var backdrop = new VisualElement();
            backdrop.AddToClassList("idle-return-backdrop");
            backdrop.RegisterCallback<ClickEvent>(_ => Close());
            _modal.Add(backdrop);

            var card = new VisualElement();
            card.AddToClassList("idle-return-card");
            card.AddToClassList("belt-split-card");

            var header = new VisualElement();
            header.AddToClassList("panel-header");
            var title = new Label("Belt split");
            title.AddToClassList("panel-title");
            header.Add(title);
            card.Add(header);

            var hint = new Label("Pick the sides this belt sends items to (2 or 3). A higher weight sends more that way. " +
                                 "The arms on the belt show where it splits; draw a belt from an arm to connect it.");
            hint.AddToClassList("game-update-message");
            hint.style.unityTextAlign = TextAnchor.UpperLeft;
            card.Add(hint);

            _rows = new VisualElement();
            _rows.AddToClassList("belt-split-rows");
            card.Add(_rows);

            _summary = new Label();
            _summary.AddToClassList("belt-split-summary");
            card.Add(_summary);

            var buttons = new VisualElement();
            buttons.AddToClassList("confirm-prompt__buttons");
            var remove = new Button(RemoveSplit) { text = "No split" };
            remove.AddToClassList("craft-btn");
            remove.AddToClassList("confirm-prompt__btn");
            remove.AddToClassList("surge-offer__decline");
            var done = new Button(Close) { text = "Done" };
            done.AddToClassList("craft-btn");
            done.AddToClassList("confirm-prompt__btn");
            buttons.Add(remove);
            buttons.Add(done);
            card.Add(buttons);

            _modal.Add(card);
            _root.Add(_modal);
            UIInputBlocker.SetModal(_modalOwner, true);
            Refresh();
        }

        void Refresh()
        {
            _rows.Clear();
            for (int d = 0; d < 4; d++) _rows.Add(SideRow(d));
            _summary.text = BeltSplitText.Summary(_mask, _inbound, _weights);
        }

        VisualElement SideRow(int dir)
        {
            bool isInput = BeltSplitRules.Has(_inbound, dir);
            bool on      = !isInput && BeltSplitRules.Has(_mask, dir);

            var row = new VisualElement();
            row.AddToClassList("belt-split-row");
            if (on) row.AddToClassList("belt-split-row--on");

            var toggle = new Button(() => ToggleSide(dir)) { text = BeltSplitText.SideLabel(SideNames[dir], isInput, on) };
            toggle.AddToClassList("craft-btn");
            toggle.AddToClassList("belt-split-row__side");
            toggle.SetEnabled(!isInput && (on || BeltSplitRules.Count(_mask) < BeltSplitRules.MaxOutputs));
            row.Add(toggle);

            var minus = new Button(() => Nudge(dir, -1)) { text = "-" };
            var plus  = new Button(() => Nudge(dir, +1)) { text = "+" };
            var weight = new Label(on ? _weights[dir].ToString() : "");
            foreach (var b in new[] { minus, plus }) { b.AddToClassList("craft-btn"); b.AddToClassList("belt-split-row__step"); }
            weight.AddToClassList("belt-split-row__weight");
            bool split = BeltSplitRules.Count(_mask) >= BeltSplitRules.MinOutputs;
            minus.SetEnabled(on && split && _weights[dir] > BeltSplitRules.MinWeight);
            plus.SetEnabled(on && split && _weights[dir] < BeltSplitRules.MaxWeight);
            row.Add(minus);
            row.Add(weight);
            row.Add(plus);
            return row;
        }

        void ToggleSide(int dir)
        {
            int next = _mask ^ (1 << dir);
            if (BeltSplitRules.Count(next) == 0) return;   // a belt always leaves somewhere
            _mask = next;
            if (BeltSplitRules.Has(_mask, dir)) _weights[dir] = BeltSplitRules.MinWeight;
            Apply();
        }

        void Nudge(int dir, int delta)
        {
            _weights[dir] = BeltSplitRules.ClampWeight(_weights[dir] + delta);
            Apply();
        }

        void RemoveSplit()
        {
            // Keep the first output as the belt's way out.
            for (int d = 0; d < 4; d++) if (BeltSplitRules.Has(_mask, d)) { _mask = 1 << d; break; }
            Apply();
            Close();
        }

        void Apply()
        {
            _placer.SetSplit(_cell, _mask, _weights);
            // Read back what the placer kept (input sides are never outputs).
            if (_placer.TryGetSplitInfo(_cell, out int inbound, out int mask, out int[] weights, out _))
            {
                _inbound = inbound; _mask = mask; _weights = weights;
            }
            if (_modal != null) Refresh();
        }

        void Close()
        {
            _modal?.RemoveFromHierarchy();
            _modal = null;
            UIInputBlocker.SetModal(_modalOwner, false);
            if (s_open == this) s_open = null;
        }
    }

    /// <summary>Pure texts for <see cref="BeltSplitCard"/>.</summary>
    public static class BeltSplitText
    {
        /// <summary>"North (in)" for the way in, "North: on" / "North: off" for the others.</summary>
        public static string SideLabel(string side, bool isInput, bool on) =>
            isInput ? $"{side} (in)" : on ? $"{side}: on" : $"{side}: off";

        /// <summary>"Splits 2 : 1 (North, South)", or "Not split" with fewer than two outputs.</summary>
        public static string Summary(int mask, int inbound, int[] weights)
        {
            int outs = mask & BeltSplitRules.AllowedOutputs(inbound);
            if (BeltSplitRules.Count(outs) < BeltSplitRules.MinOutputs) return "Not split: one way out.";
            var names = new System.Collections.Generic.List<string>(3);
            string[] side = { "North", "East", "South", "West" };
            for (int d = 0; d < 4; d++) if (BeltSplitRules.Has(outs, d)) names.Add(side[d]);
            return $"Splits {BeltSplitRules.RatioText(outs, d => weights[d])} ({string.Join(", ", names)})";
        }
    }
}
