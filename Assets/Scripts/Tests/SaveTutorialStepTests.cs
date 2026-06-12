using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Edit Mode tests for ECSLoadBridge.ResolveStepIndex — the saved tutorial step-id to
    /// ECS step-index lookup that runs on every load. A silent reset to 0 here is exactly
    /// what makes the tutorial "restart from step 0", so each fallback path is asserted.
    /// </summary>
    [TestFixture]
    public class SaveTutorialStepTests
    {
        [TearDown]
        public void TearDown() => TutorialFlowSO.ClearCurrentForTesting();

        static void MakeFlow(params string[] ids)
        {
            var so    = ScriptableObject.CreateInstance<TutorialFlowSO>();
            var steps = new TutorialStepDef[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                steps[i] = new TutorialStepDef { id = ids[i] };
            so.steps = steps;
            TutorialFlowSO.SetCurrentForTesting(so);
        }

        [Test]
        public void ResolveStepIndex_EmptyId_ReturnsZeroSilently()
        {
            MakeFlow("intro", "build", "research");
            Assert.AreEqual(0, ECSLoadBridge.ResolveStepIndex(""));
            Assert.AreEqual(0, ECSLoadBridge.ResolveStepIndex(null));
        }

        [Test]
        public void ResolveStepIndex_KnownId_ReturnsMatchingIndex()
        {
            MakeFlow("intro", "build", "research");
            Assert.AreEqual(0, ECSLoadBridge.ResolveStepIndex("intro"));
            Assert.AreEqual(1, ECSLoadBridge.ResolveStepIndex("build"));
            Assert.AreEqual(2, ECSLoadBridge.ResolveStepIndex("research"));
        }

        [Test]
        public void ResolveStepIndex_UnknownId_ReturnsZeroAndWarns()
        {
            MakeFlow("intro", "build");
            LogAssert.Expect(LogType.Warning, new Regex("not found in flow"));
            Assert.AreEqual(0, ECSLoadBridge.ResolveStepIndex("does_not_exist"));
        }

        [Test]
        public void ResolveStepIndex_NullFlow_NonEmptyId_ReturnsZeroAndWarns()
        {
            TutorialFlowSO.ClearCurrentForTesting();
            LogAssert.Expect(LogType.Warning, new Regex("TutorialFlowSO.Current is null"));
            Assert.AreEqual(0, ECSLoadBridge.ResolveStepIndex("build"));
        }
    }
}
