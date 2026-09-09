using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Qusap.Tests
{
    public sealed class QusapParryCuePlayModeTests
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
        public void CueIsHiddenWithoutIncomingFinisher()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            defender.RefreshCue(InputState.currentTime);
            AssertHidden(defender.Presenter);
        }

        [Test]
        public void CueIsHiddenDuringTelegraph()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueAppearsWhenParryWindowOpens()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.Attacker.Combat.ParryWindowOpensAt);
            AssertVisibleWindow(pair.Defender.Presenter);
        }

        [Test]
        public void CueBelongsToDefenderNotAttacker()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Attacker.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Presenter.CueRenderer.transform.IsChildOf(pair.Defender.Root.transform), Is.True);
            AssertVisibleWindow(pair.Defender.Presenter);
            AssertHidden(pair.Attacker.Presenter);
        }

        [Test]
        public void CueDisappearsWhenWindowCloses()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Defender.RefreshCue(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueDisappearsWhenFinisherBecomesReady()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowClosesAt + 0.001d);
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueDisappearsWhenFinisherIsCancelled()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Attacker.Combat.CombatAllowed = false;
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueDisappearsAfterSuccessfulParry()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.SuccessfulParry();
            Assert.That(pair.Defender.Presenter.IsShowingWindow, Is.False);
            Assert.That(pair.Defender.Combat.TryGetCurrentParryCue(pair.WindowMidpoint, out _), Is.False);
        }

        [Test]
        public void SuccessfulParryShowsGreenFlashOnce()
        {
            Pair pair = CreateArmedPair();
            pair.SuccessfulParry();
            pair.Defender.ProcessFixed();
            Assert.That(pair.Defender.Presenter.IsShowingSuccessFlash, Is.True);
            Assert.That(pair.Defender.Presenter.SuccessFlashCount, Is.EqualTo(1));
            AssertColorsEqual(
                pair.Defender.Combat.ParryCueVisualSettings.SuccessColor,
                pair.Defender.Presenter.CueRenderer.color);
        }

        [Test]
        public void SuccessFlashDisappearsAfterConfiguredDuration()
        {
            Pair pair = CreateArmedPair();
            pair.SuccessfulParry();
            double afterFlash = Time.unscaledTimeAsDouble
                + pair.Defender.Combat.ParryCueVisualSettings.SuccessFlashDuration
                + 0.01d;
            pair.Defender.Presenter.Refresh(InputState.currentTime, afterFlash);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueIsRemovedOrHiddenOnDisable()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Defender.Combat.enabled = false;
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueIsHiddenAfterRespawn()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Defender.Respawn.Respawn();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void CueCreatesNoCollider()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Presenter.CueRenderer.GetComponent<Collider>(), Is.Null);
            Assert.That(pair.Defender.Presenter.CueRenderer.GetComponent<Collider2D>(), Is.Null);
        }

        [Test]
        public void CueCreatesNoRigidbody()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Presenter.CueRenderer.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(pair.Defender.Presenter.CueRenderer.GetComponent<Rigidbody2D>(), Is.Null);
        }

        [Test]
        public void CueDoesNotChangePlayerVelocity()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Body.linearVelocity = new Vector3(3f, 2f, 0f);
            Vector3 before = pair.Defender.Body.linearVelocity;
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Body.linearVelocity, Is.EqualTo(before));
        }

        [Test]
        public void CueDoesNotChangePlayerPosition()
        {
            Pair pair = CreateArmedPair();
            Vector3 before = pair.Defender.Root.transform.position;
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Root.transform.position, Is.EqualTo(before));
        }

        [Test]
        public void CueDoesNotChangeParryTiming()
        {
            Pair pair = CreateArmedPair();
            double opensAt = pair.Attacker.Combat.ParryWindowOpensAt;
            double closesAt = pair.Attacker.Combat.ParryWindowClosesAt;
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Attacker.Combat.ParryWindowOpensAt, Is.EqualTo(opensAt));
            Assert.That(pair.Attacker.Combat.ParryWindowClosesAt, Is.EqualTo(closesAt));
        }

        [Test]
        public void CueDoesNotConsumeParryCommand()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Parry(pair.Attacker.Combat.ParryWindowOpensAt - 0.001d);
            int pendingBefore = pair.Defender.Input.PendingCombatCommandCount;
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Input.PendingCombatCommandCount, Is.EqualTo(pendingBefore));
        }

        [Test]
        public void CueDoesNotResolveFinisher()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.True);
            Assert.That(pair.Attacker.Combat.LastFinisherResolution, Is.Null);
        }

        [Test]
        public void OnlyOneCueExistsPerDefender()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            Assert.That(pair.Defender.Root.GetComponents<QusapParryCuePresenter>(), Has.Length.EqualTo(1));
            Assert.That(CountCueRenderers(pair.Defender), Is.EqualTo(1));
        }

        [Test]
        public void RepeatedWindowsReuseSameRenderer()
        {
            Pair pair = CreateArmedPair();
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            AssertVisibleWindow(pair.Defender.Presenter);
            SpriteRenderer first = pair.Defender.Presenter.CueRenderer;
            ulong firstEntityId = EntityId.ToULong(first.GetEntityId());

            pair.Attacker.Combat.ResetCombatState();
            pair.Defender.RefreshCue(pair.WindowMidpoint);

            AssertHidden(pair.Defender.Presenter);
            Assert.That(pair.Attacker.Hitstun.IsInHitstun, Is.False);
            Assert.That(pair.Defender.Hitstun.IsInHitstun, Is.False);
            Assert.That(pair.Attacker.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Idle));
            Assert.That(pair.Attacker.Combat.IsAttacking, Is.False);
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Defender.Combat.IncomingFinisherCount, Is.Zero);
            Assert.That(pair.Attacker.Input.PendingCombatCommandCount, Is.Zero);
            Assert.That(pair.Defender.Combat.CombatAllowed, Is.True);
            Assert.That(pair.Defender.Receiver.isActiveAndEnabled, Is.True);
            Assert.That(pair.Defender.Root.activeInHierarchy, Is.True);

            pair.Attacker.ArmLaunchAgainst(pair.Defender);
            pair.Attacker.AdvanceFinisher(pair.Attacker.Combat.ParryWindowOpensAt);
            pair.Defender.RefreshCue(pair.WindowMidpoint);

            AssertVisibleWindow(pair.Defender.Presenter);
            Assert.That(pair.Defender.Presenter.CueRenderer, Is.SameAs(first));
            Assert.That(
                EntityId.ToULong(pair.Defender.Presenter.CueRenderer.GetEntityId()),
                Is.EqualTo(firstEntityId));
            Assert.That(CountCueRenderers(pair.Defender), Is.EqualTo(1));
        }

        [Test]
        public void MultipleAttackersStillShowOneCue()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            PlayerHarness first = CreatePlayer("First");
            PlayerHarness second = CreatePlayer("Second");
            first.ArmLaunchAgainst(defender);
            second.ArmLaunchAgainst(defender);
            double timestamp = Math.Max(first.Combat.ParryWindowOpensAt, second.Combat.ParryWindowOpensAt) + 0.01d;
            defender.RefreshCue(timestamp);
            AssertVisibleWindow(defender.Presenter);
            Assert.That(CountCueRenderers(defender), Is.EqualTo(1));
        }

        [Test]
        public void CueRepresentsEarliestClosingWindow()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            PlayerHarness early = CreatePlayer("Early");
            PlayerHarness late = CreatePlayer("Late");
            early.ConfigureParrySettings(0.1d, 0.2d);
            late.ConfigureParrySettings(0.1d, 0.5d);
            early.ArmLaunchAgainst(defender);
            late.ArmLaunchAgainst(defender);
            double timestamp = Math.Max(early.Combat.ParryWindowOpensAt, late.Combat.ParryWindowOpensAt) + 0.01d;
            defender.RefreshCue(timestamp);
            Assert.That(defender.Presenter.CurrentCue.Attacker, Is.SameAs(early.Combat));
        }

        [Test]
        public void EntityIdStillBreaksEqualClosingTimeTie()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            PlayerHarness first = CreatePlayer("First");
            PlayerHarness second = CreatePlayer("Second");
            first.ArmLaunchAgainst(defender);
            second.ArmLaunchAgainst(defender);
            const double armedAt = 100d;
            first.RearmTimeline(armedAt);
            second.RearmTimeline(armedAt);
            double timestamp = first.Combat.ParryWindowOpensAt + 0.01d;
            defender.RefreshCue(timestamp);
            QusapCombatController expected = EntityId.ToULong(first.Combat.GetEntityId())
                < EntityId.ToULong(second.Combat.GetEntityId())
                    ? first.Combat
                    : second.Combat;
            Assert.That(defender.Presenter.CurrentCue.Attacker, Is.SameAs(expected));
        }

        [Test]
        public void RemovingFirstThreatDisplaysNextThreat()
        {
            PlayerHarness defender = CreatePlayer("Defender");
            PlayerHarness early = CreatePlayer("Early");
            PlayerHarness late = CreatePlayer("Late");
            early.ConfigureParrySettings(0.1d, 0.2d);
            late.ConfigureParrySettings(0.1d, 0.5d);
            early.ArmLaunchAgainst(defender);
            late.ArmLaunchAgainst(defender);
            double timestamp = Math.Max(early.Combat.ParryWindowOpensAt, late.Combat.ParryWindowOpensAt) + 0.01d;
            defender.RefreshCue(timestamp);
            Assert.That(defender.Presenter.CurrentCue.Attacker, Is.SameAs(early.Combat));
            early.Combat.CombatAllowed = false;
            defender.RefreshCue(timestamp);
            Assert.That(defender.Presenter.CurrentCue.Attacker, Is.SameAs(late.Combat));
        }

        [Test]
        public void CueProgressUsesInputSystemTimeDomain()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.Presenter.Refresh(pair.WindowMidpoint, visualTimestamp: 123456d);
            Assert.That(pair.Defender.Presenter.CurrentCue.WindowProgressNormalized, Is.EqualTo(0.5d).Within(0.0001d));
            Assert.That(
                pair.Defender.Presenter.CurrentCue.TimeRemaining,
                Is.EqualTo((pair.Attacker.Combat.ParryWindowClosesAt
                    - pair.Attacker.Combat.ParryWindowOpensAt) / 2d).Within(0.0001d));
        }

        [Test]
        public void DisabledVisualSettingsKeepCueHidden()
        {
            Pair pair = CreateArmedPair();
            pair.Defender.ConfigureVisualSettings(enabled: false);
            pair.Defender.RefreshCue(pair.WindowMidpoint);
            AssertHidden(pair.Defender.Presenter);
        }

        [Test]
        public void ExistingParryAndFinisherSuitesStillPass()
        {
            Assert.That(QusapFinisherParrySettings.DefaultTelegraphDelay, Is.EqualTo(0.10d));
            Assert.That(QusapFinisherParrySettings.DefaultParryWindowDuration, Is.EqualTo(0.35d));
            Assert.That(
                QusapFinisherParrySettings.DefaultSuccessfulParryAttackerVulnerabilityDuration,
                Is.EqualTo(0.60d));
            Assert.That(
                QusapFinisherParrySettings.DefaultFailedParryDefenderVulnerabilityDuration,
                Is.EqualTo(0.30d));

            Pair pair = CreateArmedPair();
            pair.SuccessfulParry();
            Assert.That(pair.Attacker.Combat.HasArmedFinisher, Is.False);
            Assert.That(pair.Attacker.Combat.FinisherDefensePhase, Is.EqualTo(QusapFinisherDefensePhase.Parried));
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

        private static int CountCueRenderers(PlayerHarness player)
        {
            int count = 0;
            SpriteRenderer[] renderers = player.Root.GetComponentsInChildren<SpriteRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].gameObject.name == QusapParryCuePresenter.VisualObjectName)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertVisibleWindow(QusapParryCuePresenter presenter)
        {
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.IsShowingWindow, Is.True);
            Assert.That(presenter.CueRenderer, Is.Not.Null);
            Assert.That(presenter.CueRenderer.enabled, Is.True);
            Assert.That(presenter.CueRenderer.sprite, Is.Not.Null);
        }

        private static void AssertHidden(QusapParryCuePresenter presenter)
        {
            Assert.That(presenter, Is.Not.Null);
            Assert.That(presenter.IsShowingWindow, Is.False);
            Assert.That(presenter.IsShowingSuccessFlash, Is.False);
            Assert.That(presenter.CueRenderer, Is.Not.Null);
            Assert.That(presenter.CueRenderer.enabled, Is.False);
        }

        private static void AssertColorsEqual(Color expected, Color actual)
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

            public void SuccessfulParry()
            {
                Defender.Parry(WindowMidpoint);
                Defender.ProcessFixed();
            }
        }

        private sealed class PlayerHarness : IDisposable
        {
            private static int nextNameId;
            private readonly InputActionAsset inputAsset;
            // Resetting combo candidates intentionally preserves monotonic input history.
            private double nextComboTimestamp = 10d;

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
                Root.AddComponent<QusapDashMotor>();
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
                Presenter = Combat.ParryCuePresenter;
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapGroundSensor Ground { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapRespawnController Respawn { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }
            public QusapParryCuePresenter Presenter { get; }

            public void ArmLaunchAgainst(PlayerHarness defender)
            {
                QusapComboDefinition definition = FindDefinition(QusapComboId.Launch);
                double timestamp = nextComboTimestamp;
                for (int i = 0; i < definition.StepCount - 1; i++)
                {
                    if (i > 0)
                    {
                        timestamp += ValidDelay(definition.GetStep(i));
                    }

                    ConfirmStep(definition.GetStep(i).Command, timestamp, defender);
                }

                timestamp += ValidDelay(definition.GetStep(definition.StepCount - 1));
                nextComboTimestamp = timestamp;
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
                QusapHitInfo hit = new(
                    Combat, attack.AttackType, Combat.CurrentAttackVariant, attack.Damage,
                    Combat.AttackDirection, attack.HorizontalKnockback, attack.VerticalKnockback,
                    attack.HitstunDuration, Vector3.zero);
                Assert.That(defender.Receiver.TryReceiveHit(hit), Is.True);
                Combat.NotifyAttackHit(defender.Receiver);
                defender.Hitstun.ResetHitstun();
                Invoke(Combat, "AdvanceAttack", 10f);
            }

            public void ConfigureParrySettings(double telegraph, double window)
            {
                SetField(
                    Combat,
                    "finisherParrySettings",
                    new QusapFinisherParrySettings(telegraph, window, 0.6d, 0.3d));
            }

            public void ConfigureVisualSettings(bool enabled)
            {
                QusapParryCueVisualSettings settings = new(
                    enabled,
                    QusapParryCueVisualSettings.DefaultLocalOffset,
                    QusapParryCueVisualSettings.DefaultMinimumSize,
                    QusapParryCueVisualSettings.DefaultMaximumSize,
                    QusapParryCueVisualSettings.DefaultPulseFrequency,
                    QusapParryCueVisualSettings.DefaultWindowColor,
                    QusapParryCueVisualSettings.DefaultSuccessColor,
                    QusapParryCueVisualSettings.DefaultSuccessFlashDuration,
                    QusapParryCueVisualSettings.DefaultSortingOrder);
                SetField(Combat, "parryCueVisualSettings", settings);
                Presenter.Configure(settings);
            }

            public void RearmTimeline(double armedAt)
            {
                QusapFinisherParryStateMachine stateMachine =
                    GetField<QusapFinisherParryStateMachine>(Combat, "finisherDefense");
                QusapFinisherParrySettings settings =
                    GetField<QusapFinisherParrySettings>(Combat, "finisherParrySettings");
                stateMachine.Arm(QusapComboId.Launch, armedAt, settings);
            }

            public void RefreshCue(double inputTimestamp)
            {
                Presenter.Refresh(inputTimestamp, Time.unscaledTimeAsDouble);
            }

            public QusapCombatCommandPress Parry(double timestamp)
            {
                return Input.EnqueueCombatCommand(QusapCombatCommand.Parry, timestamp);
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
