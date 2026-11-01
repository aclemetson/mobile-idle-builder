using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    /// <summary>
    /// FieldProximityChecker reads FieldGenerator's live registry (#283). It used to cache a
    /// FindObjectsByType scan once; after a site switch / prestige (ClearAllFields) the cached
    /// array held only destroyed fields, never re-scanned, and the proximity banner stopped
    /// working until a scene reload.
    /// </summary>
    [TestFixture]
    public class FieldProximityCheckerTests
    {
        readonly List<Object> _spawned = new();

        [SetUp]
        public void SetUp() => FieldGenerator.ClearRegistryForTests();

        [TearDown]
        public void TearDown()
        {
            FieldGenerator.ClearRegistryForTests();
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        FieldInstance SpawnField(string id, Vector3 pos, Vector2Int cell)
        {
            var so = ScriptableObject.CreateInstance<FieldSO>();
            so.id = id;
            so.displayName = id;
            _spawned.Add(so);

            var go = new GameObject(id);
            go.transform.position = pos;
            _spawned.Add(go);
            var fi = go.AddComponent<FieldInstance>();
            fi.Initialize(so);
            FieldGenerator.RegisterInstanceForTests(cell, fi);
            return fi;
        }

        FieldProximityChecker SpawnChecker(Vector3 pos)
        {
            var go = new GameObject("Character");
            go.transform.position = pos;
            _spawned.Add(go);
            return go.AddComponent<FieldProximityChecker>();   // hud left null: banner calls are null-safe
        }

        static void Tick(FieldProximityChecker c) =>
            typeof(FieldProximityChecker)
                .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(c, null);

        [Test]
        public void NoFieldsYet_ThenFieldsSpawn_PicksThemUp()
        {
            var checker = SpawnChecker(Vector3.zero);
            Tick(checker);
            Assert.IsNull(checker.CurrentField);

            var f = SpawnField("quark", new Vector3(1f, 0f, 0f), new Vector2Int(1, 0));
            Tick(checker);
            Assert.AreSame(f, checker.CurrentField, "Fields spawned after the first frame must be seen");
        }

        [Test]
        public void AfterSiteSwitch_NewSiteFieldsAreDetected()
        {
            var checker = SpawnChecker(Vector3.zero);
            var oldField = SpawnField("old_site_field", new Vector3(1f, 0f, 0f), new Vector2Int(1, 0));
            Tick(checker);
            Assert.AreSame(oldField, checker.CurrentField);

            // Site switch: every field is destroyed and the registry emptied, then the new site spawns.
            FieldGenerator.ClearRegistryForTests();
            Object.DestroyImmediate(oldField.gameObject);
            var newField = SpawnField("new_site_field", new Vector3(0f, 0f, 1f), new Vector2Int(0, 1));

            Tick(checker);
            Assert.AreSame(newField, checker.CurrentField,
                "The checker must not keep a stale cache of the previous site's (destroyed) fields");
        }

        [Test]
        public void FindClosest_PicksNearestInRange_AndSkipsDestroyed()
        {
            var near     = SpawnField("near", new Vector3(1f, 0f, 0f), new Vector2Int(1, 0));
            var nearer   = SpawnField("nearer", new Vector3(0.5f, 0f, 0f), new Vector2Int(2, 0));
            SpawnField("far", new Vector3(10f, 0f, 0f), new Vector2Int(3, 0));

            Assert.AreSame(nearer, FieldProximityChecker.FindClosest(FieldGenerator.AllInstances, Vector3.zero, 2.5f));

            Object.DestroyImmediate(nearer.gameObject);   // Unity-null while still in the registry
            Assert.AreSame(near, FieldProximityChecker.FindClosest(FieldGenerator.AllInstances, Vector3.zero, 2.5f));
            Assert.IsNull(FieldProximityChecker.FindClosest(FieldGenerator.AllInstances, Vector3.zero, 0.5f),
                "Nothing within the radius returns null");
        }
    }
}
