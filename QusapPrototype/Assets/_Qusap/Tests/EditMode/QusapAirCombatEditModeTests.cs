using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Qusap.Tests
{
    public sealed class QusapAirCombatEditModeTests
    {
        private const string PlayerPrefabPath = "Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        private const string ControlsPath = "Assets/_Qusap/Settings/QusapControls.inputactions";

        [Test]
        public void ExistingThreeInputActionsAndBindingsRemainPresent()
        {
            InputActionAsset controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);
            Assert.That(controls, Is.Not.Null);

            foreach (string actionName in new[] { "WeakKick", "StrongKick", "Headbutt" })
            {
                InputAction action = controls.FindAction($"Gameplay/{actionName}");
                Assert.That(action, Is.Not.Null, actionName);
                Assert.That(action.bindings.Count, Is.EqualTo(2), actionName);
            }
        }

        [Test]
        public void GroundAttackPrefabValuesRemainExactlyAtBaseline()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null);
            QusapCombatController combat = prefab.GetComponent<QusapCombatController>();
            Assert.That(combat, Is.Not.Null);
            SerializedObject serialized = new(combat);

            AssertGround(serialized.FindProperty("weakKick"),
                0.08f, 0.08f, 0.14f, new Vector2(1f, 0.6f), new Vector2(0.75f, -0.35f),
                1f, 4f, 1f, 0.12f, false);
            AssertGround(serialized.FindProperty("strongKick"),
                0.18f, 0.1f, 0.32f, new Vector2(1.35f, 0.75f), new Vector2(0.9f, -0.25f),
                1f, 7f, 3f, 0.24f, true);
            AssertGround(serialized.FindProperty("headbutt"),
                0.35f, 0.08f, 0.5f, new Vector2(1.2f, 0.8f), new Vector2(0.8f, 0.45f),
                1f, 16.5f, 4f, 0.4f, true);
        }

        [Test]
        public void AirDefinitionsHaveIndependentConservativeValues()
        {
            QusapAirAttackData weak = QusapAirAttackData.CreateWeakKickAir();
            QusapAirAttackData strong = QusapAirAttackData.CreateStrongKickAir();
            QusapAirAttackData dive = QusapAirAttackData.CreateDiveHeadbuttAir();

            Assert.That(weak, Is.Not.SameAs(strong));
            Assert.That(strong, Is.Not.SameAs(dive));
            Assert.That(weak.Damage, Is.LessThan(strong.Damage));
            Assert.That(strong.Damage, Is.LessThan(dive.Damage));
            Assert.That(weak.StartupTime, Is.LessThan(strong.StartupTime));
            Assert.That(weak.RecoveryTime, Is.LessThan(strong.RecoveryTime));
            Assert.That(strong.VerticalKnockback, Is.LessThan(0f));
            Assert.That(dive.VerticalKnockback, Is.LessThan(strong.VerticalKnockback));
            Assert.That(dive.ActiveDuration, Is.GreaterThan(0f));
            Assert.That(dive.DiveDownwardSpeed, Is.GreaterThan(0f));
            Assert.That(dive.BlockDash, Is.True);
        }

        private static void AssertGround(
            SerializedProperty attack,
            float startup,
            float active,
            float recovery,
            Vector2 size,
            Vector2 offset,
            float depth,
            float horizontalKnockback,
            float verticalKnockback,
            float hitstun,
            bool lockMovement)
        {
            Assert.That(attack, Is.Not.Null);
            Assert.That(attack.FindPropertyRelative("startupTime").floatValue, Is.EqualTo(startup));
            Assert.That(attack.FindPropertyRelative("activeDuration").floatValue, Is.EqualTo(active));
            Assert.That(attack.FindPropertyRelative("recoveryTime").floatValue, Is.EqualTo(recovery));
            Assert.That(attack.FindPropertyRelative("hitboxSize").vector2Value, Is.EqualTo(size));
            Assert.That(attack.FindPropertyRelative("hitboxOffset").vector2Value, Is.EqualTo(offset));
            Assert.That(attack.FindPropertyRelative("hitboxDepth").floatValue, Is.EqualTo(depth));
            Assert.That(attack.FindPropertyRelative("horizontalKnockback").floatValue, Is.EqualTo(horizontalKnockback));
            Assert.That(attack.FindPropertyRelative("verticalKnockback").floatValue, Is.EqualTo(verticalKnockback));
            Assert.That(attack.FindPropertyRelative("hitstunDuration").floatValue, Is.EqualTo(hitstun));
            Assert.That(attack.FindPropertyRelative("lockHorizontalMovement").boolValue, Is.EqualTo(lockMovement));
        }
    }
}
