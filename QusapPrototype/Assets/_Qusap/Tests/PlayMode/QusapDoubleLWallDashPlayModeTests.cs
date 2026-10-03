#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLWallDashPlayModeTests
    {
        InputTestFixture devices;Gamepad pad;GameObject player;Behaviour view;
        Rigidbody body;QusapGroundSensor ground;QusapVerticalMotor vertical;QusapWallSensor wall;QusapDashMotor dash;
        T Read<T>(string name)=>(T)view.GetType().GetProperty(name).GetValue(view);
        T Motor<T>(string name)=>(T)typeof(QusapDashMotor).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(dash);
        [UnitySetUp]public IEnumerator SetUp()
        {
            devices=new InputTestFixture();devices.Setup();InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));while(!load.isDone)yield return null;
            player=UnityEngine.Object.FindObjectsByType<QusapInputReader>().Single(i=>i.LocalPlayerSlot==QusapLocalPlayerSlot.Player2Gamepad).gameObject;
            view=player.GetComponents<Behaviour>().Single(c=>c.GetType().Name=="QusapDoubleLGroundAttackPresenter");body=player.GetComponent<Rigidbody>();ground=player.GetComponent<QusapGroundSensor>();vertical=player.GetComponent<QusapVerticalMotor>();wall=player.GetComponent<QusapWallSensor>();dash=player.GetComponent<QusapDashMotor>();
            yield return new WaitForSeconds(.3f);
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);UnityEngine.Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
        }
        void Move(int side)=>devices.Set(pad.leftStick,new Vector2(side,0));
        IEnumerator Land()
        {
            float deadline=Time.time+2;while((!ground.IsGrounded||body.linearVelocity.y>0)&&Time.time<deadline)yield return null;
            Assert.That(ground.IsGrounded,Is.True);yield return new WaitForSeconds(.25f);
        }
        [UnityTest]public IEnumerator RealWallSideAndImpulseOverrideInputThenReturnToRiseAndFallBothWays()
        {
            foreach(int side in new[]{1,-1})
            {
                Move(side);float deadline=Time.time+4;while(side*body.position.x<10.5f&&Time.time<deadline)yield return null;
                Move(0);yield return new WaitForSeconds(.16f);Assert.That(ground.IsGrounded,Is.True);
                Move(side);devices.Press(pad.buttonSouth);yield return new WaitForSeconds(.5f);devices.Release(pad.buttonSouth);
                // Let the native motor consume the previous release before the
                // next press; otherwise both edges become a short wall jump.
                yield return new WaitForFixedUpdate();yield return new WaitForFixedUpdate();
                deadline=Time.time+.5f;while(!vertical.IsWallSliding&&Time.time<deadline)yield return null;
                Assert.That(vertical.IsWallSliding,Is.True);Assert.That(vertical.WallSide,Is.EqualTo(side));Assert.That(wall.WallSide,Is.EqualTo(side));
                yield return null;Assert.That(Read<string>("PresentationState"),Is.EqualTo("WallSlide"));Assert.That(Read<int>("PresentationDirection"),Is.EqualTo(side));
                uint sequence=vertical.WallJumpSequence;
                // Keep input into the wall: the real impulse goes away from it.
                devices.Press(pad.buttonSouth);yield return new WaitForSeconds(.04f);
                Assert.That(vertical.WallJumpSequence,Is.GreaterThan(sequence));Assert.That(body.linearVelocity.x*side,Is.LessThan(0));
                Assert.That(player.GetComponent<QusapCombatController>().FacingDirection,Is.EqualTo(side));
                Assert.That(Read<string>("PresentationState"),Is.EqualTo("WallJump"));Assert.That(Read<int>("PresentationDirection"),Is.EqualTo(-side));
                Move(-side);yield return new WaitForSeconds(.14f);Assert.That(Read<string>("PresentationState"),Is.EqualTo("JumpRise"));
                yield return new WaitForSeconds(.20f);Assert.That(Read<string>("PresentationState"),Is.EqualTo("Fall"));
                devices.Release(pad.buttonSouth);Move(0);yield return Land();
            }
        }
        IEnumerator Burst(int side,bool air)
        {
            float deadline=Time.time+1.2f;while(Motor<float>("cooldownRemaining")>0&&Time.time<deadline)yield return null;
            Move(side);devices.Press(pad.rightShoulder);yield return new WaitForSeconds(.035f);devices.Release(pad.rightShoulder);
            Assert.That(dash.IsDashing,Is.True);Assert.That(body.useGravity,Is.False);
            Assert.That(Read<string>("PresentationState"),Is.EqualTo(air?"DashAir":"RunForward"));Assert.That(Read<int>("PresentationDirection"),Is.EqualTo(side));
            devices.Press(pad.buttonNorth);yield return new WaitForSeconds(.025f);devices.Release(pad.buttonNorth);
            Assert.That(player.GetComponent<QusapCombatController>().IsAttacking,Is.False);
            devices.Press(pad.buttonEast);yield return new WaitForSeconds(.025f);devices.Release(pad.buttonEast);
            Assert.That(dash.IsDashing,Is.True);Assert.That(player.GetComponent<QusapCombatController>().IsAttacking,Is.False);
            Move(0);yield return new WaitForSeconds(.15f);Assert.That(dash.IsDashing,Is.False);Assert.That(body.useGravity,Is.True);Assert.That(Read<Animator>("Animator").applyRootMotion,Is.False);
        }
        [UnityTest]public IEnumerator GroundDashKeepsApprovedRunAndAirDashBlocksBothWeaponButtons()
        {
            Move(-1);float deadline=Time.time+1;while(body.position.x>2.4f&&Time.time<deadline)yield return null;Move(0);yield return new WaitForSeconds(.25f);
            foreach(int side in new[]{1,-1}){yield return Burst(side,false);yield return new WaitForSeconds(.3f);Assert.That(Read<string>("PresentationState"),Is.EqualTo("CombatIdle_B1"));}
            while(Motor<float>("cooldownRemaining")>0)yield return null;
            devices.Press(pad.buttonSouth);yield return new WaitForSeconds(.14f);Assert.That(body.linearVelocity.y,Is.GreaterThan(0));
            yield return Burst(1,true);Assert.That(Motor<bool>("hasAirDash"),Is.False);Assert.That(Read<string>("PresentationState"),Is.EqualTo("Fall"));
            devices.Release(pad.buttonSouth);yield return Land();Assert.That(Motor<bool>("hasAirDash"),Is.True);
        }
        [UnityTest]public IEnumerator RealOneWayPlatformTraversalNeverStartsWallPresentation()
        {
            Assert.That(UnityEngine.Object.FindObjectsByType<QusapOneWayPlatform>().Length,Is.GreaterThanOrEqualTo(3));
            devices.Press(pad.buttonSouth);yield return new WaitForSeconds(.6f);devices.Release(pad.buttonSouth);yield return Land();
            Assert.That(body.position.y,Is.GreaterThan(2.8f));
            Move(-1);float begin=Time.time;
            while(Time.time-begin<.65f)
            {
                Assert.That(wall.IsTouchingWall,Is.False);Assert.That(vertical.IsWallSliding,Is.False);
                Assert.That(Read<string>("PresentationState"),Is.Not.EqualTo("WallSlide"));Assert.That(Read<string>("PresentationState"),Is.Not.EqualTo("WallJump"));yield return null;
            }
            Move(0);yield return Land();
        }
    }
}
#endif
