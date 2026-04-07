using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    [RequireComponent(typeof(UIDocument))]
    public class HUDController : MonoBehaviour
    {
        [SerializeField] private ManualCraftService          craftService;
        [SerializeField] private BuildingPlacementController placementController;

        // ---- Cached UI elements ----
        private VisualElement _inventoryBar;
        private VisualElement _recipePanel;
        private VisualElement _buildingsPanel;
        private VisualElement _placementOverlay;
        private ScrollView    _recipeList;
        private ScrollView    _buildingsList;
        private Label         _powerLabel;
        private Label         _placementLabel;

        private readonly Dictionary<int, Label> _inventoryLabels = new();

        void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;

            // Top bar
            _inventoryBar = root.Q("inventory-bar");
            _powerLabel   = root.Q<Label>("power-label");

            // Bottom bar
            root.Q<Button>("btn-recipes").clicked   += OpenRecipePanel;
            root.Q<Button>("btn-buildings").clicked += OpenBuildingsPanel;
            root.Q<Button>("btn-codex").clicked     += () => Debug.Log("[HUD] Codex — coming soon");
            root.Q<Button>("btn-research").clicked  += () => Debug.Log("[HUD] Research — coming soon");
            root.Q<Button>("btn-upgrades").clicked  += () => Debug.Log("[HUD] Upgrades — coming soon");

            // Recipe panel
            _recipePanel = root.Q("recipe-panel");
            _recipeList  = root.Q<ScrollView>("recipe-list");
            root.Q<Button>("btn-close-recipes").clicked += () => SetPanelVisible(_recipePanel, false);

            // Buildings panel
            _buildingsPanel = root.Q("buildings-panel");
            _buildingsList  = root.Q<ScrollView>("buildings-list");
            root.Q<Button>("btn-close-buildings").clicked += () => SetPanelVisible(_buildingsPanel, false);

            // Placement overlay
            _placementOverlay = root.Q("placement-overlay");
            _placementLabel   = root.Q<Label>("placement-label");
            root.Q<Button>("btn-cancel-placement").clicked += () => placementController?.CancelPlacement();

            SetPanelVisible(_recipePanel,      false);
            SetPanelVisible(_buildingsPanel,   false);
            SetPanelVisible(_placementOverlay, false);

            // Listen for placement mode changes
            if (placementController != null)
                placementController.OnPlacingChanged += OnPlacingChanged;
        }

        void OnDisable()
        {
            if (placementController != null)
                placementController.OnPlacingChanged -= OnPlacingChanged;
        }

        void Update() => RefreshInventoryBar();

        // ---- Inventory bar ----

        private void RefreshInventoryBar()
        {
            if (craftService == null || !craftService.IsReady) return;

            var counts = craftService.GetInventoryCounts();

            foreach (var (itemId, qty) in counts)
            {
                var item = ItemDatabase.Instance?.Get(itemId);
                if (item == null) continue;

                if (!_inventoryLabels.TryGetValue(itemId, out var lbl))
                {
                    lbl = new Label();
                    lbl.AddToClassList("inventory-item");
                    _inventoryBar.Add(lbl);
                    _inventoryLabels[itemId] = lbl;
                }

                lbl.text = $"{item.symbol ?? item.displayName}: {qty}";
                lbl.style.display = DisplayStyle.Flex;
            }

            foreach (var (itemId, lbl) in _inventoryLabels)
            {
                if (!counts.ContainsKey(itemId))
                    lbl.style.display = DisplayStyle.None;
            }
        }

        // ---- Recipe panel ----

        private void OpenRecipePanel()
        {
            CloseAllPanels();
            BuildRecipeList();
            SetPanelVisible(_recipePanel, true);
        }

        private void BuildRecipeList()
        {
            _recipeList.Clear();

            var recipes = RecipeDatabase.Instance?.Recipes;
            if (recipes == null) return;

            var manual = recipes.Where(r => !r.requiresBuilding).ToList();
            if (manual.Count == 0)
            {
                _recipeList.Add(new Label("No manual recipes available."));
                return;
            }

            foreach (var recipe in manual)
            {
                var row = new VisualElement();
                row.AddToClassList("recipe-row");

                var info = new VisualElement();
                info.AddToClassList("recipe-info");

                var nameLabel = new Label(recipe.name);
                nameLabel.AddToClassList("recipe-name");

                var inputsLabel = new Label(BuildInputsText(recipe));
                inputsLabel.AddToClassList("recipe-inputs");

                info.Add(nameLabel);
                info.Add(inputsLabel);

                var craftBtn = new Button();
                craftBtn.text = "Craft";
                craftBtn.AddToClassList("craft-btn");
                var captured = recipe;
                craftBtn.clicked += () => OnCraftPressed(captured);

                row.Add(info);
                row.Add(craftBtn);
                _recipeList.Add(row);
            }
        }

        private void OnCraftPressed(RecipeJson recipe)
        {
            if (!craftService.TryCraft(recipe))
                Debug.Log($"[HUD] Can't craft {recipe.name} — not enough inputs.");
        }

        private static string BuildInputsText(RecipeJson recipe)
        {
            if (recipe.inputs == null || recipe.inputs.Count == 0) return "";
            var parts = recipe.inputs.Select(i =>
            {
                var item = ItemDatabase.Instance?.Get(i.id);
                string sym = item?.symbol ?? item?.displayName ?? i.id;
                return $"{i.quantity}× {sym}";
            });
            return string.Join("  +  ", parts);
        }

        // ---- Buildings panel ----

        private void OpenBuildingsPanel()
        {
            CloseAllPanels();
            BuildBuildingsList();
            SetPanelVisible(_buildingsPanel, true);
        }

        private void BuildBuildingsList()
        {
            _buildingsList.Clear();
            if (placementController == null) return;

            foreach (var entry in placementController.availableBuildings)
            {
                var card = new VisualElement();
                card.AddToClassList("building-card");

                var name = new Label(entry.building?.displayName ?? "Building");
                name.AddToClassList("building-card-name");

                var recipe = new Label(entry.defaultRecipe != null
                    ? $"Produces: {entry.defaultRecipe.displayName}"
                    : "No recipe");
                recipe.AddToClassList("building-card-recipe");

                var placeBtn = new Button();
                placeBtn.text = "Place";
                placeBtn.AddToClassList("craft-btn");

                var captured = entry;
                placeBtn.clicked += () =>
                {
                    SetPanelVisible(_buildingsPanel, false);
                    placementController.BeginPlacement(captured);
                };

                card.Add(name);
                card.Add(recipe);
                card.Add(placeBtn);
                _buildingsList.Add(card);
            }
        }

        // ---- Placement overlay ----

        private void OnPlacingChanged(bool isPlacing)
        {
            SetPanelVisible(_placementOverlay, isPlacing);
            if (isPlacing && _placementLabel != null)
                _placementLabel.text = "Tap an empty tile to place  ·  Esc to cancel";
        }

        // ---- Helpers ----

        private void CloseAllPanels()
        {
            SetPanelVisible(_recipePanel,    false);
            SetPanelVisible(_buildingsPanel, false);
        }

        private static void SetPanelVisible(VisualElement panel, bool visible)
        {
            if (visible) panel.RemoveFromClassList("hidden");
            else         panel.AddToClassList("hidden");
        }
    }
}
