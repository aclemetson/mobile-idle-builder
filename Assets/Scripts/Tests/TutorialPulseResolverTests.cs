using NUnit.Framework;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Tests
{
    /// <summary>The tutorial pulse follows the path drawer -> panel button -> target (#202).</summary>
    [TestFixture]
    public class TutorialPulseResolverTests
    {
        private VisualElement _root, _drawer, _handle, _builderBtn, _panel, _placeConveyor;

        [SetUp]
        public void SetUp()
        {
            // Mirrors GameHUD.uxml: handle, left-drawer with the Builder button, hidden Builder panel.
            _root   = new VisualElement();
            _handle = new Button { name = TutorialPulseResolver.DrawerHandle };
            _root.Add(_handle);

            _drawer = new VisualElement { name = TutorialPulseResolver.DrawerName };
            _drawer.AddToClassList("left-drawer");
            _builderBtn = new Button { name = TutorialPulseResolver.BuilderButton };
            _drawer.Add(_builderBtn);
            _root.Add(_drawer);

            _panel = new VisualElement { name = "buildings-panel" };
            _panel.AddToClassList("slide-panel");
            _panel.AddToClassList("hidden");
            _placeConveyor = new Button { name = TutorialPulseResolver.PlaceButtonName("conveyor") };
            _panel.Add(_placeConveyor);
            _root.Add(_panel);
        }

        private VisualElement Resolve(string id) => TutorialPulseResolver.Resolve(_root, id);

        [Test]
        public void DrawerClosed_PulsesTheHandle()
            => Assert.AreSame(_handle, Resolve("btn-place-conveyor"));

        [Test]
        public void DrawerOpen_PanelClosed_PulsesThePanelButton()
        {
            _drawer.AddToClassList(TutorialPulseResolver.DrawerOpenClass);
            Assert.AreSame(_builderBtn, Resolve("btn-place-conveyor"));
        }

        [Test]
        public void PanelOpen_PulsesTheTarget()
        {
            _panel.RemoveFromClassList("hidden");
            Assert.AreSame(_placeConveyor, Resolve("btn-place-conveyor"));
        }

        [Test]
        public void PlaceButtonNotBuiltYet_GuidesToTheBuilder()
        {
            _drawer.AddToClassList(TutorialPulseResolver.DrawerOpenClass);
            Assert.AreSame(_builderBtn, Resolve("btn-place-harvester"));
        }

        [Test]
        public void DrawerButton_WithDrawerClosed_PulsesTheHandle()
            => Assert.AreSame(_handle, Resolve("btn-buildings"));

        [Test]
        public void UnknownTarget_ReturnsNull()
            => Assert.IsNull(Resolve("btn-does-not-exist"));
    }
}
