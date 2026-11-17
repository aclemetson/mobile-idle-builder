using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode guard that every ItemSO ships a tile icon. Icons are produced by
    /// MobileIdleBuilder/Generate Element Icons (regime-coloured periodic tiles under
    /// Assets/Art/Icons/Elements) and assigned by GameDataImporter's convention fallback.
    /// If a new item is added without regenerating icons, this fails and names the culprits.
    /// </summary>
    [TestFixture]
    public class ItemIconTests
    {
        private ItemSO[] _items;

        [OneTimeSetUp]
        public void LoadItems()
        {
            _items = Resources.LoadAll<ItemSO>("Items");
            Assert.IsNotNull(_items,  "Resources.LoadAll<ItemSO>(\"Items\") returned null");
            Assert.IsNotEmpty(_items, "No ItemSO assets found in Resources/Items/");
        }

        [Test]
        public void EveryItem_HasIcon()
        {
            var missing = new List<string>();
            foreach (var item in _items)
                if (item.icon == null)
                    missing.Add(item.id);

            if (missing.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append(missing.Count).Append(" item(s) have no icon assigned: ");
                sb.Append(string.Join(", ", missing));
                sb.Append(". Run 'MobileIdleBuilder/Generate Element Icons' then 'MobileIdleBuilder/Import Game Data'.");
                Assert.Fail(sb.ToString());
            }
        }

        /// <summary>
        /// Guards the list ElementIconGenerator uses to skip Blender-rendered icons: a stale id
        /// would silently stop protecting an icon, so each must be a real item with a real PNG.
        /// </summary>
        [Test]
        public void RenderedIconList_NamesRealItemsWithIcons()
        {
            const string listPath = "Assets/Art/Icons/rendered_icons.txt";
            Assert.IsTrue(File.Exists(listPath), $"Missing {listPath}");

            var itemIds = new HashSet<string>();
            foreach (var item in _items) itemIds.Add(item.id);

            foreach (var line in File.ReadAllLines(listPath))
            {
                var id = line.Trim();
                if (id.Length == 0) continue;
                Assert.IsTrue(itemIds.Contains(id), $"{listPath} names unknown item '{id}'");
                Assert.IsTrue(File.Exists($"Assets/Art/Icons/Elements/{id}.png"), $"No icon PNG for '{id}'");
            }
        }
    }
}
