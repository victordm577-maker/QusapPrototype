using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Qusap.Tests
{
    public sealed class QusapCombatFeedbackPlayModeTests
    {
        private readonly List<PlayerHarness> players = new();

        [TearDown]
        public void TearDown()
        {
            for (int i = players.Count - 1; i >= 0; i--)
            {
                players[i].Dispose();
            }

            players.Clear();
        }

        [Test]
        public void PresenterIsCreatedAutomatically()
        {
            PlayerHarness player = CreatePlayer("Player");
            Assert.That(player.Presenter, Is.Not.Null);
            Assert.That(player.Root.GetComponents<QusapCombatFeedbackPresenter>(), Has.Length.EqualTo(1));
            Assert.That(player.Presenter.PoolCapacity, Is.EqualTo(8));
        }

        [Test]
        public void PresenterIsHiddenWhileIdle()
        {
            PlayerHarness player = CreatePlayer("Player");
            Assert.That(player.Presenter.ActiveEffectCount, Is.Zero);
            AssertAllPoolObjectsHidden(player.Presenter);
        }

        [Test]
        public void DamageResolutionShowsRedFeedback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapFinisherResolution resolution = attacker.Resolve(QusapComboId.Damage, target);

            SpriteRenderer renderer = ActivePrimary(attacker.Presenter);
            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Applied));
            AssertColor(renderer.color, attacker.Combat.CombatFeedbackSettings.DamageColor);
            Assert.That(ActiveSecondary(attacker.Presenter).enabled, Is.True);
        }

        [Test]
        public void DamageFeedbackAppearsOnTarget()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Resolve(QusapComboId.Damage, target);

            Vector3 expected = target.Root.transform.position
                + attacker.Combat.CombatFeedbackSettings.TargetOffset;
            Assert.That(attacker.Presenter.LastFeedback.Value.WorldPosition, Is.EqualTo(expected));
            Assert.That(ActiveObject(attacker.Presenter).transform.position, Is.EqualTo(expected));
        }

        [Test]
        public void DamageFeedbackDoesNotApplyAdditionalDamage()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Resolve(QusapComboId.Damage, target);
            float afterResolution = target.Receiver.TotalDamageReceived;

            attacker.Presenter.Refresh(Time.unscaledTimeAsDouble + 0.1d);

            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(afterResolution));
        }

        [Test]
        public void LaunchResolutionShowsVerticalCyanFeedback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Resolve(QusapComboId.Launch, target);

            SpriteRenderer renderer = ActivePrimary(attacker.Presenter);
            AssertColor(renderer.color, attacker.Combat.CombatFeedbackSettings.LaunchColor);
            Assert.That(renderer.transform.localScale.y, Is.GreaterThan(renderer.transform.localScale.x));
        }

        [Test]
        public void LaunchFeedbackDoesNotModifyVelocity()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Resolve(QusapComboId.Launch, target);
            Vector3 velocity = target.Body.linearVelocity;

            attacker.Presenter.Refresh(Time.unscaledTimeAsDouble + 0.1d);

            Assert.That(target.Body.linearVelocity, Is.EqualTo(velocity));
        }

        [Test]
        public void LaunchFeedbackDoesNotModifyTransform()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Resolve(QusapComboId.Launch, target);
            Vector3 position = target.Root.transform.position;

            attacker.Presenter.Refresh(Time.unscaledTimeAsDouble + 0.1d);

            Assert.That(target.Root.transform.position, Is.EqualTo(position));
        }

        [Test]
        public void DisarmUnavailableShowsVioletFeedback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapFinisherResolution resolution = attacker.Resolve(QusapComboId.Disarm, target);

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.DisarmUnavailable));
            AssertColor(
                ActivePrimary(attacker.Presenter).color,
                attacker.Combat.CombatFeedbackSettings.DisarmColor);
        }

        [Test]
        public void DisarmRejectedDoesNotShowSuccessColor()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            target.AddDisarmable(accepts: false);
            QusapFinisherResolution resolution = attacker.Resolve(QusapComboId.Disarm, target);

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.DisarmRejected));
            Assert.That(ActiveSecondary(attacker.Presenter).enabled, Is.False);
            Assert.That(
                ActivePrimary(attacker.Presenter).color,
                Is.Not.EqualTo(attacker.Combat.CombatFeedbackSettings.DisarmSuccessCenterColor));
        }

        [Test]
        public void DisarmSucceededShowsGoldCenter()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            target.AddDisarmable(accepts: true);
            QusapFinisherResolution resolution = attacker.Resolve(QusapComboId.Disarm, target);

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.DisarmSucceeded));
            SpriteRenderer center = ActiveSecondary(attacker.Presenter);
            Assert.That(center.enabled, Is.True);
            AssertColor(center.color, attacker.Combat.CombatFeedbackSettings.DisarmSuccessCenterColor);
        }

        [Test]
        public void WhiffShowsGreyFeedbackOnAttacker()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            QusapFinisherResolution resolution = attacker.Resolve(
                QusapComboId.Damage, target, horizontalOffset: 10f);

            Vector3 expected = attacker.Root.transform.position
                + attacker.Combat.CombatFeedbackSettings.AttackerOffset;
            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Whiffed));
            Assert.That(attacker.Presenter.LastFeedback.Value.WorldPosition, Is.EqualTo(expected));
            AssertColor(
                ActivePrimary(attacker.Presenter).color,
                attacker.Combat.CombatFeedbackSettings.WhiffColor);
        }

        [Test]
        public void WhiffDoesNotDisplayOnDistantTarget()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Resolve(QusapComboId.Launch, target, horizontalOffset: 10f);

            Vector3 feedbackPosition = attacker.Presenter.LastFeedback.Value.WorldPosition;
            Vector3 targetFeedbackPosition = target.Root.transform.position
                + attacker.Combat.CombatFeedbackSettings.TargetOffset;
            Assert.That(feedbackPosition, Is.Not.EqualTo(targetFeedbackPosition));
        }

        [Test]
        public void RejectedShowsRejectedStyle()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            target.Receiver.AcceptsHits = false;
            QusapFinisherResolution resolution = attacker.Resolve(QusapComboId.Damage, target);

            Assert.That(resolution.Outcome, Is.EqualTo(QusapFinisherResolutionOutcome.Rejected));
            Assert.That(
                attacker.Presenter.LastFeedback.Value.FeedbackType,
                Is.EqualTo(QusapCombatFeedbackType.Rejected));
            AssertColor(
                ActivePrimary(attacker.Presenter).color,
                attacker.Combat.CombatFeedbackSettings.RejectedColor);
        }

        [Test]
        public void TooEarlyParryShowsRedFailureMark()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);

            AssertFailureMark(pair.Defender);
            Assert.That(
                pair.Defender.Presenter.LastFeedback.Value.ParryOutcome,
                Is.EqualTo(QusapParryAttemptOutcome.TooEarly));
        }

        [Test]
        public void TooLateParryShowsRedFailureMark()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);

            AssertFailureMark(pair.Defender);
            Assert.That(
                pair.Defender.Presenter.LastFeedback.Value.ParryOutcome,
                Is.EqualTo(QusapParryAttemptOutcome.TooLate));
        }

        [Test]
        public void NoIncomingParryShowsNoFailureMark()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            defender.Parry(InputState.currentTime);

            Assert.That(defender.Presenter.FailureMarkCount, Is.Zero);
            Assert.That(defender.Presenter.ActiveEffectCount, Is.Zero);
        }

        [Test]
        public void AlreadyAttemptedDoesNotDuplicateFailureMark()
        {
            Pair pair = CreateArmedPair(windowDuration: 1d);
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            pair.Defender.ProcessFixed();
            pair.Defender.Hitstun.ResetHitstun();
            int firstCount = pair.Defender.Presenter.FailureMarkCount;

            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt + 0.6d);

            Assert.That(pair.Defender.Combat.LastParryAttemptOutcome, Is.EqualTo(QusapParryAttemptOutcome.AlreadyAttempted));
            Assert.That(pair.Defender.Presenter.FailureMarkCount, Is.EqualTo(firstCount));
            Assert.That(pair.Defender.Presenter.TotalFeedbackCount, Is.EqualTo(1));
        }

        [Test]
        public void SuccessfulParryDoesNotDuplicateGreenFlash()
        {
            Pair pair = CreateArmedPair();
            int feedbackBefore = pair.Defender.Presenter.TotalFeedbackCount;
            int greenBefore = pair.Defender.CuePresenter.SuccessFlashCount;
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();

            Assert.That(pair.Defender.Presenter.TotalFeedbackCount, Is.EqualTo(feedbackBefore));
            Assert.That(pair.Defender.CuePresenter.SuccessFlashCount, Is.EqualTo(greenBefore + 1));
        }

        [Test]
        public void FeedbackDisappearsAfterConfiguredDuration()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);
            double startedAt = attacker.Presenter.LastFeedback.Value.Timestamp;

            attacker.Presenter.Refresh(
                startedAt + attacker.Combat.CombatFeedbackSettings.DamageDuration + 0.001d);

            Assert.That(attacker.Presenter.ActiveEffectCount, Is.Zero);
            AssertAllPoolObjectsHidden(attacker.Presenter);
        }

        [Test]
        public void RepeatedFeedbackReusesPooledRenderers()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            ulong[] rendererIds = CaptureRendererIds(attacker.Presenter);
            for (int i = 0; i < 20; i++)
            {
                attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);
            }

            Assert.That(CaptureRendererIds(attacker.Presenter), Is.EqualTo(rendererIds));
            Assert.That(attacker.Presenter.PoolCapacity, Is.EqualTo(8));
        }

        [Test]
        public void PoolNeverExceedsConfiguredCapacity()
        {
            PlayerHarness attacker = CreatePlayer("Attacker", poolCapacity: 3);
            PlayerHarness target = CreatePlayer("Target");
            for (int i = 0; i < 10; i++)
            {
                attacker.PublishResolution(QusapComboId.Launch, target, QusapFinisherResolutionOutcome.Applied);
            }

            Assert.That(attacker.Presenter.PoolCapacity, Is.EqualTo(3));
            Assert.That(CountFeedbackObjects(attacker), Is.EqualTo(3));
            Assert.That(attacker.Presenter.ActiveEffectCount, Is.EqualTo(3));
        }

        [Test]
        public void FullPoolReusesOldestInstance()
        {
            PlayerHarness attacker = CreatePlayer("Attacker", poolCapacity: 2);
            PlayerHarness target = CreatePlayer("Target");
            attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);
            Assert.That(attacker.Presenter.LastActivatedSlotIndex, Is.EqualTo(0));
            attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);
            Assert.That(attacker.Presenter.LastActivatedSlotIndex, Is.EqualTo(1));

            attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);

            Assert.That(attacker.Presenter.LastActivatedSlotIndex, Is.EqualTo(0));
            Assert.That(attacker.Presenter.PoolCapacity, Is.EqualTo(2));
        }

        [Test]
        public void FeedbackObjectsCreateNoColliders()
        {
            PlayerHarness player = CreatePlayer("Player");
            for (int i = 0; i < player.Presenter.PoolCapacity; i++)
            {
                Assert.That(
                    player.Presenter.GetFeedbackObject(i).GetComponentsInChildren<Collider>(true),
                    Is.Empty);
            }
        }

        [Test]
        public void FeedbackObjectsCreateNoRigidbodies()
        {
            PlayerHarness player = CreatePlayer("Player");
            for (int i = 0; i < player.Presenter.PoolCapacity; i++)
            {
                Assert.That(
                    player.Presenter.GetFeedbackObject(i).GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
            }
        }

        [Test]
        public void FeedbackDoesNotChangeParryTiming()
        {
            Pair pair = CreateArmedPair();
            double opensAt = pair.Attacker.Combat.ParryWindowOpensAt;
            double closesAt = pair.Attacker.Combat.ParryWindowClosesAt;
            pair.Attacker.PublishResolution(
                QusapComboId.Damage, pair.Defender, QusapFinisherResolutionOutcome.Applied);

            Assert.That(pair.Attacker.Combat.ParryWindowOpensAt, Is.EqualTo(opensAt));
            Assert.That(pair.Attacker.Combat.ParryWindowClosesAt, Is.EqualTo(closesAt));
        }

        [Test]
        public void FeedbackDoesNotConsumeCombatCommands()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.Input.EnqueueCombatCommand(QusapCombatCommand.BodyAttack, 50d);
            int pending = attacker.Input.PendingCombatCommandCount;

            attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);

            Assert.That(attacker.Input.PendingCombatCommandCount, Is.EqualTo(pending));
        }

        [Test]
        public void FeedbackIsHiddenOnDisable()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);

            attacker.Combat.enabled = false;

            Assert.That(attacker.Presenter.ActiveEffectCount, Is.Zero);
            AssertAllPoolObjectsHidden(attacker.Presenter);
        }

        [Test]
        public void FeedbackIsHiddenAfterRespawn()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.PublishResolution(QusapComboId.Launch, target, QusapFinisherResolutionOutcome.Applied);

            attacker.Combat.ResetCombatState();

            Assert.That(attacker.Presenter.ActiveEffectCount, Is.Zero);
            AssertAllPoolObjectsHidden(attacker.Presenter);
        }

        [Test]
        public void MultiplePlayersKeepIndependentPools()
        {
            PlayerHarness first = CreatePlayer("First");
            PlayerHarness second = CreatePlayer("Second");
            PlayerHarness target = CreatePlayer("Target");
            first.PublishResolution(QusapComboId.Damage, target, QusapFinisherResolutionOutcome.Applied);

            Assert.That(first.Presenter.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(second.Presenter.ActiveEffectCount, Is.Zero);
            Assert.That(first.Presenter.GetPrimaryRenderer(0), Is.Not.SameAs(second.Presenter.GetPrimaryRenderer(0)));
        }

        [Test]
        public void ExistingParryCueTestsStillPass()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.CuePresenter.Refresh(pair.WindowMidpoint, Time.unscaledTimeAsDouble);

            Assert.That(pair.Defender.CuePresenter.IsShowingWindow, Is.True);
            Assert.That(pair.Defender.CuePresenter.CueRenderer.enabled, Is.True);
            Assert.That(pair.Defender.Presenter.ActiveEffectCount, Is.Zero);
        }

        [Test]
        public void ExistingParryAndFinisherTestsStillPass()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();

            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        private Pair CreateArmedPair(double windowDuration = 0.35d)
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness defender = CreatePlayer("Defender");
            attacker.ConfigureParrySettings(0.1d, windowDuration);
            attacker.ArmFinisher(QusapComboId.Launch, defender);
            return new Pair(attacker, defender);
        }

        private PlayerHarness CreatePlayer(
            string name,
            int poolCapacity = QusapCombatFeedbackSettings.DefaultPoolCapacity)
        {
            PlayerHarness player = new(name, poolCapacity);
            players.Add(player);
            return player;
        }

        private static GameObject ActiveObject(QusapCombatFeedbackPresenter presenter)
        {
            return presenter.GetFeedbackObject(presenter.LastActivatedSlotIndex);
        }

        private static SpriteRenderer ActivePrimary(QusapCombatFeedbackPresenter presenter)
        {
            return presenter.GetPrimaryRenderer(presenter.LastActivatedSlotIndex);
        }

        private static SpriteRenderer ActiveSecondary(QusapCombatFeedbackPresenter presenter)
        {
            return presenter.GetSecondaryRenderer(presenter.LastActivatedSlotIndex);
        }

        private static void AssertFailureMark(PlayerHarness defender)
        {
            Assert.That(defender.Presenter.FailureMarkCount, Is.EqualTo(1));
            Assert.That(defender.Presenter.ActiveEffectCount, Is.EqualTo(1));
            Assert.That(ActiveSecondary(defender.Presenter).enabled, Is.True);
            AssertColor(
                ActivePrimary(defender.Presenter).color,
                defender.Combat.CombatFeedbackSettings.FailedParryColor);
        }

        private static void AssertAllPoolObjectsHidden(QusapCombatFeedbackPresenter presenter)
        {
            for (int i = 0; i < presenter.PoolCapacity; i++)
            {
                Assert.That(presenter.GetFeedbackObject(i).activeSelf, Is.False);
            }
        }

        private static int CountFeedbackObjects(PlayerHarness player)
        {
            int count = 0;
            Transform[] transforms = player.Root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                if (transforms[i].gameObject.name.StartsWith(
                    QusapCombatFeedbackPresenter.VisualObjectPrefix,
                    StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        private static ulong[] CaptureRendererIds(QusapCombatFeedbackPresenter presenter)
        {
            ulong[] ids = new ulong[presenter.PoolCapacity * 2];
            for (int i = 0; i < presenter.PoolCapacity; i++)
            {
                ids[i * 2] = EntityId.ToULong(presenter.GetPrimaryRenderer(i).GetEntityId());
                ids[i * 2 + 1] = EntityId.ToULong(presenter.GetSecondaryRenderer(i).GetEntityId());
            }

            return ids;
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private readonly struct Pair
        {
            public Pair(PlayerHarness attacker, PlayerHarness defender)
            {
                Attacker = attacker;
                Defender = defender;
            }

            public PlayerHarness Attacker { get; }
            public PlayerHarness Defender { get; }
            public double WindowMidpoint => Attacker.Combat.ParryWindowOpensAt
                + (Attacker.Combat.ParryWindowClosesAt - Attacker.Combat.ParryWindowOpensAt) / 2d;
        }

        private sealed class PlayerHarness : IDisposable
        {
            private static int nextId;
            private readonly InputActionAsset inputAsset;

            public PlayerHarness(string name, int poolCapacity)
            {
                Root = new GameObject($"{name}_{++nextId}");
                Root.SetActive(false);
                Root.transform.position = new Vector3(nextId * 20f, 0f, 0f);
                Body = Root.AddComponent<Rigidbody>();
                Body.useGravity = false;
                Root.AddComponent<CapsuleCollider>();
                inputAsset = ScriptableObject.CreateInstance<InputActionAsset>();
                InputActionMap map = new("Gameplay");
                inputAsset.AddActionMap(map);
                foreach (string action in new[]
                {
                    "Move", "Jump", "Drop", "Dash", "WeakKick", "StrongKick",
                    "Headbutt", "WeaponStrong", "Parry"
                })
                {
                    map.AddAction(
                        action,
                        action == "Move" ? InputActionType.Value : InputActionType.Button);
                }

                Input = Root.AddComponent<QusapInputReader>();
                SetField(Input, "inputActionAsset", inputAsset);
                Ground = Root.AddComponent<QusapGroundSensor>();
                Root.AddComponent<QusapWallSensor>();
                Root.AddComponent<QusapHorizontalMotor>();
                Root.AddComponent<QusapVerticalMotor>();
                Root.AddComponent<QusapDashMotor>();
                Receiver = Root.AddComponent<QusapHitReceiver>();
                Hitstun = Root.AddComponent<QusapHitstunController>();
                Root.AddComponent<QusapRespawnController>();
                Root.AddComponent<QusapHurtbox>();
                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(Root.transform, false);
                Hitbox = hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = Root.AddComponent<QusapCombatController>();
                SetField(Combat, "attackHitbox", Hitbox);
                SetField(Combat, "logRecognizedCombos", false);
                SetField(
                    Combat,
                    "combatFeedbackSettings",
                    new QusapCombatFeedbackSettings(
                        poolCapacity,
                        QusapCombatFeedbackSettings.DefaultDamageDuration,
                        QusapCombatFeedbackSettings.DefaultDamageMaximumSize,
                        QusapCombatFeedbackSettings.DefaultTargetOffset));
                Root.SetActive(true);
                SetProperty(Ground, "IsGrounded", true);
                Presenter = Combat.CombatFeedbackPresenter;
                CuePresenter = Combat.ParryCuePresenter;
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            public QusapCombatFeedbackPresenter Presenter { get; }
            public QusapParryCuePresenter CuePresenter { get; }

            public FeedbackTestDisarmable AddDisarmable(bool accepts)
            {
                FeedbackTestDisarmable disarmable = Root.AddComponent<FeedbackTestDisarmable>();
                disarmable.Accepts = accepts;
                return disarmable;
            }

            public void ConfigureParrySettings(double telegraph, double window)
            {
                SetField(
                    Combat,
                    "finisherParrySettings",
                    new QusapFinisherParrySettings(telegraph, window, 0.6d, 0.3d));
            }

            public void ArmFinisher(QusapComboId comboId, PlayerHarness target)
            {
                PrepareTarget(target, 0.9f);
                Invoke(Combat, "ArmFinisher", comboId, target.Receiver);
                Assert.That(Combat.HasArmedFinisher, Is.True);
            }

            public QusapFinisherResolution Resolve(
                QusapComboId comboId,
                PlayerHarness target,
                float horizontalOffset = 0.9f)
            {
                ArmFinisher(comboId, target);
                PrepareTarget(target, horizontalOffset);
                Combat.UpdateFinisherDefense(Combat.ParryWindowClosesAt + 0.001d);
                ProcessFixed();
                Assert.That(Combat.LastFinisherResolution.HasValue, Is.True);
                return Combat.LastFinisherResolution.Value;
            }

            public void PublishResolution(
                QusapComboId comboId,
                PlayerHarness target,
                QusapFinisherResolutionOutcome outcome)
            {
                bool applied = outcome == QusapFinisherResolutionOutcome.Applied
                    || outcome == QusapFinisherResolutionOutcome.DisarmSucceeded
                    || outcome == QusapFinisherResolutionOutcome.DisarmUnavailable
                    || outcome == QusapFinisherResolutionOutcome.DisarmRejected;
                Invoke(
                    Combat,
                    "PublishFinisherResolution",
                    new QusapFinisherResolution(
                        comboId,
                        target?.Receiver,
                        outcome,
                        applied,
                        outcome == QusapFinisherResolutionOutcome.DisarmSucceeded));
            }

            public void Parry(double timestamp)
            {
                Input.EnqueueCombatCommand(QusapCombatCommand.Parry, timestamp);
            }

            public void ProcessFixed()
            {
                Invoke(Combat, "FixedUpdate");
            }

            private void PrepareTarget(PlayerHarness target, float horizontalOffset)
            {
                target.Root.transform.position = Root.transform.position
                    + new Vector3(horizontalOffset, 0f, 0f);
                target.Body.linearVelocity = Vector3.zero;
                target.Hitstun.ResetHitstun();
                Physics.SyncTransforms();
            }

            public void Dispose()
            {
                if (Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }

                if (inputAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(inputAsset);
                }
            }

            private static void SetField(object target, string name, object value)
            {
                FieldInfo field = target.GetType().GetField(
                    name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"Missing field {name}");
                field.SetValue(target, value);
            }

            private static void SetProperty(object target, string name, object value)
            {
                PropertyInfo property = target.GetType().GetProperty(
                    name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null, $"Missing property {name}");
                property.SetValue(target, value);
            }

            private static void Invoke(object target, string name, params object[] arguments)
            {
                MethodInfo method = target.GetType().GetMethod(
                    name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null, $"Missing method {name}");
                method.Invoke(target, arguments);
            }
        }
    }

    public sealed class FeedbackTestDisarmable : MonoBehaviour, IQusapDisarmable
    {
        public bool Accepts { get; set; }
        public bool CanBeDisarmed => true;

        public bool TryDisarm(QusapCombatController source)
        {
            return Accepts;
        }
    }
}
