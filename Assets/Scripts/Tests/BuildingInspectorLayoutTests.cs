using System.Collections.Generic;
using NUnit.Framework;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Pins the rules that keep the building inspector's layout from bouncing as items flow through a
    /// building: buffer row counts depend on what the recipe expects, not on what happens to be in the
    /// buffer this half-second, and the input section doesn't blink in and out.
    /// </summary>
    public class BuildingInspectorLayoutTests
    {
        static List<(int itemId, int quantity)> Rows(IReadOnlyList<int> expected,
                                                      params (int itemId, int quantity)[] contents)
            => HUDBuildingInspectorSubController.StableBufferRows(expected, contents);

        [Test]
        public void StableBufferRows_EmptyBuffer_StillListsEveryExpectedItemAtZero()
        {
            var rows = Rows(new[] { 1, 2 });
            Assert.AreEqual(new List<(int, int)> { (1, 0), (2, 0) }, rows);
        }

        [Test]
        public void StableBufferRows_RowCountIsTheSameEmptyAndFull()
        {
            var expected = new[] { 1, 2 };
            Assert.AreEqual(Rows(expected).Count, Rows(expected, (2, 5), (1, 3)).Count);
        }

        [Test]
        public void StableBufferRows_KeepsRecipeOrderRegardlessOfBufferOrder()
        {
            var rows = Rows(new[] { 1, 2 }, (2, 5), (1, 3));
            Assert.AreEqual(new List<(int, int)> { (1, 3), (2, 5) }, rows);
        }

        [Test]
        public void StableBufferRows_UnexpectedItemsAppendAfterExpected()
        {
            var rows = Rows(new[] { 1 }, (9, 4), (1, 2));
            Assert.AreEqual(new List<(int, int)> { (1, 2), (9, 4) }, rows);
        }

        [Test]
        public void StableBufferRows_MergesDuplicateIds()
        {
            var rows = Rows(new[] { 1, 1 }, (1, 2), (1, 3));
            Assert.AreEqual(new List<(int, int)> { (1, 5) }, rows);
        }

        [Test]
        public void StableBufferRows_NoRecipeAndEmptyBuffer_IsEmpty()
        {
            Assert.IsEmpty(Rows(null));
        }

        [Test]
        public void ShowInputBufferSection_RecipeWithInputs_ShownWhileBufferEmpty()
        {
            Assert.IsTrue(HUDBuildingInspectorSubController.ShowInputBufferSection(0, 2, isSink: false));
        }

        [Test]
        public void ShowInputBufferSection_Sink_ShownWhileBufferEmpty()
        {
            Assert.IsTrue(HUDBuildingInspectorSubController.ShowInputBufferSection(0, 0, isSink: true));
        }

        [Test]
        public void ShowInputBufferSection_NoInputsAtAll_Hidden()
        {
            Assert.IsFalse(HUDBuildingInspectorSubController.ShowInputBufferSection(0, 0, isSink: false));
        }

        [Test]
        public void ShowInputBufferSection_LeftoverItems_Shown()
        {
            Assert.IsTrue(HUDBuildingInspectorSubController.ShowInputBufferSection(1, 0, isSink: false));
        }
    }
}
