using System.Collections.Generic;
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
    }
}
