using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Validates the manager catalogue authored in game_data.json (the single source of truth)
    /// and the ManagerSO schema it imports into. Parses the raw JSON directly so the test does not
    /// depend on the Editor importer having generated the assets.
    /// </summary>
    [TestFixture]
    public class ManagerDataTests
    {
        [Serializable] private class Root { public List<Manager> managers = new(); }
        [Serializable] private class Manager
        {
            public string id; public string display_name; public string description;
            public string bonus_type; public float bonus_value; public int hire_cost_prestige;
        }

        static Root LoadData()
        {
            string path = Path.Combine(Application.dataPath, "Data/game_data.json");
            Assert.IsTrue(File.Exists(path), $"game_data.json not found at {path}");
            return JsonUtility.FromJson<Root>(File.ReadAllText(path));
        }

        // ── ManagerSO schema ──────────────────────────────────────────────────

        [Test]
        public void ManagerSO_HasRequiredFields()
        {
            var type = typeof(ManagerSO);
            Assert.IsNotNull(type.GetField("id"),               "ManagerSO missing: id");
            Assert.IsNotNull(type.GetField("displayName"),      "ManagerSO missing: displayName");
            Assert.IsNotNull(type.GetField("bonusType"),        "ManagerSO missing: bonusType");
            Assert.IsNotNull(type.GetField("bonusValue"),       "ManagerSO missing: bonusValue");
            Assert.IsNotNull(type.GetField("hireCostPrestige"), "ManagerSO missing: hireCostPrestige");
        }

        // ── Catalogue ─────────────────────────────────────────────────────────

        [Test]
        public void Catalogue_HasEightValidManagers()
        {
            var data = LoadData();
            Assert.AreEqual(8, data.managers.Count, "Manager catalogue must have 8 entries");

            var seen = new HashSet<string>();
            foreach (var m in data.managers)
            {
                Assert.IsFalse(string.IsNullOrEmpty(m.id), "Manager id must not be empty");
                Assert.IsTrue(seen.Add(m.id), $"Duplicate manager id '{m.id}'");
                Assert.IsFalse(string.IsNullOrEmpty(m.display_name), $"Manager '{m.id}' display_name empty");
                Assert.IsTrue(Enum.IsDefined(typeof(ManagerBonusType), m.bonus_type),
                    $"Manager '{m.id}' bonus_type '{m.bonus_type}' is not a valid ManagerBonusType");
                Assert.Greater(m.hire_cost_prestige, 0, $"Manager '{m.id}' hire cost must be positive");
            }
        }

        [Test]
        public void Catalogue_MatchesBalanceTable()
        {
            var byId = new Dictionary<string, Manager>();
            foreach (var m in LoadData().managers) byId[m.id] = m;

            AssertManager(byId, "mgr_tinker",       ManagerBonusType.CraftSpeed,     1.25f, 10);
            AssertManager(byId, "mgr_stoker",       ManagerBonusType.PowerDiscount,  0.8f,  10);
            AssertManager(byId, "mgr_packrat",      ManagerBonusType.OutputQuantity, 2.0f,  25);
            AssertManager(byId, "mgr_overclocker",  ManagerBonusType.CraftSpeed,     1.5f,  40);
            AssertManager(byId, "mgr_conductor",    ManagerBonusType.PowerDiscount,  0.6f,  60);
            AssertManager(byId, "mgr_duplicator",   ManagerBonusType.OutputQuantity, 3.0f,  150);
            AssertManager(byId, "mgr_chronomancer", ManagerBonusType.CraftSpeed,     2.0f,  300);
            AssertManager(byId, "mgr_demiurge",     ManagerBonusType.OutputQuantity, 4.0f,  800);
        }

        static void AssertManager(Dictionary<string, Manager> byId, string id,
            ManagerBonusType bonus, float value, int cost)
        {
            Assert.IsTrue(byId.TryGetValue(id, out var m), $"Manager '{id}' missing from catalogue");
            Assert.AreEqual(bonus.ToString(), m.bonus_type, $"{id} bonus_type");
            Assert.AreEqual(value, m.bonus_value, 0.0001f,  $"{id} bonus_value");
            Assert.AreEqual(cost,  m.hire_cost_prestige,    $"{id} hire_cost_prestige");
        }
    }
}
