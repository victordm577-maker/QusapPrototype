using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapComboSetupAttackPlayModeTests
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
        public void BodyAttackCandidateStartsComboSetupAttack()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.BodyAttack, 1d, grounded: true);
            Assert.That(attacker.Combat.IsComboSetupAttack, Is.True);
            Assert.That(attacker.Combat.CurrentComboSetupCommand, Is.EqualTo(QusapCombatCommand.BodyAttack));
            Assert.That(attacker.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.WeakKickGround));
        }

        [Test]
        public void WeaponLightCandidateStartsComboSetupAttack()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.WeaponLight, 1d, grounded: true);
            Assert.That(attacker.Combat.IsComboSetupAttack, Is.True);
            Assert.That(attacker.Combat.CurrentComboSetupCommand, Is.EqualTo(QusapCombatCommand.WeaponLight));
            Assert.That(attacker.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.StrongKickGround));
        }

        [Test]
        public void GroundWeaponLightSetupUsesLowKnockback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            HitResult result = attacker.PerformSetupHit(
                QusapCombatCommand.WeaponLight, 1d, grounded: true, target);
            Assert.That(result.Velocity.x, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void GroundWeaponLightSetupDoesNotLaunchTargetAway()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            HitResult result = attacker.PerformSetupHit(
                QusapCombatCommand.WeaponLight, 1d, grounded: true, target);
            Assert.That(Mathf.Abs(result.Velocity.x), Is.LessThan(2f));
            Assert.That(Mathf.Abs(result.Velocity.y), Is.LessThan(1f));
        }

        [Test]
        public void GroundBodySetupUsesLowKnockback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            HitResult result = attacker.PerformSetupHit(
                QusapCombatCommand.BodyAttack, 1d, grounded: true, target);
            Assert.That(result.Velocity.x, Is.EqualTo(1.75f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(0.35f).Within(0.001f));
        }

        [Test]
        public void SetupHitAppliesOnlySmallDamage()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            HitResult result = attacker.PerformSetupHit(
                QusapCombatCommand.BodyAttack, 1d, grounded: true, target);
            Assert.That(result.DamageApplied, Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void SecondWeaponLightSetupAlsoUsesLowKnockback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.PerformSetupHit(QusapCombatCommand.WeaponLight, 1d, true, target);
            attacker.FinishAttack();
            target.Hitstun.ResetHitstun();

            HitResult second = attacker.PerformSetupHit(
                QusapCombatCommand.WeaponLight, 1.2d, true, target);
            Assert.That(second.Velocity.x, Is.EqualTo(1.25f).Within(0.001f));
            Assert.That(second.Velocity.y, Is.EqualTo(0.25f).Within(0.001f));
        }

        [Test]
        public void SetupHitsStillConfirmAgainstSameTarget()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(QusapComboId.Launch, target);
            Assert.That(attacker.Combat.HasArmedFinisher, Is.True);
            Assert.That(attacker.Combat.ArmedFinisherTarget, Is.SameAs(target.Receiver));
        }

        [Test]
        public void SetupHitAgainstDifferentTargetDoesNotAdvanceCombo()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness firstTarget = CreatePlayer("FirstTarget");
            PlayerHarness otherTarget = CreatePlayer("OtherTarget");
            attacker.PerformSetupHit(QusapCombatCommand.BodyAttack, 1d, true, firstTarget);
            attacker.FinishAttack();
            firstTarget.Hitstun.ResetHitstun();
            firstTarget.MoveFarAway();

            attacker.PerformSetupHit(QusapCombatCommand.WeaponLight, 1.2d, true, otherTarget);
            attacker.FinishAttack();
            attacker.Enqueue(QusapCombatCommand.BodyAttack, 1.4d);
            attacker.ProcessFixed();

            Assert.That(attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(attacker.Combat.ActiveComboCandidateCount, Is.Zero);
        }

        [Test]
        public void SetupWhiffStillResetsCombo()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.BodyAttack, 1d, grounded: true);
            attacker.EnterActive();
            attacker.RunHitboxFixed();
            attacker.FinishAttack();
            Assert.That(attacker.Combat.ActiveComboCandidateCount, Is.Zero);
            Assert.That(attacker.Combat.IsComboSetupAttack, Is.False);
        }

        [Test]
        public void SetupAttackCanBeInterruptedByThirdPlayer()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness third = CreatePlayer("Third");
            attacker.StartSetup(QusapCombatCommand.BodyAttack, 1d, grounded: true);
            attacker.EnterActive();
            attacker.ReceiveInterruptingHitFrom(third);
            Assert.That(attacker.Combat.IsAttacking, Is.False);
            Assert.That(attacker.Combat.IsComboSetupAttack, Is.False);
            Assert.That(attacker.Hitbox.IsActive, Is.False);
        }

        [Test]
        public void SetupAttackClearsDiagnosticStateOnFinish()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.BodyAttack, 1d, grounded: true);
            attacker.EnterActive();
            attacker.FinishAttack();
            AssertDiagnosticCleared(attacker);
        }

        [Test]
        public void SetupAttackClearsDiagnosticStateOnCancel()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.WeaponLight, 1d, grounded: true);
            attacker.Combat.CancelAttack();
            AssertDiagnosticCleared(attacker);
        }

        [Test]
        public void FinalLaunchInputDoesNotStartSetupAttack()
        {
            AssertFinalInputArmsWithoutAttack(QusapComboId.Launch);
        }

        [Test]
        public void FinalDisarmInputDoesNotStartLegacyHeadbutt()
        {
            AssertFinalInputArmsWithoutAttack(QusapComboId.Disarm);
        }

        [Test]
        public void FinalDamageInputDoesNotStartAttack()
        {
            AssertFinalInputArmsWithoutAttack(QusapComboId.Damage);
        }

        [Test]
        public void DirectStrongKickStillUsesLegacyKnockback()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.StartDirectAttack(QusapAttackType.StrongKick, grounded: true);
            HitResult result = attacker.HitCurrentAttack(target);
            Assert.That(result.Velocity.x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(3f).Within(0.001f));
            Assert.That(result.DamageApplied, Is.Zero);
            Assert.That(attacker.Combat.IsComboSetupAttack, Is.False);
        }

        [Test]
        public void RecognitionDisabledUsesLegacyStrongKick()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.SetComboRecognitionEnabled(false);
            attacker.SetGrounded(true);
            attacker.PressLegacyStrongKick();
            attacker.ProcessFixed();
            Assert.That(attacker.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.StrongKick));
            Assert.That(attacker.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.StrongKickGround));
            HitResult result = attacker.HitCurrentAttack(target);
            Assert.That(result.Velocity.x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void StandaloneHeadbuttStillUsesLegacyData()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.SetGrounded(true);
            attacker.Enqueue(QusapCombatCommand.Headbutt, 1d);
            attacker.ProcessFixed();
            HitResult result = attacker.HitCurrentAttack(target);
            Assert.That(result.Velocity.x, Is.EqualTo(9f).Within(0.001f));
            Assert.That(result.Velocity.y, Is.EqualTo(4f).Within(0.001f));
            Assert.That(attacker.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.HeadbuttGround));
        }

        [Test]
        public void AirBodyCandidateUsesAirSetupProfile()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.BodyAttack, 1d, grounded: false);
            Assert.That(attacker.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.WeakKickAir));
            Assert.That(attacker.ActiveAttack.Damage, Is.EqualTo(1f));
            Assert.That(attacker.ActiveAttack.HorizontalKnockback, Is.EqualTo(1.5f));
        }

        [Test]
        public void AirWeaponLightCandidateUsesAirSetupProfile()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.WeaponLight, 1d, grounded: false);
            Assert.That(attacker.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.StrongKickAir));
            Assert.That(attacker.ActiveAttack.Damage, Is.EqualTo(1f));
            Assert.That(attacker.ActiveAttack.HorizontalKnockback, Is.EqualTo(1.25f));
        }

        [Test]
        public void AirSetupLandingStillEndsActiveWindowCorrectly()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.StartSetup(QusapCombatCommand.BodyAttack, 1d, grounded: false);
            attacker.EnterActive();
            Assert.That(attacker.Hitbox.IsActive, Is.True);
            attacker.SetGrounded(true);
            attacker.ProcessFixed();
            Assert.That(attacker.Hitbox.IsActive, Is.False);
            Assert.That(attacker.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Recovery));
        }

        [Test]
        public void AirSetupUsesItsOwnHorizontalRetention()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            attacker.Body.linearVelocity = new Vector3(10f, 2f, 0f);
            attacker.StartSetup(QusapCombatCommand.WeaponLight, 1d, grounded: false);
            Assert.That(attacker.Body.linearVelocity.x, Is.EqualTo(9.8f).Within(0.001f));
            Assert.That(attacker.Body.linearVelocity.y, Is.EqualTo(2f).Within(0.001f));
        }

        [Test]
        public void ParryWindowStillOpensAfterConfirmedCombo()
        {
            Pair pair = CreateArmedPair();
            int opened = 0;
            pair.Attacker.Combat.FinisherParryWindowOpened += (_, _) => opened++;
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            Assert.That(pair.Attacker.Combat.IsParryWindowOpen, Is.True);
            Assert.That(opened, Is.EqualTo(1));
        }

        [Test]
        public void SuccessfulParryStillCancelsFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
        }

        [Test]
        public void FailedParryStillAppliesConfiguredVulnerability()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.Hitstun.IsInHitstun, Is.True);
            Assert.That(pair.Defender.Hitstun.TimeRemaining, Is.EqualTo(0.30f).Within(0.001f));
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.True);
        }

        [Test]
        public void ReadyFinisherCanStillBeConsumedOnce()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            Assert.That(pair.Attacker.Combat.TryConsumeReadyFinisher(out QusapComboId combo, out QusapHitReceiver target), Is.True);
            Assert.That(combo, Is.EqualTo(QusapComboId.Launch));
            Assert.That(target, Is.SameAs(pair.Defender.Receiver));
            Assert.That(pair.Attacker.Combat.TryConsumeReadyFinisher(out _, out _), Is.False);
        }

        [Test]
        public void SetupAttacksDoNotMoveAttackerMagnetically()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            Vector3 position = attacker.Root.transform.position;
            attacker.PerformSetupHit(QusapCombatCommand.BodyAttack, 1d, true, target);
            Assert.That(attacker.Root.transform.position, Is.EqualTo(position));
        }

        [Test]
        public void SetupAttacksDoNotModifyTargetTransformDirectly()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.PrepareTargetForHit(target);
            Vector3 position = target.Root.transform.position;
            attacker.StartSetup(QusapCombatCommand.WeaponLight, 1d, grounded: true);
            attacker.EnterActive();
            attacker.RunHitboxFixed();
            Assert.That(target.Root.transform.position, Is.EqualTo(position));
        }

        [Test]
        public void ExistingComboAndParrySuitesStillPass()
        {
            Pair pair = CreateArmedPair();
            Assert.That(pair.Attacker.Combat.ArmedFinisherCombo, Is.EqualTo(QusapComboId.Launch));
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Defender.Parry(pair.WindowMidpoint);
            pair.Defender.ProcessFixed();
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
        }

        private void AssertFinalInputArmsWithoutAttack(QusapComboId comboId)
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness target = CreatePlayer("Target");
            attacker.ArmCombo(comboId, target);
            Assert.That(attacker.Combat.HasArmedFinisher, Is.True);
            Assert.That(attacker.Combat.IsAttacking, Is.False);
            AssertDiagnosticCleared(attacker);
        }

        private Pair CreateArmedPair()
        {
            PlayerHarness attacker = CreatePlayer("Attacker");
            PlayerHarness defender = CreatePlayer("Defender");
            attacker.ArmCombo(QusapComboId.Launch, defender);
            return new Pair(attacker, defender);
        }

        private PlayerHarness CreatePlayer(string name)
        {
            PlayerHarness player = new(name);
            players.Add(player);
            return player;
        }

        private static void AssertDiagnosticCleared(PlayerHarness player)
        {
            Assert.That(player.Combat.IsComboSetupAttack, Is.False);
            Assert.That(player.Combat.CurrentComboSetupCommand, Is.Null);
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

        private readonly struct HitResult
        {
            public HitResult(Vector3 velocity, float damageApplied)
            {
                Velocity = velocity;
                DamageApplied = damageApplied;
            }

            public Vector3 Velocity { get; }
            public float DamageApplied { get; }
        }

        private sealed class PlayerHarness : IDisposable
        {
            private static int nextPlayerId;
            private readonly InputActionAsset inputAsset;
            private readonly float homeX;

            public PlayerHarness(string name)
            {
                int playerId = ++nextPlayerId;
                homeX = playerId * 20f;
                Root = new GameObject($"{name}_{playerId}");
                Root.SetActive(false);
                Root.transform.position = new Vector3(homeX, 0f, 0f);
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
                Root.SetActive(true);
                SetGrounded(false);
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            public IQusapAttackDefinition ActiveAttack =>
                Combat.GetAttackDefinition(Combat.CurrentAttackVariant);

            public void StartSetup(
                QusapCombatCommand command,
                double timestamp,
                bool grounded)
            {
                SetGrounded(grounded);
                Enqueue(command, timestamp);
                ProcessFixed();
                Assert.That(Combat.IsAttacking, Is.True);
                Assert.That(Combat.IsComboSetupAttack, Is.True);
            }

            public void StartDirectAttack(QusapAttackType attackType, bool grounded)
            {
                SetGrounded(grounded);
                Assert.That(Combat.TryStartAttack(attackType), Is.True);
                Assert.That(Combat.IsComboSetupAttack, Is.False);
            }

            public HitResult PerformSetupHit(
                QusapCombatCommand command,
                double timestamp,
                bool grounded,
                PlayerHarness target)
            {
                PrepareTargetForHit(target);
                StartSetup(command, timestamp, grounded);
                return HitCurrentAttack(target);
            }

            public HitResult HitCurrentAttack(PlayerHarness target)
            {
                PrepareTargetForHit(target);
                EnterActive();
                float damageBefore = target.Receiver.TotalDamageReceived;
                RunHitboxFixed();
                float damageApplied = target.Receiver.TotalDamageReceived - damageBefore;
                Assert.That(damageApplied, Is.GreaterThanOrEqualTo(0f));
                return new HitResult(target.Body.linearVelocity, damageApplied);
            }

            public void PrepareTargetForHit(PlayerHarness target)
            {
                target.Root.transform.position = Root.transform.position + new Vector3(0.9f, 0f, 0f);
                target.Body.linearVelocity = Vector3.zero;
                target.Hitstun.ResetHitstun();
                Physics.SyncTransforms();
            }

            public void EnterActive()
            {
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Startup));
                IQusapAttackDefinition attack = ActiveAttack;
                Assert.That(attack, Is.Not.Null);
                Advance(attack.StartupTime);
                Assert.That(Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
                Assert.That(Hitbox.IsActive, Is.True);
            }

            public void FinishAttack()
            {
                Assert.That(Combat.IsAttacking, Is.True);
                Advance(10f);
                Assert.That(Combat.IsAttacking, Is.False);
            }

            public void ArmCombo(QusapComboId comboId, PlayerHarness target)
            {
                QusapComboDefinition definition = FindDefinition(comboId);
                double timestamp = 10d;
                for (int i = 0; i < definition.StepCount - 1; i++)
                {
                    if (i > 0)
                    {
                        timestamp += ValidDelay(definition.GetStep(i));
                    }

                    PerformSetupHit(definition.GetStep(i).Command, timestamp, true, target);
                    FinishAttack();
                    target.Hitstun.ResetHitstun();
                }

                timestamp += ValidDelay(definition.GetStep(definition.StepCount - 1));
                Enqueue(definition.GetStep(definition.StepCount - 1).Command, timestamp);
                ProcessFixed();
                Assert.That(Combat.HasArmedFinisher, Is.True);
            }

            public void ReceiveInterruptingHitFrom(PlayerHarness source)
            {
                QusapHitInfo hit = new(
                    source.Combat,
                    QusapAttackType.WeakKick,
                    QusapAttackVariant.WeakKickGround,
                    0f,
                    1,
                    0f,
                    0f,
                    0.2f,
                    Vector3.zero);
                Assert.That(Receiver.TryReceiveHit(hit), Is.True);
            }

            public void Enqueue(QusapCombatCommand command, double timestamp)
            {
                Input.EnqueueCombatCommand(command, timestamp);
            }

            public void Parry(double timestamp)
            {
                Enqueue(QusapCombatCommand.Parry, timestamp);
            }

            public void ProcessFixed()
            {
                Invoke(Combat, "FixedUpdate");
            }

            public void RunHitboxFixed()
            {
                Physics.SyncTransforms();
                Invoke(Hitbox, "FixedUpdate");
            }

            public void Advance(float seconds)
            {
                Invoke(Combat, "AdvanceAttack", seconds);
            }

            public void AdvanceFinisher(double timestamp)
            {
                Combat.UpdateFinisherDefense(timestamp);
            }

            public void SetGrounded(bool grounded)
            {
                SetProperty(Ground, "IsGrounded", grounded);
            }

            public void SetComboRecognitionEnabled(bool enabled)
            {
                SetField(Combat, "comboRecognitionEnabled", enabled);
            }

            public void PressLegacyStrongKick()
            {
                SetField(Input, "strongKickPressed", true);
            }

            public void MoveFarAway()
            {
                Root.transform.position = new Vector3(homeX + 1000f, 0f, 0f);
                Body.linearVelocity = Vector3.zero;
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

            private static QusapComboDefinition FindDefinition(QusapComboId comboId)
            {
                IReadOnlyList<QusapComboDefinition> definitions =
                    QusapComboDefinition.CreateDefaultDefinitions();
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
}
