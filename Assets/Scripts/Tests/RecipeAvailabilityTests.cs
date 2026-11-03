using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Research-gated recipe availability and the single-recipe auto-assign rule (#186).
    /// </summary>
    public class RecipeAvailabilityTests
    {
        readonly List<Object> _created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _created) Object.DestroyImmediate(o);
            _created.Clear();
        }

        T Make<T>() where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            _created.Add(o);
            return o;
        }

        RecipeSO Recipe(string researchId)
        {
            var r = Make<RecipeSO>();
            if (researchId != null)
            {
                r.requiredResearch = Make<ResearchSO>();
                r.requiredResearch.id = researchId;
            }
            return r;
        }

        BuildingSO Building(params RecipeSO[] recipes)
        {
            var b = Make<BuildingSO>();
            b.supportedRecipes = recipes;
            return b;
        }

        [Test]
        public void SoleAvailable_ReturnsTheOnlyUnlockedRecipe()
        {
            var hydrogen = Recipe("hydrogen_synthesis");
            var helium   = Recipe("helium_fusion");
            var b = Building(hydrogen, helium);

            Assert.AreSame(hydrogen, RecipeAvailability.SoleAvailable(b, id => id == "hydrogen_synthesis"));
        }

        [Test]
        public void SoleAvailable_NullWhenSeveralOrNoneAvailable()
        {
            var b = Building(Recipe("a"), Recipe("b"));

            Assert.IsNull(RecipeAvailability.SoleAvailable(b, _ => true));
            Assert.IsNull(RecipeAvailability.SoleAvailable(b, _ => false));
        }

        [Test]
        public void IsAvailable_UngatedRecipeIsAlwaysAvailable()
            => Assert.IsTrue(RecipeAvailability.IsAvailable(Recipe(null), _ => false));

        [Test]
        public void SoleAvailable_NullBuildingOrRecipes()
        {
            Assert.IsNull(RecipeAvailability.SoleAvailable(null, _ => true));
            Assert.IsNull(RecipeAvailability.SoleAvailable(Building(), _ => true));
        }

        [Test]
        public void GateBlockedMessage_NamesTheAllowedBuilding()
            => StringAssert.Contains("the Atom Generator",
                BuildingInspectorController.GateBlockedMessage("the Atom Generator"));
    }
}
