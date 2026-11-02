using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Shape rules for the research tree and the capture ladder (2026-11-02 balance pass).
    /// </summary>
    [TestFixture]
    public class ProgressionBalanceTests
    {
        [Serializable] class GdResearch { public string id; public List<string> prerequisites; public long cost_base_currency; }
        [Serializable] class GdIng      { public string item; public int quantity; }
        [Serializable] class GdRecipe   { public string id; public List<GdIng> inputs; public string output_item; public List<string> valid_buildings; public string required_research; public float base_craft_time; }
        [Serializable] class GameData   { public List<GdResearch> research; public List<GdRecipe> recipes; }

        GameData _gd;
        Dictionary<string, GdResearch> _research;

        [OneTimeSetUp]
        public void Load()
        {
            _gd       = JsonUtility.FromJson<GameData>(File.ReadAllText("Assets/Data/game_data.json"));
            _research = _gd.research.ToDictionary(r => r.id);
        }

        [Test]
        public void ResearchNeverCostsLessThanItsPrerequisites()
        {
            // The tutorial's first nodes are cheap on purpose: Hydrogen Synthesis and Automation I are taught early.
            var tutorialDiscounts = new HashSet<string> { "hydrogen_synthesis", "automation_i" };
            foreach (var r in _gd.research.Where(r => !tutorialDiscounts.Contains(r.id)))
                foreach (var p in r.prerequisites ?? new List<string>())
                    if (_research.TryGetValue(p, out var pre))
                        Assert.GreaterOrEqual(r.cost_base_currency, pre.cost_base_currency,
                            $"{r.id} ({r.cost_base_currency:N0}) costs less than its prerequisite {p} ({pre.cost_base_currency:N0})");
        }

        [Test]
        public void CaptureTakesLonger_TheLaterItsResearch()
        {
            int Depth(string id)
            {
                var r = _research[id];
                return r.prerequisites == null || r.prerequisites.Count == 0
                    ? 0 : 1 + r.prerequisites.Where(_research.ContainsKey).Select(Depth).DefaultIfEmpty(-1).Max();
            }

            foreach (var prefix in new[] { "s_capture_", "r_capture_" })
            {
                var byTier = _gd.recipes.Where(r => r.id.StartsWith(prefix))
                                        .GroupBy(r => Depth(r.required_research))
                                        .OrderBy(g => g.Key).ToList();
                for (int i = 1; i < byTier.Count; i++)
                    Assert.GreaterOrEqual(byTier[i].Min(r => r.base_craft_time), byTier[i - 1].Max(r => r.base_craft_time),
                        $"{prefix}: research depth {byTier[i].Key} must not be faster than depth {byTier[i - 1].Key}");
                Assert.Greater(byTier.Last().Min(r => r.base_craft_time), byTier.First().Max(r => r.base_craft_time),
                    $"{prefix}: the latest capture tier should take longer than the first");
            }
        }
    }
}
