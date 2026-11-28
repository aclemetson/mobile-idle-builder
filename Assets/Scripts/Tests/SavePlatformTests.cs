using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The platform stamp test-player cleanup relies on (SaveData.platforms).</summary>
    public class SavePlatformTests
    {
        [Test]
        public void Tag_MapsEveryEditorToEditor_AndPhonesToTheirNames()
        {
            Assert.AreEqual(SavePlatform.Editor,  SavePlatform.Tag(RuntimePlatform.WindowsEditor));
            Assert.AreEqual(SavePlatform.Editor,  SavePlatform.Tag(RuntimePlatform.OSXEditor));
            Assert.AreEqual(SavePlatform.Android, SavePlatform.Tag(RuntimePlatform.Android));
            Assert.AreEqual(SavePlatform.IOS,     SavePlatform.Tag(RuntimePlatform.IPhonePlayer));
            Assert.AreEqual("WindowsPlayer",      SavePlatform.Tag(RuntimePlatform.WindowsPlayer));
            Assert.AreEqual(SavePlatform.Editor,  SavePlatform.Current, "tests run in the Editor");
        }

        [Test]
        public void Stamp_AddsEachPlatformOnce_AndKeepsEarlierOnes()
        {
            var save = new SaveData();
            SavePlatform.Stamp(save, SavePlatform.Android);
            SavePlatform.Stamp(save, SavePlatform.Editor);
            SavePlatform.Stamp(save, SavePlatform.Android);
            CollectionAssert.AreEqual(new[] { SavePlatform.Android, SavePlatform.Editor }, save.platforms);

            var old = JsonUtility.FromJson<SaveData>("{\"playerId\":\"p\"}");   // a save from before the field
            SavePlatform.Stamp(old, SavePlatform.IOS);
            CollectionAssert.AreEqual(new[] { SavePlatform.IOS }, old.platforms);
            Assert.DoesNotThrow(() => SavePlatform.Stamp(null));
        }

        [Test]
        public void Platforms_RoundTripThroughJson()
        {
            var save = new SaveData();
            SavePlatform.Stamp(save, SavePlatform.Android);
            var back = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save));
            CollectionAssert.AreEqual(new[] { SavePlatform.Android }, back.platforms);
        }
    }
}
