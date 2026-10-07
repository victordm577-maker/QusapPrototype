using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLLaunchFinisherEditModeTests
    {
        const string Root="Assets/_Qusap/Presentation/DoubleLGroundAttacks/";
        AnimatorController Controller=>AssetDatabase.LoadAssetAtPath<AnimatorController>(Root+"DoubleLGroundAttacks.controller");
        AnimatorState State(string name)=>Controller.layers[0].stateMachine.states.Single(s=>s.state.name==name).state;

        [Test] public void LaunchHasExclusiveApprovedE2StateAndUneditedSource()
        {
            var state=State("LaunchFinisherE2");
            Assert.That(state.motion.name,Is.EqualTo("1Hand_Base_Attack_E_2_InPlace"));
            Assert.That(AssetDatabase.GetAssetPath(state.motion),Does.EndWith("Attack_E/InPlace/1Hand_Base_Attack_E_2_InPlace.fbx"));
            Assert.That(state.speed,Is.EqualTo(1));
            Assert.That(((AnimationClip)state.motion).length,Is.EqualTo(1.6f).Within(.00001f));
            Assert.That(((AnimationClip)state.motion).events,Is.Empty);
        }
        [Test] public void E2HasNoAutomaticExitAndControllerHasNoExitTime()
        {
            Assert.That(State("LaunchFinisherE2").transitions,Is.Empty);
            foreach(var layer in Controller.layers)
            {
                Assert.That(layer.stateMachine.anyStateTransitions.All(t=>!t.hasExitTime),Is.True);
                Assert.That(layer.stateMachine.states.SelectMany(s=>s.state.transitions).All(t=>!t.hasExitTime),Is.True);
            }
        }
        [Test] public void DamageDisarmAndBodyMappingsStayDistinct()
        {
            Assert.That(State("FiveAttack_2").motion.name,Is.EqualTo("1Hand_Base_Attack_B_2_InPlace"));
            Assert.That(State("BodyAttack").motion.name,Is.EqualTo("1Hand_Base_Skill_7_InPlace"));
            Assert.That(State("Headbutt").motion,Is.EqualTo(State("CombatIdle_B1").motion));
            Assert.That(Controller.parameters.Any(p=>p.name.Contains("Launch")),Is.False,"existing direct state selection needs no persistent parameter");
        }
        [Test] public void ProductionRootMotionRemainsDisabledAndFixturesAreNotDependencies()
        {
            const string path="Assets/_Qusap/Prefabs/QusapCombatPlayer.prefab";
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab.GetComponentsInChildren<Animator>(true).All(a=>!a.applyRootMotion),Is.True);
            Assert.That(AssetDatabase.GetDependencies(path,true).Any(p=>p.Contains("LaunchFinisherPreselection")),Is.False);
        }
    }
}
