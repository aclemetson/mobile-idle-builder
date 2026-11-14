using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    public class RecipeTextTests
    {
        static List<(string, int)> L(params (string, int)[] a) => new List<(string, int)>(a);

        [Test]
        public void MethodLine_WithInputs_ListsBuildingAndInputs()
        {
            string s = RecipeText.MethodLine("Atomic Assembler", "", L(("Proton", 1), ("Neutron", 1), ("Electron", 1)), L());
            Assert.AreEqual("Atomic Assembler · 1 Proton, 1 Neutron, 1 Electron", s);
        }

        [Test]
        public void MethodLine_WithMethodLabel_PutsInputsInBrackets()
        {
            string s = RecipeText.MethodLine("Fusion Reactor", "p-p chain", L(("Hydrogen", 4)), L());
            Assert.AreEqual("Fusion Reactor · p-p chain (4 Hydrogen)", s);
        }

        [Test]
        public void MethodLine_WithCoProducts_AppendsThem()
        {
            string s = RecipeText.MethodLine("Fission Reactor", "", L(("Uranium-235", 1)), L(("Krypton", 1), ("Neutron", 3)));
            Assert.AreEqual("Fission Reactor · 1 Uranium-235 → + 1 Krypton, 3 Neutron", s);
        }

        [Test]
        public void MethodLine_NoInputs_IsJustTheBuilding()
        {
            Assert.AreEqual("Nucleon Harvester", RecipeText.MethodLine("Nucleon Harvester", "", L(), L()));
            Assert.AreEqual("Nucleon Harvester", RecipeText.MethodLine("Nucleon Harvester", null, null, null));
        }

        [Test]
        public void MethodLine_NullRecipe_IsEmpty()
        {
            Assert.AreEqual("", RecipeText.MethodLine((RecipeSO)null));
        }
    }
}
