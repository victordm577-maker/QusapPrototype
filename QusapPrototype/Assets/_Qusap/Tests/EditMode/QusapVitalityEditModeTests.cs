using NUnit.Framework;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapVitalityEditModeTests
    {
        private GameObject root;
        private QusapHitReceiver receiver;

        [SetUp] public void SetUp()
        {
            root = new GameObject("Vitality_EditFixture", typeof(Rigidbody), typeof(QusapHitReceiver));
            receiver = root.GetComponent<QusapHitReceiver>();
        }
        [TearDown] public void TearDown() => Object.DestroyImmediate(root);

        [Test] public void SerializedMaxHealthAndSingleDamageAuthorityDetermineHealth()
        {
            var serialized = new SerializedObject(receiver);
            serialized.FindProperty("maxHealth").floatValue = 80;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(receiver.MaxHealth, Is.EqualTo(80));
            Assert.That(receiver.CurrentHealth, Is.EqualTo(80));
            receiver.TryReceiveEnvironmentDamage(31, root);
            Assert.That(receiver.TotalDamageReceived, Is.EqualTo(31));
            Assert.That(receiver.CurrentHealth, Is.EqualTo(49));
        }

        [Test] public void PartialHealingReducesExistingAccumulatorAndPublishesAppliedAmount()
        {
            receiver.TryReceiveEnvironmentDamage(40, root);
            int changes = 0, heals = 0;
            receiver.HealthChanged += change =>
            {
                changes++;
                Assert.That(change.Source, Is.EqualTo(QusapHealthChangeSource.Healing));
                Assert.That(change.PreviousDamage, Is.EqualTo(40));
                Assert.That(change.TotalDamage, Is.EqualTo(25));
                Assert.That(change.CurrentHealth, Is.EqualTo(75));
            };
            receiver.Healed += change => { heals++; Assert.That(change.AppliedAmount, Is.EqualTo(15)); };
            Assert.That(receiver.Heal(15), Is.EqualTo(15));
            Assert.That(receiver.TotalDamageReceived, Is.EqualTo(25));
            Assert.That(changes, Is.EqualTo(1)); Assert.That(heals, Is.EqualTo(1));
        }

        [Test] public void OverhealingClampsDamageToZeroAndFullHealthDoesNotEmitHealing()
        {
            receiver.TryReceiveEnvironmentDamage(12, root);
            int heals = 0; receiver.Healed += _ => heals++;
            Assert.That(receiver.Heal(500), Is.EqualTo(12));
            Assert.That(receiver.CurrentHealth, Is.EqualTo(receiver.MaxHealth));
            Assert.That(receiver.TotalDamageReceived, Is.Zero);
            Assert.That(receiver.Heal(25), Is.Zero);
            Assert.That(heals, Is.EqualTo(1));
        }

        [TestCase(-1f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidDamageAndHealingDoNotChangeAuthority(float amount)
        {
            receiver.TryReceiveEnvironmentDamage(10, root);
            Assert.That(receiver.TryReceiveEnvironmentDamage(amount, root), Is.False);
            Assert.That(receiver.Heal(amount), Is.Zero);
            Assert.That(receiver.TotalDamageReceived, Is.EqualTo(10));
        }

        [Test] public void DepletionIsLatchedBeforeDeferredEliminationAndCannotBeHealed()
        {
            int depleted = 0;
            receiver.HealthDepleted += change =>
            {
                depleted++;
                Assert.That(change.Source, Is.EqualTo(QusapHealthChangeSource.Environment));
                Assert.That(change.SourceObject, Is.SameAs(root));
                Assert.That(receiver.IsEliminated, Is.False, "Removal must occur after this callback returns.");
            };
            Assert.That(receiver.TryReceiveEnvironmentDamage(150, root), Is.True);
            Assert.That(receiver.CurrentHealth, Is.Zero);
            Assert.That(receiver.TotalDamageReceived, Is.EqualTo(150));
            Assert.That(receiver.AcceptsHits, Is.False);
            Assert.That(receiver.Heal(100), Is.Zero);
            Assert.That(receiver.TryReceiveEnvironmentDamage(10, root), Is.False);
            receiver.ResetDamage();
            Assert.That(receiver.CurrentHealth, Is.Zero, "Legacy reset cannot revive within the session.");
            Assert.That(depleted, Is.EqualTo(1));
        }

        [Test] public void OnlyExplicitNewSessionRestoresDepletedHealth()
        {
            receiver.TryReceiveEnvironmentDamage(100, root);
            QusapHealthChange? reset = null; receiver.HealthChanged += change => reset = change;
            receiver.ResetForNewSession();
            Assert.That(receiver.IsHealthDepleted, Is.False);
            Assert.That(receiver.CurrentHealth, Is.EqualTo(100));
            Assert.That(receiver.AcceptsHits, Is.True);
            Assert.That(reset.Value.Source, Is.EqualTo(QusapHealthChangeSource.NewSession));
        }

        [Test] public void LegacyLiveDamageResetPreservesAnExplicitHitGate()
        {
            receiver.TryReceiveEnvironmentDamage(10, root);
            receiver.AcceptsHits = false;
            receiver.ResetDamage();
            Assert.That(receiver.CurrentHealth, Is.EqualTo(100));
            Assert.That(receiver.AcceptsHits, Is.False);
        }

        [Test] public void ZeroHealingDoesNotChangeHealthOrEmitEvents()
        {
            Assert.That(receiver.Heal(25), Is.Zero);
            receiver.TryReceiveEnvironmentDamage(40, root);
            int changes = 0, heals = 0;
            receiver.HealthChanged += _ => changes++;
            receiver.Healed += _ => heals++;
            Assert.That(receiver.Heal(0), Is.Zero);
            Assert.That(receiver.CurrentHealth, Is.EqualTo(60));
            Assert.That(changes, Is.Zero); Assert.That(heals, Is.Zero);
        }

        [Test] public void MainPrefabAndCombatPlaygroundOpenWithValidVitalityReferences()
        {
            var prefab = PrefabUtility.LoadPrefabContents("Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab");
            try
            {
                Assert.That(prefab.GetComponent<QusapHitReceiver>().MaxHealth, Is.EqualTo(100));
                Assert.That(prefab.GetComponent<QusapCombatController>(), Is.Not.Null);
                Assert.That(prefab.GetComponentsInChildren<Animator>(true).All(a => !a.applyRootMotion), Is.True);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }

            var scene = EditorSceneManager.OpenScene("Assets/_Qusap/Scenes/CombatPlayground.unity", OpenSceneMode.Additive);
            try
            {
                var arena = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<QusapCombatArenaController>(true)).Single();
                var hud = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<QusapVitalityDebugHud>(true)).Single();
                Assert.That(arena.PlayerOne, Is.Not.Null); Assert.That(arena.PlayerTwo, Is.Not.Null);
                Assert.That(new SerializedObject(hud).FindProperty("arena").objectReferenceValue, Is.SameAs(arena));
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
