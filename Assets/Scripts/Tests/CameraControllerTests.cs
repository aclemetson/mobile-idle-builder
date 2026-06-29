using NUnit.Framework;
using UnityEngine;

namespace MobileIdleBuilder.Tests
{
    [TestFixture]
    public class CameraControllerTests
    {
        // ── OrbitOffset ──────────────────────────────────────────────────────
        // The orbit rig seeds yaw/pitch/distance from the legacy offset (0, 8, -6),
        // so OrbitOffset at that yaw/pitch/distance must round-trip to (0, 8, -6).

        [Test]
        public void OrbitOffset_DefaultRig_MatchesLegacyOffset()
        {
            Vector3 legacy   = new Vector3(0f, 8f, -6f);
            float   distance = legacy.magnitude;                       // 10
            float   horizLen = new Vector2(legacy.x, legacy.z).magnitude; // 6
            float   pitch    = Mathf.Atan2(legacy.y, horizLen) * Mathf.Rad2Deg;
            float   yaw      = Mathf.Atan2(legacy.x, -legacy.z) * Mathf.Rad2Deg;

            Vector3 result = CameraController.OrbitOffset(yaw, pitch, distance);

            Assert.AreEqual(legacy.x, result.x, 1e-4f);
            Assert.AreEqual(legacy.y, result.y, 1e-4f);
            Assert.AreEqual(legacy.z, result.z, 1e-4f);
        }

        [Test]
        public void OrbitOffset_PitchNinety_IsStraightDown()
        {
            Vector3 result = CameraController.OrbitOffset(0f, 90f, 10f);

            Assert.AreEqual(0f, result.x, 1e-4f);
            Assert.AreEqual(10f, result.y, 1e-4f);
            Assert.AreEqual(0f, result.z, 1e-4f);
        }

        [Test]
        public void OrbitOffset_YawNinety_RotatesAroundY()
        {
            // At yaw 90, pitch 0 (horizontal), the camera offset swings to +X.
            Vector3 result = CameraController.OrbitOffset(90f, 0f, 10f);

            Assert.AreEqual(10f, result.x, 1e-4f);
            Assert.AreEqual(0f, result.y, 1e-4f);
            Assert.AreEqual(0f, result.z, 1e-4f);
        }

        [Test]
        public void OrbitOffset_PreservesDistanceAtAnyAngle()
        {
            Vector3 result = CameraController.OrbitOffset(37f, 42f, 13.5f);
            Assert.AreEqual(13.5f, result.magnitude, 1e-3f);
        }
    }
}
