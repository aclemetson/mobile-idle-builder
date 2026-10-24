using NUnit.Framework;
using Block = MobileIdleBuilder.BuildingPlacementController.PlacementBlock;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// A tap on a cell the placement ✓ can't confirm says why in the notification banner (#258):
    /// a Harvester tapped onto bare ground used to show a disabled ✓ and nothing else.
    /// </summary>
    [TestFixture]
    public class PlacementBlockMessageTests
    {
        [Test]
        public void ValidCell_NoMessage() =>
            Assert.IsNull(BuildingPlacementController.BlockMessage(Block.None, "Harvester", isField: false));

        [Test]
        public void HarvesterOffField_SaysToUseAFieldTile() =>
            Assert.AreEqual("Place the Harvester on a field tile.",
                BuildingPlacementController.BlockMessage(Block.NeedsField, "Harvester", isField: false));

        [Test]
        public void WrongFieldType_NamesTheBuilding() =>
            Assert.AreEqual("The Harvester can't harvest this field.",
                BuildingPlacementController.BlockMessage(Block.WrongField, "Harvester", isField: false));

        [Test]
        public void BuildingOnField_AndFieldOnField_DifferByWhatIsPlaced()
        {
            Assert.AreEqual("Fields are for Harvesters. Pick an empty tile.",
                BuildingPlacementController.BlockMessage(Block.OnField, "Basic Generator", isField: false));
            Assert.AreEqual("A new field needs empty ground.",
                BuildingPlacementController.BlockMessage(Block.OnField, null, isField: true));
        }

        [Test]
        public void MissingName_StillReadsAsASentence() =>
            Assert.AreEqual("Place it on a field tile.",
                BuildingPlacementController.BlockMessage(Block.NeedsField, null, isField: false));

        [Test]
        public void EveryBlock_FitsTheOneLineBanner()
        {
            foreach (Block b in System.Enum.GetValues(typeof(Block)))
            {
                var msg = BuildingPlacementController.BlockMessage(b, "Radioactive Containment", isField: false);
                if (msg != null) Assert.LessOrEqual(msg.Length, 56, msg);
            }
        }
    }
}
