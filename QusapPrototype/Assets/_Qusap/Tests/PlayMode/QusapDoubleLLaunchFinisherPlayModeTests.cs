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
    public sealed class QusapDoubleLLaunchFinisherPlayModeTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        InputTestFixture devices; Keyboard keyboard; Gamepad pad; GameObject[] players; object runtime;
        static Behaviour View(GameObject p)=>p.GetComponents<Behaviour>().Single(c=>c.GetType().Name=="QusapDoubleLGroundAttackPresenter");
        static T Read<T>(GameObject p,string name)=>(T)View(p).GetType().GetProperty(name).GetValue(View(p));
        static QusapCombatController Combat(GameObject p)=>p.GetComponent<QusapCombatController>();
        void Clock()
        {
            runtime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(runtime,0d);
            runtime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(runtime,devices.currentTime);
        }
        IEnumerator Tick(float seconds)
        {
            float end=Time.time+seconds;
            while(Time.time<end){devices.currentTime+=Time.deltaTime;Clock();InputSystem.Update();yield return null;}
        }
        IEnumerator Button(GameObject p,QusapCombatCommand command)
        {
            var key=command==QusapCombatCommand.BodyAttack?keyboard.jKey:command==QusapCombatCommand.WeaponLight?keyboard.kKey:keyboard.lKey;
            var button=command==QusapCombatCommand.BodyAttack?pad.buttonWest:command==QusapCombatCommand.WeaponLight?pad.buttonNorth:
                command==QusapCombatCommand.WeaponStrong?pad.buttonEast:command==QusapCombatCommand.Parry?pad.leftTrigger:pad.leftShoulder;
            if(p==players[0])devices.Press(key);else devices.Press(button);
            yield return Tick(.025f);
            if(p==players[0])devices.Release(key);else devices.Release(button);
        }
        IEnumerator Face(GameObject p,int side)
        {
            if(p==players[0])devices.Press(side<0?keyboard.aKey:keyboard.dKey);else devices.Set(pad.leftStick,new Vector2(side,0));
            yield return Tick(.05f);Stop(p,side);yield return Tick(.15f);
        }
        void Stop(GameObject p,int side)
        {if(p==players[0])devices.Release(side<0?keyboard.aKey:keyboard.dKey);else devices.Set(pad.leftStick,Vector2.zero);}
        void Place(GameObject a,GameObject b,int side,float distance=1.15f)
        {
            a.GetComponent<Rigidbody>().position=new Vector3(3-side*distance*.5f,1.01f,0);
            b.GetComponent<Rigidbody>().position=new Vector3(3+side*distance*.5f,1.01f,0);
            foreach(var p in players)p.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;
            Physics.SyncTransforms();
        }
        [UnitySetUp] public IEnumerator SetUp()
        {
            devices=new InputTestFixture();devices.Setup();keyboard=InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            runtime=typeof(InputTestFixture).GetProperty("runtime",Private).GetValue(devices);
            runtime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(runtime,0d);
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone)yield return null;
            new GameObject("Launch_TestInputClock").AddComponent<QusapBodyHeadbuttTestClock>().Synchronize=Clock;
            yield return Tick(.25f);
            players=UnityEngine.Object.FindObjectsByType<QusapInputReader>().OrderBy(p=>p.LocalPlayerSlot).Select(p=>p.gameObject).ToArray();
        }
        [UnityTearDown] public IEnumerator TearDown()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);UnityEngine.Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
        }
        IEnumerator SetupLaunch(GameObject a,GameObject b,int side,bool moving=false)
        {
            yield return Face(a,side);Place(a,b,side);yield return Tick(.08f);
            int hits=0;QusapHitReceiver first=null;
            void Hit(QusapAttackType type,QusapHitReceiver target){hits++;if(first==null)first=target;else Assert.That(target,Is.SameAs(first));}
            Combat(a).AttackHit+=Hit;
            if(moving){if(a==players[0])devices.Press(side<0?keyboard.aKey:keyboard.dKey);else devices.Set(pad.leftStick,new Vector2(side,0));yield return Tick(.04f);}
            yield return Button(a,QusapCombatCommand.BodyAttack);Stop(a,side);yield return Tick(.32f);
            Place(a,b,side);yield return Button(a,QusapCombatCommand.WeaponLight);yield return Tick(.36f);
            Assert.That(hits,Is.EqualTo(2),"real InputActions and two same-rival physics hits");
            Combat(a).AttackHit-=Hit;Place(a,b,side);
            yield return Button(a,QusapCombatCommand.BodyAttack);
            Assert.That(Combat(a).ArmedFinisherCombo,Is.EqualTo(QusapComboId.Launch));
            Assert.That(Read<int>(a,"ClipIndex"),Is.EqualTo(7));
            Assert.That(Read<string>(a,"PresentationState"),Is.EqualTo("LaunchFinisherE2"));
            Assert.That(Read<float>(a,"SourceTime"),Is.LessThan(.1f),"a new execution restarts E2");
            Assert.That(Read<Animator>(a,"Animator").GetCurrentAnimatorStateInfo(0).IsName("LaunchFinisherE2"),Is.True);
        }
        IEnumerator Outcome(string outcome)
        {
            foreach(var a in players)foreach(int side in new[]{-1,1})foreach(bool moving in new[]{false,true})
            {
                // Only P2 has the production Parry binding. The attack combo
                // still uses actual J,K,J; never mutate player slots in runtime.
                if((outcome=="Parry"||outcome=="FailedParry")&&a==players[1])continue;
                var b=players.Single(p=>p!=a);yield return SetupLaunch(a,b,side,moving);
                float damage=b.GetComponent<QusapHitReceiver>().TotalDamageReceived;
                bool resolved=false,contact=false,parried=false;
                void End(QusapCombatVisualContext ctx)
                {
                    if(!ctx.IsFinisher||ctx.ComboId!=QusapComboId.Launch)return;
                    if(ctx.CancellationReason==QusapCombatVisualCancellationReason.Completed)
                    {contact=true;Assert.That(Read<float>(a,"SourceTime"),Is.EqualTo(.5f).Within(.000001f));}
                    else Assert.That(Read<int>(a,"ClipIndex"),Is.EqualTo(-1));
                }
                void Resolved(QusapFinisherResolution r)
                {
                    resolved=true;Assert.That(r.ComboId,Is.EqualTo(QusapComboId.Launch));Assert.That(r.DisarmApplied,Is.False);
                    bool whiff=outcome=="Whiff";
                    Assert.That(r.Outcome,Is.EqualTo(whiff?QusapFinisherResolutionOutcome.Whiffed:QusapFinisherResolutionOutcome.Applied));
                    Assert.That(b.GetComponent<QusapHitReceiver>().TotalDamageReceived-damage,Is.EqualTo(whiff?0:3));
                    if(!whiff)
                    {
                        Assert.That(b.GetComponent<QusapHitstunController>().TimeRemaining,Is.EqualTo(.55f).Within(.00001f));
                        Assert.That(b.GetComponent<Rigidbody>().linearVelocity.x,Is.EqualTo(side*4).Within(.0001f));
                        Assert.That(b.GetComponent<Rigidbody>().linearVelocity.y,Is.GreaterThanOrEqualTo(10));
                        if(outcome=="Air")Assert.That(b.GetComponent<QusapGroundSensor>().IsGrounded,Is.False);
                    }
                }
                void Parried(QusapComboId id,QusapHitReceiver target){parried=true;Assert.That(Read<int>(a,"ClipIndex"),Is.EqualTo(-1));}
                Combat(a).CombatVisualExecutionEnded+=End;Combat(a).FinisherResolved+=Resolved;Combat(a).FinisherParried+=Parried;
                if(outcome=="Whiff")Place(a,b,side,4);
                if(outcome=="Parry"||outcome=="FailedParry")
                {
                    while(InputState.currentTime<Read<double>(a,"FinisherStartedAt")+(outcome=="Parry"?.15:.03))yield return Tick(.005f);
                    devices.Press(pad.leftTrigger);yield return Tick(.025f);devices.Release(pad.leftTrigger);
                    Assert.That(Combat(b).LastParryAttemptOutcome,Is.EqualTo(outcome=="Parry"?QusapParryAttemptOutcome.Success:QusapParryAttemptOutcome.TooEarly));
                    if(outcome=="Parry")Assert.That(Read<string>(a,"PresentationState"),Is.EqualTo("AttackerParried"));
                }
                if(outcome=="Air")
                {
                    while(InputState.currentTime<Read<double>(a,"FinisherStartedAt")+.33)yield return Tick(.005f);
                    if(b==players[0])devices.Press(keyboard.spaceKey);else devices.Press(pad.buttonSouth);
                    yield return Tick(.035f);
                    if(b==players[0])devices.Release(keyboard.spaceKey);else devices.Release(pad.buttonSouth);
                }
                yield return Tick(.50f);
                Assert.That(Combat(a).HasArmedFinisher,Is.False);
                Assert.That(resolved,Is.EqualTo(outcome!="Parry"));Assert.That(contact,Is.EqualTo(outcome!="Parry"));Assert.That(parried,Is.EqualTo(outcome=="Parry"));
                Assert.That(b.GetComponent<QusapWeaponEquipment>().HasWeapon,Is.True);
                if(outcome=="Parry"||outcome=="Whiff")Assert.That(b.GetComponent<QusapHitReceiver>().TotalDamageReceived,Is.EqualTo(damage));
                yield return Tick(1.4f);Assert.That(Read<int>(a,"ClipIndex"),Is.EqualTo(-1));
                Combat(a).CombatVisualExecutionEnded-=End;Combat(a).FinisherResolved-=Resolved;Combat(a).FinisherParried-=Parried;
                foreach(var p in players)Assert.That(Read<Animator>(p,"Animator").applyRootMotion,Is.False);
            }
        }
        [UnityTest] public IEnumerator RealInputsHitBothPlayersBothDirectionsIdleAndMovement()=>Outcome("Hit");
        [UnityTest] public IEnumerator RealInputsWhiffConsumesAndRecovers()=>Outcome("Whiff");
        [UnityTest] public IEnumerator RealInputsParryInterruptsImmediately()=>Outcome("Parry");
        [UnityTest] public IEnumerator RealInputsFailedParryStillLaunches()=>Outcome("FailedParry");
        [UnityTest] public IEnumerator RealInputsLaunchAirborneTarget()=>Outcome("Air");
        [UnityTest] public IEnumerator IncompleteOrUnconfirmedRealSequenceCannotSelectE2()
        {
            foreach(var a in players)
            {
                var b=players.Single(p=>p!=a);yield return Face(a,1);Place(a,b,1,4);
                yield return Button(a,QusapCombatCommand.BodyAttack);yield return Tick(.32f);
                yield return Button(a,QusapCombatCommand.WeaponLight);yield return Tick(.36f);
                Assert.That(Combat(a).HasArmedFinisher,Is.False);
                yield return Button(a,QusapCombatCommand.BodyAttack);yield return Tick(.35f);
                Assert.That(Combat(a).HasArmedFinisher,Is.False);Assert.That(Read<int>(a,"ClipIndex"),Is.Not.EqualTo(7));
            }
        }
    }
}
#endif
