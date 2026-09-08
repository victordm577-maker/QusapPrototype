using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Qusap.Tests
{
    public sealed class QusapFinisherParryPlayModeTests
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
        public void ArmedFinisherRegistersIncomingOpportunityOnTarget()
        {
            Pair pair = CreateArmedPair();
            Assert.That(pair.Defender.Combat.IncomingFinisherCount, Is.EqualTo(1));
        }

        [Test]
        public void ParryInsideWindowCancelsFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
        }

        [Test]
        public void SuccessfulParryAppliesHitstunToAttacker()
        {
            Pair pair = CreateArmedPair();
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Hitstun.IsInHitstun, Is.True);
        }

        [Test]
        public void SuccessfulParryDoesNotHitstunDefender()
        {
            Pair pair = CreateArmedPair();
            pair.SuccessfulParry();
            Assert.That(pair.Defender.Hitstun.IsInHitstun, Is.False);
        }

        [Test]
        public void SuccessfulParryDoesNotApplyDamage()
        {
            Pair pair = CreateArmedPair();
            float attackerDamage = pair.Attacker.Receiver.TotalDamageReceived;
            float defenderDamage = pair.Defender.Receiver.TotalDamageReceived;
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Receiver.TotalDamageReceived, Is.EqualTo(attackerDamage));
            Assert.That(pair.Defender.Receiver.TotalDamageReceived, Is.EqualTo(defenderDamage));
        }

        [Test]
        public void SuccessfulParryDoesNotApplyKnockback()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.Body.linearVelocity = new Vector3(2f, 3f, 0f);
            pair.Defender.Body.linearVelocity = new Vector3(-2f, 1f, 0f);
            Vector3 attackerVelocity = pair.Attacker.Body.linearVelocity;
            Vector3 defenderVelocity = pair.Defender.Body.linearVelocity;
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Body.linearVelocity, Is.EqualTo(attackerVelocity));
            Assert.That(pair.Defender.Body.linearVelocity, Is.EqualTo(defenderVelocity));
        }

        [Test]
        public void SuccessfulParryDoesNotEmitReadyEvent()
        {
            Pair pair = CreateArmedPair();
            int readyCount = 0;
            pair.Attacker.Combat.FinisherReadyToResolve += (_, _) => readyCount++;
            pair.SuccessfulParry();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 1d);
            Assert.That(readyCount, Is.Zero);
        }

        [Test]
        public void EarlyParryAppliesFailureHitstunToDefender()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.Hitstun.IsInHitstun, Is.True);
        }

        [Test]
        public void LateParryAppliesFailureHitstunToDefender()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.Hitstun.IsInHitstun, Is.True);
        }

        [Test]
        public void ParryWithoutIncomingFinisherFails()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            QusapParryAttemptOutcome outcome = QusapParryAttemptOutcome.None;
            defender.Combat.ParryFailed += value => outcome = value;
            defender.Parry(InputState.currentTime);
            defender.ProcessFixed();
            Assert.That(outcome, Is.EqualTo(QusapParryAttemptOutcome.NoIncomingFinisher));
        }

        [Test]
        public void FailedParryDoesNotCancelIncomingFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.True);
        }

        [Test]
        public void FailedParryDoesNotChangeFinisherTarget()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Attacker.Combat.ArmedFinisherTarget, Is.SameAs(pair.Defender.Receiver));
        }

        [Test]
        public void FailedParryDoesNotApplyDamageOrKnockback()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Body.linearVelocity = new Vector3(4f, 2f, 0f);
            float damage = pair.Defender.Receiver.TotalDamageReceived;
            Vector3 velocity = pair.Defender.Body.linearVelocity;
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.Receiver.TotalDamageReceived, Is.EqualTo(damage));
            Assert.That(pair.Defender.Body.linearVelocity, Is.EqualTo(velocity));
        }

        [Test]
        public void IneligibleParryWhileAttackingFails()
        {
            Pair pair = CreateArmedPair();
            Assert.That(pair.Defender.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            ObserveFailure(pair.Defender);
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.LastFailure, Is.EqualTo(QusapParryAttemptOutcome.Ineligible));
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.True);
        }

        [Test]
        public void IneligibleParryWhileDashingFails()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.SetDashing(true);
            ObserveFailure(pair.Defender);
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.LastFailure, Is.EqualTo(QusapParryAttemptOutcome.Ineligible));
        }

        [Test]
        public void IneligibleParryWhileInHitstunCannotSucceed()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Hitstun.EnterHitstun(1f);
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.True);
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.Not.EqualTo(QusapFinisherDefensePhase.Parried));
        }

        [Test]
        public void HeldParryDoesNotResolveMoreThanOnce()
        {
            Pair pair = CreateArmedPair();
            int successCount = 0;
            pair.Defender.Combat.ParrySucceeded += (_, _) => successCount++;
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            pair.Defender.ProcessFixed();
            Assert.That(successCount, Is.EqualTo(1));
        }

        [Test]
        public void ParryCommandIsNotProcessedTwiceFromEventAndFifo()
        {
            Pair pair = CreateArmedPair();
            int successCount = 0;
            int failureCount = 0;
            pair.Defender.Combat.ParrySucceeded += (_, _) => successCount++;
            pair.Defender.Combat.ParryFailed += _ => failureCount++;
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            pair.Defender.ProcessFixed();
            Assert.That(successCount, Is.EqualTo(1));
            Assert.That(failureCount, Is.Zero);
            Assert.That(pair.Defender.Input.PendingCombatCommandCount, Is.Zero);
        }

        [Test]
        public void UnopposedFinisherBecomesReadyToResolve()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            Assert.That(pair.Attacker.Combat.HasFinisherReadyToResolve, Is.True);
        }

        [Test]
        public void ReadyEventFiresExactlyOnce()
        {
            Pair pair = CreateArmedPair();
            int count = 0;
            pair.Attacker.Combat.FinisherReadyToResolve += (_, _) => count++;
            double after = pair.Attacker.Combat.ParryWindowClosesAt + 0.001d;
            pair.Attacker.AdvanceFinisher(after);
            pair.Attacker.AdvanceFinisher(after + 1d);
            Assert.That(count, Is.EqualTo(1));
        }

        [Test]
        public void TryConsumeReadyFinisherSucceedsOnce()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            Assert.That(pair.Attacker.Combat.TryConsumeReadyFinisher(out QusapComboId combo, out QusapHitReceiver target), Is.True);
            Assert.That(combo, Is.EqualTo(QusapComboId.Launch));
            Assert.That(target, Is.SameAs(pair.Defender.Receiver));
            Assert.That(pair.Attacker.Combat.TryConsumeReadyFinisher(out _, out _), Is.False);
        }

        [Test]
        public void TryConsumeReadyFinisherHasNoGameplayEffect()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            float damage = pair.Defender.Receiver.TotalDamageReceived;
            Vector3 velocity = pair.Defender.Body.linearVelocity;
            bool hitbox = pair.Attacker.Hitbox.IsActive;
            pair.Attacker.Combat.TryConsumeReadyFinisher(out _, out _);
            Assert.That(pair.Defender.Receiver.TotalDamageReceived, Is.EqualTo(damage));
            Assert.That(pair.Defender.Body.linearVelocity, Is.EqualTo(velocity));
            Assert.That(pair.Attacker.Hitbox.IsActive, Is.EqualTo(hitbox));
        }

        [Test]
        public void AttackerHitDuringWindowCancelsFinisher()
        {
            Pair pair = CreateArmedPair();
            PlayerHarness third = CreatePlayer("Third");
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Attacker.ReceiveHarmlessHitFrom(third);
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void AttackerDashDuringWindowCancelsFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.SetDashing(true);
            pair.Attacker.ProcessFixed();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void AttackerRespawnDuringWindowCancelsFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.Respawn.Respawn();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void DisabledTargetCancelsFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.SetActive(false);
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
        }

        [Test]
        public void CombatDisabledCancelsOutgoingFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.Combat.CombatAllowed = false;
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Defender.Combat.IncomingFinisherCount, Is.Zero);
        }

        [Test]
        public void OnDisableRemovesIncomingRegistration()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.SetActive(false);
            Assert.That(pair.Defender.Combat.IncomingFinisherCount, Is.Zero);
        }

        [Test]
        public void DifferentDefenderCannotParryAnotherTargetsFinisher()
        {
            Pair pair = CreateArmedPair();
            PlayerHarness other = CreatePlayer("OtherDefender");
            other.Parry(pair.WindowMidpoint);
            other.ProcessFixed();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.True);
        }

        [Test]
        public void TwoIncomingFinishersUseEarliestClosingWindow()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            PlayerHarness early = CreatePlayer("EarlyAttacker");
            PlayerHarness late = CreatePlayer("LateAttacker");
            early.ConfigureParrySettings(0.1d, 0.2d);
            late.ConfigureParrySettings(0.1d, 0.5d);
            early.ArmLaunchAgainst(defender);
            late.ArmLaunchAgainst(defender);
            double timestamp = Math.Max(early.Combat.ParryWindowOpensAt, late.Combat.ParryWindowOpensAt) + 0.01d;
            defender.Parry(timestamp);
            defender.ProcessFixed();
            Assert.That(early.Combat.HasArmedFinisher, Is.False);
            Assert.That(late.Combat.HasArmedFinisher, Is.True);
        }

        [Test]
        public void OneParryPressCannotCancelTwoFinishers()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            PlayerHarness first = CreatePlayer("FirstAttacker");
            PlayerHarness second = CreatePlayer("SecondAttacker");
            first.ArmLaunchAgainst(defender);
            second.ArmLaunchAgainst(defender);
            double timestamp = Math.Max(first.Combat.ParryWindowOpensAt, second.Combat.ParryWindowOpensAt) + 0.01d;
            defender.Parry(timestamp);
            defender.ProcessFixed();
            int remaining = (first.Combat.HasArmedFinisher ? 1 : 0)
                + (second.Combat.HasArmedFinisher ? 1 : 0);
            Assert.That(remaining, Is.EqualTo(1));
        }

        [Test]
        public void OffensiveCommandsCannotStartAnotherAttackDuringWindow()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Attacker.Enqueue(QusapCombatCommand.BodyAttack, 50d);
            pair.Attacker.ProcessFixed();
            Assert.That(pair.Attacker.Combat.IsAttacking, Is.False);
        }

        [Test]
        public void CommandsEnteredDuringWindowDoNotExecuteLater()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Attacker.Enqueue(QusapCombatCommand.BodyAttack, 50d);
            pair.Attacker.ProcessFixed();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            pair.Attacker.ProcessFixed();
            Assert.That(pair.Attacker.Input.PendingCombatCommandCount, Is.Zero);
            Assert.That(pair.Attacker.Combat.IsAttacking, Is.False);
        }

        [Test]
        public void ParryWindowUsesInputSystemTimeDomain()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness defender = CreatePlayer("Defender");
            double before = InputState.currentTime;
            attacker.ArmLaunchAgainst(defender);
            double after = InputState.currentTime;
            Assert.That(attacker.Combat.ParryWindowOpensAt, Is.GreaterThanOrEqualTo(before + 0.1d));
            Assert.That(attacker.Combat.ParryWindowOpensAt, Is.LessThanOrEqualTo(after + 0.1d));
        }

        [Test]
        public void BufferedFinalInputDoesNotCreateAlreadyExpiredParryWindow()
        {
            Pair pair = CreateArmedPair();
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Telegraph));
            Assert.That(pair.Attacker.Combat.ParryWindowClosesAt, Is.GreaterThan(InputState.currentTime));
        }

        [Test]
        public void ExistingComboRecognitionTestsStillPass()
        {
            Pair pair = CreateArmedPair();
            Assert.That(pair.Attacker.Combat.LastCompletedCombo, Is.EqualTo(QusapComboId.Launch));
            Assert.That(pair.Attacker.Combat.ArmedFinisherTarget, Is.SameAs(pair.Defender.Receiver));
        }

        [Test]
        public void LegacyAttacksStillWorkWhenRecognitionIsDisabled()
        {
            PlayerHarness player = CreatePlayer("Legacy");
            player.SetComboRecognitionEnabled(false);
            player.PressLegacyWeakKick();
            player.ProcessFixed();
            Assert.That(player.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.WeakKick));
        }

        [Test]
        public void FinisherParryDoesNotModifyHitboxes()
        {
            Pair pair = CreateArmedPair();
            Assert.That(pair.Attacker.Hitbox.IsActive, Is.False);
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Hitbox.IsActive, Is.False);
            Assert.That(pair.Defender.Hitbox.IsActive, Is.False);
        }

        [Test]
        public void FinisherParryDoesNotMoveEitherPlayer()
        {
            Pair pair = CreateArmedPair();
            Vector3 attackerPosition = pair.Attacker.Root.transform.position;
            Vector3 defenderPosition = pair.Defender.Root.transform.position;
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Root.transform.position, Is.EqualTo(attackerPosition));
            Assert.That(pair.Defender.Root.transform.position, Is.EqualTo(defenderPosition));
        }

        [Test]
        public void ThirdPartyInterruptionStillCancelsAttackerFinisher()
        {
            Pair pair = CreateArmedPair();
            PlayerHarness third = CreatePlayer("Third");
            pair.Attacker.ReceiveHarmlessHitFrom(third);
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
        }

        private Pair CreateArmedPair()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness defender = CreatePlayer("Defender");
            attacker.ArmLaunchAgainst(defender);
            return new Pair(attacker, defender);
        }

        private PlayerHarness CreatePlayer(string name)
        {
            PlayerHarness player = new(name);
            players.Add(player);
            return player;
        }

        private static void ObserveFailure(PlayerHarness defender)
        {
            defender.Combat.ParryFailed += outcome => defender.LastFailure = outcome;
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

            public void SuccessfulParry()
            {
                Attacker.AdvanceFinisher(Attacker.Combat.ParryWindowOpensAt);
                Defender.Parry(WindowMidpoint);
                Defender.ProcessFixed();
            }
        }

        private sealed class PlayerHarness : IDisposable
        {
            private static int nextNameId;
            private readonly InputActionAsset inputAsset;

            public PlayerHarness(string name)
            {
                Root = new GameObject($"{name}_{++nextNameId}");
                Root.SetActive(false);
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
                    map.AddAction(action, action == "Move" ? InputActionType.Value : InputActionType.Button);
                }

                Input = Root.AddComponent<QusapInputReader>();
                SetField(Input, "inputActionAsset", inputAsset);
                Ground = Root.AddComponent<QusapGroundSensor>();
                Root.AddComponent<QusapWallSensor>();
                Root.AddComponent<QusapHorizontalMotor>();
                Root.AddComponent<QusapVerticalMotor>();
                Dash = Root.AddComponent<QusapDashMotor>();
                Receiver = Root.AddComponent<QusapHitReceiver>();
                Hitstun = Root.AddComponent<QusapHitstunController>();
                Respawn = Root.AddComponent<QusapRespawnController>();
                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(Root.transform, false);
                Hitbox = hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = Root.AddComponent<QusapCombatController>();
                SetField(Combat, "attackHitbox", Hitbox);
                SetField(Combat, "logRecognizedCombos", false);
                Root.SetActive(true);
                SetProperty(Ground, "IsGrounded", true);
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapDashMotor Dash { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapRespawnController Respawn { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            public QusapParryAttemptOutcome LastFailure { get; set; }

            public void ArmLaunchAgainst(PlayerHarness defender)
            {
                QusapComboDefinition definition = FindDefinition(QusapComboId.Launch);
                double timestamp = 10d;
                for (int i = 0; i < definition.StepCount - 1; i++)
                {
                    if (i > 0)
                    {
                        timestamp += ValidDelay(definition.GetStep(i));
                    }

                    ConfirmStep(definition.GetStep(i).Command, timestamp, defender);
                }

                timestamp += ValidDelay(definition.GetStep(definition.StepCount - 1));
                Enqueue(definition.GetStep(definition.StepCount - 1).Command, timestamp);
                ProcessFixed();
                Assert.That(Combat.HasArmedFinisher, Is.True);
            }

            public void ConfirmStep(QusapCombatCommand command, double timestamp, PlayerHarness defender)
            {
                Enqueue(command, timestamp);
                ProcessFixed();
                IQusapAttackDefinition attack = Combat.GetAttackDefinition(Combat.CurrentAttackVariant);
                Invoke(Combat, "AdvanceAttack", attack.StartupTime);
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
                QusapHitInfo hit = new(
                    Combat, attack.AttackType, Combat.CurrentAttackVariant, attack.Damage,
                    Combat.AttackDirection, attack.HorizontalKnockback, attack.VerticalKnockback,
                    attack.HitstunDuration, Vector3.zero);
                Assert.That(defender.Receiver.TryReceiveHit(hit), Is.True);
                Combat.NotifyAttackHit(defender.Receiver);
                defender.Hitstun.ResetHitstun();
                Invoke(Combat, "AdvanceAttack", 10f);
                Assert.That(Combat.IsAttacking, Is.False);
            }

            public void Parry(double timestamp)
            {
                Input.EnqueueCombatCommand(QusapCombatCommand.Parry, timestamp);
            }

            public void Enqueue(QusapCombatCommand command, double timestamp)
            {
                Input.EnqueueCombatCommand(command, timestamp);
            }

            public void ProcessFixed()
            {
                Invoke(Combat, "FixedUpdate");
            }

            public void AdvanceFinisher(double timestamp)
            {
                Combat.UpdateFinisherDefense(timestamp);
            }

            public void ReceiveHarmlessHitFrom(PlayerHarness source)
            {
                QusapHitInfo hit = new(
                    source.Combat, QusapAttackType.WeakKick, QusapAttackVariant.WeakKickGround,
                    0f, 1, 0f, 0f, 0f, Vector3.zero);
                Assert.That(Receiver.TryReceiveHit(hit), Is.True);
            }

            public void ConfigureParrySettings(double telegraph, double window)
            {
                SetField(
                    Combat,
                    "finisherParrySettings",
                    new QusapFinisherParrySettings(telegraph, window, 0.6d, 0.3d));
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

            public void SetActive(bool active)
            {
                Root.SetActive(active);
            }

            public void Dispose()
            {
                if (Root != null) UnityEngine.Object.DestroyImmediate(Root);
                if (inputAsset != null) UnityEngine.Object.DestroyImmediate(inputAsset);
            }

            private static QusapComboDefinition FindDefinition(QusapComboId comboId)
            {
                IReadOnlyList<QusapComboDefinition> definitions = QusapComboDefinition.CreateDefaultDefinitions();
                for (int i = 0; i < definitions.Count; i++)
                {
                    if (definitions[i].ComboId == comboId) return definitions[i];
                }

                Assert.Fail($"Missing combo definition {comboId}.");
                return null;
            }

            private static double ValidDelay(QusapComboStep step)
            {
                return (step.MinimumDelay + step.MaximumDelay) / 2d;
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
