using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Recipes panel (recipe-panel in GameHUD.uxml) (#286, moved out of HUDController): item-type tabs over a
    /// horizontal row of cards (RecipeMenu, #488), one card per output item with its chosen method and the
    /// x1/x5/x10/Max craft buttons, the method drop-up, and the craft-queue strip floating over the panel bottom.
    /// HUDController still owns the drawer (closes other panels before <see cref="Open"/>, raises
    /// OnRecipePanelOpened for the tutorial) and the shared recipe text helpers (BuildIngredientsUI,
    /// WorthEachText, EscapeBonusText, ResearchLockText). Services are read live from the HUD (a scene reload
    /// leaves the serialized copies as doomed duplicates). Sibling MonoBehaviour on the HUD GameObject.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class RecipesPanelSubController : MonoBehaviour
    {
        const string TabPref = "recipes_tab";
        const int CraftMax = -1;
        static readonly int[] CraftQuantities = { 1, 5, 10, CraftMax };

        HUDController _hud;
        VisualElement _panel;
        ScrollView    _list;
        VisualElement _tabs;
        readonly Dictionary<string, VisualElement> _pages = new();
        string _tab;
        string _openDropUp;   // output id of the card whose method drop-up is open

        CraftQueueView     _queueView;
        ManualCraftService _queueSource;   // the service the strip and QueueChanged are bound to

        // Session-only memory of which method the player last picked per output item.
        readonly Dictionary<string, string> _methodChoice = new();
        Dictionary<string, string> _buildingNames, _researchNames;
        Dictionary<string, BuildingSO> _buildingsById;

        ManualCraftService Craft    => _hud != null ? _hud.Craft : null;
        ResearchService    Research => _hud != null ? _hud.Research : null;

        public bool IsOpen => _panel != null && !_panel.ClassListContains("hidden");

        public void Initialize(VisualElement root, HUDController hud)
        {
            _hud   = hud;
            _panel = root.Q("recipe-panel");
            _list  = root.Q<ScrollView>("recipe-list");
        }

        /// <summary>
        /// Creates the craft-queue strip for <paramref name="craft"/> and follows its queue. It floats over the
        /// bottom of the panel (added last so it draws and picks on top) instead of sitting above the list: above
        /// it, every show/hide shoved the rows ~230px, so a quick second tap on a 1s Proton landed on a label.
        /// </summary>
        public void AttachCraftService(ManualCraftService craft)
        {
            DetachCraftService();
            if (craft == null) return;
            _queueSource = craft;
            _queueView   = new CraftQueueView(craft);
            _list?.parent?.Add(_queueView.Root);
            _queueView.Root.RegisterCallback<GeometryChangedEvent>(_ => UpdateListClearance());
            craft.QueueChanged += OnQueueChanged;
        }

        public void DetachCraftService()
        {
            if (_queueSource != null) _queueSource.QueueChanged -= OnQueueChanged;
            _queueSource = null;
            _queueView?.Root.RemoveFromHierarchy();
            _queueView = null;
        }

        /// <summary>Per-frame: the queue strip's countdowns while the panel is open.</summary>
        public void Tick(DateTime now)
        {
            if (_queueView != null && IsOpen) _queueView.Tick(now);
        }

        /// <summary>Shows the panel. The caller closes other panels first.</summary>
        public void Open()
        {
            BuildList();
            HUDController.SetElementVisible(_panel, true);
        }

        /// <summary>Rebuilds an open panel (a research taught new recipes).</summary>
        public void RefreshIfOpen()
        {
            if (IsOpen) BuildList();
        }

        void EnsureTabs()
        {
            if (_tabs != null || _panel == null || _list == null) return;
            _tab  = PlayerPrefs.GetString(TabPref, null);
            _tabs = MenuTabs.MakeTabBar(RecipeMenu.Prefix, RecipeMenu.Tabs, RecipeMenu.TabLabel, tab =>
            {
                _tab        = tab;
                _openDropUp = null;
                PlayerPrefs.SetString(TabPref, tab);
                MenuTabs.ShowTab(RecipeMenu.Prefix, _tabs, _pages, tab);
                _list.scrollOffset = Vector2.zero;
            });
            _panel.Insert(_panel.IndexOf(_list), _tabs);
            _list.mode = ScrollViewMode.Horizontal;
            _list.horizontalScrollerVisibility = ScrollerVisibility.Auto;
            _list.verticalScrollerVisibility   = ScrollerVisibility.Hidden;
            _list.AddToClassList("recipe-cards");
        }

        void BuildList()
        {
            if (_list == null) return;
            EnsureTabs();
            _list.Clear();
            _pages.Clear();

            var inventoryCounts = Craft?.GetInventoryCounts() ?? new Dictionary<int, int>();

            var recipes = RecipeDatabase.Instance?.Recipes;
            if (recipes == null) return;
            if (recipes.Count == 0)
            {
                _list.Add(new Label("No recipes available."));
                return;
            }

            foreach (var tab in RecipeMenu.Tabs)
            {
                var page = MenuTabs.MakePage(RecipeMenu.Prefix, tab, "builder-page");
                _pages[tab] = page;
                _list.Add(page);
            }

            // One card per output item (#336): hide recipes the player has never unlocked or that are made in
            // another world (Chemistry's molecules stay out of the Physics list), then group the rest.
            var ws = WorldService.Instance;
            string activeWorldId = ws?.GetWorld(ws.ActiveIndex)?.id;
            var known = new List<RecipeJson>();
            foreach (var recipe in recipes)
                if ((RecipeKnowledgeService.Instance?.IsKnown(recipe.id) ?? false)
                    && WorldGate.RecipeInWorld(recipe.crafted_in, BuildingById, activeWorldId))
                    known.Add(recipe);

            foreach (var group in RecipeGrouping.Group(known))
            {
                var chosen = RecipeGrouping.PickMethod(group,
                    _methodChoice.TryGetValue(group.outputId, out var rid) ? rid : null,
                    m => !IsLocked(m));
                var outputItem = ItemDatabase.Instance?.Get(group.outputId);
                string tab = RecipeMenu.TabOf(outputItem != null ? outputItem.category : (ItemCategory?)null);
                _pages[tab].Add(BuildCard(group, chosen, outputItem, inventoryCounts));
            }

            var withCards = new HashSet<string>();
            foreach (var kv in _pages) if (kv.Value.childCount > 0) withCards.Add(kv.Key);
            _tab = MenuTabs.PickTab(_tab, withCards, RecipeMenu.Tabs);
            MenuTabs.ShowTab(RecipeMenu.Prefix, _tabs, _pages, _tab);
        }

        // Rebuilds the list in place, keeping the scroll position (after expand / method changes).
        void RebuildKeepingScroll()
        {
            var scroll = _list.scrollOffset;
            BuildList();
            _list.scrollOffset = scroll;
        }

        bool IsLocked(RecipeJson recipe) =>
            !string.IsNullOrEmpty(recipe.requires_research)
            && (Research == null || !Research.IsUnlocked(recipe.requires_research));

        BuildingSO BuildingById(string buildingId)
        {
            if (_buildingsById == null)
            {
                _buildingsById = new Dictionary<string, BuildingSO>();
                var all = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings;
                if (all != null)
                    foreach (var b in all)
                        if (b != null && !string.IsNullOrEmpty(b.id)) _buildingsById[b.id] = b;
            }
            return buildingId != null && _buildingsById.TryGetValue(buildingId, out var so) ? so : null;
        }

        string BuildingNameFor(string buildingId)
        {
            if (_buildingNames == null)
            {
                _buildingNames = new Dictionary<string, string>();
                var all = Resources.Load<BuildingDatabaseSO>("BuildingDatabase")?.allBuildings;
                if (all != null)
                    foreach (var b in all)
                        if (b != null && !string.IsNullOrEmpty(b.id)) _buildingNames[b.id] = b.displayName;
            }
            return _buildingNames.TryGetValue(buildingId, out var n) ? n : null;
        }

        string ResearchNameFor(string researchId)
        {
            if (_researchNames == null)
            {
                _researchNames = new Dictionary<string, string>();
                var all = Resources.Load<ResearchDatabaseSO>("ResearchDatabase")?.allResearch;
                if (all != null)
                    foreach (var r in all)
                        if (r != null && !string.IsNullOrEmpty(r.id)) _researchNames[r.id] = r.displayName;
            }
            return _researchNames.TryGetValue(researchId, out var n) && !string.IsNullOrEmpty(n) ? n : researchId;
        }

        // One card per item (#488): picture, name, inputs, hints, craft time and worth, then the x1/x5/x10/Max
        // buttons for the chosen method. An item with several methods shows the chosen one and a drop-up to pick
        // another (the choice is remembered for the session).
        VisualElement BuildCard(RecipeGroup group, RecipeJson recipe, ItemSO outputItem, Dictionary<int, int> inventoryCounts)
        {
            var craftService = Craft;
            bool isLocked = IsLocked(recipe);
            int  max      = isLocked ? 0 : (craftService?.MaxCraftable(recipe) ?? 0);

            var card = new VisualElement();
            card.AddToClassList("recipe-card");
            if (isLocked) card.AddToClassList("recipe-card--locked");   // fades the content, not the drop-up
            else if (max < 1) card.AddToClassList("recipe-card--unavailable");

            var pic = new VisualElement();
            pic.AddToClassList("recipe-card__icon");
            if (outputItem?.icon != null) pic.style.backgroundImage = new StyleBackground(outputItem.icon);
            else
            {
                var g = new GlyphIcon(GlyphIcon.Kind.Recipes);
                g.AddToClassList("building-tile__glyph");
                pic.Add(g);
            }
            card.Add(pic);

            var nameLabel = new Label(recipe.name);
            nameLabel.AddToClassList("recipe-name");
            nameLabel.AddToClassList("recipe-card__name");
            card.Add(nameLabel);

            var inputs = HUDController.BuildIngredientsUI(recipe, inventoryCounts);
            inputs.AddToClassList("recipe-card__inputs");
            card.Add(inputs);

            // A building-only recipe needs one of its buildings placed (any recipe, busy or idle, #309).
            // Its speed sets the craft time, same as the building takes.
            float speed = 1f;
            string missingBuilding = null;
            bool hasBuilding = craftService == null
                || craftService.HasCraftingBuilding(recipe, out speed, out missingBuilding);
            if (isLocked)
                card.Add(CardHint(HUDController.ResearchLockText(ResearchNameFor(recipe.requires_research)), "recipe-lock-hint"));
            else if (!hasBuilding)
                card.Add(CardHint($"Place {AOrAn(missingBuilding)} to craft this", "recipe-lock-hint"));

            string meta = $"{ManualCraftService.UnitSeconds(recipe, speed):0.#}s";
            if (outputItem != null)
                meta += " · " + HUDController.WorthEachText(ItemValue.PricePerItem(outputItem), ItemStars.Count(outputItem.itemId));
            card.Add(CardHint(meta, "recipe-card__meta"));

            float bonus = recipe.EscapeEntropyPerCraft(ItemDatabase.Instance);
            if (bonus > 0f) card.Add(CardHint(HUDController.EscapeBonusText(bonus), "recipe-bonus-hint"));

            var spacer = new VisualElement();
            spacer.AddToClassList("recipe-card__spacer");
            card.Add(spacer);

            bool several = group != null && group.methods.Count > 1;
            string methodTitle = RecipeMenu.MethodTitle(recipe, BuildingNameFor);
            if (several || recipe.requiresBuilding)
                card.Add(CardHint(methodTitle, "recipe-card__method"));

            if (several)
            {
                bool open = _openDropUp == group.outputId;
                var toggle = new Button { text = RecipeMenu.WaysText(group.methods.Count, open) };
                toggle.AddToClassList("recipe-methods-toggle");
                toggle.AddToClassList("recipe-card__ways");
                string outId = group.outputId;
                ListTapRouter.Register(_list, toggle, () =>
                {
                    _openDropUp = _openDropUp == outId ? null : outId;
                    RebuildKeepingScroll();
                });
                card.Add(toggle);
            }

            var craft = BuildCraftButtons(recipe, isLocked, hasBuilding, max);
            craft.AddToClassList("recipe-card__craft");
            card.Add(craft);

            if (several && _openDropUp == group.outputId)
                card.Add(BuildMethodDropUp(group, recipe));
            return card;
        }

        static Label CardHint(string text, string ussClass)
        {
            var l = new Label(text);
            l.AddToClassList(ussClass);
            l.AddToClassList("recipe-card__hint");
            return l;
        }

        // The drop-up over a card: one button per method (where it is made, or locked behind what research).
        // Picking one makes it the card's method and closes the drop-up.
        VisualElement BuildMethodDropUp(RecipeGroup group, RecipeJson chosen)
        {
            var menu = new VisualElement();
            menu.AddToClassList("recipe-dropup");
            foreach (var method in group.methods)
            {
                string text = RecipeMenu.ShortMethodTitle(method, BuildingNameFor);
                if (IsLocked(method)) text += $" · needs {ResearchNameFor(method.requires_research)}";
                var option = new Button { text = text };
                option.AddToClassList("recipe-dropup__option");
                if (method == chosen) option.AddToClassList("recipe-dropup__option--selected");
                var m = method;
                ListTapRouter.Register(_list, option, () =>
                {
                    _methodChoice[group.outputId] = m.id;
                    _openDropUp = null;
                    RebuildKeepingScroll();
                });
                menu.Add(option);
            }
            return menu;
        }

        // x1 / x5 / x10 / Max (#310). One the inventory can't pay for (or with no building placed) is styled
        // unavailable but stays tappable: ListTapRouter swallows taps on disabled buttons, so a disabled one
        // looked pressed and did nothing. Tapping it says what is missing instead.
        VisualElement BuildCraftButtons(RecipeJson recipe, bool isLocked, bool hasBuilding, int max)
        {
            var group = new VisualElement();
            group.AddToClassList("craft-qty-group");

            if (isLocked)
            {
                var locked = new Button { text = "Locked" };
                locked.AddToClassList("craft-btn");
                locked.SetEnabled(false);
                group.Add(locked);
                return group;
            }

            foreach (int qty in CraftQuantities)
            {
                bool isMax = qty == CraftMax;
                var btn = new Button { text = isMax ? "Max" : $"x{qty}" };
                btn.AddToClassList("craft-btn");
                btn.AddToClassList("craft-qty-btn");
                btn.EnableInClassList("craft-btn--unavailable", !(hasBuilding && (isMax ? max >= 1 : max >= qty)));
                // Routed from the list, not the button: a Button's own Clickable consumes the pointer-down before
                // any manipulator added later. See ListTapRouter.
                ListTapRouter.Register(_list, btn, () => OnCraftPressed(recipe, qty));
                group.Add(btn);
            }
            return group;
        }

        void OnCraftPressed(RecipeJson recipe, int count)
        {
            var craftService = Craft;   // live: a destroyed reload duplicate reads as null
            if (craftService == null) return;
            if (count == CraftMax) count = craftService.MaxCraftable(recipe);
            int wanted = Mathf.Max(1, count);   // Max with nothing affordable: report what one craft needs

            switch (craftService.TryEnqueue(recipe, wanted, out string missingBuilding))
            {
                case ManualCraftService.EnqueueResult.NoBuilding:
                    InventoryPopupController.NotifyWarning($"Place {AOrAn(missingBuilding)} first");
                    break;
                case ManualCraftService.EnqueueResult.NotEnoughMaterials:
                case ManualCraftService.EnqueueResult.NotReady:
                    string missing = craftService.DescribeMissing(recipe, wanted);
                    InventoryPopupController.NotifyWarning(missing != null
                        ? $"Need {missing} in your inventory"
                        : "Not enough materials");
                    break;
            }
        }

        // Rebuild the queue strip, and the list (counts and button states moved) unless a finger is down on it:
        // replacing a button mid-press drops the tap (see ListTapRouter).
        void OnQueueChanged()
        {
            _queueView?.Rebuild();
            UpdateListClearance();
            if (IsOpen && !ListTapRouter.GestureInProgress) RebuildKeepingScroll();
        }

        // While the queue strip floats over the bottom of the panel, lift the bottom of the card row by its
        // height so the craft buttons stay clear of it (#488: the cards scroll sideways, not down).
        void UpdateListClearance()
        {
            var strip = _queueView?.Root;
            if (_list == null) return;
            bool shown = strip != null && !strip.ClassListContains("hidden") && !float.IsNaN(strip.layout.height);
            _list.style.marginBottom = shown ? new StyleLength(strip.layout.height) : new StyleLength(StyleKeyword.Null);
        }

        static string AOrAn(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a building";
            return ("AEIOU".IndexOf(char.ToUpperInvariant(name[0])) >= 0 ? "an " : "a ") + name;
        }
    }
}
