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
    // Editor player-loop restores the native input clock offset after a mock
    // input update. Keep the test clock consistent before native combat reads it.
    [DefaultExecutionOrder(-10000)]
    public sealed class QusapBodyHeadbuttTestClock:MonoBehaviour
    {
        public Action Synchronize;
        void FixedUpdate()=>Synchronize?.Invoke();
    }
    public sealed class QusapDoubleLBodyHeadbuttPlayModeTests
    {
        const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        InputTestFixture devices;Keyboard keyboard;Gamepad pad;GameObject[] players;
        object inputRuntime;
        void SyncInputClock()
        {
            inputRuntime.GetType().GetProperty("currentTimeOffsetToRealtimeSinceStartup").SetValue(inputRuntime,0d);
            inputRuntime.GetType().GetProperty("currentTimeForFixedUpdate").SetValue(inputRuntime,devices.currentTime);
        }
        static Behaviour View(GameObject p)=>p.GetComponents<Behaviour>().Single(c=>c.GetType().Name=="QusapDoubleLGroundAttackPresenter");
        static T Read<T>(GameObject p,string property)=>(T)View(p).GetType().GetProperty(property).GetValue(View(p));
        static QusapCombatController Combat(GameObject p)=>p.GetComponent<QusapCombatController>();
        void Press(GameObject p,QusapCombatCommand command)
        {
            devices.currentTime+=.001;SyncInputClock();InputSystem.Update();
            typeof(QusapInputReader).GetMethod("EnqueueCombatCommand",Private).Invoke(p.GetComponent<QusapInputReader>(),new object[]{command,InputState.currentTime});
        }
        IEnumerator Tick(float seconds)
        {
            float end=Time.time+seconds;
            while(Time.time<end){devices.currentTime+=Time.deltaTime;SyncInputClock();InputSystem.Update();yield return null;}
        }
        IEnumerator Face(GameObject p,int side)
        {
            if(p==players[1])devices.Set(pad.leftStick,new Vector2(side,0));else devices.Press(side<0?keyboard.aKey:keyboard.dKey);
            yield return Tick(.06f);
            if(p==players[1])devices.Set(pad.leftStick,Vector2.zero);else devices.Release(side<0?keyboard.aKey:keyboard.dKey);
            yield return Tick(.18f);
        }
        void Place(GameObject attacker,GameObject target,int side,float distance=1.15f)
        {
            foreach(var p in players){p.GetComponent<Rigidbody>().linearVelocity=Vector3.zero;}
            var a=attacker.GetComponent<Rigidbody>();var b=target.GetComponent<Rigidbody>();
            a.position=new Vector3(3-side*distance*.5f,a.position.y,0);b.position=new Vector3(3+side*distance*.5f,b.position.y,0);
            Physics.SyncTransforms();
        }
        [UnitySetUp]public IEnumerator SetUp()
        {
            devices=new InputTestFixture();devices.Setup();keyboard=InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            inputRuntime=typeof(InputTestFixture).GetProperty("runtime",Private).GetValue(devices);
            inputRuntime.GetType().GetProperty("advanceTimeEachDynamicUpdate").SetValue(inputRuntime,0d);
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone)yield return null;
            new GameObject("BodyHeadbutt_TestInputClock").AddComponent<QusapBodyHeadbuttTestClock>().Synchronize=SyncInputClock;
            yield return Tick(.20f);
            players=UnityEngine.Object.FindObjectsByType<QusapInputReader>().OrderBy(p=>p.LocalPlayerSlot).Select(p=>p.gameObject).ToArray();
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);UnityEngine.Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
        }
        [UnityTest]public IEnumerator NativeXAndLbStartAndEndBothPlayersBothDirections()
        {
            foreach(var p in players)foreach(int side in new[]{-1,1})foreach(var command in new[]{QusapCombatCommand.BodyAttack,QusapCombatCommand.Headbutt})
            {
                yield return Face(p,side);var target=players.Single(t=>t!=p);Place(p,target,side,4);
                int starts=0,ends=0;
                void Started(QusapAttackVariant v){starts++;Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(command==QusapCombatCommand.BodyAttack?5:6));}
                void Ended(QusapCombatVisualContext ctx){ends++;Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));}
                Combat(p).AttackVariantStarted+=Started;Combat(p).CombatVisualExecutionEnded+=Ended;
                Press(p,command);yield return Tick(.04f);
                Assert.That(Read<string>(p,"PresentationState"),Is.EqualTo(command==QusapCombatCommand.BodyAttack?"BodyAttack":"Headbutt"));
                Assert.That(Read<int>(p,"PresentationDirection"),Is.EqualTo(side));
                yield return Tick(command==QusapCombatCommand.BodyAttack?.48f:1.08f);
                Assert.That(starts,Is.EqualTo(1));Assert.That(ends,Is.EqualTo(1));Assert.That(Combat(p).IsAttacking,Is.False);
                Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));
                Combat(p).AttackVariantStarted-=Started;Combat(p).CombatVisualExecutionEnded-=Ended;
            }
        }
        [UnityTest]public IEnumerator AirCommandsRetainNativeVariantsAndRecoverWithoutVisualMotion()
        {
            var p=players[1];var rb=p.GetComponent<Rigidbody>();
            foreach(int side in new[]{-1,1})foreach(var command in new[]{QusapCombatCommand.BodyAttack,QusapCombatCommand.Headbutt})
            {
                yield return Face(p,side);Place(p,players[0],side,4);
                devices.Press(pad.buttonSouth);yield return Tick(.12f);devices.Release(pad.buttonSouth);
                Assert.That(p.GetComponent<QusapGroundSensor>().IsGrounded,Is.False);
                Vector3 local=Read<GameObject>(p,"Visual").transform.localPosition;
                Press(p,command);yield return Tick(.04f);
                Assert.That(Combat(p).CurrentAttackVariant,Is.EqualTo(command==QusapCombatCommand.BodyAttack?QusapAttackVariant.WeakKickAir:QusapAttackVariant.DiveHeadbuttAir));
                Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(command==QusapCombatCommand.BodyAttack?5:6));
                Assert.That(Read<GameObject>(p,"Visual").transform.localPosition,Is.EqualTo(local));Assert.That(Read<Animator>(p,"Animator").applyRootMotion,Is.False);
                yield return Tick(1.5f);Assert.That(Combat(p).IsAttacking,Is.False);Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));
            }
        }
        [UnityTest]public IEnumerator HitstunCancelsVisualAndPausedLbCannotReappear()
        {
            var p=players[1];
            foreach(var command in new[]{QusapCombatCommand.BodyAttack,QusapCombatCommand.Headbutt})
            {
                Place(p,players[0],1,4);Press(p,command);yield return Tick(.04f);
                Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(command==QusapCombatCommand.BodyAttack?5:6));
                p.GetComponent<QusapHitstunController>().EnterHitstun(.20f);
                Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));
                yield return Tick(.30f);Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1),"paused native LB must not revive its canceled pose");
                yield return Tick(1.05f);
            }
        }
        [UnityTest]public IEnumerator MovementReturnsToRunAndDashCannotStartBodyCommands()
        {
            var p=players[1];
            foreach(int side in new[]{-1,1})foreach(var command in new[]{QusapCombatCommand.BodyAttack,QusapCombatCommand.Headbutt})
            {
                Place(p,players[0],side,4);devices.Set(pad.leftStick,new Vector2(side,0));yield return Tick(.08f);
                Press(p,command);yield return Tick(.04f);Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(command==QusapCombatCommand.BodyAttack?5:6));
                yield return Tick(command==QusapCombatCommand.BodyAttack?.50f:1.12f);
                Assert.That(Read<string>(p,"PresentationState"),Is.EqualTo("RunForward"));devices.Set(pad.leftStick,Vector2.zero);yield return Tick(.20f);
                Place(p,players[0],side,4);devices.Press(pad.rightShoulder);yield return Tick(.025f);devices.Release(pad.rightShoulder);
                Assert.That(p.GetComponent<QusapDashMotor>().IsDashing,Is.True);
                Press(p,command);yield return Tick(.04f);Assert.That(Combat(p).IsAttacking,Is.False);Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));
                yield return Tick(.50f);
            }
        }
        [UnityTest]public IEnumerator RealMatcherDisarmUsesH2AndActualEquipmentDisarm()
        {
            var p=players[1];var target=players[0];yield return Face(p,-1);Place(p,target,-1);
            int hits=0;Combat(p).AttackHit+=(t,r)=>hits++;
            foreach(var command in new[]{QusapCombatCommand.WeaponLight,QusapCombatCommand.WeaponLight})
            {Place(p,target,-1);Press(p,command);yield return Tick(.39f);}
            Assert.That(hits,Is.EqualTo(2),"two physics-confirmed matcher steps");Place(p,target,-1);
            var equipment=target.GetComponent<QusapWeaponEquipment>();Assert.That(equipment.EquippedWeapon,Is.Not.Null);
            QusapFinisherResolution? result=null;Combat(p).FinisherResolved+=r=>result=r;
            Press(p,QusapCombatCommand.Headbutt);yield return Tick(.04f);
            Assert.That(Combat(p).ArmedFinisherCombo,Is.EqualTo(QusapComboId.Disarm));Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(6));
            bool impact=false;
            Combat(p).CombatVisualExecutionEnded+=ctx=>{if(ctx.IsFinisher){impact=true;Assert.That(Read<float>(p,"SourceTime"),Is.EqualTo(.45f).Within(.0001f));}};
            yield return Tick(.55f);Assert.That(impact,Is.True);Assert.That(result.HasValue,Is.True);
            Assert.That(equipment.EquippedWeapon,Is.Null,"native disarm actually removed target equipment");Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));
            Assert.That(Read<GameObject>(target,"Visual").GetComponentsInChildren<MeshFilter>(true).Any(m=>m.GetComponent<Renderer>().enabled),Is.False,"disarmed target cannot retain an equipped sword visual");
        }
        [UnityTest]public IEnumerator RealMatcherDamageUsesB2AndCanStillBeParried()
        {
            var p=players[1];var target=players[0];yield return Face(p,-1);Place(p,target,-1);
            int hits=0;Combat(p).AttackHit+=(t,r)=>hits++;
            foreach(var command in new[]{QusapCombatCommand.WeaponLight,QusapCombatCommand.WeaponLight,QusapCombatCommand.BodyAttack})
            {Place(p,target,-1);Press(p,command);yield return Tick(command==QusapCombatCommand.BodyAttack?.36f:.39f);}
            Assert.That(hits,Is.EqualTo(3));Place(p,target,-1);
            Press(p,QusapCombatCommand.WeaponStrong);yield return Tick(.04f);
            Assert.That(Combat(p).ArmedFinisherCombo,Is.EqualTo(QusapComboId.Damage));Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(2));
            double close=Combat(p).ParryWindowClosesAt;
            while(InputState.currentTime<Combat(p).ParryWindowOpensAt+.04)yield return Tick(.01f);
            Press(target,QusapCombatCommand.Parry);yield return Tick(.04f);
            Assert.That(Combat(target).LastParryAttemptOutcome,Is.EqualTo(QusapParryAttemptOutcome.Success));
            Assert.That(Combat(p).HasArmedFinisher,Is.False);yield return Tick(.60f);
            Assert.That(Read<int>(p,"ClipIndex"),Is.EqualTo(-1));
        }
    }
}
#endif
