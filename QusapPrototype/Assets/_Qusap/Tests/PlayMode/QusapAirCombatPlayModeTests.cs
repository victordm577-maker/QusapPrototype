using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapAirCombatPlayModeTests
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

        [TestCase(QusapAttackType.WeakKick, true, QusapAttackVariant.WeakKickGround)]
        [TestCase(QusapAttackType.WeakKick, false, QusapAttackVariant.WeakKickAir)]
        [TestCase(QusapAttackType.StrongKick, true, QusapAttackVariant.StrongKickGround)]
        [TestCase(QusapAttackType.StrongKick, false, QusapAttackVariant.StrongKickAir)]
        [TestCase(QusapAttackType.Headbutt, true, QusapAttackVariant.HeadbuttGround)]
        [TestCase(QusapAttackType.Headbutt, false, QusapAttackVariant.DiveHeadbuttAir)]
        public void SameThreeInputsSelectGroundOrAirVariant(
            QusapAttackType input,
            bool grounded,
            QusapAttackVariant expected)
        {
            Harness harness = CreateHarness();
            harness.SetGrounded(grounded);

            Assert.That(harness.Combat.TryStartAttack(input), Is.True);
            Assert.That(harness.Combat.CurrentAttackVariant, Is.EqualTo(expected));
        }

        [Test]
        public void AttackCannotBeginDuringDash()
        {
            Harness harness = CreateHarness();
            harness.SetDashing(true);

            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.False);
            Assert.That(harness.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.None));
        }

        [Test]
        public void WeakKickAirDoesNotChangeVelocity()
        {
            Harness harness = CreateHarness();
            Vector3 expected = new(6f, 4f, 0f);
            harness.Body.linearVelocity = expected;

            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            Assert.That(harness.Body.linearVelocity, Is.EqualTo(expected));
        }

        [Test]
        public void StrongKickAirRetainsNinetyTwoPercentOfHorizontalVelocity()
        {
            Harness harness = CreateHarness();
            harness.Body.linearVelocity = new Vector3(10f, 4f, 0f);

            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.StrongKick), Is.True);
            Assert.That(harness.Body.linearVelocity.x, Is.EqualTo(9.2f).Within(0.001f));
            Assert.That(harness.Body.linearVelocity.y, Is.EqualTo(4f).Within(0.001f));
        }

        [Test]
        public void DiveAppliesConfiguredDownwardVelocityAndBlocksDash()
        {
            Harness harness = CreateHarness();
            harness.Body.linearVelocity = new Vector3(3f, 8f, 0f);
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.True);

            QusapAirAttackData data = harness.Combat.GetAirAttackData(QusapAttackVariant.DiveHeadbuttAir);
            harness.Advance(data.StartupTime);
            harness.Advance(data.DiveBrakeDuration);

            Assert.That(harness.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Active));
            Assert.That(harness.Body.linearVelocity.y, Is.EqualTo(-data.DiveDownwardSpeed).Within(0.001f));
            Assert.That(harness.Combat.BlocksDash, Is.True);
        }

        [Test]
        public void DiveIsOncePerAirtimeAndOnlyGroundReloadsIt()
        {
            Harness harness = CreateHarness();
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.True);
            harness.Combat.CancelAttack();

            // Staying airborne includes wall contact and wall jump: neither touches Grounded.
            Assert.That(harness.Combat.DiveHeadbuttAvailable, Is.False);
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.False);

            harness.SetGrounded(true);
            harness.RunCombatFixedUpdate();
            Assert.That(harness.Combat.DiveHeadbuttAvailable, Is.True);

            harness.SetGrounded(false);
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.True);
        }

        [Test]
        public void LandingEndsDiveHitboxWithoutChangingSelectedVariant()
        {
            Harness harness = CreateHarness();
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.True);
            QusapAirAttackData data = harness.Combat.GetAirAttackData(QusapAttackVariant.DiveHeadbuttAir);
            harness.Advance(data.StartupTime);
            harness.Advance(data.DiveBrakeDuration);
            Assert.That(harness.Hitbox.IsActive, Is.True);

            harness.SetGrounded(true);
            harness.RunCombatFixedUpdate();

            Assert.That(harness.Hitbox.IsActive, Is.False);
            Assert.That(harness.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Recovery));
            Assert.That(harness.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.DiveHeadbuttAir));
            Assert.That(harness.Body.linearVelocity.y, Is.GreaterThanOrEqualTo(0f));
        }

        [Test]
        public void DiveHitBouncesOnceAndCannotRepeatDuringRecovery()
        {
            Harness attacker = CreateHarness();
            Harness target = CreateHarness();
            Assert.That(attacker.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.True);
            QusapAirAttackData data = attacker.Combat.GetAirAttackData(QusapAttackVariant.DiveHeadbuttAir);
            attacker.Advance(data.StartupTime);
            attacker.Advance(data.DiveBrakeDuration);

            attacker.Combat.NotifyAttackHit(target.Receiver);
            float firstBounce = attacker.Body.linearVelocity.y;
            attacker.Combat.NotifyAttackHit(target.Receiver);

            Assert.That(firstBounce, Is.EqualTo(data.DiveBounceSpeed).Within(0.001f));
            Assert.That(attacker.Body.linearVelocity.y, Is.EqualTo(firstBounce).Within(0.001f));
            Assert.That(attacker.Hitbox.IsActive, Is.False);
            Assert.That(attacker.Combat.CurrentPhase, Is.EqualTo(QusapAttackPhase.Recovery));
        }

        [Test]
        public void DiveHitAppliesDownwardKnockbackAndDamageToTarget()
        {
            Harness attacker = CreateHarness();
            Harness target = CreateHarness();
            QusapAirAttackData data = attacker.Combat.GetAirAttackData(QusapAttackVariant.DiveHeadbuttAir);
            target.Body.linearVelocity = new Vector3(0f, 3f, 0f);
            QusapHitInfo hit = new(
                attacker.Combat,
                QusapAttackType.Headbutt,
                QusapAttackVariant.DiveHeadbuttAir,
                data.Damage,
                1,
                data.HorizontalKnockback,
                data.VerticalKnockback,
                data.HitstunDuration,
                Vector3.zero);

            Assert.That(target.Receiver.TryReceiveHit(hit), Is.True);
            Assert.That(target.Body.linearVelocity.y, Is.EqualTo(data.VerticalKnockback).Within(0.001f));
            Assert.That(target.Receiver.TotalDamageReceived, Is.EqualTo(data.Damage).Within(0.001f));
        }

        [Test]
        public void HitstunAndRespawnCancelAirAttackAndRespawnReloadsDive()
        {
            Harness harness = CreateHarness();
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            harness.Hitstun.EnterHitstun(0.2f);
            Assert.That(harness.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.None));
            Assert.That(harness.Hitbox.IsActive, Is.False);

            harness.Hitstun.ResetHitstun();
            Assert.That(harness.Combat.TryStartAttack(QusapAttackType.Headbutt), Is.True);
            Assert.That(harness.Combat.DiveHeadbuttAvailable, Is.False);
            harness.Respawn.Respawn();

            Assert.That(harness.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.None));
            Assert.That(harness.Combat.DiveHeadbuttAvailable, Is.True);
            Assert.That(harness.Hitbox.IsActive, Is.False);
        }

        private Harness CreateHarness()
        {
            Harness harness = new();
            harnesses.Add(harness);
            return harness;
        }

        private sealed class Harness : IDisposable
        {
            private readonly InputActionAsset inputAsset;
            private readonly GameObject root;

            public Harness()
            {
                root = new GameObject("AirCombatTestPlayer");
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

                QusapInputReader input = root.AddComponent<QusapInputReader>();
                SetField(input, "inputActionAsset", inputAsset);
                Ground = root.AddComponent<QusapGroundSensor>();
                root.AddComponent<QusapWallSensor>();
                root.AddComponent<QusapHorizontalMotor>();
                root.AddComponent<QusapVerticalMotor>();
                Dash = root.AddComponent<QusapDashMotor>();
                Receiver = root.AddComponent<QusapHitReceiver>();
                Hitstun = root.AddComponent<QusapHitstunController>();
                Respawn = root.AddComponent<QusapRespawnController>();

                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(root.transform, false);
                Hitbox = hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = root.AddComponent<QusapCombatController>();
                SetField(Combat, "attackHitbox", Hitbox);
                root.SetActive(true);
                SetGrounded(false);
            }

            public Rigidbody Body { get; }
            public QusapGroundSensor Ground { get; }
            public QusapDashMotor Dash { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapRespawnController Respawn { get; }
            public QusapAttackHitbox Hitbox { get; }
            public QusapCombatController Combat { get; }

            public void SetGrounded(bool value)
            {
                SetProperty(Ground, "IsGrounded", value);
            }

            public void SetDashing(bool value)
            {
                SetProperty(Dash, "IsDashing", value);
            }

            public void Advance(float seconds)
            {
                Invoke(Combat, "AdvanceAttack", seconds);
            }

            public void RunCombatFixedUpdate()
            {
                Invoke(Combat, "FixedUpdate");
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
