using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// An arrow must mean "this port still needs a belt", matching ConveyorSystem: an output feeds a belt that
    /// starts on the tile in front of it, and an input is fed by a belt that ends there. A belt just passing by
    /// connects nothing, and a deleted belt must bring the arrow back. Also checks the chevron shape.
    /// </summary>
    [TestFixture]
    public class PortGroundArrowTests
    {
        private static bool Show(bool isOutput, bool tileFree = true, bool hasBelt = false, bool head = false, bool tail = false) =>
            PortArrowRules.ShouldShow(isOutput, tileFree, hasBelt, head, tail);

        [TestCase(true)]
        [TestCase(false)]
        public void NoBelt_ShowsArrow(bool isOutput) => Assert.IsTrue(Show(isOutput));

        [TestCase(true)]
        [TestCase(false)]
        public void TileBlockedByABuildingOrTheGridEdge_HidesArrow(bool isOutput) =>
            Assert.IsFalse(Show(isOutput, tileFree: false));

        [Test]
        public void Output_HidesWhenABeltStartsThere() =>
            Assert.IsFalse(Show(true, hasBelt: true, head: true));

        [Test]
        public void Output_KeepsArrow_WhenABeltOnlyEndsOrPassesThere()
        {
            Assert.IsTrue(Show(true, hasBelt: true, tail: true), "a belt ending here takes nothing from the output");
            Assert.IsTrue(Show(true, hasBelt: true), "a belt passing through takes nothing from the output");
        }

        [Test]
        public void Input_HidesWhenABeltEndsThere() =>
            Assert.IsFalse(Show(false, hasBelt: true, tail: true));

        [Test]
        public void Input_KeepsArrow_WhenABeltOnlyStartsOrPassesThere()
        {
            Assert.IsTrue(Show(false, hasBelt: true, head: true), "a belt starting here delivers nothing to the input");
            Assert.IsTrue(Show(false, hasBelt: true), "a belt passing through delivers nothing to the input");
        }

        [Test]
        public void DeletingTheBelt_BringsTheArrowBack()
        {
            Assert.IsFalse(Show(true, hasBelt: true, head: true));
            Assert.IsTrue(Show(true, hasBelt: false));
        }

        [Test]
        public void Chevron_PointsForward_FacesUp_AndIsSymmetric()
        {
            const float halfWidth = 0.3f, depth = 0.2f, thickness = 0.1f, y = 0.07f;
            var mesh = ChevronMeshBuilder.Build(halfWidth, depth, thickness, y);
            try
            {
                var verts = mesh.vertices;
                Assert.AreEqual(new Vector3(0f, y, depth * 0.5f), verts[0], "the tip points along +Z");
                foreach (var v in verts)
                {
                    Assert.AreEqual(y, v.y, 1e-5f, "flat");
                    Assert.LessOrEqual(v.z, depth * 0.5f + 1e-5f, "nothing ahead of the tip");
                }
                foreach (var n in mesh.normals)
                    Assert.Greater(n.y, 0.99f, "culled from above if it faces down");
                Assert.AreEqual(0f, mesh.bounds.center.x, 1e-5f);
                Assert.AreEqual(halfWidth, mesh.bounds.extents.x, 1e-5f);
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
