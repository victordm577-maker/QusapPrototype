using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLParryHitstunEditModeTests
    {
        const string Path="Assets/_Qusap/Presentation/DoubleLGroundAttacks/DoubleLGroundAttacks.controller";
        static readonly string[] Names={"ParryAttempt","ParrySuccess","AttackerParried","HitstunLight","HitstunHeavy"};
        AnimatorController Controller => AssetDatabase.LoadAssetAtPath<AnimatorController>(Path);
        [Test] public void ParametersUseOnlyTheDefinitiveNamesAndTypes()
        {
            var controller=Controller;
            foreach(string name in new[]{"ParryAttemptActive","ParrySuccessActive","IsHitstunned"})
                Assert.That(controller.parameters.Single(p=>p.name==name).type,Is.EqualTo(AnimatorControllerParameterType.Bool));
            foreach(string name in Names.Skip(1))
                Assert.That(controller.parameters.Single(p=>p.name==name).type,Is.EqualTo(AnimatorControllerParameterType.Trigger));
            Assert.That(controller.parameters.Any(p=>p.name=="ParryActive"||p.name=="StateRecovered"),Is.False);
        }
        [Test] public void SourceClipsRemainFbxAssetsWithoutAnimationEvents()
        {
            string[] expected={"1Hand_Base_Shield_Block_Idle_1","1Hand_Base_Shield_Block_Parry_1","1Hand_Base_Shield_Block_Hit_3_InPlace","1Hand_Base_Shield_Block_Hit_1_InPlace","1Hand_Base_Shield_Block_Hit_2_InPlace"};
            for(int i=0;i<Names.Length;i++)
            {
                var state=Controller.layers[0].stateMachine.states.Single(s=>s.state.name==Names[i]).state;
                var clip=state.motion as AnimationClip;
                Assert.That(clip.name,Is.EqualTo(expected[i]));Assert.That(clip.events,Is.Empty);
                Assert.That(AssetDatabase.GetAssetPath(clip),Does.EndWith(".fbx"));
            }
        }
        [Test] public void ImpactTransitionsConsumeTriggersAndAllowSameStateReentry()
        {
            var machine=Controller.layers[0].stateMachine;
            foreach(string name in Names.Skip(2))
            {
                var transition=machine.anyStateTransitions.Single(t=>t.destinationState.name==name);
                Assert.That(transition.canTransitionToSelf,Is.True);
                Assert.That(transition.conditions.Any(c=>c.parameter==name&&c.mode==AnimatorConditionMode.If),Is.True);
                Assert.That(transition.conditions.Any(c=>c.parameter=="IsHitstunned"&&c.mode==AnimatorConditionMode.If),Is.True);
                Assert.That(transition.hasExitTime,Is.False);Assert.That(transition.duration,Is.Zero);
            }
        }
        [Test] public void RecoveryConditionsAreAuthoritativeAndMovingOnlySelectsDestination()
        {
            var machine=Controller.layers[0].stateMachine;
            foreach(string name in Names)
            {
                var state=machine.states.Single(s=>s.state.name==name).state;
                foreach(var transition in state.transitions)
                {
                    Assert.That(transition.hasExitTime,Is.False);
                    if(transition.destinationState.name=="Hitstun")continue;
                    string authority=name=="ParryAttempt"?"ParryAttemptActive":name=="ParrySuccess"?"ParrySuccessActive":"IsHitstunned";
                    Assert.That(transition.conditions.Any(c=>c.parameter==authority&&c.mode==AnimatorConditionMode.IfNot),Is.True);
                    Assert.That(transition.conditions.Any(c=>c.parameter=="IsMoving"),Is.True);
                }
            }
            foreach(var state in machine.states.Where(s=>s.state.name.StartsWith("FiveAttack_")))Assert.That(state.state.transitions,Is.Empty);
        }
        [Test] public void AttemptCannotReplaceSuccessAndHitstunCanInterruptBothParryStates()
        {
            var machine=Controller.layers[0].stateMachine;
            Assert.That(machine.anyStateTransitions.Any(t=>t.destinationState.name=="ParryAttempt"),Is.False);
            foreach(string name in Names.Take(2))
            {
                var state=machine.states.Single(s=>s.state.name==name).state;
                var interruption=state.transitions.Single(t=>t.destinationState.name=="Hitstun");
                Assert.That(interruption.conditions.Single().parameter,Is.EqualTo("IsHitstunned"));
                Assert.That(interruption.conditions.Single().mode,Is.EqualTo(AnimatorConditionMode.If));
            }
            var success=machine.anyStateTransitions.Single(t=>t.destinationState.name=="ParrySuccess");
            Assert.That(success.conditions.Any(c=>c.parameter=="IsHitstunned"&&c.mode==AnimatorConditionMode.IfNot),Is.True);
            Assert.That(success.conditions.Any(c=>c.parameter=="ParryAttemptActive"),Is.False);
        }
        [Test] public void ImpactAndSuccessTransitionsPrecedeNormalAttemptExit()
        {
            var machine=Controller.layers[0].stateMachine;
            var destinations=machine.anyStateTransitions.Select(t=>t.destinationState.name).ToArray();
            foreach(string impact in Names.Skip(2))
                Assert.That(System.Array.IndexOf(destinations,impact),Is.LessThan(System.Array.IndexOf(destinations,"ParrySuccess")));
            var attempt=machine.states.Single(s=>s.state.name=="ParryAttempt").state;
            foreach(var exit in attempt.transitions.Where(t=>t.destinationState.name!="Hitstun"))
                Assert.That(exit.conditions.Any(c=>c.parameter=="IsHitstunned"&&c.mode==AnimatorConditionMode.IfNot),Is.True);
            var success=machine.states.Single(s=>s.state.name=="ParrySuccess").state;
            Assert.That(success.transitions.SelectMany(t=>t.conditions).Any(c=>c.parameter=="ParryAttemptActive"),Is.False);
        }
    }
}
