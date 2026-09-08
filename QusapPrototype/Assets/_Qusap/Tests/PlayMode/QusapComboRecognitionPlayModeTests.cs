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

        [Test]
        public void DamageSequenceFromInputQueueCompletesOnce()
        {
            Harness harness = CreateHarness();
            List<QusapComboId> completions = ObserveCompletions(harness);

            harness.Enqueue(Events(QusapComboId.Damage));
            harness.ProcessCombatCommands();

            AssertCompletedOnce(harness, completions, QusapComboId.Damage);
        }

        [Test]
        public void DisarmSequenceFromInputQueueCompletesOnce()
        {
            Harness harness = CreateHarness();
            List<QusapComboId> completions = ObserveCompletions(harness);

            harness.Enqueue(Events(QusapComboId.Disarm));
            harness.ProcessCombatCommands();

            AssertCompletedOnce(harness, completions, QusapComboId.Disarm);
        }

        [Test]
        public void LaunchSequenceFromInputQueueCompletesOnce()
        {
            Harness harness = CreateHarness();
            List<QusapComboId> completions = ObserveCompletions(harness);

            harness.Enqueue(Events(QusapComboId.Launch));
            harness.ProcessCombatCommands();

            AssertCompletedOnce(harness, completions, QusapComboId.Launch);
        }

        [Test]
        public void CommandsPreserveFifoOrderInsideController()
        {
            Harness harness = CreateHarness();
            TimedCommand[] damage = Events(QusapComboId.Damage);

            harness.Enqueue(damage);
            Assert.That(harness.Input.PendingCombatCommandCount, Is.EqualTo(damage.Length));
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.EqualTo(QusapComboId.Damage));
            Assert.That(harness.Input.PendingCombatCommandCount, Is.Zero);
        }

        [Test]
        public void ParryCommandDoesNotCompleteOrCancelOffensiveCandidate()
        {
            Harness harness = CreateHarness();
            TimedCommand[] damage = Events(QusapComboId.Damage);
            harness.Enqueue(damage[0]);
            harness.ProcessCombatCommands();
            int candidatesBeforeParry = harness.Combat.ActiveComboCandidateCount;

            harness.Enqueue(QusapCombatCommand.Parry, Midpoint(damage[0].Timestamp, damage[1].Timestamp));
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.EqualTo(candidatesBeforeParry));
            harness.Enqueue(damage, 1);
            harness.ProcessCombatCommands();
            Assert.That(harness.Combat.LastCompletedCombo, Is.EqualTo(QusapComboId.Damage));
        }

        [Test]
        public void DashResetsPartialCombo()
        {
            Harness harness = CreateHarness();
            TimedCommand[] damage = Events(QusapComboId.Damage);
            harness.Enqueue(damage, 0, 2);
            harness.ProcessCombatCommands();
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.GreaterThan(0));

            harness.SetDashing(true);
            harness.ProcessCombatCommands();
            harness.SetDashing(false);
            harness.Enqueue(damage, 2);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void HitstunResetsPartialCombo()
        {
            Harness harness = CreateHarness();
            TimedCommand[] damage = Events(QusapComboId.Damage);
            harness.Enqueue(damage, 0, 2);
            harness.ProcessCombatCommands();

            harness.Hitstun.EnterHitstun(1f);
            harness.ProcessCombatCommands();
            harness.Hitstun.ResetHitstun();
            harness.Enqueue(damage, 2);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
            Assert.That(harness.Combat.ActiveComboCandidateCount, Is.Zero);
        }

        [Test]
        public void ResetCombatStateResetsPartialCombo()
        {
            Harness harness = CreateHarness();
            TimedCommand[] damage = Events(QusapComboId.Damage);
            harness.Enqueue(damage, 0, 2);
            harness.ProcessCombatCommands();

            harness.Combat.ResetCombatState();
            harness.Enqueue(damage, 2);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void DisablingCombatResetsPartialCombo()
        {
            Harness harness = CreateHarness();
            TimedCommand[] damage = Events(QusapComboId.Damage);
            harness.Enqueue(damage, 0, 2);
            harness.ProcessCombatCommands();

            harness.Combat.CombatAllowed = false;
            harness.Combat.CombatAllowed = true;
            harness.Enqueue(damage, 2);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void CommandsBufferedBeforeDashDoNotReappearAfterDash()
        {
            Harness harness = CreateHarness();
            harness.Enqueue(Events(QusapComboId.Damage));

            harness.SetDashing(true);
            harness.ProcessCombatCommands();
            harness.SetDashing(false);
            harness.ProcessCombatCommands();

            Assert.That(harness.Input.PendingCombatCommandCount, Is.Zero);
            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void CommandsBufferedBeforeHitstunDoNotReappearAfterHitstun()
        {
            Harness harness = CreateHarness();
            harness.Enqueue(Events(QusapComboId.Damage));

            harness.Hitstun.EnterHitstun(1f);
            harness.ProcessCombatCommands();
            harness.Hitstun.ResetHitstun();
            harness.ProcessCombatCommands();

            Assert.That(harness.Input.PendingCombatCommandCount, Is.Zero);
            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void ComboCompletionDoesNotActivateHitbox()
        {
            Harness harness = CreateHarness();

            harness.Enqueue(Events(QusapComboId.Damage));
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.EqualTo(QusapComboId.Damage));
            Assert.That(harness.Hitbox.IsActive, Is.False);
        }

        [Test]
        public void ComboCompletionDoesNotAddDamage()
        {
            Harness harness = CreateHarness();
            float damageBefore = harness.Receiver.TotalDamageReceived;

            harness.Enqueue(Events(QusapComboId.Damage));
            harness.ProcessCombatCommands();

            Assert.That(harness.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
        }

        [Test]
        public void ComboCompletionDoesNotChangeRigidbodyVelocity()
        {
            Harness harness = CreateHarness();
            Vector3 velocity = new(3f, 4f, 0f);
            harness.Body.linearVelocity = velocity;

            harness.Enqueue(Events(QusapComboId.Damage));
            harness.ProcessCombatCommands();

            Assert.That(harness.Body.linearVelocity, Is.EqualTo(velocity));
        }

        [Test]
        public void CompletionEventFiresExactlyOnce()
        {
            Harness harness = CreateHarness();
            int completionCount = 0;
            harness.Combat.ComboCompleted += _ => completionCount++;

            harness.Enqueue(Events(QusapComboId.Disarm));
            harness.ProcessCombatCommands();
            harness.ProcessCombatCommands();

            Assert.That(completionCount, Is.EqualTo(1));
        }

        [Test]
        public void SecondCompletionRequiresFullNewSequence()
        {
            Harness harness = CreateHarness();
            int completionCount = 0;
            harness.Combat.ComboCompleted += _ => completionCount++;
            TimedCommand[] first = Events(QusapComboId.Damage, 10d);
            harness.Enqueue(first);
            harness.ProcessCombatCommands();

            harness.Enqueue(QusapCombatCommand.WeaponStrong, first[first.Length - 1].Timestamp + 0.1d);
            harness.ProcessCombatCommands();
            Assert.That(completionCount, Is.EqualTo(1));

            harness.Enqueue(Events(QusapComboId.Damage, 20d));
            harness.ProcessCombatCommands();
            Assert.That(completionCount, Is.EqualTo(2));
        }

        [Test]
        public void ExpiredSequenceDoesNotCompleteInsideController()
        {
            Harness harness = CreateHarness();
            QusapComboDefinition launch = Definition(QusapComboId.Launch);
            double firstTimestamp = 10d;
            double expiredTimestamp = firstTimestamp + launch.GetStep(1).MaximumDelay + 0.001d;
            double finalTimestamp = expiredTimestamp + ValidDelay(launch.GetStep(2));

            harness.Enqueue(launch.GetStep(0).Command, firstTimestamp);
            harness.Enqueue(launch.GetStep(1).Command, expiredTimestamp);
            harness.Enqueue(launch.GetStep(2).Command, finalTimestamp);
            harness.ProcessCombatCommands();

            Assert.That(harness.Combat.LastCompletedCombo, Is.Null);
        }

        [Test]
        public void ExistingSingleAttackCanStillStart()
        {
            Harness harness = CreateHarness();

            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            Assert.That(harness.Combat.IsAttacking, Is.True);
            Assert.That(harness.Combat.CurrentAttackType, Is.EqualTo(QusapAttackType.WeakKick));
        }

        private Harness CreateHarness()
        {
            Harness harness = new();
            harnesses.Add(harness);
            return harness;
        }

        private static List<QusapComboId> ObserveCompletions(Harness harness)
        {
            List<QusapComboId> completions = new();
            harness.Combat.ComboCompleted += completions.Add;
            return completions;
        }

        private static void AssertCompletedOnce(
            Harness harness,
            List<QusapComboId> completions,
            QusapComboId expected)
        {
            Assert.That(harness.Combat.LastCompletedCombo, Is.EqualTo(expected));
            Assert.That(completions, Is.EqualTo(new[] { expected }));
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
                root.SetActive(true);
                SetProperty(Ground, "IsGrounded", false);
            }

            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapDashMotor Dash { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }

            public void Enqueue(QusapCombatCommand command, double timestamp)
            {
                Input.EnqueueCombatCommand(command, timestamp);
            }

            public void Enqueue(TimedCommand command)
            {
                Enqueue(command.Command, command.Timestamp);
            }

            public void Enqueue(TimedCommand[] commands, int startIndex = 0, int count = -1)
            {
                int endIndex = count < 0 ? commands.Length : startIndex + count;
                for (int i = startIndex; i < endIndex; i++)
                {
                    Enqueue(commands[i]);
                }
            }

            public void ProcessCombatCommands()
            {
                Invoke(Combat, "FixedUpdate");
            }

            public void SetDashing(bool value)
            {
                SetProperty(Dash, "IsDashing", value);
            }

            public void Dispose()
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (inputAsset != null)
                {
                    UnityEngine.Object.DestroyImmediate(inputAsset);
                }
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
