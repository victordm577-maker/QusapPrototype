using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapComboRecognitionPlayModeTests
    {
        private readonly List<Harness> harnesses = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Harness harness in harnesses)
            {
                harness.Dispose();
            }

            harnesses.Clear();
        }

        [TestCase(QusapComboId.Damage)]
        [TestCase(QusapComboId.Disarm)]
        [TestCase(QusapComboId.Launch)]
        public void RawButtonsWithoutHitsDoNotArmFinisher(QusapComboId comboId)
        {
            Harness harness = CreateHarness();
            harness.Enqueue(Events(comboId));
            harness.ProcessCombatCommands();
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.HasArmedFinisher, Is.False);
            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void RawDamageButtonsWithoutHitsDoNotArmFinisher()
        {
            RawButtonsWithoutHitsDoNotArmFinisher(QusapComboId.Damage);
        }

        [Test]
        public void RawDisarmButtonsWithoutHitsDoNotArmFinisher()
        {
            RawButtonsWithoutHitsDoNotArmFinisher(QusapComboId.Disarm);
        }

        [Test]
        public void RawLaunchButtonsWithoutHitsDoNotArmFinisher()
        {
            RawButtonsWithoutHitsDoNotArmFinisher(QusapComboId.Launch);
        }

        [Test]
        public void DamageArmsAfterSetupHitsSameTarget()
        {
            AssertArmsAfterSetupHits(QusapComboId.Damage);
        }

        [Test]
        public void DisarmArmsAfterSetupHitsSameTarget()
        {
            AssertArmsAfterSetupHits(QusapComboId.Disarm);
        }

        [Test]
        public void LaunchArmsAfterSetupHitsSameTarget()
        {
            AssertArmsAfterSetupHits(QusapComboId.Launch);
        }

        [Test]
        public void DifferentTargetCannotConfirmNextStep()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.Enqueue(sequence[1]);
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetB);
            harness.FinishAttack();
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.HasArmedFinisher, Is.False);
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.Zero);
        }

        [Test]
        public void WrongTargetHitDoesNotReplaceLockedTarget()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.Enqueue(sequence[1]);
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetB);

            Assert.That(harness.PendingComboStep, Is.True);
            Assert.That(harness.ComboTarget, Is.SameAs(harness.TargetA));
        }

        [Test]
        public void CorrectTargetCanConfirmAfterWrongTargetInSameActiveWindow()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.Enqueue(sequence[1]);
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetB);
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.FinishAttack();
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();

            AssertArmed(harness, QusapComboId.Disarm, harness.TargetA);
        }

        [Test]
        public void SharedPrefixContinuationKeepsLockedTarget()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Damage);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.ConfirmStep(sequence[1], harness.TargetA);
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();
            harness.EnterActive();

            harness.AcceptAndNotifyHit(harness.TargetB);

            Assert.That(harness.PendingComboStep, Is.True);
            Assert.That(harness.ComboTarget, Is.SameAs(harness.TargetA));
        }

        [Test]
        public void WhiffResetsComboProgress()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.Enqueue(sequence[1]);
            harness.ProcessCombatCommands();
            harness.FinishAttack();

            Assert.That(harness.PendingComboStep, Is.False);
            Assert.That(harness.ComboTarget, Is.Null);
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.Zero);
            Assert.That(harness.Input.PendingCombatCommandCount, Is.Zero);
        }

        [Test]
        public void SingleAttackHitCannotAdvanceTwice()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.Enqueue(sequence[0]);
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.Combat.NotifyAttackHit(harness.TargetA);
            harness.FinishAttack();
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void MultiTargetHitCannotAdvanceMultipleSteps()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.Enqueue(sequence[0]);
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.AcceptAndNotifyHit(harness.TargetB);
            harness.FinishAttack();
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.HasArmedFinisher, Is.False);
        }

        [TestCase(QusapComboId.Disarm)]
        [TestCase(QusapComboId.Launch)]
        public void FinalInputDoesNotStartLegacyAttack(QusapComboId comboId)
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(comboId);
            harness.ConfirmSetup(sequence, harness.TargetA);
            int startsBeforeFinal = harness.AttackStartCount;
            harness.Enqueue(sequence[sequence.Length - 1]);
            harness.ProcessCombatCommands();

            Assert.That(harness.AttackStartCount, Is.EqualTo(startsBeforeFinal));
            Assert.That(harness.Combat.IsAttacking, Is.False);
        }

        [Test]
        public void FinalInputDoesNotActivateHitbox()
        {
            Harness harness = CreateHarness();
            harness.Arm(QusapComboId.Damage, harness.TargetA);
            Assert.That(harness.Hitbox.IsActive, Is.False);
        }

        [Test]
        public void FinalInputDoesNotAddDamage()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Damage);
            harness.ConfirmSetup(sequence, harness.TargetA);
            float damageBefore = harness.TargetA.TotalDamageReceived;
            harness.Enqueue(sequence[sequence.Length - 1]);
            harness.ProcessCombatCommands();

            Assert.That(harness.TargetA.TotalDamageReceived, Is.EqualTo(damageBefore));
        }

        [Test]
        public void FinalInputDoesNotApplyKnockback()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Damage);
            harness.ConfirmSetup(sequence, harness.TargetA);
            Vector3 velocity = new(2f, 3f, 0f);
            harness.TargetABody.linearVelocity = velocity;
            harness.Enqueue(sequence[sequence.Length - 1]);
            harness.ProcessCombatCommands();

            Assert.That(harness.TargetABody.linearVelocity, Is.EqualTo(velocity));
        }

        [Test]
        public void FinisherArmedEventFiresExactlyOnce()
        {
            Harness harness = CreateHarness();
            int armedCount = 0;
            harness.Combat.FinisherArmed += (_, _) => armedCount++;
            harness.Arm(QusapComboId.Disarm, harness.TargetA);
            harness.ProcessCombatCommands();
            Assert.That(armedCount, Is.EqualTo(1));
        }

        [Test]
        public void ComboCompletedCompatibilityEventFiresExactlyOnce()
        {
            Harness harness = CreateHarness();
            int completionCount = 0;
            harness.Combat.ComboCompleted += _ => completionCount++;
            harness.Arm(QusapComboId.Disarm, harness.TargetA);
            harness.ProcessCombatCommands();
            Assert.That(completionCount, Is.EqualTo(1));
        }

        [Test]
        public void SecondArmRequiresFullNewConfirmedSequence()
        {
            Harness harness = CreateHarness();
            int armedCount = 0;
            harness.Combat.FinisherArmed += (_, _) => armedCount++;
            harness.Arm(QusapComboId.Damage, harness.TargetA, 10d);
            harness.Enqueue(QusapCombatCommand.WeaponStrong, 10.4d);
            harness.ProcessCombatCommands();
            Assert.That(armedCount, Is.EqualTo(1));

            harness.Combat.ResetComboRecognition();
            harness.Arm(QusapComboId.Damage, harness.TargetA, 20d);
            Assert.That(armedCount, Is.EqualTo(2));
        }

        [Test]
        public void QueuedInputPreservesOriginalTimestamp()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.Enqueue(sequence[0]);
            harness.ProcessCombatCommands();
            harness.Enqueue(sequence[1]);
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.FinishAttack();
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.FinishAttack();
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();

            AssertArmed(harness, QusapComboId.Disarm, harness.TargetA);
        }

        [Test]
        public void ExpiredQueuedInputCannotArmFinisher()
        {
            Harness harness = CreateHarness();
            QusapComboDefinition definition = Definition(QusapComboId.Disarm);
            double firstTime = 10d;
            double expiredTime = firstTime + definition.GetStep(1).MaximumDelay + 0.01d;
            harness.Enqueue(definition.GetStep(0).Command, firstTime);
            harness.ProcessCombatCommands();
            harness.Enqueue(definition.GetStep(1).Command, expiredTime);
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.FinishAttack();
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.FinishAttack();
            harness.Enqueue(definition.GetStep(2).Command, expiredTime + 0.1d);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void ParryDoesNotCancelConfirmedPrefix()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.EnqueueWithoutInputEvent(
                QusapCombatCommand.Parry,
                Midpoint(sequence[0].Timestamp, sequence[1].Timestamp));
            harness.Enqueue(sequence[1]);
            harness.ProcessCombatCommands();
            harness.EnterActive();
            harness.AcceptAndNotifyHit(harness.TargetA);
            harness.FinishAttack();
            harness.Enqueue(sequence[2]);
            harness.ProcessCombatCommands();

            AssertArmed(harness, QusapComboId.Disarm, harness.TargetA);
        }

        [Test]
        public void DashClearsPendingStepAndTarget()
        {
            Harness harness = PendingSecondStepHarness();
            harness.SetDashing(true);
            harness.ProcessCombatCommands();
            AssertPendingAndTargetCleared(harness);
        }

        [Test]
        public void HitstunClearsPendingStepAndTarget()
        {
            Harness harness = PendingSecondStepHarness();
            harness.Hitstun.EnterHitstun(1f);
            AssertPendingAndTargetCleared(harness);
        }

        [Test]
        public void ResetCombatStateClearsPendingStepTargetAndArmedFinisher()
        {
            Harness harness = CreateHarness();
            harness.Arm(QusapComboId.Disarm, harness.TargetA);
            harness.Combat.ResetCombatState();
            AssertRecognitionCleared(harness);
        }

        [Test]
        public void CombatDisabledClearsPendingStepTargetAndArmedFinisher()
        {
            Harness harness = CreateHarness();
            harness.Arm(QusapComboId.Disarm, harness.TargetA);
            harness.Combat.CombatAllowed = false;
            AssertRecognitionCleared(harness);
        }

        [Test]
        public void CancelAttackClearsPendingComboStep()
        {
            Harness harness = PendingSecondStepHarness();
            harness.Combat.CancelAttack();
            AssertPendingAndTargetCleared(harness);
        }

        [Test]
        public void ThirdPartyInterruptionPreventsFinisher()
        {
            Harness attacker = CreateHarness();
            Harness thirdParty = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            attacker.ConfirmStep(sequence[0], attacker.TargetA);
            QusapHitInfo interruption = new(
                thirdParty.Combat, QusapAttackType.WeakKick, QusapAttackVariant.WeakKickGround,
                0f, 1, 0f, 0f, 0f, Vector3.zero);

            Assert.That(attacker.Receiver.TryReceiveHit(interruption), Is.True);
            attacker.Enqueue(sequence[2]);
            attacker.ProcessCombatCommands();
            Assert.That(attacker.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void StandaloneHeadbuttStillStartsOutsideDisarmFinisher()
        {
            Harness harness = CreateHarness();
            harness.Enqueue(QusapCombatCommand.Headbutt, 1d);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.IsAttacking, Is.True);
            Assert.That(harness.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.Headbutt));
            Assert.That(harness.PendingComboStep, Is.False);
        }

        [Test]
        public void LegacyAttacksStillWorkWhenComboRecognitionDisabled()
        {
            Harness harness = CreateHarness();
            harness.SetComboRecognitionEnabled(false);
            harness.PressLegacyWeakKick();
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.IsAttacking, Is.True);
            Assert.That(harness.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.WeakKick));
        }

        [Test]
        public void DirectTryStartAttackStillWorks()
        {
            Harness harness = CreateHarness();
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            Assert.That(harness.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.WeakKick));
        }

        [Test]
        public void StartingComboAttackDoesNotAdvanceAttackPhaseInSameFixedUpdate()
        {
            Harness harness = CreateHarness();
            float startup = harness.Combat.GetAttackData(QusapAttackType.WeakKick).StartupTime;
            harness.Enqueue(QusapCombatCommand.BodyAttack, 1d);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Startup));
            Assert.That(harness.PhaseTimeRemaining, Is.EqualTo(startup).Within(0.0001f));
        }

        private Harness CreateHarness()
        {
            Harness harness = new();
            harnesses.Add(harness);
            return harness;
        }

        private void AssertArmsAfterSetupHits(QusapComboId comboId)
        {
            Harness harness = CreateHarness();
            harness.Arm(comboId, harness.TargetA);
            AssertArmed(harness, comboId, harness.TargetA);
        }

        private Harness PendingSecondStepHarness()
        {
            Harness harness = CreateHarness();
            TimedCommand[] sequence = Events(QusapComboId.Disarm);
            harness.ConfirmStep(sequence[0], harness.TargetA);
            harness.Enqueue(sequence[1]);
            harness.ProcessCombatCommands();
            Assert.That(harness.PendingComboStep, Is.True);
            return harness;
        }

        private static void AssertArmed(Harness harness, QusapComboId comboId, QusapHitReceiver target)
        {
            Assert.That(harness.Combat.HasArmedFinisher, Is.True);
            Assert.That(harness.Combat.ArmedFinisherCombo, Is.EqualTo(comboId));
            Assert.That(harness.Combat.ArmedFinisherTarget, Is.SameAs(target));
            Assert.That(harness.Combat.LastCompletedCombo, Is.EqualTo(comboId));
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.Zero);
            Assert.That(harness.Combat.IsAttacking, Is.False);
        }

        private static void AssertPendingAndTargetCleared(Harness harness)
        {
            Assert.That(harness.PendingComboStep, Is.False);
            Assert.That(harness.ComboTarget, Is.Null);
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.Zero);
        }

        private static void AssertRecognitionCleared(Harness harness)
        {
            AssertPendingAndTargetCleared(harness);
            Assert.That(harness.Combat.HasArmedFinisher, Is.False);
            Assert.That(harness.Combat.ArmedFinisherCombo, Is.Null);
            Assert.That(harness.Combat.ArmedFinisherTarget, Is.Null);
            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
            Assert.That(harness.Input.PendingCombatCommandCount, Is.Zero);
        }

        private static TimedCommand[] Events(QusapComboId comboId, double startTimestamp = 10d)
        {
            QusapComboDefinition definition = Definition(comboId);
            TimedCommand[] events = new TimedCommand[definition.StepCount];
            double timestamp = startTimestamp;
            for (int i = 0; i < definition.StepCount; i++)
            {
                if (i > 0)
                {
                    timestamp += ValidDelay(definition.GetStep(i));
                }

                events[i] = new TimedCommand(definition.GetStep(i).Command, timestamp);
            }

            return events;
        }

        private static QusapComboDefinition Definition(QusapComboId comboId)
        {
            IReadOnlyList<QusapComboDefinition> definitions = QusapComboDefinition.CreateDefaultDefinitions();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i].ComboId == comboId)
                {
                    return definitions[i];
                }
            }

            Assert.Fail($"Missing combo definition {comboId}.");
            return null;
        }

        private static double ValidDelay(QusapComboStep step)
        {
            return (step.MinimumDelay + step.MaximumDelay) / 2d;
        }

        private static double Midpoint(double left, double right)
        {
            return left + (right - left) / 2d;
        }

        private readonly struct TimedCommand
        {
            public TimedCommand(QusapCombatCommand command, double timestamp)
            {
                Command = command;
                Timestamp = timestamp;
            }

            public QusapCombatCommand Command { get; }
            public double Timestamp { get; }
        }

        private sealed class Harness : IDisposable
        {
            private readonly InputActionAsset inputAsset;
            private readonly GameObject root;
            private readonly GameObject targetAObject;
            private readonly GameObject targetBObject;

            public Harness()
            {
                root = new GameObject("ComboRecognitionTestPlayer");
                root.SetActive(false);
                Body = root.AddComponent<Rigidbody>();
                Body.useGravity = false;
                root.AddComponent<CapsuleCollider>();
                inputAsset = ScriptableObject.CreateInstance<InputActionAsset>();
                InputActionMap map = new("Gameplay");
                inputAsset.AddActionMap(map);
                map.AddAction("Move", InputActionType.Value);
                map.AddAction("Jump", InputActionType.Button);
                map.AddAction("Drop", InputActionType.Button);
                map.AddAction("Dash", InputActionType.Button);
                map.AddAction("WeakKick", InputActionType.Button);
                map.AddAction("StrongKick", InputActionType.Button);
                map.AddAction("Headbutt", InputActionType.Button);
                map.AddAction("WeaponStrong", InputActionType.Button);
                map.AddAction("Parry", InputActionType.Button);
                Input = root.AddComponent<QusapInputReader>();
                SetField(Input, "inputActionAsset", inputAsset);
                Ground = root.AddComponent<QusapGroundSensor>();
                root.AddComponent<QusapWallSensor>();
                root.AddComponent<QusapHorizontalMotor>();
                root.AddComponent<QusapVerticalMotor>();
                Dash = root.AddComponent<QusapDashMotor>();
                Receiver = root.AddComponent<QusapHitReceiver>();
                Hitstun = root.AddComponent<QusapHitstunController>();
                root.AddComponent<QusapRespawnController>();
                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(root.transform, false);
                Hitbox = hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = root.AddComponent<QusapCombatController>();
                SetField(Combat, "attackHitbox", Hitbox);
                SetField(Combat, "logRecognizedCombos", false);
                Combat.AttackStarted += _ => AttackStartCount++;
                root.SetActive(true);
                SetProperty(Ground, "IsGrounded", true);
                targetAObject = CreateTarget("ComboTargetA", out QusapHitReceiver targetA, out Rigidbody bodyA);
                targetBObject = CreateTarget("ComboTargetB", out QusapHitReceiver targetB, out _);
                TargetA = targetA;
                TargetABody = bodyA;
                TargetB = targetB;
            }

            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapDashMotor Dash { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            public QusapHitReceiver TargetA { get; }
            public Rigidbody TargetABody { get; }
            public QusapHitReceiver TargetB { get; }
            public int AttackStartCount { get; private set; }
            public bool PendingComboStep => GetField<bool>(Combat, "hasPendingComboStep");
            public QusapHitReceiver ComboTarget => GetField<QusapHitReceiver>(Combat, "comboTarget");
            public float PhaseTimeRemaining => GetField<float>(Combat, "phaseTimeRemaining");

            public void Enqueue(QusapCombatCommand command, double timestamp)
            {
                Input.EnqueueCombatCommand(command, timestamp);
            }

            public void EnqueueWithoutInputEvent(
                QusapCombatCommand command,
                double timestamp)
            {
                QusapCombatCommandBuffer buffer = GetField<QusapCombatCommandBuffer>(
                    Input, "combatCommandBuffer");
                buffer.Enqueue(command, timestamp);
            }

            public void Enqueue(TimedCommand command)
            {
                Enqueue(command.Command, command.Timestamp);
            }

            public void Enqueue(TimedCommand[] commands)
            {
                for (int i = 0; i < commands.Length; i++)
                {
                    Enqueue(commands[i]);
                }
            }

            public void ProcessCombatCommands()
            {
                Invoke(Combat, "FixedUpdate");
            }

            public void EnterActive()
            {
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Startup));
                IQusapAttackDefinition attack = Combat.GetAttackDefinition(Combat.CurrentAttackVariant);
                Invoke(Combat, "AdvanceAttack", attack.StartupTime);
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
                Assert.That(Hitbox.IsActive, Is.True);
            }

            public void AcceptAndNotifyHit(QusapHitReceiver receiver)
            {
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
                IQusapAttackDefinition attack = Combat.GetAttackDefinition(Combat.CurrentAttackVariant);
                QusapHitInfo hitInfo = new(
                    Combat, attack.AttackType, Combat.CurrentAttackVariant, attack.Damage,
                    Combat.AttackDirection, attack.HorizontalKnockback, attack.VerticalKnockback,
                    attack.HitstunDuration, Vector3.zero);
                Assert.That(receiver.TryReceiveHit(hitInfo), Is.True);
                Combat.NotifyAttackHit(receiver);
            }

            public void FinishAttack()
            {
                Assert.That(Combat.IsAttacking, Is.True);
                Invoke(Combat, "AdvanceAttack", 10f);
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Idle));
            }

            public void ConfirmStep(TimedCommand command, QusapHitReceiver receiver)
            {
                Enqueue(command);
                ProcessCombatCommands();
                EnterActive();
                AcceptAndNotifyHit(receiver);
                FinishAttack();
            }

            public void ConfirmSetup(TimedCommand[] sequence, QusapHitReceiver receiver)
            {
                for (int i = 0; i < sequence.Length - 1; i++)
                {
                    ConfirmStep(sequence[i], receiver);
                }
            }

            public void Arm(QusapComboId comboId, QusapHitReceiver receiver, double startTimestamp = 10d)
            {
                TimedCommand[] sequence = Events(comboId, startTimestamp);
                ConfirmSetup(sequence, receiver);
                Enqueue(sequence[sequence.Length - 1]);
                ProcessCombatCommands();
            }

            public void SetDashing(bool value)
            {
                SetProperty(Dash, "IsDashing", value);
            }

            public void SetComboRecognitionEnabled(bool value)
            {
                SetField(Combat, "comboRecognitionEnabled", value);
            }

            public void PressLegacyWeakKick()
            {
                SetField(Input, "weakKickPressed", true);
            }

            public void Dispose()
            {
                if (targetBObject != null) UnityEngine.Object.DestroyImmediate(targetBObject);
                if (targetAObject != null) UnityEngine.Object.DestroyImmediate(targetAObject);
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                if (inputAsset != null) UnityEngine.Object.DestroyImmediate(inputAsset);
            }

            private static GameObject CreateTarget(
                string name, out QusapHitReceiver receiver, out Rigidbody body)
            {
                GameObject target = new(name);
                target.transform.position = new Vector3(100f, 100f, 100f);
                body = target.AddComponent<Rigidbody>();
                body.useGravity = false;
                receiver = target.AddComponent<QusapHitReceiver>();
                return target;
            }

            private static T GetField<T>(object target, string name)
            {
                FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"Missing field {name}");
                return (T)field.GetValue(target);
            }

            private static void SetField(object target, string name, object value)
            {
                FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, $"Missing field {name}");
                field.SetValue(target, value);
            }

            private static void SetProperty(object target, string name, object value)
            {
                PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
                Assert.That(property, Is.Not.Null, $"Missing property {name}");
                property.SetValue(target, value);
            }

            private static void Invoke(object target, string name, params object[] args)
            {
                MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null, $"Missing method {name}");
                method.Invoke(target, args);
            }
        }
    }
}
