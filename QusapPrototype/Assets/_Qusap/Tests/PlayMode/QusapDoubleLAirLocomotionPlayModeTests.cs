#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLAirLocomotionPlayModeTests
    {
        InputTestFixture devices;Gamepad pad;GameObject player;Behaviour view;
        Rigidbody body;QusapGroundSensor ground;
        T Read<T>(string name)=>(T)view.GetType().GetProperty(name).GetValue(view);
        [UnitySetUp]public IEnumerator SetUp()
        {
            devices=new InputTestFixture();devices.Setup();InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone)yield return null;
            player=Object.FindObjectsByType<QusapInputReader>().Single(i=>i.LocalPlayerSlot==QusapLocalPlayerSlot.Player2Gamepad).gameObject;
            view=player.GetComponents<Behaviour>().Single(c=>c.GetType().Name=="QusapDoubleLGroundAttackPresenter");
            body=player.GetComponent<Rigidbody>();ground=player.GetComponent<QusapGroundSensor>();
            yield return new WaitForSeconds(.3f);
            devices.Set(pad.leftStick,Vector2.left);float deadline=Time.time+2;
            while(body.position.x>2.4f&&Time.time<deadline)yield return null;
            devices.Set(pad.leftStick,Vector2.zero);yield return new WaitForSeconds(.3f);
            Assert.That(ground.IsGrounded,Is.True);
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
        }
        [UnityTest]public IEnumerator NativeIdleJumpBothWaysShowsTakeoffRiseFallAndLandingWithoutRootMotion()
        {
            foreach(int side in new[]{1,-1})
            {
                devices.Set(pad.leftStick,new Vector2(side,0));yield return new WaitForSeconds(.06f);
                devices.Set(pad.leftStick,Vector2.zero);yield return new WaitForSeconds(.15f);
                Assert.That(player.GetComponent<QusapCombatController>().FacingDirection,Is.EqualTo(side));
                var states=new HashSet<string>();float start=body.position.y,max=start,begin=Time.time;
                devices.Press(pad.buttonSouth);
                while(Time.time-begin<1.1f)
                {
                    states.Add(Read<string>("PresentationState"));max=Mathf.Max(max,body.position.y);
                    Assert.That(Read<Animator>("Animator").applyRootMotion,Is.False);
                    yield return null;
                }
                devices.Release(pad.buttonSouth);yield return new WaitForSeconds(.25f);
                Assert.That(max-start,Is.GreaterThan(2.1f));Assert.That(ground.IsGrounded,Is.True);
                foreach(string state in new[]{"JumpTakeoff","JumpRise","Fall","LandSoft"})Assert.That(states,Does.Contain(state));
                Assert.That(Read<string>("PresentationState"),Is.EqualTo("CombatIdle_B1"));
            }
        }
        [UnityTest]public IEnumerator NativeJumpCutReducesHeightAndMovementInterruptsLanding()
        {
            float start=body.position.y,max=start,begin=Time.time;
            devices.Press(pad.buttonSouth);yield return new WaitForSeconds(.06f);devices.Release(pad.buttonSouth);
            while(Time.time-begin<1)
            {max=Mathf.Max(max,body.position.y);yield return null;}
            Assert.That(max-start,Is.LessThan(1.5f));Assert.That(max-start,Is.GreaterThan(.2f));
            Assert.That(ground.IsGrounded,Is.True);
            devices.Set(pad.leftStick,Vector2.left);yield return new WaitForSeconds(.15f);
            Assert.That(body.linearVelocity.x,Is.LessThan(-1));
            Assert.That(Read<string>("PresentationState"),Is.EqualTo("RunForward"));
            devices.Set(pad.leftStick,Vector2.zero);yield return new WaitForSeconds(.25f);
            Assert.That(Read<string>("PresentationState"),Is.EqualTo("CombatIdle_B1"));
        }
        [UnityTest]public IEnumerator ExistingAirDashContinuesToRejectYAndB()
        {
            devices.Press(pad.buttonSouth);yield return new WaitForSeconds(.15f);
            devices.Set(pad.leftStick,Vector2.right);devices.Press(pad.rightShoulder);yield return new WaitForSeconds(.04f);
            Assert.That(player.GetComponent<QusapDashMotor>().IsDashing,Is.True);
            devices.Press(pad.buttonNorth);yield return new WaitForSeconds(.025f);devices.Release(pad.buttonNorth);
            Assert.That(player.GetComponent<QusapCombatController>().IsAttacking,Is.False);
            devices.Press(pad.buttonEast);yield return new WaitForSeconds(.025f);devices.Release(pad.buttonEast);
            Assert.That(player.GetComponent<QusapCombatController>().IsAttacking,Is.False);
            Assert.That(Read<Animator>("Animator").applyRootMotion,Is.False);
        }
    }
}
#endif
