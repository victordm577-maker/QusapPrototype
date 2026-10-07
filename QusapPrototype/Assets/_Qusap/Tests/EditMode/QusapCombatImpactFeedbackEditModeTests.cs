using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapCombatImpactFeedbackEditModeTests
    {
        [TestCase(QusapCombatFeedbackType.Light, .035f)]
        [TestCase(QusapCombatFeedbackType.Heavy, .055f)]
        [TestCase(QusapCombatFeedbackType.Damage, .075f)]
        [TestCase(QusapCombatFeedbackType.Disarm, .075f)]
        [TestCase(QusapCombatFeedbackType.Launch, .075f)]
        [TestCase(QusapCombatFeedbackType.ParrySucceeded, .075f)]
        public void ApprovedProvisionalPauseIsBounded(QusapCombatFeedbackType kind, float seconds)
        {
            var profile = QusapCombatImpactProfile.For(kind);
            Assert.That(profile.Hold, Is.EqualTo(seconds));
            Assert.That(profile.ShakeDuration, Is.LessThanOrEqualTo(.18f));
        }

        [TestCase(QusapCombatFeedbackType.Whiff)]
        [TestCase(QusapCombatFeedbackType.Rejected)]
        [TestCase(QusapCombatFeedbackType.ParryFailed)]
        public void NonImpactsCannotPauseOrShake(QusapCombatFeedbackType kind)
        {
            var profile = QusapCombatImpactProfile.For(kind);
            Assert.That(profile.Hold, Is.Zero); Assert.That(profile.ShakeAmplitude, Is.Zero);
        }

        [Test] public void ContactUsesActualColliderAtTranslatedAndAirborneLocations()
        {
            var target = new GameObject("ImpactContactTarget", typeof(Rigidbody), typeof(BoxCollider), typeof(QusapHitReceiver));
            try
            {
                target.transform.position = new Vector3(8, 5, 0);
                Physics.SyncTransforms();
                Vector3 probe = new(7, 5, 0);
                Vector3 contact = (Vector3)typeof(QusapCombatFeedbackPresenter).GetMethod("ContactOnTarget",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(null, new object[] { target.GetComponent<QusapHitReceiver>(), probe });
                Assert.That(contact, Is.EqualTo(new Vector3(7.5f, 5, 0)));
            }
            finally { Object.DestroyImmediate(target); }
        }

        [Test] public void RenderHoldRestoresLatestLivePoseAndCameraOnReset()
        {
            var visual = new GameObject("ImpactPoseVisual");
            var bone = new GameObject("ImpactPoseBone").transform; bone.SetParent(visual.transform);
            var cameraRoot = new GameObject("ImpactTestCamera", typeof(Camera));
            var worldRoot = new GameObject("ImpactTestWorld", typeof(QusapCombatImpactWorld));
            try
            {
                var world = worldRoot.GetComponent<QusapCombatImpactWorld>(); var camera = cameraRoot.GetComponent<Camera>();
                float scale = Time.timeScale, delta = Time.fixedDeltaTime;
                world.Request(visual.transform, QusapCombatImpactProfile.For(QusapCombatFeedbackType.Launch));
                world.BeginCamera(camera); world.EndCamera();
                bone.localPosition = new Vector3(1, 2, 3); var pose = bone.localPosition;
                camera.transform.position = new Vector3(4, 5, -10); var cameraPosition = camera.transform.position;
                world.BeginCamera(camera);
                Assert.That(bone.localPosition, Is.Not.EqualTo(pose));
                world.ResetPresentation();
                Assert.That(bone.localPosition, Is.EqualTo(pose)); Assert.That(camera.transform.position, Is.EqualTo(cameraPosition));
                Assert.That(world.HoldCount, Is.Zero); Assert.That(Time.timeScale, Is.EqualTo(scale)); Assert.That(Time.fixedDeltaTime, Is.EqualTo(delta));
            }
            finally { Object.DestroyImmediate(worldRoot); Object.DestroyImmediate(cameraRoot); Object.DestroyImmediate(visual); }
        }
    }
}
