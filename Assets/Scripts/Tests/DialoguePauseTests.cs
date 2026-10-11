using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>A dialogue pauses (or not) as a whole, never page by page (#235).</summary>
    [TestFixture]
    public class DialoguePauseTests
    {
        private static DialogueSO Make(params bool[] pauses)
        {
            var so = ScriptableObject.CreateInstance<DialogueSO>();
            so.lines = new DialogueLine[pauses.Length];
            for (int i = 0; i < pauses.Length; i++)
                so.lines[i] = new DialogueLine { text = "line " + i, pauseGame = pauses[i] };
            return so;
        }

        [Test]
        public void FirstPagePausesLaterPagesDoNot_WholeDialoguePauses()
            => Assert.IsTrue(DialogueController.PausesGame(Make(true, false, false)));

        [Test]
        public void NoPagePauses_DialogueDoesNotPause()
            => Assert.IsFalse(DialogueController.PausesGame(Make(false, false)));

        [Test]
        public void EmptyOrMissingDialogue_DoesNotPause()
        {
            Assert.IsFalse(DialogueController.PausesGame(null));
            Assert.IsFalse(DialogueController.PausesGame(Make()));
        }
    }
}
