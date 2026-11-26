using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// Moving a placed field (#542): which cells move together (a quark pair moves as one unit) and the
    /// cell registry re-keying that keeps each field's instance (and so its tap cooldown).
    /// </summary>
    [TestFixture]
    public class FieldMoveTests
    {
        private readonly List<Object> _spawned = new();

        [SetUp]    public void SetUp()    => FieldGenerator.ClearRegistryForTests();

        [TearDown]
        public void TearDown()
        {
            FieldGenerator.ClearRegistryForTests();
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        private FieldSO Field(string id, int groupSize)
        {
            var so = ScriptableObject.CreateInstance<FieldSO>();
            so.id = id;
            so.displayName = id;
            so.purchaseGroupSize = groupSize;
            _spawned.Add(so);
            return so;
        }

        private FieldInstance Place(FieldSO so, int x, int y)
        {
            var go = new GameObject(so.id);
            _spawned.Add(go);
            var fi = go.AddComponent<FieldInstance>();
            fi.Initialize(so);
            FieldGenerator.RegisterInstanceForTests(new Vector2Int(x, y), fi);
            return fi;
        }

        // ── FieldGroupAt ─────────────────────────────────────────────────────

        [Test]
        public void GroupAt_NoField_IsEmpty()
        {
            Assert.IsEmpty(FieldGenerator.FieldGroupAt(new Vector2Int(3, 3)));
        }

        [Test]
        public void GroupAt_SingleField_MovesAlone_EvenNextToTheSameField()
        {
            var lepton = Field("electron_field", 1);
            Place(lepton, 3, 3);
            Place(lepton, 4, 3);

            CollectionAssert.AreEqual(new[] { new Vector2Int(3, 3) }, FieldGenerator.FieldGroupAt(new Vector2Int(3, 3)));
        }

        [Test]
        public void GroupAt_QuarkPair_TakesItsPartner_AnchorFirst()
        {
            var quark = Field("quark_field", 2);
            Place(quark, 5, 2);
            Place(quark, 4, 2);

            // Held on the right-hand tile: the pair is still listed left tile first.
            CollectionAssert.AreEqual(new[] { new Vector2Int(4, 2), new Vector2Int(5, 2) },
                FieldGenerator.FieldGroupAt(new Vector2Int(5, 2)));
        }

        [Test]
        public void GroupAt_UprightPair_ListsLowerTileFirst()
        {
            var quark = Field("quark_field", 2);
            Place(quark, 4, 6);
            Place(quark, 4, 5);

            CollectionAssert.AreEqual(new[] { new Vector2Int(4, 5), new Vector2Int(4, 6) },
                FieldGenerator.FieldGroupAt(new Vector2Int(4, 6)));
        }

        [Test]
        public void GroupAt_QuarkPair_IgnoresANeighbouringOtherField()
        {
            var quark  = Field("quark_field", 2);
            var lepton = Field("electron_field", 1);
            Place(quark, 4, 4);
            Place(lepton, 5, 4);

            CollectionAssert.AreEqual(new[] { new Vector2Int(4, 4) }, FieldGenerator.FieldGroupAt(new Vector2Int(4, 4)));
        }

        [Test]
        public void GroupAt_TwoPossiblePartners_PrefersTheBareOne()
        {
            var quark = Field("quark_field", 2);
            Place(quark, 4, 4);
            Place(quark, 5, 4); // has a Harvester on it
            Place(quark, 4, 5); // bare

            var harvester = new Vector2Int(5, 4);
            var group = FieldGenerator.FieldGroupAt(new Vector2Int(4, 4), c => c == harvester);

            CollectionAssert.AreEqual(new[] { new Vector2Int(4, 4), new Vector2Int(4, 5) }, group);
        }

        // ── MoveInRegistry ───────────────────────────────────────────────────

        [Test]
        public void Move_ReKeysTheSameInstance()
        {
            var lepton = Field("electron_field", 1);
            var fi = Place(lepton, 2, 2);

            Assert.IsTrue(FieldGenerator.MoveInRegistry(new[] { new Vector2Int(2, 2) }, new[] { new Vector2Int(7, 8) }));

            Assert.IsNull(FieldGenerator.GetFieldAt(2, 2));
            Assert.IsNull(FieldGenerator.GetFieldInstanceAt(2, 2));
            Assert.AreSame(lepton, FieldGenerator.GetFieldAt(7, 8));
            Assert.AreSame(fi, FieldGenerator.GetFieldInstanceAt(7, 8), "the instance (and its cooldown) moves with it");
        }

        [Test]
        public void Move_PairShiftedByOneTile_OverlapsItsOwnOldCell()
        {
            var quark = Field("quark_field", 2);
            var a = Place(quark, 3, 3);
            var b = Place(quark, 4, 3);

            Assert.IsTrue(FieldGenerator.MoveInRegistry(
                new[] { new Vector2Int(3, 3), new Vector2Int(4, 3) },
                new[] { new Vector2Int(4, 3), new Vector2Int(5, 3) }));

            Assert.IsNull(FieldGenerator.GetFieldAt(3, 3));
            Assert.AreSame(a, FieldGenerator.GetFieldInstanceAt(4, 3));
            Assert.AreSame(b, FieldGenerator.GetFieldInstanceAt(5, 3));
        }

        [Test]
        public void Move_OntoAnotherField_IsRefused_AndChangesNothing()
        {
            var lepton = Field("electron_field", 1);
            var quark  = Field("quark_field", 2);
            var moving = Place(lepton, 2, 2);
            var other  = Place(quark, 6, 6);

            Assert.IsFalse(FieldGenerator.MoveInRegistry(new[] { new Vector2Int(2, 2) }, new[] { new Vector2Int(6, 6) }));

            Assert.AreSame(moving, FieldGenerator.GetFieldInstanceAt(2, 2));
            Assert.AreSame(other,  FieldGenerator.GetFieldInstanceAt(6, 6));
        }

        [Test]
        public void Move_FromAnEmptyCell_OrMismatchedCounts_IsRefused()
        {
            var lepton = Field("electron_field", 1);
            Place(lepton, 2, 2);

            Assert.IsFalse(FieldGenerator.MoveInRegistry(new[] { new Vector2Int(9, 9) }, new[] { new Vector2Int(1, 1) }));
            Assert.IsFalse(FieldGenerator.MoveInRegistry(
                new[] { new Vector2Int(2, 2) },
                new[] { new Vector2Int(1, 1), new Vector2Int(1, 2) }));
            Assert.IsNotNull(FieldGenerator.GetFieldAt(2, 2));
        }
    }
}
