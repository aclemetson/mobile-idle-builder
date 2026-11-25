using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The Codex panel (codex-panel in GameHUD.uxml) (#286, moved out of HUDController): one entry (tile icon,
    /// name, lore) for every item whose crafting recipe the player has discovered, using the same known-recipe
    /// gate as the recipe list. Rebuilt each time the panel opens. HUDController closes other panels first.
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class CodexPanelSubController : MonoBehaviour
    {
        VisualElement _panel;
        ScrollView    _list;

        public void Initialize(VisualElement root)
        {
            _panel = root.Q("codex-panel");
            _list  = root.Q<ScrollView>("codex-list");
        }

        /// <summary>Shows the panel. The caller closes other panels first.</summary>
        public void Open()
        {
            BuildList();
            HUDController.SetElementVisible(_panel, true);
        }

        void BuildList()
        {
            if (_list == null) return;
            _list.Clear();

            var recipes = RecipeDatabase.Instance?.Recipes;
            if (recipes == null) return;

            var seen  = new HashSet<int>();
            int shown = 0;
            foreach (var recipe in recipes)
            {
                if (!(RecipeKnowledgeService.Instance?.IsKnown(recipe.id) ?? false)) continue;
                var item = recipe.output != null ? ItemDatabase.Instance?.Get(recipe.output.id) : null;
                if (item == null || !seen.Add(item.itemId)) continue;

                var row = new VisualElement();
                row.AddToClassList("codex-row");

                var icon = HUDController.MakeItemIcon(item.icon, "codex-icon");
                if (icon != null) row.Add(icon);

                var infoCol = new VisualElement();
                infoCol.AddToClassList("codex-info");

                var name = new Label(item.displayName ?? item.symbol ?? item.id);
                name.AddToClassList("codex-name");
                infoCol.Add(name);

                if (!string.IsNullOrEmpty(item.codexEntry))
                {
                    var entry = new Label(item.codexEntry);
                    entry.AddToClassList("codex-entry");
                    infoCol.Add(entry);
                }

                row.Add(infoCol);
                _list.Add(row);
                shown++;
            }

            if (shown == 0) _list.Add(new Label("Craft items to fill your codex."));
        }
    }
}
