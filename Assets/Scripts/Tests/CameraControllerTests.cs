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

        // ── SignedAngleDelta (two-finger twist -> yaw) ───────────────────────

        [Test]
        public void SignedAngleDelta_NoTwist_IsZero()
        {
            // Fingers move apart (pinch) but the connecting angle is unchanged -> no yaw.
            float twist = CameraController.SignedAngleDelta(
                new Vector2(0, 0), new Vector2(1, 0),   // prev vector (1,0)
                new Vector2(0, 0), new Vector2(2, 0));  // cur  vector (2,0), same angle
            Assert.AreEqual(0f, twist, 1e-3f);
        }

        [Test]
        public void SignedAngleDelta_QuarterTurn_Is90Degrees()
        {
            float twist = CameraController.SignedAngleDelta(
                new Vector2(0, 0), new Vector2(1, 0),   // prev vector (1,0)
                new Vector2(0, 0), new Vector2(0, 1));  // cur  vector (0,1) -> +90
            Assert.AreEqual(90f, twist, 1e-3f);
        }

        [Test]
        public void SignedAngleDelta_DegenerateInput_IsZero()
        {
            float twist = CameraController.SignedAngleDelta(
                new Vector2(1, 1), new Vector2(1, 1),   // zero-length prev
                new Vector2(0, 0), new Vector2(0, 1));
            Assert.AreEqual(0f, twist, 1e-4f);
        }

        // ── DecayVelocity (pan inertia) ──────────────────────────────────────

        [Test]
        public void DecayVelocity_ZeroDamping_KeepsVelocity()
        {
            Vector3 v = new Vector3(3, 0, 4);
            Assert.AreEqual(v, CameraController.DecayVelocity(v, 0f, 0.016f));
        }

        [Test]
        public void DecayVelocity_HalfStep_HalvesVelocity()
        {
            Vector3 v = new Vector3(4, 0, 8);
            Vector3 r = CameraController.DecayVelocity(v, 1f, 0.5f); // damping*dt = 0.5
            Assert.AreEqual(2f, r.x, 1e-4f);
            Assert.AreEqual(4f, r.z, 1e-4f);
        }

        [Test]
        public void DecayVelocity_OverfullStep_ClampsToZero()
        {
            Vector3 r = CameraController.DecayVelocity(new Vector3(5, 0, 5), 100f, 1f);
            Assert.AreEqual(Vector3.zero, r);
        }
    }
}
