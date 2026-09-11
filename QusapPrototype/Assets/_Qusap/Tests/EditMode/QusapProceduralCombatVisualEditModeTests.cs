using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapProceduralCombatVisualEditModeTests
    {
        private const string OfficialPrefabPath =
            "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string DefaultProfilePath =
            "Assets/_Qusap/Settings/CombatVisuals/QusapModularProceduralCombatVisualProfile.asset";
        private QusapProceduralCombatVisualProfile profile;

        [SetUp]
        public void SetUp()
        {
            profile = QusapProceduralCombatVisualProfile.CreateDefault();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void ProfileCanBeBuiltThroughPublicApiWithoutReflection()
        {
            var pose = new QusapProceduralCombatPose(
                Vector3.one, Vector3.one, Vector3.one, Vector3.one,
                Vector3.one, Vector3.one, Vector3.one, Vector3.one);
            var motion = new QusapProceduralCombatMotion(
                pose, pose, pose, pose, pose);
            profile.ConfigureMotion(QusapProceduralCombatMotionId.BodyAttack, motion);
            Assert.That(profile.GetMotion(QusapProceduralCombatMotionId.BodyAttack), Is.SameAs(motion));
        }

        [Test]
        public void AssignedDefaultProfileAssetContainsVisibleChoreography()
        {
            QusapProceduralCombatVisualProfile assigned =
                AssetDatabase.LoadAssetAtPath<QusapProceduralCombatVisualProfile>(
                    DefaultProfilePath);
            Assert.That(assigned, Is.Not.Null);

            QusapProceduralCombatPoseValue pose = assigned.Evaluate(
                new QusapCombatVisualContext(
                    1, QusapCombatCommand.BodyAttack, null, -1, false, 1,
                    QusapAttackPhase.Active, 0.68f),
                out QusapProceduralCombatMotionId motionId);

            Assert.That(motionId, Is.EqualTo(QusapProceduralCombatMotionId.BodyAttack));
            Assert.That(pose.LeftFootLocalPosition.magnitude, Is.GreaterThan(0.2f));
        }

        [Test]
        public void NonFinitePoseValuesAreNormalized()
        {
            var invalid = new QusapProceduralCombatPose(
                new Vector3(float.NaN, 0f, 0f), Vector3.zero,
                Vector3.zero, new Vector3(0f, float.PositiveInfinity, 0f),
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero);
            profile.ConfigureMotion(
                QusapProceduralCombatMotionId.BodyAttack,
                new QusapProceduralCombatMotion(invalid, invalid, invalid, invalid, invalid));
            QusapProceduralCombatPose neutral =
                profile.GetMotion(QusapProceduralCombatMotionId.BodyAttack).Neutral;
            Assert.That(neutral.BodyLocalPosition, Is.EqualTo(Vector3.zero));
            Assert.That(neutral.LeftFootLocalEulerAngles, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void ExcessiveTranslationsAndRotationsAreClamped()
        {
            var unsafePose = new QusapProceduralCombatPose(
                Vector3.one * 99f, Vector3.one * -999f,
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                Vector3.zero, Vector3.zero);
            var motion = new QusapProceduralCombatMotion(
                unsafePose, unsafePose, unsafePose, unsafePose, unsafePose);
            profile.ConfigureMotion(QusapProceduralCombatMotionId.BodyAttack, motion);
            Assert.That(motion.Neutral.BodyLocalPosition.x,
                Is.EqualTo(profile.SafeTranslationLimit));
            Assert.That(motion.Neutral.BodyLocalEulerAngles.x,
                Is.EqualTo(-profile.SafeRotationLimit));
        }

        [Test]
        public void NullCurvesReceiveSafeDefaults()
        {
            var motion = new QusapProceduralCombatMotion(
                null, null, null, null, null, null, null, null);
            profile.ConfigureMotion(QusapProceduralCombatMotionId.BodyAttack, motion);
            Assert.That(motion.StartupCurve, Is.Not.Null);
            Assert.That(motion.ActiveCurve, Is.Not.Null);
            Assert.That(motion.RecoveryCurve, Is.Not.Null);
            Assert.That(motion.StartupCurve.length, Is.GreaterThanOrEqualTo(2));
        }

        [Test]
        public void InvalidIntensityAndSafetyLimitsAreNormalized()
        {
            profile.ConfigureSafety(float.NaN, float.PositiveInfinity, -50f);
            Assert.That(profile.GlobalIntensity, Is.EqualTo(1f));
            Assert.That(profile.SafeTranslationLimit,
                Is.EqualTo(QusapProceduralCombatVisualProfile.DefaultTranslationLimit));
            Assert.That(profile.SafeRotationLimit, Is.EqualTo(1f));
            profile.ConfigureSafety(-2f, 0.5f, 90f);
            Assert.That(profile.GlobalIntensity, Is.Zero);
        }

        [Test]
        public void InvalidComboStepIndexIsRejectedWithoutReflection()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new QusapCombatVisualContext(
                    1, QusapCombatCommand.BodyAttack, null, -2, false, 1,
                    QusapAttackPhase.Startup, 0f));
        }

        [TestCase(QusapCombatCommand.BodyAttack, QusapProceduralCombatMotionId.BodyAttack)]
        [TestCase(QusapCombatCommand.WeaponLight, QusapProceduralCombatMotionId.WeaponLightFirst)]
        [TestCase(QusapCombatCommand.WeaponStrong, QusapProceduralCombatMotionId.WeaponStrong)]
        [TestCase(QusapCombatCommand.Headbutt, QusapProceduralCombatMotionId.Headbutt)]
        public void EachBasicCommandSelectsExpectedMotion(
            QusapCombatCommand command,
            QusapProceduralCombatMotionId expected)
        {
            Assert.That(QusapProceduralCombatVisualProfile.SelectMotionId(
                Context(1, command)), Is.EqualTo(expected));
        }

        [Test]
        public void ConsecutiveWeaponLightsSelectComplementaryMotions()
        {
            QusapCombatVisualContext first = Context(
                1, QusapCombatCommand.WeaponLight, null, 0);
            QusapCombatVisualContext second = Context(
                2, QusapCombatCommand.WeaponLight, null, 1);
            Assert.That(QusapProceduralCombatVisualProfile.SelectMotionId(first),
                Is.EqualTo(QusapProceduralCombatMotionId.WeaponLightFirst));
            Assert.That(QusapProceduralCombatVisualProfile.SelectMotionId(second),
                Is.EqualTo(QusapProceduralCombatMotionId.WeaponLightSecond));
        }

        [Test]
        public void SharedTwoLightPrefixDoesNotChooseDamageOrDisarm()
        {
            QusapCombatVisualContext shared = Context(
                2, QusapCombatCommand.WeaponLight, null, 1);
            Assert.That(shared.ComboId, Is.Null);
            Assert.That(QusapProceduralCombatVisualProfile.SelectMotionId(shared),
                Is.EqualTo(QusapProceduralCombatMotionId.WeaponLightSecond));
        }

        [TestCase(QusapComboId.Damage, QusapProceduralCombatMotionId.DamageFinisher)]
        [TestCase(QusapComboId.Disarm, QusapProceduralCombatMotionId.DisarmFinisher)]
        [TestCase(QusapComboId.Launch, QusapProceduralCombatMotionId.LaunchFinisher)]
        public void FinishersSelectTheirConfirmedComboMotion(
            QusapComboId combo,
            QusapProceduralCombatMotionId expected)
        {
            var context = new QusapCombatVisualContext(
                9, QusapCombatCommand.BodyAttack, combo, 2, true, 1,
                QusapAttackPhase.Active, 0.5f);
            Assert.That(QusapProceduralCombatVisualProfile.SelectMotionId(context),
                Is.EqualTo(expected));
        }

        [Test]
        public void FacingMirrorsPoseDeterministically()
        {
            QusapProceduralCombatPoseValue right = profile.Evaluate(
                Context(1, QusapCombatCommand.BodyAttack, null, -1, false, 1), out _);
            QusapProceduralCombatPoseValue left = profile.Evaluate(
                Context(2, QusapCombatCommand.BodyAttack, null, -1, false, -1), out _);
            Assert.That(left.LeftFootLocalPosition.x,
                Is.EqualTo(-right.LeftFootLocalPosition.x).Within(0.00001f));
            Assert.That(left.LeftFootLocalPosition.y,
                Is.EqualTo(right.LeftFootLocalPosition.y).Within(0.00001f));
            Assert.That(left.LeftFootLocalEulerAngles.z,
                Is.EqualTo(-right.LeftFootLocalEulerAngles.z).Within(0.00001f));
        }

        [Test]
        public void CapturedFacingDoesNotDependOnLaterFacingValues()
        {
            QusapCombatVisualContext captured = Context(
                1, QusapCombatCommand.BodyAttack, null, -1, false, -1);
            QusapProceduralCombatPoseValue before = profile.Evaluate(captured, out _);
            int unrelatedCurrentFacing = 1;
            QusapProceduralCombatPoseValue after = profile.Evaluate(captured, out _);
            Assert.That(unrelatedCurrentFacing, Is.EqualTo(1));
            Assert.That(after.LeftFootLocalPosition, Is.EqualTo(before.LeftFootLocalPosition));
        }

        [Test]
        public void CompletionRestoresExactNeutralPose()
        {
            using var fixture = new PresenterFixture(profile);
            Vector3 neutral = fixture.Rig.BodyPivot.localPosition;
            fixture.Presenter.Present(Context(1, QusapCombatCommand.BodyAttack));
            fixture.Presenter.CancelExecution(1, QusapCombatVisualCancellationReason.Completed);
            Assert.That(fixture.Rig.BodyPivot.localPosition, Is.EqualTo(neutral));
        }

        [Test]
        public void CancellationRestoresExactNeutralPose()
        {
            using var fixture = new PresenterFixture(profile);
            Quaternion neutral = fixture.Rig.FootPivotLeft.localRotation;
            fixture.Presenter.Present(Context(1, QusapCombatCommand.BodyAttack));
            fixture.Presenter.CancelExecution(1, QusapCombatVisualCancellationReason.Hitstun);
            Assert.That(fixture.Rig.FootPivotLeft.localRotation, Is.EqualTo(neutral));
        }

        [Test]
        public void RestoringTwiceIsIdempotent()
        {
            using var fixture = new PresenterFixture(profile);
            fixture.Presenter.Present(Context(1, QusapCombatCommand.BodyAttack));
            fixture.Presenter.CancelExecution(1);
            Vector3 once = fixture.Rig.FootPivotLeft.localPosition;
            fixture.Presenter.CancelExecution(1);
            Assert.That(fixture.Rig.FootPivotLeft.localPosition, Is.EqualTo(once));
        }

        [Test]
        public void RepeatedExecutionIdDoesNotRestartAnimation()
        {
            using var fixture = new PresenterFixture(profile);
            QusapCombatVisualContext context = Context(5, QusapCombatCommand.BodyAttack);
            Assert.That(fixture.Presenter.Present(context), Is.True);
            Vector3 once = fixture.Rig.BodyPivot.localPosition;
            Assert.That(fixture.Presenter.Present(context), Is.True);
            Assert.That(fixture.Presenter.ActiveExecutionId, Is.EqualTo(5));
            Assert.That(fixture.Rig.BodyPivot.localPosition, Is.EqualTo(once));
        }

        [Test]
        public void LateContextForCancelledExecutionIsIgnored()
        {
            using var fixture = new PresenterFixture(profile);
            QusapCombatVisualContext context = Context(3, QusapCombatCommand.BodyAttack);
            fixture.Presenter.Present(context);
            fixture.Presenter.CancelExecution(3);
            Assert.That(fixture.Presenter.Present(context), Is.False);
            Assert.That(fixture.Presenter.IsPresenting, Is.False);
        }

        [Test]
        public void OneHundredExecutionsDoNotDrift()
        {
            using var fixture = new PresenterFixture(profile);
            Vector3 body = fixture.Rig.BodyPivot.localPosition;
            Vector3 left = fixture.Rig.FootPivotLeft.localPosition;
            for (ulong id = 1; id <= 100; id++)
            {
                fixture.Presenter.Present(Context(id, QusapCombatCommand.BodyAttack));
                fixture.Presenter.CancelExecution(id, QusapCombatVisualCancellationReason.Completed);
            }
            Assert.That(fixture.Rig.BodyPivot.localPosition, Is.EqualTo(body));
            Assert.That(fixture.Rig.FootPivotLeft.localPosition, Is.EqualTo(left));
        }

        [Test]
        public void TwoPresentersKeepIndependentState()
        {
            using var first = new PresenterFixture(profile);
            using var second = new PresenterFixture(profile);
            first.Presenter.Present(Context(7, QusapCombatCommand.BodyAttack));
            Assert.That(first.Presenter.IsPresenting, Is.True);
            Assert.That(second.Presenter.IsPresenting, Is.False);
            Assert.That(second.Rig.BodyPivot.localPosition, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void OfficialPrefabDefinesOneWriterForEachCombatTransform()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(OfficialPrefabPath);
            try
            {
                Assert.That(root.GetComponents<QusapModularCombatVisualPresenter>(), Has.Length.EqualTo(1));
                Assert.That(root.GetComponent<QusapModularCombatVisualPresenter>().enabled, Is.True);
                Assert.That(root.GetComponent<QusapWeaponAttackVisualPresenter>().enabled, Is.False);
                Assert.That(root.GetComponentsInChildren<QusapModularVisualRig>(true), Has.Length.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<Transform>(true)
                    .Count(item => item.name == "CombatFacingCapturePivot"), Is.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void ProceduralPresentationNeverWritesNegativeScale()
        {
            using var fixture = new PresenterFixture(profile);
            fixture.Presenter.Present(Context(1, QusapCombatCommand.BodyAttack));
            foreach (Transform item in fixture.Root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(item.localScale.x, Is.GreaterThan(0f));
                Assert.That(item.localScale.y, Is.GreaterThan(0f));
                Assert.That(item.localScale.z, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void NewRuntimeAndTestsContainNoReflectionApiCalls()
        {
            string[] paths =
            {
                "Assets/_Qusap/Scripts/Animation/QusapModularCombatVisualPresenter.cs",
                "Assets/_Qusap/Scripts/Animation/QusapProceduralCombatVisualProfile.cs",
                "Assets/_Qusap/Scripts/Combat/Weapons/QusapCombatVisualContext.cs"
            };
            foreach (string path in paths)
            {
                string source = File.ReadAllText(path);
                Assert.That(source, Does.Not.Contain("System." + "Reflection"), path);
                Assert.That(source, Does.Not.Contain("Get" + "Field("), path);
                Assert.That(source, Does.Not.Contain("Get" + "Property("), path);
            }
        }

        private static QusapCombatVisualContext Context(
            ulong id,
            QusapCombatCommand command,
            QusapComboId? combo = null,
            int step = -1,
            bool finisher = false,
            int facing = 1)
        {
            return new QusapCombatVisualContext(
                id, command, combo, step, finisher, facing,
                QusapAttackPhase.Active, 0.5f);
        }

        private sealed class PresenterFixture : System.IDisposable
        {
            public PresenterFixture(QusapProceduralCombatVisualProfile visualProfile)
            {
                Root = new GameObject("PresenterFixture");
                var visual = new GameObject("QusapLuzModularVisual");
                visual.transform.SetParent(Root.transform, false);
                Transform visualRoot = Child(visual.transform, "QusapVisualRoot");
                Transform bodyPivot = Child(visualRoot, "BodyPivot");
                Transform body = MeshChild(bodyPivot, "Body");
                Transform leftPivot = Child(visualRoot, "FootPivot_L");
                Transform left = MeshChild(leftPivot, "FloatingFoot_L");
                Transform rightPivot = Child(visualRoot, "FootPivot_R");
                Transform right = MeshChild(rightPivot, "FloatingFoot_R");
                Rig = visual.AddComponent<QusapModularVisualRig>();
                Rig.Configure(
                    visualRoot, bodyPivot, body, leftPivot, left, rightPivot, right);
                Presenter = Root.AddComponent<QusapModularCombatVisualPresenter>();
                Presenter.Configure(null, Rig, null, visualProfile);
            }

            public GameObject Root { get; }
            public QusapModularVisualRig Rig { get; }
            public QusapModularCombatVisualPresenter Presenter { get; }

            public void Dispose()
            {
                Object.DestroyImmediate(Root);
            }

            private static Transform Child(Transform parent, string name)
            {
                var child = new GameObject(name);
                child.transform.SetParent(parent, false);
                return child.transform;
            }

            private static Transform MeshChild(Transform parent, string name)
            {
                Transform child = Child(parent, name);
                child.gameObject.AddComponent<MeshFilter>();
                child.gameObject.AddComponent<MeshRenderer>();
                return child;
            }
        }
    }
}
