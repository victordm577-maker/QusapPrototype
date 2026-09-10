#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapModularVisualPlayModeTests
    {
        private const string PreviewScenePath =
            "Assets/_Qusap/Scenes/CombatPlayground_ModularVisualPreview.unity";

        private Keyboard keyboard;
        private Gamepad gamepad;

        [UnitySetUp]
        public IEnumerator LoadPreview()
        {
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(
                PreviewScenePath,
                new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone)
                yield return null;

            for (int i = 0; i < 8; i++)
                yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator UnloadPreview()
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
                Object.Destroy(root);
            yield return null;

            if (keyboard != null && keyboard.added)
                InputSystem.RemoveDevice(keyboard);
            if (gamepad != null && gamepad.added)
                InputSystem.RemoveDevice(gamepad);
        }

        [Test]
        public void PreviewContainsTwoVisibleModularPlayersAndNoActiveLegacyVisual()
        {
            PlayerView[] players = Players();
            Assert.That(players, Has.Length.EqualTo(2));
            foreach (PlayerView player in players)
            {
                Assert.That(player.Rig, Is.Not.Null);
                Assert.That(player.Rig.Body.gameObject.activeInHierarchy, Is.True);
                Assert.That(player.Rig.FootLeft.gameObject.activeInHierarchy, Is.True);
                Assert.That(player.Rig.FootRight.gameObject.activeInHierarchy, Is.True);
                Assert.That(player.Root.transform.Find("PlayerVisual").gameObject.activeInHierarchy, Is.False);
                Assert.That(player.Root.transform.Find("PlayerVisual_v1_Backup").gameObject.activeInHierarchy, Is.False);
                Assert.That(player.Root.GetComponentsInChildren<QusapModularVisualRig>(false),
                    Has.Length.EqualTo(1));
            }
        }

        [Test]
        public void SpawnAlignmentAndPhysicalRootRemainCorrect()
        {
            foreach (PlayerView player in Players())
            {
                CapsuleCollider capsule = player.Root.GetComponent<CapsuleCollider>();
                Rigidbody body = player.Root.GetComponent<Rigidbody>();
                Assert.That(capsule, Is.Not.Null);
                Assert.That(capsule.height, Is.EqualTo(2f));
                Assert.That(capsule.center, Is.EqualTo(Vector3.zero));
                Assert.That(body, Is.Not.Null);
                Assert.That(body.useGravity, Is.True);
                Assert.That(body.constraints,
                    Is.EqualTo(RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionZ));
                Assert.That(player.Root.GetComponents<Rigidbody>(), Has.Length.EqualTo(1));
                Assert.That(player.Root.GetComponents<Collider>(), Has.Length.EqualTo(1));

                Bounds visualBounds = CombinedBounds(player.Rig.GetComponentsInChildren<Renderer>());
                float colliderBottom = player.Root.transform.position.y + capsule.center.y - capsule.height * 0.5f;
                Assert.That(visualBounds.min.y, Is.EqualTo(colliderBottom).Within(0.06f));
                Assert.That(visualBounds.size.y, Is.EqualTo(1.902766f).Within(0.08f));
                Assert.That(player.Rig.transform.lossyScale.x, Is.EqualTo(1f).Within(0.001f));
                Assert.That(player.Rig.transform.lossyScale.y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(player.Rig.transform.lossyScale.z, Is.EqualTo(1f).Within(0.001f));
            }
        }

        [UnityTest]
        public IEnumerator KeyboardAndGamepadPlayersMoveAndJumpWithExistingBindings()
        {
            PlayerView playerOne = Player(QusapLocalPlayerSlot.Player1Keyboard);
            PlayerView playerTwo = Player(QusapLocalPlayerSlot.Player2Gamepad);
            float playerOneStartX = playerOne.Root.transform.position.x;
            float playerTwoStartX = playerTwo.Root.transform.position.x;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            InputSystem.QueueStateEvent(gamepad, new GamepadState { leftStick = Vector2.left });
            InputSystem.Update();
            for (int i = 0; i < 8; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(playerOne.Root.transform.position.x, Is.GreaterThan(playerOneStartX + 0.05f));
            Assert.That(playerTwo.Root.transform.position.x, Is.LessThan(playerTwoStartX - 0.05f));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
            InputSystem.QueueStateEvent(
                gamepad,
                new GamepadState().WithButton(GamepadButton.South));
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(playerOne.Root.GetComponent<Rigidbody>().linearVelocity.y, Is.GreaterThan(0f));
            Assert.That(playerTwo.Root.GetComponent<Rigidbody>().linearVelocity.y, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator DashAndFacingUseExistingLogicWithoutDuplicatingVisualOrWeapon()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            QusapEquippedWeaponPresenter weaponPresenter =
                player.Root.GetComponent<QusapEquippedWeaponPresenter>();
            GameObject weaponVisual = weaponPresenter.EquippedVisual;
            QusapModularVisualRig rig = player.Rig;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();
            Assert.That(player.Combat.FacingDirection, Is.EqualTo(-1));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(
                player.Facing.OrientationPivot.localEulerAngles.y,
                player.Facing.LeftFacingYaw)), Is.LessThan(0.01f));

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A, Key.LeftShift));
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(player.Root.GetComponent<QusapDashMotor>().IsDashing, Is.True);
            Assert.That(player.Root.GetComponentInChildren<QusapModularVisualRig>(), Is.SameAs(rig));
            Assert.That(weaponPresenter.EquippedVisual, Is.SameAs(weaponVisual));
            Assert.That(weaponPresenter.WeaponSocket.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator BodyAndEquippedSwordAttacksStillStartOnLogicalRoot()
        {
            PlayerView playerOne = Player(QusapLocalPlayerSlot.Player1Keyboard);
            PlayerView playerTwo = Player(QusapLocalPlayerSlot.Player2Gamepad);
            Assert.That(playerOne.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            Assert.That(playerOne.Combat.CurrentAttackVariant,
                Is.EqualTo(QusapAttackVariant.WeakKickGround)
                    .Or.EqualTo(QusapAttackVariant.WeakKickAir));
            QusapAttackHitbox hitbox = playerOne.Root.GetComponentInChildren<QusapAttackHitbox>(true);
            Assert.That(hitbox, Is.Not.Null);
            Assert.That(hitbox.transform.IsChildOf(playerOne.Root.transform), Is.True);
            Assert.That(hitbox.transform.IsChildOf(playerOne.Rig.transform), Is.False);

            InputSystem.QueueStateEvent(
                gamepad,
                new GamepadState().WithButton(GamepadButton.North));
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(playerTwo.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.WeaponLight));
            Assert.That(playerTwo.Root.GetComponent<QusapEquippedWeaponPresenter>().EquippedVisual,
                Is.Not.Null);
        }

        [Test]
        public void WeaponBootstrapKeepsOneVisibleSwordAndDistinctStableIdentities()
        {
            QusapWeaponMatchBootstrap bootstrap = Object.FindFirstObjectByType<QusapWeaponMatchBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.PlayerOneWeapon, Is.Not.Null);
            Assert.That(bootstrap.PlayerTwoWeapon, Is.Not.Null);
            Assert.That(bootstrap.PlayerOneWeapon.InstanceId,
                Is.Not.EqualTo(bootstrap.PlayerTwoWeapon.InstanceId));
            foreach (PlayerView player in Players())
            {
                QusapEquippedWeaponPresenter presenter =
                    player.Root.GetComponent<QusapEquippedWeaponPresenter>();
                Assert.That(presenter.EquippedVisual, Is.Not.Null);
                Assert.That(presenter.WeaponSocket.childCount, Is.EqualTo(1));
                Assert.That(player.Root.GetComponentsInChildren<Transform>(true)
                    .Count(item => item.name == "WeaponSocket"), Is.EqualTo(1));
            }
        }

        [UnityTest]
        public IEnumerator ReactivationRestoresNeutralPoseWithoutRootMotion()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            QusapModularVisualRig rig = player.Rig;
            Vector3 rootPosition = player.Root.transform.position;
            Vector3 bodyPosition = rig.BodyPivot.localPosition;
            Quaternion footRotation = rig.FootPivotLeft.localRotation;
            rig.BodyPivot.localPosition += Vector3.up * 0.4f;
            rig.FootPivotLeft.localRotation *= Quaternion.Euler(15f, 0f, 0f);

            player.Root.SetActive(false);
            player.Root.SetActive(true);
            yield return null;

            Assert.That(rig.BodyPivot.localPosition, Is.EqualTo(bodyPosition));
            Assert.That(rig.FootPivotLeft.localRotation, Is.EqualTo(footRotation));
            Assert.That(player.Root.transform.position, Is.EqualTo(rootPosition));
            Assert.That(player.Root.GetComponentsInChildren<Animator>(false), Is.Empty);
            Assert.That(player.Root.GetComponent<QusapAnimationDriver>().enabled, Is.False);
        }

        [Test]
        public void WallMovementCombatAndWeaponSystemsRemainOnLogicalRoot()
        {
            foreach (PlayerView player in Players())
            {
                Assert.That(player.Root.GetComponent<QusapHorizontalMotor>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapVerticalMotor>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapWallSensor>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapDashMotor>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapCombatController>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapWeaponEquipment>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapEquippedWeaponPresenter>().enabled, Is.True);
                Assert.That(player.Root.GetComponent<QusapWeaponAttackVisualPresenter>().enabled, Is.True);
                Assert.That(player.Root.GetComponentsInChildren<QusapHurtbox>(true), Has.Length.EqualTo(1));
                Assert.That(player.Root.GetComponentsInChildren<QusapAttackHitbox>(true), Has.Length.EqualTo(1));
            }
        }

        private static PlayerView[] Players()
        {
            return Object.FindObjectsByType<QusapInputReader>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Select(input => new PlayerView(input.gameObject))
                .OrderBy(player => player.Input.LocalPlayerSlot)
                .ToArray();
        }

        private static PlayerView Player(QusapLocalPlayerSlot slot)
        {
            PlayerView[] matches = Players().Where(player => player.Input.LocalPlayerSlot == slot).ToArray();
            Assert.That(matches, Has.Length.EqualTo(1), slot.ToString());
            return matches[0];
        }

        private static Bounds CombinedBounds(Renderer[] renderers)
        {
            Assert.That(renderers, Is.Not.Empty);
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        private readonly struct PlayerView
        {
            public PlayerView(GameObject root)
            {
                Root = root;
                Input = root.GetComponent<QusapInputReader>();
                Combat = root.GetComponent<QusapCombatController>();
                Rig = root.GetComponentInChildren<QusapModularVisualRig>(true);
                Facing = root.GetComponent<QusapModularFacingPresenter>();
            }

            public GameObject Root { get; }
            public QusapInputReader Input { get; }
            public QusapCombatController Combat { get; }
            public QusapModularVisualRig Rig { get; }
            public QusapModularFacingPresenter Facing { get; }
        }
    }
}
#endif
