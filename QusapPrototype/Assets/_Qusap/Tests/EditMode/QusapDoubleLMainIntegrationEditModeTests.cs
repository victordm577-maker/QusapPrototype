using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLMainIntegrationEditModeTests
    {
        const string Prefab="Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
        GameObject root;
        [SetUp]public void SetUp(){root=PrefabUtility.LoadPrefabContents(Prefab);}
        [TearDown]public void TearDown(){PrefabUtility.UnloadPrefabContents(root);}
        [Test]public void GameplayProvidersRemainOnTheOriginalRoot()
        {
            Assert.That(root.name,Is.EqualTo("QusapCombatPlayer"));
            Assert.That(root.GetComponentsInChildren<Rigidbody>(true),Has.Length.EqualTo(1));
            Assert.That(root.GetComponentsInChildren<Collider>(true),Has.Length.EqualTo(1));
            Assert.That(root.GetComponent<CapsuleCollider>().radius,Is.EqualTo(.5f));
            Assert.That(root.GetComponent<CapsuleCollider>().height,Is.EqualTo(2f));
            Assert.That(root.GetComponent<Rigidbody>().mass,Is.EqualTo(1f));
            Assert.That(root.GetComponent<QusapHorizontalMotor>(),Is.Not.Null);
            Assert.That(root.GetComponent<QusapVerticalMotor>(),Is.Not.Null);
            Assert.That(root.GetComponent<QusapDashMotor>(),Is.Not.Null);
            Assert.That(root.GetComponent<QusapCombatController>(),Is.Not.Null);
            Assert.That(root.GetComponent<QusapInputReader>(),Is.Not.Null);
        }
        [Test]public void PresentationUsesTheOriginalGameplayProvidersAndDisablesRootMotion()
        {
            var script=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Qusap/Presentation/DoubleLGroundAttacks/QusapDoubleLGroundAttackPresenter.cs");
            var component=root.GetComponent(script.GetClass());var serialized=new SerializedObject(component);
            Assert.That(serialized.FindProperty("body").objectReferenceValue,Is.SameAs(root.GetComponent<Rigidbody>()));
            Assert.That(serialized.FindProperty("combat").objectReferenceValue,Is.SameAs(root.GetComponent<QusapCombatController>()));
            Assert.That(serialized.FindProperty("ground").objectReferenceValue,Is.SameAs(root.GetComponent<QusapGroundSensor>()));
            foreach(var animator in root.GetComponentsInChildren<Animator>(true))Assert.That(animator.applyRootMotion,Is.False);
            foreach(var writer in root.GetComponents<Behaviour>().Where(c=>c is QusapModularCombatVisualPresenter || c is QusapModularFacingPresenter || c is QusapAnimationDriver || c is QusapHitReactionVisual || c is QusapEquippedWeaponPresenter || c is QusapWeaponAttackVisualPresenter))Assert.That(writer.enabled,Is.False);
        }
        [Test]public void ThreeGroundStatesUseUneditedClipsAndJumpAttacksHaveNoTransitions()
        {
            var controller=AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/_Qusap/Presentation/DoubleLGroundAttacks/DoubleLGroundAttacks.controller");
            var states=controller.layers[0].stateMachine.states;
            var expected=new[]{"1Hand_Base_Attack_A_1_InPlace","1Hand_Base_Attack_A_4_InPlace","1Hand_Base_Attack_B_2_InPlace","1Hand_Base_Jump_Attack_1_InPlace","1Hand_Base_Jump_Attack_2_InPlace"};
            for(int i=0;i<5;i++)
            {
                var state=states.Single(s=>s.state.name=="FiveAttack_"+i).state;
                Assert.That(state.motion.name,Is.EqualTo(expected[i]));
                Assert.That((state.motion as AnimationClip).events,Is.Empty);
                Assert.That(state.transitions,Is.Empty);
                Assert.That(AssetDatabase.GetAssetPath(state.motion),Does.EndWith(".fbx"));
            }
        }
        [Test]public void OnlyApprovedVisualMeshesRenderAndSwordPivotRemainsOnP1()
        {
            var visual=root.transform.Find("ApprovedDoubleL_Presentation");Assert.That(visual,Is.Not.Null);
            var renderers=root.GetComponentsInChildren<Renderer>(true).Where(r=>r.enabled).ToArray();
            Assert.That(renderers.Length,Is.EqualTo(2));
            Assert.That(renderers.All(r=>r.transform.IsChildOf(visual)),Is.True);
            var facingScript=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Qusap/Tests/NewQusap75KAnimationTest/WeaponGripV1/HandSwitchV5/QusapSwordHandSwitchV5.cs");
            var facing=new SerializedObject(visual.GetComponent(facingScript.GetClass()));
            var sword=(Transform)facing.FindProperty("swordVisual").objectReferenceValue;
            Assert.That(sword.parent.name,Does.StartWith("WeaponGripSocket_"));
            Assert.That(sword.localPosition,Is.EqualTo(Vector3.zero));
            Assert.That(sword.GetComponentsInChildren<MeshFilter>(true),Has.Length.EqualTo(1));
            foreach(var t in visual.GetComponentsInChildren<Transform>(true))Assert.That(Mathf.Min(t.localScale.x,t.localScale.y,t.localScale.z),Is.GreaterThan(0));
        }
        [Test]public void StartupGeneratorRecognizesTheCompleteApprovedPlayer()
        {
            var generator=AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/_Qusap/Editor/CombatPlaygroundGenerator.cs").GetClass();
            var check=generator.GetMethod("IsCombatPlayerPrefabComplete",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
            Assert.That(check.Invoke(null,null),Is.EqualTo(true),"Opening Unity must preserve the approved main prefab and scene.");
        }
    }
}
