using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Walks the tutorial in game_data.json in order and checks that every step can actually be done
    /// with what earlier steps unlocked: the research it buys has its prerequisites, the buildings it
    /// asks for are unlocked, the items it waits on have an unlocked recipe, and there are enough
    /// fields for the Harvesters it asks for. Guards #275 (a tutorial that asks for something the player
    /// can't have yet is a softlock nobody notices until a playtest reaches it).
    ///
    /// Parses the raw JSON so it doesn't depend on the importer having run.
    /// </summary>
    [TestFixture]
    public class TutorialDataFlowTests
    {
        // Starting map: 2 quark fields + 1 lepton field (FieldGenerator default setup).
        const int StartingQuarkFields  = 2;
        const int StartingLeptonFields = 1;
        const int QuarkFieldsPerPurchase = 2;

        [Serializable] private class Root
        {
            public List<Research> research = new();
            public List<Building> buildings = new();
            public List<Recipe>   recipes   = new();
            public List<Item>     items     = new();
            public List<Field>    fields    = new();
            public List<Step>     tutorial_steps = new();
        }
        [Serializable] private class Research { public string id; public string[] prerequisites; }
        [Serializable] private class Building { public string id; public string required_research; public bool available_from_start; }
        [Serializable] private class Recipe   { public string id; public string output_item; public string required_research; public string[] valid_buildings; }
        [Serializable] private class Item     { public string id; public int item_id; }
        [Serializable] private class Field    { public string id; public string purchase_research; public List<Drop> drops = new(); }
        [Serializable] private class Drop     { public string item; }
        [Serializable] private class Step
        {
            public string id;
            public Condition advance_condition;
            public OnEnter   on_enter;
        }
        [Serializable] private class Condition
        {
            public string type; public string research_id; public string building_id; public string from_building_id;
            public string field_id; public int min_count; public List<ItemReq> items = new();
        }
        [Serializable] private class ItemReq  { public int item_id; public int quantity; }
        [Serializable] private class OnEnter  { public string pulse_button_id; public string highlight_target; }

        Root _data;
        Dictionary<string, Research> _research;
        Dictionary<string, Building> _buildings;
        Dictionary<string, Field>    _fields;
        readonly HashSet<string> _unlocked = new();

        [OneTimeSetUp]
        public void Load()
        {
            string path = Path.Combine(Application.dataPath, "Data/game_data.json");
            Assert.IsTrue(File.Exists(path), $"game_data.json not found at {path}");
            _data = JsonUtility.FromJson<Root>(File.ReadAllText(path));

            _research  = new(); foreach (var r in _data.research)  if (!string.IsNullOrEmpty(r.id)) _research[r.id]  = r;
            _buildings = new(); foreach (var b in _data.buildings) if (!string.IsNullOrEmpty(b.id)) _buildings[b.id] = b;
            _fields    = new(); foreach (var f in _data.fields)    if (!string.IsNullOrEmpty(f.id)) _fields[f.id]    = f;
        }

        bool Unlocked(string researchId) => string.IsNullOrEmpty(researchId) || _unlocked.Contains(researchId);

        bool BuildingAvailable(string id)
            => _buildings.TryGetValue(id, out var b) && (b.available_from_start || Unlocked(b.required_research));

        string ItemKey(int itemId) => _data.items.Find(i => i.item_id == itemId)?.id;

        /// <summary>Null when the item can be had now; otherwise why not. Field drops can always be tapped.</summary>
        string WhyItemUnavailable(int itemId)
        {
            string key = ItemKey(itemId);
            if (key == null) return $"item_id {itemId} is not an item";
            foreach (var f in _data.fields)
                if (f.drops != null && f.drops.Exists(dr => dr.item == key)) return null;
            var recipes = _data.recipes.FindAll(r => r.output_item == key);
            if (recipes.Count == 0) return null;
            foreach (var r in recipes)
            {
                if (!Unlocked(r.required_research)) continue;
                if (r.valid_buildings == null || r.valid_buildings.Length == 0) return null;
                foreach (var b in r.valid_buildings) if (BuildingAvailable(b)) return null;
            }
            return $"no recipe for '{key}' is unlocked with an available building";
        }

        [Test]
        public void EveryStep_OnlyNeedsWhatEarlierStepsUnlocked()
        {
            _unlocked.Clear();
            int quarkFields = StartingQuarkFields, leptonFields = StartingLeptonFields;
            var problems = new List<string>();

            foreach (var s in _data.tutorial_steps)
            {
                if (string.IsNullOrEmpty(s.id)) continue; // _comment entries
                var c = s.advance_condition;

                void NeedBuilding(string id, string role)
                {
                    if (string.IsNullOrEmpty(id)) return;
                    if (!_buildings.ContainsKey(id)) problems.Add($"{s.id}: {role} '{id}' is not a building");
                    else if (!BuildingAvailable(id)) problems.Add($"{s.id}: {role} '{id}' is still locked");
                }

                if (c != null)
                {
                    switch (c.type)
                    {
                        case "ResearchUnlocked":
                            if (!_research.TryGetValue(c.research_id ?? "", out var r))
                                problems.Add($"{s.id}: research '{c.research_id}' does not exist");
                            else if (r.prerequisites != null)
                                foreach (var pre in r.prerequisites)
                                    if (!Unlocked(pre)) problems.Add($"{s.id}: '{r.id}' needs '{pre}', not bought yet");
                            break;
                        case "BuildingRecipeSet":
                        case "BuildingMin":
                        case "BuildingUpgradeMin":
                            NeedBuilding(c.building_id, "building");
                            if (c.building_id == "harvester" && c.min_count > quarkFields + leptonFields)
                                problems.Add($"{s.id}: wants {c.min_count} Harvesters but only " +
                                             $"{quarkFields + leptonFields} fields exist (one Harvester per field)");
                            break;
                        case "ConveyorLinked":
                            if (c.building_id != "maxwells_demon") NeedBuilding(c.building_id, "belt target");
                            NeedBuilding(c.from_building_id, "belt source");
                            break;
                        case "FieldPurchasedMin":
                            if (!_fields.TryGetValue(c.field_id ?? "", out var f))
                                problems.Add($"{s.id}: field '{c.field_id}' does not exist");
                            else if (!Unlocked(f.purchase_research))
                                problems.Add($"{s.id}: buying '{f.id}' needs '{f.purchase_research}'");
                            break;
                        case "InventoryMin":
                        case "ItemCraftedMin":
                            foreach (var req in c.items)
                            {
                                string why = WhyItemUnavailable(req.item_id);
                                if (why != null) problems.Add($"{s.id}: {why}");
                            }
                            break;
                    }
                }

                // Pulsed place buttons and world highlights must point at something the player can place.
                string pulse = s.on_enter?.pulse_button_id;
                if (!string.IsNullOrEmpty(pulse) && pulse.StartsWith("btn-place-") && pulse != "btn-place-conveyor")
                {
                    string id = pulse.Substring("btn-place-".Length);
                    if (_fields.TryGetValue(id, out var pf)) { if (!Unlocked(pf.purchase_research)) problems.Add($"{s.id}: pulses '{pulse}' before '{pf.purchase_research}'"); }
                    else NeedBuilding(id, "pulsed button");
                }
                string target = s.on_enter?.highlight_target;
                if (TutorialRouteGuide.TryParsePower(target, out var powered, out var near))
                {
                    NeedBuilding(powered, "highlight");
                    NeedBuilding(near, "highlight anchor");
                }

                // Effects of finishing this step apply to the steps after it.
                if (c?.type == "ResearchUnlocked" && !string.IsNullOrEmpty(c.research_id)) _unlocked.Add(c.research_id);
                if (c?.type == "FieldPurchasedMin" && c.field_id == "quark_field")
                    quarkFields = StartingQuarkFields + QuarkFieldsPerPurchase * Math.Max(1, c.min_count);
            }

            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void Tutorial_EndsWithCarbonFromFusion()
        {
            var ids = _data.tutorial_steps.FindAll(s => !string.IsNullOrEmpty(s.id)).ConvertAll(s => s.id);
            Assert.AreEqual("tutorial_complete", ids[ids.Count - 1]);
            Assert.Less(ids.IndexOf("buy_fusion_i"), ids.IndexOf("craft_first_carbon"));
            Assert.IsFalse(ids.Contains("buy_fission_i"), "Fission is a goal after the tutorial (#275)");
            Assert.IsFalse(ids.Contains("craft_first_uranium"), "Uranium is a goal after the tutorial (#275)");
        }
    }
}
