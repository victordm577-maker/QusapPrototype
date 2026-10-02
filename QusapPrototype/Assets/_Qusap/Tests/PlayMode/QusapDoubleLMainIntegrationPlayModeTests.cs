#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapDoubleLMainIntegrationPlayModeTests
    {
        InputTestFixture devices;
        Gamepad pad;
        GameObject[] players;
        static Behaviour View(GameObject player) => player.GetComponents<Behaviour>()
            .Single(component=>component.GetType().Name=="QusapDoubleLGroundAttackPresenter");
        static T Read<T>(object view,string property) => (T)view.GetType().GetProperty(property).GetValue(view);
        [UnitySetUp]public IEnumerator SetUp()
        {
            devices=new InputTestFixture();devices.Setup();
            InputSystem.AddDevice<Keyboard>();pad=InputSystem.AddDevice<Gamepad>();
            var load=EditorSceneManager.LoadSceneAsyncInPlayMode("Assets/_Qusap/Scenes/CombatPlayground.unity",new LoadSceneParameters(LoadSceneMode.Single));
            while(!load.isDone)yield return null;
            for(int i=0;i<8;i++)yield return new WaitForFixedUpdate();
            players=UnityEngine.Object.FindObjectsByType<QusapInputReader>()
                .OrderBy(input=>input.LocalPlayerSlot).Select(input=>input.gameObject).ToArray();
        }
        [UnityTearDown]public IEnumerator TearDown()
        {
            foreach(var root in SceneManager.GetActiveScene().GetRootGameObjects()){root.SetActive(false);UnityEngine.Object.Destroy(root);}
            yield return null;yield return null;devices.TearDown();
        }
        [Test]public void RealSceneInheritsTwoMainPresentersAndOneVisibleSwordPerPlayer()
        {
            var match=UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            Assert.That(match.IsInitialized,Is.True);
            Assert.That(match.InitialWhiteWeapon,Is.Not.Null);
            Assert.That(match.PlayerOneWeapon,Is.SameAs(match.PlayerOneEquipment.EquippedWeapon));
            Assert.That(match.PlayerTwoWeapon,Is.SameAs(match.PlayerTwoEquipment.EquippedWeapon));
            Assert.That(players,Has.Length.EqualTo(2));
            foreach(var player in players)
            {
                var view=View(player);Assert.That(view.enabled,Is.True);
                var visual=Read<GameObject>(view,"Visual");
                var meshes=player.GetComponentsInChildren<Renderer>(false).Where(r=>r.enabled&&(r is MeshRenderer||r is SkinnedMeshRenderer)).ToArray();
                Assert.That(meshes,Has.Length.EqualTo(2));Assert.That(meshes.All(r=>r.transform.IsChildOf(visual.transform)),Is.True);
                Assert.That(visual.GetComponentsInChildren<MeshFilter>(true),Has.Length.EqualTo(1));
                foreach(var animator in player.GetComponentsInChildren<Animator>(true))Assert.That(animator.applyRootMotion,Is.False);
                Assert.That(player.GetComponentsInChildren<Rigidbody>(true),Has.Length.EqualTo(1));
                Assert.That(player.GetComponent<QusapModularCombatVisualPresenter>().enabled,Is.False);
                Assert.That(player.GetComponent<QusapEquippedWeaponPresenter>().enabled,Is.False);
            }
        }
        [UnityTest]public IEnumerator ExistingYAndBBindingsStartApprovedClipsAndPreserveHeavyBrakingBothWays()
        {
            var player=players.Single(p=>p.GetComponent<QusapInputReader>().LocalPlayerSlot==QusapLocalPlayerSlot.Player2Gamepad);
            var combat=player.GetComponent<QusapCombatController>();var view=View(player);var body=player.GetComponent<Rigidbody>();
            int clipAtStart=-1,directionAtStart=0;
            combat.AttackVariantStarted+=variant=>{clipAtStart=Read<int>(view,"ClipIndex");directionAtStart=Read<int>(view,"PresentationDirection");};
            foreach(int side in new[]{-1,1})
            {
                devices.Set(pad.leftStick,new Vector2(side,0));yield return new WaitForSeconds(.15f);
                devices.Press(pad.buttonNorth);yield return new WaitForSeconds(.04f);devices.Release(pad.buttonNorth);
                Assert.That(combat.CurrentAttackVariant,Is.EqualTo(QusapAttackVariant.WeaponLight));
                Assert.That(clipAtStart,Is.EqualTo(0));Assert.That(directionAtStart,Is.EqualTo(side));
                yield return new WaitForSeconds(.40f);
                devices.Press(pad.buttonEast);yield return new WaitForSeconds(.04f);devices.Release(pad.buttonEast);
                Assert.That(combat.CurrentAttackVariant,Is.EqualTo(QusapAttackVariant.WeaponStrong));
                Assert.That(clipAtStart,Is.EqualTo(1));Assert.That(directionAtStart,Is.EqualTo(side));
                Assert.That(body.linearVelocity.x,Is.EqualTo(0).Within(.001f));
                devices.Set(pad.leftStick,Vector2.zero);yield return new WaitForSeconds(.70f);
            }
        }
        [UnityTest]public IEnumerator NativeStartupHitstunCancelsMainPresentationAndRestoresMotorControl()
        {
            var player=players.Single(p=>p.GetComponent<QusapInputReader>().LocalPlayerSlot==QusapLocalPlayerSlot.Player2Gamepad);
            var combat=player.GetComponent<QusapCombatController>();var view=View(player);
            devices.Press(pad.buttonNorth);yield return new WaitForSeconds(.04f);devices.Release(pad.buttonNorth);
            Assert.That(combat.CurrentPhase,Is.EqualTo(QusapAttackPhase.Startup));Assert.That(Read<int>(view,"ClipIndex"),Is.EqualTo(0));
            player.GetComponent<QusapHitstunController>().EnterHitstun(.20f);
            Assert.That(combat.IsAttacking,Is.False);Assert.That(Read<int>(view,"ClipIndex"),Is.EqualTo(-1));
            yield return null;Assert.That(Read<string>(view,"PresentationState"),Is.EqualTo("Hitstun"));
            yield return new WaitForSeconds(.25f);
            Assert.That(player.GetComponent<QusapHorizontalMotor>().enabled,Is.True);
            Assert.That(player.GetComponent<QusapVerticalMotor>().enabled,Is.True);
            Assert.That(player.GetComponent<QusapDashMotor>().enabled,Is.True);
            Assert.That(player.GetComponent<Rigidbody>().useGravity,Is.True);
        }
    }
}
#endif
