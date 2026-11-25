using System.Collections.Generic;
using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Research panel (research-panel in GameHUD.uxml) (#286, moved out of HUDController): the tree and the
    /// list (toggle remembered in PlayerPrefs), one tab per unlocked world, research cards with cost / Start, the
    /// live countdown and two-tap Skip on the active research, and the 1 Hz affordability refresh.
    /// HUDController still owns the drawer: it closes other panels before <see cref="Open"/>, raises
    /// OnResearchPanelOpened for the tutorial, and keeps the shared formatters (FormatResearchTimer,
    /// ResearchTimeText, ShortNumber). Sibling MonoBehaviour on the HUD GameObject (added at runtime when missing).
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class ResearchPanelSubController : MonoBehaviour
    {
        const string ViewPref = "research_view_mode";

        HUDController   _hud;
        // Live: after a scene reload the HUD's serialized reference is a doomed duplicate (BindLiveServices).
        ResearchService _service => _hud != null ? _hud.Research : null;
        EntityManager   _em;
        EntityQuery     _progressQuery, _tutorialQuery;
        bool            _ecsReady;

        VisualElement   _panel;
        ScrollView      _list;
        Label           _countdownLabel;     // live countdown on the in-progress card
        ResearchTreeView _tree;              // #337: shown instead of the list once the tutorial is done
        Button          _modeBtn;
        bool            _treeMode = true;
        VisualElement   _tabs;               // one tab per unlocked world (Physics / Chemistry / Biology)
        string          _worldTab;           // world id whose research is shown; set to the live world on open
        string          _selectedId;
        bool            _focusPending;
        ResearchBlock[] _blocks;
        Button          _skipBtn;
        bool            _skipConfirming;
        IVisualElementScheduledItem _ticker;

        // A Start button that was unaffordable at panel-open stayed dead until the panel was reopened. Rows are
        // captured in BuildList and re-evaluated by the 1 Hz ticker. The research is stored rather than a bool
        // because CanPurchase() is the authority on unlocked + prereqs + affordability; labBusy is tracked too
        // so buttons revive when the lab frees up.
        readonly List<(Button btn, Label costLabel, ResearchSO research)> _affordRows = new();
        long _lastAffordEntropy = long.MinValue;
        bool _lastAffordLabBusy;

        public bool IsOpen => _panel != null && !_panel.ClassListContains("hidden");

        public void Initialize(VisualElement root, HUDController hud)
        {
            _hud     = hud;
            _panel   = root.Q("research-panel");
            _list    = root.Q<ScrollView>("research-list");
            SetUpTree();
        }

        public void SetECSContext(EntityManager em)
        {
            _em            = em;
            _progressQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
            _tutorialQuery = em.CreateEntityQuery(ComponentType.ReadOnly<TutorialStateData>());
            _ecsReady      = true;
        }

        bool TutorialActive => _ecsReady && !_tutorialQuery.IsEmpty && _tutorialQuery.GetSingleton<TutorialStateData>().IsActive;

        long CurrentEntropy => _ecsReady && !_progressQuery.IsEmpty
            ? _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity()).BaseCurrency : 0;

        void SetUpTree()
        {
            if (_panel == null || _list == null || _tree != null) return;
            _treeMode = PlayerPrefs.GetInt(ViewPref, 1) == 1;
            _blocks   = Resources.Load<ResearchDatabaseSO>("ResearchDatabase")?.blocks ?? System.Array.Empty<ResearchBlock>();

            _tree = new ResearchTreeView();
            _tree.NodeSelected += r => { _selectedId = r.id; BuildList(); };
            int at = _panel.IndexOf(_list);
            _panel.Insert(at + 1, _tree);

            // Tree / List toggle in the panel header, left of the close button.
            var header = _panel.Q("panel-header");
            _modeBtn = new Button { name = "btn-research-view" };
            _modeBtn.AddToClassList("research-view-btn");
            _modeBtn.clicked += () =>
            {
                _treeMode = !_treeMode;
                PlayerPrefs.SetInt(ViewPref, _treeMode ? 1 : 0);
                _focusPending = _treeMode;
                BuildList();
            };
            var close = header?.Q<Button>("btn-close-research");
            if (header != null) header.Insert(close != null ? header.IndexOf(close) : header.childCount, _modeBtn);

            // World tabs under the header: each world's research on its own page.
            _tabs = new VisualElement { name = "research-world-tabs" };
            _tabs.AddToClassList("research-tabs");
            _panel.Insert(_panel.IndexOf(_list), _tabs);
        }

        /// <summary>World a research belongs to for the tabs (ResearchSO.world; empty = Physics).</summary>
        internal static string TabOf(ResearchSO r) =>
            string.IsNullOrEmpty(r?.world) ? WorldGate.Physics : r.world;

        /// <summary>"◈ 750", or "◈ 750 (-25%)" when Memory Resonance discounts it.</summary>
        internal static string CostText(long cost, long baseCost)
        {
            if (baseCost <= 0 || cost >= baseCost) return $"◈ {cost:N0}";
            int pct = (int)System.Math.Round(100d * (baseCost - cost) / baseCost);
            return pct > 0 ? $"◈ {cost:N0} (-{pct}%)" : $"◈ {cost:N0}";
        }

        /// <summary>Shows the panel on the world the player is in. The caller closes other panels first.</summary>
        public void Open()
        {
            _focusPending = true;
            // Open on the world the player is in: its research first, the others one tap away.
            _worldTab = WorldGate.ActiveWorldId() ?? WorldGate.Physics;
            BuildList();
            HUDController.SetElementVisible(_panel, true);

            // Drive the live research countdown while the panel is open (paused when it closes).
            if (_list == null) return;
            if (_ticker == null) _ticker = _list.schedule.Execute(UpdateCountdown).Every(1000);
            else                 _ticker.Resume();
        }

        public void Close() => HUDController.SetElementVisible(_panel, false);

        /// <summary>
        /// A research finished. During the tutorial the panel closes: every step after a tutorial research needs
        /// the world or another panel, and a Research panel left open silently blocked field taps (2026-11-07
        /// playtest: 18 turns lost at gather_for_hydrogen). Otherwise an open panel rebuilds.
        /// </summary>
        public void OnResearchUnlocked()
        {
            if (!IsOpen) return;
            if (TutorialActive) Close();
            else                BuildList();
        }

        void RefreshTabs()
        {
            if (_tabs == null) return;
            _tabs.Clear();
            var ws = WorldService.Instance;
            var worlds = ws?.AllWorlds;
            int shown = 0;
            if (worlds != null)
                for (int i = 0; i < worlds.Count; i++)
                {
                    var w = worlds[i];
                    if (w == null || !ws.IsUnlocked(i)) continue;
                    shown++;
                    string id = w.id;
                    var tab = new Button(() =>
                    {
                        if (_worldTab == id) return;
                        _worldTab     = id;
                        _selectedId   = null;
                        _focusPending = true;
                        BuildList();
                    }) { text = w.displayName, name = "research-tab-" + id };
                    tab.AddToClassList("research-tab");
                    if (id == _worldTab) tab.AddToClassList("research-tab--selected");
                    _tabs.Add(tab);
                }
            HUDController.SetElementVisible(_tabs, shown > 1);
        }

        // Updates just the active card's countdown + skip cost each second, so an open inline
        // skip confirm is not blown away by a full rebuild.
        void UpdateCountdown()
        {
            if (!IsOpen) { _ticker?.Pause(); return; }

            // MUST run before the HasActiveResearch guard below: "no research running" is exactly
            // the state where Start buttons matter.
            RefreshAffordability();

            if (_service == null || !_service.HasActiveResearch) return;

            double rem = _service.ActiveRemainingSeconds;
            if (_countdownLabel != null)
                _countdownLabel.text = $"⏳ {HudText.FormatResearchTimer(rem)}";

            if (_skipBtn != null && !_skipConfirming)
            {
                long cost     = _service.ActiveSkipCost;
                long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;
                _skipBtn.text = $"Skip  ♦{cost:N0}";
                _skipBtn.SetEnabled(crystals >= cost);
            }
        }

        /// <summary>
        /// Re-evaluates Start-button state for the open panel: BuildList only runs on open / purchase / skip /
        /// cancel / research-unlock, never on an entropy change, so automation earning the cost had no visible
        /// effect until the panel was reopened. Driven by the 1 Hz ticker.
        /// </summary>
        void RefreshAffordability()
        {
            if (_service == null || _affordRows.Count == 0) return;
            if (!_ecsReady || _progressQuery.IsEmpty) return;

            long entropy = CurrentEntropy;
            bool labBusy = _service.HasActiveResearch;

            // labBusy is part of the key, not just entropy: when a research finishes the lab frees
            // up and every other Start button becomes live again at unchanged entropy.
            if (entropy == _lastAffordEntropy && labBusy == _lastAffordLabBusy) return;
            _lastAffordEntropy = entropy;
            _lastAffordLabBusy = labBusy;

            foreach (var (btn, costLabel, research) in _affordRows)
            {
                if (btn == null || research == null) continue;
                // CanPurchase is the authority, so this cannot drift from what Purchase() will accept.
                btn.SetEnabled(!labBusy && _service.CanPurchase(research));
                btn.text = StartText(labBusy, research);
                // Research cost has only an --unaffordable class; there is no --affordable pair.
                costLabel?.EnableInClassList("research-card-cost--unaffordable", entropy < _service.EffectiveCost(research));
            }
        }

        /// <summary>The Start button's label: "Lab busy", "Later" while the tutorial holds this research back
        /// (TutorialResearchPolicy), else "Start".</summary>
        string StartText(bool labBusy, ResearchSO research) =>
            labBusy ? "Lab busy"
            : !TutorialResearchPolicy.CanBuy(research.id, _service.IsUnlocked) ? "Later"
            : "Start";

        void BuildList()
        {
            if (_list == null) return;
            _list.Clear();

            // Reset per-build references for the active-research card (reassigned below if present).
            _countdownLabel = null;
            _skipBtn        = null;
            _skipConfirming = false;

            // The rows about to be discarded must not be refreshed after their elements are gone, and the entropy
            // cache is reset so the next tick re-evaluates rather than short-circuiting on an unchanged value.
            _affordRows.Clear();
            _lastAffordEntropy = long.MinValue;

            if (_service == null || _service.AllResearch == null || _service.AllResearch.Count == 0)
            {
                _list.Add(new Label("No research available."));
                return;
            }

            long currentEntropy = CurrentEntropy;
            bool tutorialActive = TutorialActive;

            _worldTab ??= WorldGate.ActiveWorldId() ?? WorldGate.Physics;
            RefreshTabs();
            // Only the selected world's research (the tabs); the tutorial's target always shows.
            string tutorialTarget = tutorialActive ? TutorialTargetId() : null;
            var shown = _service.AllResearch
                .Where(r => r != null && (TabOf(r) == _worldTab || r.id == tutorialTarget)).ToList();

            bool treeMode = _tree != null && _treeMode;
            HUDController.SetElementVisible(_list, !treeMode);
            if (_tree != null) HUDController.SetElementVisible(_tree, treeMode);
            if (_modeBtn != null)
            {
                HUDController.SetElementVisible(_modeBtn, _tree != null);
                _modeBtn.text = treeMode ? "List" : "Tree";
            }

            if (treeMode)
            {
                BuildTree(shown, currentEntropy, tutorialTarget);
                return;
            }

            foreach (var research in shown)
            {
                bool unlocked   = _service.IsUnlocked(research.id);
                bool prereqsMet = PrereqsMet(research);
                // During the tutorial, only show research that is already unlocked or on the immediate frontier.
                if (tutorialActive && !unlocked && !prereqsMet) continue;
                _list.Add(BuildCard(research, currentEntropy));
            }
        }

        // Prerequisite research done AND its world (WorldGate) open.
        bool PrereqsMet(ResearchSO research) =>
            _service.WorldOpen(research) &&
            (research.prerequisites == null || research.prerequisites.Length == 0 ||
             System.Array.TrueForAll(research.prerequisites, p => p == null || _service.IsUnlocked(p.id)));

        /// <summary>"Opens with Chemistry" while the research's world is still locked, else null.</summary>
        string WorldLockText(ResearchSO research) =>
            _service.WorldOpen(research) ? null : $"Opens with {WorldGate.DisplayName(research.requiredWorld)}";

        /// <summary>The research the current tutorial step is waiting for, or null when it waits for something else.</summary>
        string TutorialTargetId()
        {
            var steps = TutorialFlowSO.Current?.steps;
            int index = _tutorialQuery.GetSingleton<TutorialStateData>().CurrentStepIndex;
            if (steps == null || index < 0 || index >= steps.Length) return null;
            var condition = steps[index].advanceCondition;
            return condition.type == ConditionType.ResearchUnlocked ? condition.researchId : null;
        }

        void BuildTree(IReadOnlyList<ResearchSO> all, long currentEntropy, string tutorialTargetId)
        {
            ResearchTreeView.NodeState State(ResearchSO r) =>
                _service.IsUnlocked(r.id) ? ResearchTreeView.NodeState.Done
                : _service.HasActiveResearch && _service.ActiveResearchId == r.id ? ResearchTreeView.NodeState.Active
                : PrereqsMet(r) ? ResearchTreeView.NodeState.Available
                : ResearchTreeView.NodeState.Locked;
            string Meta(ResearchSO r) => State(r) switch
            {
                ResearchTreeView.NodeState.Done      => "Researched",
                ResearchTreeView.NodeState.Active    => $"In progress · {HudText.FormatResearchTimer(_service.ActiveRemainingSeconds)}",
                ResearchTreeView.NodeState.Available => $"◈ {HudText.ShortNumber(_service.EffectiveCost(r))}" + (currentEntropy >= _service.EffectiveCost(r) ? "  Ready" : ""),
                _ => WorldLockText(r) ?? $"Locked · ◈ {HudText.ShortNumber(_service.EffectiveCost(r))}",
            };

            // Open on the research the player is working toward: the one the tutorial is asking for (selected,
            // so its Start button is on screen), else the one in progress, else the first available.
            if (_focusPending)
            {
                _focusPending = false;
                var tutorialTarget = all.FirstOrDefault(r => r != null && r.id == tutorialTargetId);
                if (tutorialTarget != null) _selectedId = tutorialTarget.id;
                var target = tutorialTarget
                          ?? all.FirstOrDefault(r => r != null && State(r) == ResearchTreeView.NodeState.Active)
                          ?? all.Where(r => r != null && State(r) == ResearchTreeView.NodeState.Available)
                                .OrderBy(r => System.Array.FindIndex(_blocks, b => b.id == r.block)).ThenBy(r => r.costBaseCurrency)
                                .FirstOrDefault();
                if (target != null) { _selectedId ??= target.id; _tree.FocusOn(target.id); }
            }

            _tree.Rebuild(all, _blocks, State, Meta, _selectedId);

            var detail = _tree.Detail;
            detail.Clear();
            var selected = all.FirstOrDefault(r => r != null && r.id == _selectedId);
            if (selected == null)
            {
                var hint = new Label("Tap a research to see what it unlocks and start it. Drag to move around, pinch to zoom.");
                hint.AddToClassList("research-card-desc");
                detail.Add(hint);
                return;
            }
            var block = System.Array.Find(_blocks, b => b.id == selected.block);
            if (block != null)
            {
                var phase = new Label(block.displayName);
                phase.AddToClassList("rt-detail-phase");
                detail.Add(phase);
            }
            detail.Add(BuildCard(selected, currentEntropy));
        }

        /// <summary>One research card: name, description, and cost/Start, the live timer + skip, or what it requires.
        /// The list shows one per research; the tree shows the selected node's.</summary>
        VisualElement BuildCard(ResearchSO research, long currentEntropy)
        {
            bool unlocked    = _service.IsUnlocked(research.id);
            bool prereqsMet  = PrereqsMet(research);
            bool canPurchase = !unlocked && prereqsMet && _service.CanPurchase(research);

            var card = new VisualElement();
            card.AddToClassList("research-card");
            if (unlocked) card.AddToClassList("research-card--unlocked");

            var nameLabel = new Label(research.displayName);
            nameLabel.AddToClassList("research-card-name");
            card.Add(unlocked ? GlyphIcon.TitleRow(GlyphIcon.Kind.Check, nameLabel) : nameLabel);

            if (!string.IsNullOrEmpty(research.description))
            {
                var descLabel = new Label(research.description);
                descLabel.AddToClassList("research-card-desc");
                card.Add(descLabel);
            }

            if (research.effects != null)
                foreach (var effect in research.effects)
                {
                    var effectLabel = new Label(ResearchEffectText.Describe(effect));
                    effectLabel.AddToClassList("research-card-effect");
                    card.Add(effectLabel);
                }

            bool isActive = _service.HasActiveResearch && _service.ActiveResearchId == research.id;
            bool labBusy  = _service.HasActiveResearch && !isActive;
            if (isActive) card.AddToClassList("research-card--in-progress");

            if (unlocked) return card;

            var footer = new VisualElement();
            footer.AddToClassList("research-card-footer");

            if (!prereqsMet)
            {
                // Show what's blocking this research: its world first, else the missing prerequisites
                var prereqNames = string.Join(", ",
                    System.Array.ConvertAll(research.prerequisites ?? System.Array.Empty<ResearchSO>(),
                        p => p != null ? p.displayName : "?"));
                var prereqLabel = new Label(WorldLockText(research) ?? $"Requires: {prereqNames}");
                prereqLabel.AddToClassList("research-card-prereq");
                footer.Add(prereqLabel);
            }
            else if (isActive)
            {
                // In-progress: live countdown + skip-for-crystals
                _countdownLabel = new Label($"⏳ {HudText.FormatResearchTimer(_service.ActiveRemainingSeconds)}");
                _countdownLabel.AddToClassList("research-card-cost");
                footer.Add(_countdownLabel);
                BuildSkipUi(footer);
            }
            else
            {
                // Cost + effective research time (after the Research Overdrive discount),
                // stacked so the Start button stays on the right.
                var info = new VisualElement();
                info.AddToClassList("research-card-info");

                long cost       = _service.EffectiveCost(research);
                var costLabel   = new Label(CostText(cost, research.costBaseCurrency));
                costLabel.AddToClassList("research-card-cost");
                if (currentEntropy < cost) costLabel.AddToClassList("research-card-cost--unaffordable");
                info.Add(costLabel);

                var timeLabel = new Label(HudText.ResearchTimeText(_service.EffectiveDurationSeconds(research),
                                                                         _service.FreeResearchLeft));
                timeLabel.AddToClassList("research-card-time");
                info.Add(timeLabel);
                footer.Add(info);

                // Start button (disabled while the lab is busy with another research)
                var startBtn = new Button { text = StartText(labBusy, research) };
                startBtn.AddToClassList("craft-btn");
                startBtn.SetEnabled(!labBusy && canPurchase);
                _affordRows.Add((startBtn, costLabel, research));   // live affordability refresh

                var captured = research;
                startBtn.clicked += () =>
                {
                    _service.Purchase(captured);
                    BuildList();
                };
                footer.Add(startBtn);
            }

            card.Add(footer);
            return card;
        }

        // Skip button for the in-progress research. First tap reveals a Confirm/Cancel row so
        // spending crystals is always a deliberate two-tap action (no generic confirm modal exists).
        void BuildSkipUi(VisualElement footer)
        {
            long cost     = _service.ActiveSkipCost;
            long crystals = SaveManager.Instance?.Current?.paidCurrency ?? 0;

            _skipBtn = new Button { text = $"Skip  ♦{cost:N0}" };
            _skipBtn.AddToClassList("craft-btn");
            _skipBtn.SetEnabled(crystals >= cost);
            _skipBtn.clicked += () =>
            {
                _skipConfirming = true;
                footer.Remove(_skipBtn);

                var confirm = new Button { text = $"Confirm  ♦{_service.ActiveSkipCost:N0}" };
                confirm.AddToClassList("craft-btn");
                confirm.clicked += () =>
                {
                    _service.SkipActive(); // completion fires OnResearchUnlocked → rebuild
                    BuildList();
                };

                var cancel = new Button();
                cancel.AddToClassList("craft-btn");
                GlyphIcon.SetOn(cancel, GlyphIcon.Kind.Close);
                cancel.clicked += BuildList;

                footer.Add(confirm);
                footer.Add(cancel);
            };
            footer.Add(_skipBtn);
        }
    }
}
