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

        [Test]
        public void DamageComboLightsUseLargeOpposedSwordCutsWithFullBodyWeightTransfer()
        {
            QusapProceduralCombatMotion first = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightFirst);
            QusapProceduralCombatMotion second = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightSecond);
            float firstArc = first.Impact.WeaponLocalEulerAngles.z
                - first.Preparation.WeaponLocalEulerAngles.z;
            float secondArc = second.Impact.WeaponLocalEulerAngles.z
                - second.Preparation.WeaponLocalEulerAngles.z;

            Assert.That(Mathf.Abs(firstArc), Is.GreaterThanOrEqualTo(100f));
            Assert.That(Mathf.Abs(secondArc), Is.GreaterThanOrEqualTo(110f));
            Assert.That(Mathf.Sign(firstArc), Is.Not.EqualTo(Mathf.Sign(secondArc)));
            Assert.That(Vector3.Distance(first.Preparation.WeaponLocalPosition,
                first.Impact.WeaponLocalPosition), Is.GreaterThan(0.35f));
            Assert.That(Vector3.Distance(second.Preparation.WeaponLocalPosition,
                second.Impact.WeaponLocalPosition), Is.GreaterThan(0.25f));
            Assert.That(first.Preparation.BodyLocalEulerAngles.z,
                Is.GreaterThan(10f));
            Assert.That(second.Preparation.BodyLocalEulerAngles.z,
                Is.LessThan(-10f));
            Assert.That(first.Impact.LeftFootLocalPosition.x,
                Is.GreaterThan(0.2f));
            Assert.That(second.Impact.RightFootLocalPosition.x,
                Is.GreaterThan(0.2f));
            Assert.That(Mathf.Abs(first.FollowThrough.WeaponLocalEulerAngles.z),
                Is.GreaterThan(Mathf.Abs(first.Impact.WeaponLocalEulerAngles.z)));
            Assert.That(Mathf.Abs(second.FollowThrough.WeaponLocalEulerAngles.z),
                Is.GreaterThan(Mathf.Abs(second.Impact.WeaponLocalEulerAngles.z)));
        }

        [Test]
        public void DamageKickPlantsSupportFootAndMovesAttackFootWithinAuthoredRange()
        {
            QusapProceduralCombatMotion kick = profile.GetMotion(
                QusapProceduralCombatMotionId.DamageBodyAttack);

            Assert.That(kick.Impact.LeftFootLocalPosition.x, Is.InRange(0.55f, 0.60f));
            Assert.That(kick.Impact.RightFootLocalPosition.magnitude, Is.LessThan(0.08f));
            Assert.That(kick.Preparation.BodyLocalPosition.y, Is.LessThan(-0.1f));
            Assert.That(Mathf.Abs(kick.Impact.BodyLocalEulerAngles.z),
                Is.GreaterThan(15f));
            Assert.That(kick.Preparation.WeaponLocalPosition.magnitude,
                Is.GreaterThan(0.1f));
            Assert.That(Mathf.Abs(kick.FollowThrough.WeaponLocalEulerAngles.z),
                Is.GreaterThan(40f));
            Assert.That(kick.ReturnToNeutral.WeaponLocalPosition.y,
                Is.GreaterThan(0.14f));
        }

        [Test]
        public void DamageFinisherTelegraphsThenTraversesTwoHundredTenDegreeArcAtImpact()
        {
            QusapProceduralCombatMotion finisher = profile.GetMotion(
                QusapProceduralCombatMotionId.DamageFinisher);
            QusapProceduralCombatMotion first = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightFirst);
            float arc = Mathf.Abs(finisher.Impact.WeaponLocalEulerAngles.z
                - finisher.Preparation.WeaponLocalEulerAngles.z);

            Assert.That(arc, Is.InRange(190f, 220f));
            Assert.That(finisher.Preparation.WeaponLocalPosition.y,
                Is.GreaterThan(0.25f));
            Assert.That(finisher.Preparation.WeaponLocalPosition.x,
                Is.LessThan(-0.25f));
            Assert.That(Mathf.Abs(finisher.Preparation.LeftFootLocalPosition.x
                - finisher.Preparation.RightFootLocalPosition.x),
                Is.GreaterThan(0.4f));
            Assert.That(finisher.FollowThrough.WeaponLocalPosition.magnitude,
                Is.GreaterThan(finisher.Impact.WeaponLocalPosition.magnitude));
            Assert.That(Mathf.Abs(finisher.FollowThrough.WeaponLocalEulerAngles.z),
                Is.GreaterThan(Mathf.Abs(finisher.Impact.WeaponLocalEulerAngles.z)));
            Assert.That(finisher.Impact.BodyLocalPosition.magnitude,
                Is.GreaterThan(first.Impact.BodyLocalPosition.magnitude));

            QusapProceduralCombatPoseValue activePeak = finisher.Evaluate(
                QusapAttackPhase.Active, 0.68f);
            Assert.That(activePeak.WeaponLocalPosition,
                Is.EqualTo(finisher.Impact.WeaponLocalPosition));
            Assert.That(activePeak.WeaponLocalEulerAngles,
                Is.EqualTo(finisher.Impact.WeaponLocalEulerAngles));
        }

        [Test]
        public void DamageComboRecoverySilhouettesPrepareTheFollowingAuthoredStep()
        {
            QusapProceduralCombatMotion first = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightFirst);
            QusapProceduralCombatMotion second = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightSecond);
            QusapProceduralCombatMotion kick = profile.GetMotion(
                QusapProceduralCombatMotionId.DamageBodyAttack);
            QusapProceduralCombatMotion finisher = profile.GetMotion(
                QusapProceduralCombatMotionId.DamageFinisher);

            Assert.That(Mathf.Abs(first.ReturnToNeutral.WeaponLocalEulerAngles.z
                - second.Preparation.WeaponLocalEulerAngles.z), Is.LessThanOrEqualTo(45f));
            Assert.That(Mathf.Abs(second.ReturnToNeutral.WeaponLocalEulerAngles.z
                - kick.Preparation.WeaponLocalEulerAngles.z), Is.LessThanOrEqualTo(20f));
            Assert.That(Mathf.Abs(kick.ReturnToNeutral.WeaponLocalEulerAngles.z
                - finisher.Preparation.WeaponLocalEulerAngles.z), Is.LessThanOrEqualTo(30f));
        }

        [Test]
        public void DamageArtPassCurvesAreDistinctFiniteAndClamped()
        {
            QusapProceduralCombatMotion first = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightFirst);
            QusapProceduralCombatMotion second = profile.GetMotion(
                QusapProceduralCombatMotionId.WeaponLightSecond);
            QusapProceduralCombatMotion finisher = profile.GetMotion(
                QusapProceduralCombatMotionId.DamageFinisher);

            Assert.That(Mathf.Abs(first.StartupCurve.Evaluate(0.72f)
                - second.StartupCurve.Evaluate(0.72f)), Is.GreaterThan(0.01f));
            Assert.That(finisher.StartupCurve.Evaluate(0.5f),
                Is.LessThan(first.StartupCurve.Evaluate(0.5f)));
            foreach (QusapProceduralCombatMotionId id in new[]
            {
                QusapProceduralCombatMotionId.WeaponLightFirst,
                QusapProceduralCombatMotionId.WeaponLightSecond,
                QusapProceduralCombatMotionId.DamageBodyAttack,
                QusapProceduralCombatMotionId.DamageFinisher
            })
            {
                QusapProceduralCombatMotion motion = profile.GetMotion(id);
                foreach (float progress in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    QusapProceduralCombatPoseValue value = motion.Evaluate(
                        QusapAttackPhase.Active, progress);
                    Assert.That(float.IsFinite(value.BodyLocalPosition.x), Is.True, id.ToString());
                    Assert.That(Mathf.Abs(value.BodyLocalPosition.x),
                        Is.LessThanOrEqualTo(profile.SafeTranslationLimit), id.ToString());
                    Assert.That(Mathf.Abs(value.WeaponLocalEulerAngles.z),
                        Is.LessThanOrEqualTo(profile.SafeRotationLimit), id.ToString());
                }
            }
        }

        [Test]
        public void EveryDamageComboPoseMirrorsFromCapturedFacingWithoutScaleInversion()
        {
            foreach ((QusapCombatCommand command, QusapComboId? combo, int step,
                bool finisher) in new[]
            {
                (QusapCombatCommand.WeaponLight, (QusapComboId?)null, 0, false),
                (QusapCombatCommand.WeaponLight, (QusapComboId?)null, 1, false),
                (QusapCombatCommand.BodyAttack, (QusapComboId?)QusapComboId.Damage, 2, false),
                (QusapCombatCommand.WeaponStrong, (QusapComboId?)QusapComboId.Damage, 3, true)
            })
            {
                var rightContext = new QusapCombatVisualContext(
                    20, command, combo, step, finisher, 1,
                    QusapAttackPhase.Active, 0.68f);
                var leftContext = new QusapCombatVisualContext(
                    21, command, combo, step, finisher, -1,
                    QusapAttackPhase.Active, 0.68f);
                QusapProceduralCombatPoseValue right = profile.Evaluate(rightContext, out _);
                QusapProceduralCombatPoseValue left = profile.Evaluate(leftContext, out _);

                Assert.That(left.BodyLocalPosition.x,
                    Is.EqualTo(-right.BodyLocalPosition.x).Within(0.00001f));
                Assert.That(left.WeaponLocalPosition.x,
                    Is.EqualTo(-right.WeaponLocalPosition.x).Within(0.00001f));
                Assert.That(left.WeaponLocalPosition.y,
                    Is.EqualTo(right.WeaponLocalPosition.y).Within(0.00001f));
                Assert.That(left.WeaponLocalEulerAngles.z,
                    Is.EqualTo(-right.WeaponLocalEulerAngles.z).Within(0.00001f));
            }
        }

        [Test]
        public void AssignedProfileSerializesDamageArtPassInsteadOfDependingOnFallbacks()
        {
            QusapProceduralCombatVisualProfile assigned =
                AssetDatabase.LoadAssetAtPath<QusapProceduralCombatVisualProfile>(
                    DefaultProfilePath);
            var serialized = new SerializedObject(assigned);

            Assert.That(serialized.FindProperty("contentVersion").intValue,
                Is.GreaterThanOrEqualTo(1));
            Assert.That(serialized.FindProperty("weaponLightFirst")
                .FindPropertyRelative("impact")
                .FindPropertyRelative("weaponLocalEulerAngles").vector3Value.z,
                Is.EqualTo(-58f));
            Assert.That(serialized.FindProperty("damageBodyAttack")
                .FindPropertyRelative("impact")
                .FindPropertyRelative("leftFootLocalPosition").vector3Value.x,
                Is.EqualTo(0.58f));
            Assert.That(serialized.FindProperty("damageFinisher")
                .FindPropertyRelative("preparation")
                .FindPropertyRelative("weaponLocalEulerAngles").vector3Value.z,
                Is.EqualTo(105f));
        }

        [Test]
        public void OutOfScopeAttackChoreographiesKeepTheirPreviousAuthoredValues()
        {
            Assert.That(profile.GetMotion(QusapProceduralCombatMotionId.WeaponStrong)
                .Preparation.WeaponLocalEulerAngles.z, Is.EqualTo(78f));
            Assert.That(profile.GetMotion(QusapProceduralCombatMotionId.Headbutt)
                .Impact.BodyLocalPosition.x, Is.EqualTo(0.29f));
            Assert.That(profile.GetMotion(QusapProceduralCombatMotionId.DisarmFinisher)
                .Impact.BodyLocalPosition.x, Is.EqualTo(0.36f));
            Assert.That(profile.GetMotion(QusapProceduralCombatMotionId.LaunchWeaponLight)
                .Impact.WeaponLocalEulerAngles.z, Is.EqualTo(58f));
            Assert.That(profile.GetMotion(QusapProceduralCombatMotionId.LaunchFinisher)
                .Impact.LeftFootLocalPosition.y, Is.EqualTo(0.36f));
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
