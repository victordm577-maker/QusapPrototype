using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapCombatInputEditModeTests
    {
        private const string ControlsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";

        [Test]
        public void ApprovedXboxBindingsAreConfigured()
        {
            InputActionAsset controls = LoadControls();
            AssertBinding(controls, "Jump", "<Gamepad>/buttonSouth", "Player2");
            AssertBinding(controls, "WeakKick", "<Gamepad>/buttonWest", "Player2");
            AssertBinding(controls, "StrongKick", "<Gamepad>/buttonNorth", "Player2");
            AssertBinding(controls, "WeaponStrong", "<Gamepad>/buttonEast", "Player2");
            AssertBinding(controls, "Headbutt", "<Gamepad>/leftShoulder", "Player2");
            AssertBinding(controls, "Parry", "<Gamepad>/leftTrigger", "Player2");
            AssertBinding(controls, "Dash", "<Gamepad>/rightShoulder", "Player2");
        }

        [Test]
        public void ButtonEastIsNotBoundToHeadbutt()
        {
            InputAction headbutt = LoadControls().FindAction("Gameplay/Headbutt");

            Assert.That(headbutt, Is.Not.Null);
            Assert.That(HasBinding(headbutt, "<Gamepad>/buttonEast", "Player2"), Is.False);
        }

        [Test]
        public void LegacyKeyboardBindingsRemainPresent()
        {
            InputActionAsset controls = LoadControls();
            AssertBinding(controls, "WeakKick", "<Keyboard>/j", "Player1");
            AssertBinding(controls, "StrongKick", "<Keyboard>/k", "Player1");
            AssertBinding(controls, "Headbutt", "<Keyboard>/l", "Player1");
        }

        [Test]
        public void PlayerControlSchemesRemainPresent()
        {
            InputActionAsset controls = LoadControls();

            AssertControlScheme(controls, "Player1", "<Keyboard>");
            AssertControlScheme(controls, "Player2", "<Gamepad>");
        }

        [Test]
        public void BufferGeneratesStrictlyIncreasingPressIds()
        {
            QusapCombatCommandBuffer buffer = new();

            ulong first = buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d).PressId;
            ulong second = buffer.Enqueue(QusapCombatCommand.BodyAttack, 2d).PressId;
            ulong third = buffer.Enqueue(QusapCombatCommand.BodyAttack, 3d).PressId;

            Assert.That(second, Is.GreaterThan(first));
            Assert.That(third, Is.GreaterThan(second));
        }

        [Test]
        public void BufferPreservesFifoOrder()
        {
            QusapCombatCommandBuffer buffer = new();
            buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d);
            buffer.Enqueue(QusapCombatCommand.WeaponLight, 2d);
            buffer.Enqueue(QusapCombatCommand.Headbutt, 3d);

            AssertDequeue(buffer, QusapCombatCommand.BodyAttack, 1d);
            AssertDequeue(buffer, QusapCombatCommand.WeaponLight, 2d);
            AssertDequeue(buffer, QusapCombatCommand.Headbutt, 3d);
        }

        [Test]
        public void ClearRemovesPendingCommands()
        {
            QusapCombatCommandBuffer buffer = new();
            buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d);

            buffer.Clear();

            Assert.That(buffer.PendingCount, Is.Zero);
            Assert.That(buffer.TryDequeue(out _), Is.False);
        }

        [Test]
        public void ClearDoesNotResetPressIdSequence()
        {
            QusapCombatCommandBuffer buffer = new();
            ulong beforeClear = buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d).PressId;
            buffer.Clear();

            ulong afterClear = buffer.Enqueue(QusapCombatCommand.BodyAttack, 2d).PressId;

            Assert.That(afterClear, Is.GreaterThan(beforeClear));
        }

        [Test]
        public void CapacityDropsOldestAndKeepsNewest()
        {
            QusapCombatCommandBuffer buffer = new(2);
            buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d);
            buffer.Enqueue(QusapCombatCommand.WeaponLight, 2d);
            buffer.Enqueue(QusapCombatCommand.WeaponStrong, 3d);

            Assert.That(buffer.PendingCount, Is.EqualTo(2));
            AssertDequeue(buffer, QusapCombatCommand.WeaponLight, 2d);
            AssertDequeue(buffer, QusapCombatCommand.WeaponStrong, 3d);
        }

        [Test]
        public void CapacityIsNeverLowerThanOne()
        {
            QusapCombatCommandBuffer buffer = new(0);

            Assert.That(buffer.Capacity, Is.EqualTo(1));
            buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d);
            buffer.Enqueue(QusapCombatCommand.WeaponLight, 2d);
            Assert.That(buffer.PendingCount, Is.EqualTo(1));
        }

        [Test]
        public void NonFiniteTimestampIsRejected()
        {
            QusapCombatCommandBuffer buffer = new();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                buffer.Enqueue(QusapCombatCommand.BodyAttack, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                buffer.Enqueue(QusapCombatCommand.BodyAttack, double.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                buffer.Enqueue(QusapCombatCommand.BodyAttack, double.NegativeInfinity));
            Assert.That(buffer.PendingCount, Is.Zero);
        }

        [Test]
        public void DifferentCommandsReceiveDifferentPressIds()
        {
            QusapCombatCommandBuffer buffer = new();
            QusapCombatCommandPress first = buffer.Enqueue(QusapCombatCommand.BodyAttack, 1d);
            QusapCombatCommandPress second = buffer.Enqueue(QusapCombatCommand.Parry, 2d);

            Assert.That(second.PressId, Is.Not.EqualTo(first.PressId));
        }

        [Test]
        public void BufferedCommandsCanCompleteDamageCombo()
        {
            IReadOnlyList<QusapComboDefinition> definitions =
                QusapComboDefinition.CreateDefaultDefinitions();
            QusapComboDefinition damage = FindDefinition(definitions, QusapComboId.Damage);
            QusapCombatCommandBuffer buffer = new();
            QusapComboSequenceMatcher matcher = new(definitions);
            double timestamp = 10d;

            for (int i = 0; i < damage.StepCount; i++)
            {
                if (i > 0)
                {
                    QusapComboStep step = damage.GetStep(i);
                    timestamp += (step.MinimumDelay + step.MaximumDelay) / 2d;
                }

                buffer.Enqueue(damage.GetStep(i).Command, timestamp);
            }

            QusapComboMatchResult result = default;
            while (buffer.TryDequeue(out QusapCombatCommandPress press))
            {
                result = matcher.ProcessPress(press.Command, press.PressId, press.Timestamp);
            }

            Assert.That(result.Completed, Is.True);
            Assert.That(result.CompletedComboId, Is.EqualTo(QusapComboId.Damage));
        }

        private static InputActionAsset LoadControls()
        {
            InputActionAsset controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);
            Assert.That(controls, Is.Not.Null);
            return controls;
        }

        private static void AssertBinding(
            InputActionAsset controls,
            string actionName,
            string path,
            string group)
        {
            InputAction action = controls.FindAction($"Gameplay/{actionName}");
            Assert.That(action, Is.Not.Null, actionName);
            Assert.That(HasBinding(action, path, group), Is.True, $"{actionName}: {path} [{group}]");
            Assert.That(action.type, Is.EqualTo(InputActionType.Button));
        }

        private static bool HasBinding(InputAction action, string path, string group)
        {
            foreach (InputBinding binding in action.bindings)
            {
                if (binding.path == path && binding.groups == group)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertControlScheme(
            InputActionAsset controls,
            string schemeName,
            string devicePath)
        {
            InputControlScheme? scheme = controls.FindControlScheme(schemeName);
            Assert.That(scheme.HasValue, Is.True, schemeName);
            Assert.That(scheme.Value.bindingGroup, Is.EqualTo(schemeName));
            Assert.That(scheme.Value.deviceRequirements.Count, Is.EqualTo(1));
            Assert.That(scheme.Value.deviceRequirements[0].controlPath, Is.EqualTo(devicePath));
        }

        private static void AssertDequeue(
            QusapCombatCommandBuffer buffer,
            QusapCombatCommand expectedCommand,
            double expectedTimestamp)
        {
            Assert.That(buffer.TryDequeue(out QusapCombatCommandPress press), Is.True);
            Assert.That(press.Command, Is.EqualTo(expectedCommand));
            Assert.That(press.Timestamp, Is.EqualTo(expectedTimestamp));
        }

        private static QusapComboDefinition FindDefinition(
            IReadOnlyList<QusapComboDefinition> definitions,
            QusapComboId comboId)
        {
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
    }
}
