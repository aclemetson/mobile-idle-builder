using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Build menu (buildings-panel in GameHUD.uxml) (#286, moved out of HUDController): role tabs over a
    /// horizontal row of cards (BuilderMenu, #487), the belt / channel card, field purchases, every buildable
    /// building of the live world, the (i) detail cards, and the live affordability refresh.
    /// HUDController still owns the drawer (closes other panels before <see cref="Open"/>) and charges for a
    /// placed building. Sibling MonoBehaviour on the HUD GameObject (added at runtime when missing).
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class BuildingsPanelSubController : MonoBehaviour
    {
        const string TabPref = "builder_tab";

        HUDController _hud;
        EntityManager _em;
        EntityQuery   _progressQuery, _prestigeQuery;
        bool          _ecsReady;

        VisualElement _panel;
        ScrollView    _list;
        VisualElement _tabs;
        readonly Dictionary<string, VisualElement> _pages = new();
        string        _tab;
        FieldGenerator _fieldGenerator;   // source of the fields sold here

        // Automation can raise entropy past a cost while the panel is open: buttons re-enable on an entropy
        // change instead of only at panel-open. Rows captured in BuildList, re-evaluated in Tick.
        readonly List<(Button btn, Label costLabel, long cost)> _affordRows = new();
        long _lastAffordEntropy = long.MinValue;

        public bool IsOpen => _panel != null && !_panel.ClassListContains("hidden");

        public void Initialize(VisualElement root, HUDController hud)
        {
            _hud   = hud;
            _panel = root.Q("buildings-panel");
            _list  = root.Q<ScrollView>("buildings-list");
        }

        public void SetECSContext(EntityManager em)
        {
            _em            = em;
            _progressQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerProgressData>());
            _prestigeQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PrestigeData>());
            _ecsReady      = true;
        }

        long CurrentEntropy => _ecsReady && !_progressQuery.IsEmpty
            ? _em.GetComponentData<PlayerProgressData>(_progressQuery.GetSingletonEntity()).BaseCurrency : 0;

        /// <summary>Shows the menu. The caller closes other panels first.</summary>
        public void Open()
        {
            BuildList();
            HUDController.SetElementVisible(_panel, true);
        }

        void Close() => HUDController.SetElementVisible(_panel, false);

        /// <summary>Rebuilds an open menu (a research unlocked new buildings).</summary>
        public void RefreshIfOpen()
        {
            if (IsOpen) BuildList();
        }

        /// <summary>
        /// Re-evaluates affordability for the open menu whenever entropy changes, so a button that was
        /// unaffordable at open becomes tappable the moment automation earns enough. Called every frame.
        /// </summary>
        public void Tick()
        {
            if (!IsOpen || !_ecsReady || _progressQuery.IsEmpty || _affordRows.Count == 0) return;
            long entropy = CurrentEntropy;
            if (entropy == _lastAffordEntropy) return;
            _lastAffordEntropy = entropy;

            foreach (var (btn, costLabel, cost) in _affordRows)
            {
                bool afford = entropy >= cost;
                btn.SetEnabled(afford);
                costLabel.EnableInClassList("building-card-recipe--affordable", afford);
                costLabel.EnableInClassList("building-card-recipe--unaffordable", !afford);
            }
        }

        void EnsureTabs()
        {
            if (_tabs != null || _panel == null || _list == null) return;
            _tab  = PlayerPrefs.GetString(TabPref, null);
            _tabs = BuilderMenu.MakeTabBar(tab =>
            {
                _tab = tab;
                PlayerPrefs.SetString(TabPref, tab);
                BuilderMenu.ShowTab(_tabs, _pages, tab);
                _list.scrollOffset = Vector2.zero;
            });
            _panel.Insert(_panel.IndexOf(_list), _tabs);
            _list.mode = ScrollViewMode.Horizontal;
            _list.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _list.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
            _list.AddToClassList("builder-cards");
        }

        void BuildList()
        {
            if (_list == null) return;
            EnsureTabs();
            _list.Clear();
            _pages.Clear();
            _affordRows.Clear();
            _lastAffordEntropy = long.MinValue;   // force a re-evaluate on the next Tick
            foreach (var tab in BuilderMenu.Tabs)
            {
                var page = new VisualElement { name = BuilderMenu.PageName(tab) };
                page.AddToClassList(BuilderMenu.PageClass);
                page.AddToClassList("builder-page");
                _pages[tab] = page;
                _list.Add(page);
            }

            long currentEntropy = CurrentEntropy;
            var conveyor  = _hud != null ? _hud.ConveyorController : null;
            var placement = _hud != null ? _hud.PlacementController : null;
            var research  = _hud != null ? _hud.Research : null;

            // ---- Conveyor Belt entry (always first in Logistics, always free) ----
            if (conveyor != null)
            {
                // Named so tutorial steps can point at it (TutorialPulseResolver).
                var placeBtn = new Button { text = "Place", name = TutorialPulseResolver.PlaceButtonName("conveyor") };
                ListTapRouter.Register(_list, placeBtn, () =>
                {
                    Close();
                    conveyor.BeginConveyorMode();
                });

                // Stream worlds (Chemistry): the same tool draws water channels from a Spring.
                bool streams = WorldRules.StreamTransport(SaveManager.Instance?.Current);
                string beltName = streams ? "Channel" : "Conveyor Belt";
                string beltInfo = streams
                    ? "Draw a stream from a Spring past your buildings; each takes what its reaction needs"
                    : "Draw paths to move items between buildings.\n\n" +
                      "Path splitting: hold a placed belt to split it. Its items share out 2 or 3 ways, and each " +
                      "side can take a bigger share (1-5). Turn sides off until one is left to make it a plain bend.";
                _pages["logistics"].Add(BuilderMenu.MakeCard(beltName, null, GlyphIcon.Kind.Flip,
                    MakeCostLabel(0, true), placeBtn, InfoButton(beltName, beltInfo)));
            }

            if (placement != null)
            {
                AddFieldPurchaseRows(placement, currentEntropy);

                var ws = WorldService.Instance;
                string activeWorldId = ws?.GetWorld(ws.ActiveIndex)?.id;
                float reduction = _ecsReady && !_prestigeQuery.IsEmpty
                    ? _em.GetComponentData<PrestigeData>(_prestigeQuery.GetSingletonEntity()).CostReduction
                    : 0f;
                foreach (var entry in placement.availableBuildings)
                {
                    // Skip buildings that belong to another world (the Molecular Synthesizer is built in Chemistry)
                    var bso = entry.building;
                    if (bso != null && !WorldGate.BuildingInWorld(bso, activeWorldId)) continue;

                    // Skip buildings whose required research has not been purchased yet
                    if (bso != null && !bso.availableFromStart && bso.requiredResearch != null
                        && (research == null || !research.IsUnlocked(bso.requiredResearch.id)))
                        continue;

                    string detail = entry.defaultRecipe != null ? $"Produces: {entry.defaultRecipe.displayName}" : "";
                    if (!string.IsNullOrEmpty(bso?.description))
                        detail = string.IsNullOrEmpty(detail) ? bso.description : $"{detail}\n\n{bso.description}";
                    if (IsObsolete(bso, research))
                        detail = $"Obsolete. {bso.obsoleteCondition}\n\n{detail}";

                    int rawCost    = entry.building?.entropyCost ?? 0;
                    long cost      = EntropyWallet.ReducedCost(rawCost, reduction);
                    bool canAfford = currentEntropy >= cost;
                    var costLabel  = MakeCostLabel(cost, canAfford);

                    var placeBtn = new Button { text = "Place" };
                    if (bso != null) placeBtn.name = TutorialPulseResolver.PlaceButtonName(bso.id);
                    placeBtn.SetEnabled(canAfford);
                    if (cost > 0) _affordRows.Add((placeBtn, costLabel, cost));

                    var captured = entry;
                    ListTapRouter.Register(_list, placeBtn, () =>
                    {
                        Close();
                        placement.BeginPlacement(captured);
                    });

                    string name = entry.building?.displayName ?? "Building";
                    _pages[BuilderMenu.TabOf(bso)].Add(BuilderMenu.MakeCard(name,
                        BuilderMenu.CardSprite(bso, entry.defaultRecipe), GlyphIcon.Kind.Build,
                        costLabel, placeBtn, InfoButton(name, detail)));
                }
            }

            var withCards = new HashSet<string>();
            foreach (var kv in _pages) if (kv.Value.childCount > 0) withCards.Add(kv.Key);
            _tab = BuilderMenu.PickTab(_tab, withCards);
            BuilderMenu.ShowTab(_tabs, _pages, _tab);
        }

        // The (i) on a Builder card: the building's details in a card (#487). Routed through ListTapRouter like
        // the Place button, since the ScrollView swallows a Button's own click on touch.
        Button InfoButton(string title, string detail)
        {
            if (string.IsNullOrEmpty(detail)) return null;
            var info = new Button();
            ListTapRouter.Register(_list, info, () =>
                ConfirmPrompt.Show(title, detail, "OK", onConfirm: null, cancelLabel: "Close", destructive: false));
            return info;
        }

        /// <summary>
        /// Fields for sale after Field Synthesis (see <see cref="FieldPurchaseService"/>). Quark fields
        /// sell as a pair. The price shown is the next one; it climbs steeply with every purchase.
        /// </summary>
        void AddFieldPurchaseRows(BuildingPlacementController placement, long currentEntropy)
        {
            _fieldGenerator ??= FindAnyObjectByType<FieldGenerator>();
            if (_fieldGenerator == null) return;

            var ws = WorldService.Instance;
            string activeWorldId = ws?.GetWorld(ws.ActiveIndex)?.id;
            foreach (var field in _fieldGenerator.KnownFields)
            {
                // Graphite and nitrogen are bought in Chemistry, quark pairs in Physics.
                if (!WorldGate.FieldInWorld(field, activeWorldId)) continue;
                if (!FieldPurchaseService.IsAvailable(field)) continue;

                long cost      = FieldPurchaseService.NextPrice(field);
                bool canAfford = currentEntropy >= cost;
                var costLabel  = MakeCostLabel(cost, canAfford);

                var placeBtn = new Button { text = "Place", name = TutorialPulseResolver.PlaceButtonName(field.id) };
                placeBtn.SetEnabled(canAfford);
                _affordRows.Add((placeBtn, costLabel, cost));

                var captured = field;
                ListTapRouter.Register(_list, placeBtn, () =>
                {
                    Close();
                    placement.BeginFieldPlacement(captured);
                });

                string name = FieldPurchaseName(field);
                var drops = field.outputItems;
                var picture = field.fieldIcon != null ? field.fieldIcon
                            : drops.Count > 0 && drops[0] != null ? drops[0].icon : null;
                _pages["harvest"].Add(BuilderMenu.MakeCard(name, picture, GlyphIcon.Kind.Domains,
                    costLabel, placeBtn, InfoButton(name, FieldPurchaseDetail(field))));
            }
        }

        internal static string FieldPurchaseName(FieldSO field)
            => field.purchaseGroupSize > 1 ? $"{field.displayName} Pair" : field.displayName;

        internal static string FieldPurchaseDetail(FieldSO field)
        {
            string what = field.purchaseGroupSize > 1
                ? $"{field.purchaseGroupSize} fields side by side"
                : "One field on an empty tile";
            return $"{what}. Each one costs {field.purchaseCostMultiplier:0.#}x the last.";
        }

        /// <summary>True once the research that replaces <paramref name="bso"/> is done (e.g. Nucleon Harvesting
        /// for the Strong Force Combiner). The building stays placeable; the menu just says so.</summary>
        static bool IsObsolete(BuildingSO bso, ResearchService research) =>
            bso != null && bso.isObsoleteable && !string.IsNullOrEmpty(bso.obsoletedByResearch)
            && research != null && research.IsUnlocked(bso.obsoletedByResearch);

        // Zero-cost buildings (conveyors, Maxwell's Demon) say "Free" rather than leaving the cost blank (#197).
        static Label MakeCostLabel(long cost, bool canAfford)
        {
            var costLabel = new Label(HudText.FormatBuildCost(cost));
            costLabel.AddToClassList("building-card-cost");
            costLabel.AddToClassList(canAfford ? "building-card-recipe--affordable" : "building-card-recipe--unaffordable");
            return costLabel;
        }
    }
}
