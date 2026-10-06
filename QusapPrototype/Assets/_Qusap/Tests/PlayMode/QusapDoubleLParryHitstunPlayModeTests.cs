#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLParryHitstunPlayModeTests
    {
        InputTestFixture devices;
        Gamepad pad; Keyboard keyboard; GameObject[] players;
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        static Behaviour View(GameObject root)=>root.GetComponents<Behaviour>().Single(c=>c.GetType().Name=="QusapDoubleLGroundAttackPresenter");
        static Animator AnimatorOf(GameObject root)=>(Animator)View(root).GetType().GetProperty("Animator").GetValue(View(root));
        static string viewState(GameObject root)=>(string)View(root).GetType().GetProperty("PresentationState").GetValue(View(root));
        static void Enqueue(GameObject root,QusapCombatCommand command,double time)=>typeof(QusapInputReader).GetMethod("EnqueueCombatCommand",Private).Invoke(root.GetComponent<QusapInputReader>(),new object[]{command,time});
        void FreezeInputClock()
        {
            var runtime=typeof(InputTestFixture).GetProperty("runtime",Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime,0d);
        }
        void PressParry(GameObject player)
        { if(player==players[1]){devices.Press(pad.leftTrigger);InputSystem.Update();}else Enqueue(player,QusapCombatCommand.Parry,InputState.currentTime); }
        void ReleaseParry(GameObject player)
        { if(player==players[1])devices.Release(pad.leftTrigger); }
        static void Arm(GameObject attacker,GameObject defender)=>typeof(QusapCombatController).GetMethod("ArmFinisher",Private)
            .Invoke(attacker.GetComponent<QusapCombatController>(),new object[]{QusapComboId.Damage,defender.GetComponent<QusapHitReceiver>()});
        IEnumerator Face(GameObject player,int side)
        {
            if(player==players[1])devices.Set(pad.leftStick,new Vector2(side,0));else devices.Press(side<0?keyboard.aKey:keyboard.dKey);
            yield return new WaitForSeconds(.08f);
            if(player==players[1])devices.Set(pad.leftStick,Vector2.zero);else devices.Release(side<0?keyboard.aKey:keyboard.dKey);
            yield return new WaitForSeconds(.18f);
            Assert.That(player.GetComponent<QusapCombatController>().FacingDirection,Is.EqualTo(side));
        }
        void CleanupCase()
        {
            foreach(var player in players){player.GetComponent<QusapHitstunController>().ResetHitstun();player.GetComponent<QusapCombatController>().CancelAttack();}
            AdvanceInputTime(1);
        }
        void AdvanceInputTime(double amount)
        {
            var runtime=typeof(InputTestFixture).GetProperty("runtime",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(devices)
                ??typeof(InputTestFixture).GetField("runtime",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)?.GetValue(devices);
            Assert.That(runtime,Is.Not.Null);
            var property=runtime.GetType().GetProperty("currentTime",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(property!=null)property.SetValue(runtime,(double)property.GetValue(runtime)+amount);
            else {var field=runtime.GetType().GetField("currentTime",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);field.SetValue(runtime,(double)field.GetValue(runtime)+amount);}
            InputSystem.Update();
        }
        [UnitySetUp] public IEnumerator SetUp()
        {
            devices=new InputTestFixture();devices.Setup();keyboard=InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone)yield return null;
            for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();
            players=UnityEngine.Object.FindObjectsByType<QusapInputReader>().OrderBy(p=>p.LocalPlayerSlot).Select(p=>p.gameObject).ToArray();
            Assert.That(players,Has.Length.EqualTo(2));
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);UnityEngine.Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
        }
        // Regression for the reviewed sequence. Native LT with no opponent
        // finisher reproduces the same penalty -> pose revival without ArmFinisher,
        // direct receiver calls, setup hits, or a custom evidence component.
        [UnityTest] public IEnumerator ResolvedFailedParryCannotResumeAttemptDuringGateRecovery()
        {
            var player=players[1];var combat=player.GetComponent<QusapCombatController>();var animator=AnimatorOf(player);
            var receiver=player.GetComponent<QusapHitReceiver>();var stun=player.GetComponent<QusapHitstunController>();
            int impacts=0;receiver.HitReceived+=_=>impacts++;receiver.FinisherReceived+=_=>impacts++;
            float damage=receiver.TotalDamageReceived;
            // The input mock normally advances 1/60 per rendered update, even
            // when uncapped batch frames advance Unity time by much less. Keep
            // its clock fixed, then advance it by the actual penalty interval.
            var runtime=typeof(InputTestFixture).GetProperty("runtime",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime,0d);
            double pressedAt=InputState.currentTime;
            devices.Press(pad.leftTrigger);yield return null;devices.Release(pad.leftTrigger);
            yield return new WaitForFixedUpdate();
            Assert.That(combat.LastParryAttemptOutcome,Is.EqualTo(QusapParryAttemptOutcome.NoIncomingFinisher));
            Assert.That(stun.IsInHitstun,Is.True,"native failed-parry penalty");
            yield return new WaitForSeconds(.34f);
            devices.currentTime=pressedAt+.34;InputSystem.Update();
            yield return null;yield return null;
            Assert.That(stun.IsInHitstun,Is.False,"native penalty ended");
            Assert.That(combat.IsParryAttemptRecovering,Is.True,"gate still recovering");
            Assert.That(impacts,Is.Zero);Assert.That(receiver.TotalDamageReceived,Is.EqualTo(damage));
            Debug.Log("PARRY_REVIVAL_DIAGNOSTIC nativeLT=true fixtureImpacts=0 outcome="+combat.LastParryAttemptOutcome+
                " gate="+combat.IsParryAttemptRecovering+" stun="+stun.IsInHitstun+
                " attempt="+animator.GetBool("ParryAttemptActive")+" ParryAttemptState="+animator.GetCurrentAnimatorStateInfo(0).IsName("ParryAttempt"));
            Assert.That(animator.GetBool("ParryAttemptActive"),Is.False,"resolved failure must not represent a pending attempt");
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("ParryAttempt"),Is.False,"gate recovery must not resume the pose");
        }
        [UnityTest] public IEnumerator HeldLtCreatesOneAttemptAndCannotKeepItsPoseAfterGateRecovery()
        {
            var player=players[1];var combat=player.GetComponent<QusapCombatController>();var animator=AnimatorOf(player);
            int attempts=0,accepted=0;combat.ParryAttemptFinished+=_=>attempts++;
            combat.ParryAttemptAccepted+=_=>{accepted++;Assert.That(animator.GetBool("ParryAttemptActive"),Is.True,"accepted and not resolved yet");};
            devices.Press(pad.leftTrigger);yield return null;yield return null;
            Assert.That(attempts,Is.EqualTo(1));Assert.That(accepted,Is.EqualTo(1));Assert.That(animator.GetBool("ParryAttemptActive"),Is.False,"already resolved");
            AdvanceInputTime(combat.ParryAttemptRecoveryDuration+.01);yield return null;yield return null;
            Assert.That(pad.leftTrigger.isPressed,Is.True);Assert.That(attempts,Is.EqualTo(1));Assert.That(accepted,Is.EqualTo(1));
            Assert.That(combat.IsParryAttemptRecovering,Is.False);Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);
            devices.Release(pad.leftTrigger);
        }
        [UnityTest] public IEnumerator RejectedCooldownPressCannotActivateAnUnobservedAttempt()
        {
            foreach(var player in players)
            {
                var combat=player.GetComponent<QusapCombatController>();
                var gate=(QusapParryAttemptGate)typeof(QusapCombatController).GetField("parryAttemptGate",Private).GetValue(combat);
                gate.ProcessPress(0,InputState.currentTime,null,QusapFinisherDefensePhase.None,true);
                Enqueue(player,QusapCombatCommand.Parry,InputState.currentTime);yield return null;yield return null;
                Assert.That(combat.LastParryAttemptOutcome,Is.EqualTo(QusapParryAttemptOutcome.OnRecovery));
                Assert.That(AnimatorOf(player).GetBool("ParryAttemptActive"),Is.False);
                Assert.That(AnimatorOf(player).GetCurrentAnimatorStateInfo(0).IsName("ParryAttempt"),Is.False);
            }
        }
        [UnityTest] public IEnumerator TooEarlyEndsAttemptImmediatelyAndCannotReviveBothPlayersBothDirections()
        {
            FreezeInputClock();
            foreach(var player in players)foreach(int side in new[]{-1,1})
            {
                CleanupCase();yield return Face(player,side);
                var attacker=players.Single(p=>p!=player);var combat=player.GetComponent<QusapCombatController>();
                var animator=AnimatorOf(player);var stun=player.GetComponent<QusapHitstunController>();
                int accepted=0,finished=0;
                void Accepted(QusapCombatCommandPress p){accepted++;Assert.That(animator.GetBool("ParryAttemptActive"),Is.True);}
                void Finished(QusapParryAttemptFeedback f){finished++;Assert.That(f.Outcome,Is.EqualTo(QusapParryAttemptOutcome.TooEarly));Assert.That(animator.GetBool("ParryAttemptActive"),Is.False,"terminal callback cancels immediately");}
                combat.ParryAttemptAccepted+=Accepted;combat.ParryAttemptFinished+=Finished;
                Arm(attacker,player);double pressed=InputState.currentTime;PressParry(player);
                Assert.That(accepted,Is.EqualTo(1));Assert.That(finished,Is.EqualTo(1));
                Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);
                yield return null;ReleaseParry(player);yield return new WaitForFixedUpdate();
                Assert.That(stun.IsInHitstun,Is.True);Assert.That(stun.TimeRemaining,Is.InRange(.25f,.30f));
                yield return new WaitForSeconds(.34f);devices.currentTime=pressed+.34;InputSystem.Update();yield return null;yield return null;
                Assert.That(stun.IsInHitstun,Is.False);Assert.That(combat.IsParryAttemptRecovering,Is.True);
                Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("ParryAttempt"),Is.False);
                // Let the existing 0.12 s locomotion blend complete, still
                // before the unchanged 0.50 s input recovery deadline.
                yield return new WaitForSeconds(.14f);devices.currentTime=pressed+.48;InputSystem.Update();
                Assert.That(combat.IsParryAttemptRecovering,Is.True);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("CombatIdle_B1")||animator.GetCurrentAnimatorStateInfo(0).IsName("RunForward")||animator.GetCurrentAnimatorStateInfo(0).IsName("RunBackward"),Is.True,"player="+player.name+" state="+viewState(player));
                combat.ParryAttemptAccepted-=Accepted;combat.ParryAttemptFinished-=Finished;
            }
            CleanupCase();
        }
        [UnityTest] public IEnumerator SuccessUsesIndependentGateRecoveryAndAllowsHitstunBothPlayersBothDirections()
        {
            FreezeInputClock();
            foreach(var player in players)foreach(int side in new[]{-1,1})foreach(bool interrupt in new[]{false,true})
            {
                CleanupCase();yield return Face(player,side);
                var attacker=players.Single(p=>p!=player);var combat=player.GetComponent<QusapCombatController>();var animator=AnimatorOf(player);
                Arm(attacker,player);AdvanceInputTime(.16);
                PressParry(player);
                Assert.That(combat.LastParryAttemptOutcome,Is.EqualTo(QusapParryAttemptOutcome.Success));
                Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);
                yield return null;ReleaseParry(player);yield return new WaitForFixedUpdate();yield return null;yield return null;
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("ParrySuccess"),Is.True);
                Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);Assert.That(animator.GetBool("ParrySuccessActive"),Is.True);
                Assert.That(animator.GetBool("ParrySuccess"),Is.False,"entry trigger consumed");
                Assert.That(attacker.GetComponent<QusapCombatController>().HasArmedFinisher,Is.False);
                Assert.That(AnimatorOf(attacker).GetCurrentAnimatorStateInfo(0).IsName("AttackerParried"),Is.True);
                if(interrupt)
                {
                    var hit=new QusapHitInfo(attacker.GetComponent<QusapCombatController>(),QusapAttackType.StrongKick,QusapAttackVariant.WeaponLight,1,side,0,0,.6f,Vector3.zero);
                    Assert.That(player.GetComponent<QusapHitReceiver>().TryReceiveHit(hit),Is.True);
                    Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);Assert.That(animator.GetBool("ParrySuccessActive"),Is.False);
                    yield return null;yield return null;
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitstunLight"),Is.True);
                }
                AdvanceInputTime(.51);yield return null;yield return null;
                Assert.That(combat.IsParryAttemptRecovering,Is.False);Assert.That(animator.GetBool("ParrySuccessActive"),Is.False);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(interrupt?"HitstunLight":"ParrySuccess"),Is.EqualTo(interrupt));
                if(interrupt)Assert.That(animator.GetBool("IsHitstunned"),Is.True,"gate cannot end stun");
                else Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("CombatIdle_B1")||animator.GetCurrentAnimatorStateInfo(0).IsName("RunForward")||animator.GetCurrentAnimatorStateInfo(0).IsName("RunBackward"),Is.True);
            }
            CleanupCase();
        }
        [UnityTest] public IEnumerator PendingAttemptIsInterruptedByNativeHitstunBothPlayersBothDirections()
        {
            foreach(var player in players)foreach(int side in new[]{-1,1})
            {
                CleanupCase();yield return Face(player,side);
                var view=View(player);var animator=AnimatorOf(player);var stun=player.GetComponent<QusapHitstunController>();
                // Isolate the acceptance boundary: normal gameplay resolves it in
                // the same dispatch. No artificial pending duration in production.
                view.GetType().GetMethod("ParryAttemptAccepted",Private).Invoke(view,new object[]{default(QusapCombatCommandPress)});
                Assert.That(animator.GetBool("ParryAttemptActive"),Is.True);
                animator.enabled=true;animator.Play("ParryAttempt",0,0);animator.Update(0);
                stun.EnterHitstun(.6f);
                Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);Assert.That(animator.GetBool("IsHitstunned"),Is.True);
                yield return null;yield return null;
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("Hitstun"),Is.True);
                stun.ResetHitstun();yield return null;yield return null;
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("ParryAttempt"),Is.False);
            }
        }
        [UnityTest] public IEnumerator RepeatedLightAndHeavyImpactsRestartBothPlayersBothDirectionsAndRecoverEarly()
        {
            foreach(var player in players)foreach(int side in new[]{-1,1})
            {
                var combat=player.GetComponent<QusapCombatController>();var receiver=player.GetComponent<QusapHitReceiver>();var stun=player.GetComponent<QusapHitstunController>();var animator=AnimatorOf(player);
                var source=players.Single(p=>p!=player).GetComponent<QusapCombatController>();
                if(player==players[1])devices.Set(pad.leftStick,new Vector2(side,0));else devices.Press(side<0?keyboard.aKey:keyboard.dKey);
                yield return new WaitForSeconds(.08f);
                if(player==players[1])devices.Set(pad.leftStick,Vector2.zero);else devices.Release(side<0?keyboard.aKey:keyboard.dKey);
                Assert.That(combat.FacingDirection,Is.EqualTo(side));
                QusapHitInfo Hit(bool heavy)=>new(source,QusapAttackType.StrongKick,heavy?QusapAttackVariant.WeaponStrong:QusapAttackVariant.WeaponLight,1,side,0,0,.6f,Vector3.zero);
                Assert.That(receiver.TryReceiveHit(Hit(false)),Is.True);yield return null;yield return new WaitForSeconds(.12f);
                string diagnostic=player.name+" side="+side+" native="+stun.IsInHitstun+" remaining="+stun.TimeRemaining+" presentation="+View(player).GetType().GetProperty("PresentationState").GetValue(View(player))+" hash="+animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
                Assert.That(animator.GetBool("IsHitstunned"),Is.True,diagnostic);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitstunLight"),Is.True,diagnostic);
                float before=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
                Assert.That(before,Is.GreaterThan(.02f),"reaction must advance between impacts");
                int starts=0;stun.HitstunStarted+=Started;void Started(){starts++;}
                Assert.That(receiver.TryReceiveHit(Hit(false)),Is.True);yield return null;yield return null;
                Assert.That(starts,Is.Zero);Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitstunLight"),Is.True);
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime,Is.LessThan(before),"repeat trigger="+animator.GetBool("HitstunLight")+" remaining="+stun.TimeRemaining);
                Assert.That(receiver.TryReceiveHit(Hit(true)),Is.True);yield return null;yield return null;
                Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitstunHeavy"),Is.True);
                Assert.That(animator.GetBool("HitstunLight"),Is.False);Assert.That(animator.GetBool("HitstunHeavy"),Is.False);
                float heavyTime=animator.GetCurrentAnimatorStateInfo(0).normalizedTime;Assert.That(heavyTime,Is.LessThan(1));
                stun.ResetHitstun();yield return null;yield return null;
                Assert.That(animator.GetBool("IsHitstunned"),Is.False);Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitstunHeavy"),Is.False);
                stun.HitstunStarted-=Started;
                foreach(var a in player.GetComponentsInChildren<Animator>(true))Assert.That(a.applyRootMotion,Is.False);
            }
        }
        [UnityTest] public IEnumerator GateRecoveryCannotReleaseAnActiveImpactReaction()
        {
            var player=players[1];var combat=player.GetComponent<QusapCombatController>();var animator=AnimatorOf(player);
            devices.Press(pad.leftTrigger);yield return null;devices.Release(pad.leftTrigger);yield return new WaitForFixedUpdate();
            var hit=new QusapHitInfo(players[0].GetComponent<QusapCombatController>(),QusapAttackType.StrongKick,QusapAttackVariant.WeaponStrong,1,1,0,0,1,Vector3.zero);
            Assert.That(player.GetComponent<QusapHitReceiver>().TryReceiveHit(hit),Is.True);yield return null;yield return null;
            AdvanceInputTime(combat.ParryAttemptRecoveryDuration+.01);yield return null;yield return null;
            Assert.That(animator.GetBool("ParryAttemptActive"),Is.False);Assert.That(animator.GetBool("IsHitstunned"),Is.True);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName("HitstunHeavy"),Is.True);
            player.GetComponent<QusapHitstunController>().ResetHitstun();yield return null;yield return null;
            Assert.That(animator.GetBool("IsHitstunned"),Is.False);
        }
    }
}
#endif
