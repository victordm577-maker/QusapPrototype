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
        private InputTestFixture inputFixture;
        private string[] inputDevicesBeforeFixture;
        private int[] gamepadIdsBeforeFixture;
        private int keyboardCurrentBeforeFixture;
        private int gamepadCurrentBeforeFixture;
        private float timeScaleBeforeFixture;

        [UnitySetUp]
        public IEnumerator LoadPreview()
        {
            timeScaleBeforeFixture = Time.timeScale;
            Time.timeScale = 1f;
            inputDevicesBeforeFixture = CaptureInputDevices();
            gamepadIdsBeforeFixture = Gamepad.all.Select(item => item.deviceId).ToArray();
            keyboardCurrentBeforeFixture = Keyboard.current?.deviceId ?? 0;
            gamepadCurrentBeforeFixture = Gamepad.current?.deviceId ?? 0;
            Debug.Log($"[Qusap Modular Input] Before fixture: "
                + $"{string.Join("; ", inputDevicesBeforeFixture)} | "
                + CurrentInputDevices());

            inputFixture = new InputTestFixture();
            inputFixture.Setup();
            keyboard = InputSystem.AddDevice<Keyboard>();
            gamepad = InputSystem.AddDevice<Gamepad>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.Update();

            Assert.That(InputSystem.devices, Has.Count.EqualTo(2));
            Assert.That(InputSystem.devices[0], Is.SameAs(keyboard));
            Assert.That(InputSystem.devices[1], Is.SameAs(gamepad));
            Assert.That(Keyboard.current, Is.SameAs(keyboard));
            Assert.That(Gamepad.current, Is.SameAs(gamepad));
            Assert.That(Gamepad.all, Has.Count.EqualTo(1));
            Assert.That(Gamepad.all[0], Is.SameAs(gamepad));
            Assert.That(keyboard.native, Is.False);
            Assert.That(gamepad.native, Is.False);
            Debug.Log($"[Qusap Modular Input] Test devices: "
                + $"{string.Join("; ", CaptureInputDevices())} | "
                + CurrentInputDevices());

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
            Time.timeScale = 1f;
            if (keyboard != null && keyboard.added)
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            if (gamepad != null && gamepad.added)
                InputSystem.QueueStateEvent(gamepad, new GamepadState());
            InputSystem.Update();

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                root.SetActive(false);
                Object.Destroy(root);
            }
            yield return null;
            yield return null;

            keyboard = null;
            gamepad = null;
            try
            {
                inputFixture.TearDown();
            }
            finally
            {
                inputFixture = null;
                Time.timeScale = timeScaleBeforeFixture;
            }

            string[] restoredDevices = CaptureInputDevices();
            Debug.Log($"[Qusap Modular Input] After fixture: "
                + $"{string.Join("; ", restoredDevices)} | "
                + CurrentInputDevices());
            Assert.That(restoredDevices, Is.EqualTo(inputDevicesBeforeFixture));
            Assert.That(Gamepad.all.Select(item => item.deviceId).ToArray(),
                Is.EqualTo(gamepadIdsBeforeFixture));
            Assert.That(Keyboard.current?.deviceId ?? 0,
                Is.EqualTo(keyboardCurrentBeforeFixture));
            Assert.That(Gamepad.current?.deviceId ?? 0,
                Is.EqualTo(gamepadCurrentBeforeFixture));
            inputDevicesBeforeFixture = null;
            gamepadIdsBeforeFixture = null;
            keyboardCurrentBeforeFixture = 0;
            gamepadCurrentBeforeFixture = 0;
            timeScaleBeforeFixture = 1f;
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

            inputFixture.Press(keyboard.dKey);
            inputFixture.Set(gamepad.leftStick, Vector2.left);
            InputSystem.Update();
            for (int i = 0; i < 8; i++)
                yield return new WaitForFixedUpdate();
            Assert.That(playerOne.Root.transform.position.x, Is.GreaterThan(playerOneStartX + 0.05f));
            Assert.That(playerTwo.Root.transform.position.x, Is.LessThan(playerTwoStartX - 0.05f));

            inputFixture.Release(keyboard.dKey);
            inputFixture.Set(gamepad.leftStick, Vector2.zero);
            inputFixture.Press(keyboard.spaceKey);
            inputFixture.Press(gamepad.buttonSouth);
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(playerOne.Root.GetComponent<Rigidbody>().linearVelocity.y, Is.GreaterThan(0f));
            Assert.That(playerTwo.Root.GetComponent<Rigidbody>().linearVelocity.y, Is.GreaterThan(0f));

            inputFixture.Release(keyboard.spaceKey);
            inputFixture.Release(gamepad.buttonSouth);
            InputSystem.Update();
        }

        [UnityTest]
        public IEnumerator DashAndFacingUseExistingLogicWithoutDuplicatingVisualOrWeapon()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            QusapEquippedWeaponPresenter weaponPresenter =
                player.Root.GetComponent<QusapEquippedWeaponPresenter>();
            GameObject weaponVisual = weaponPresenter.EquippedVisual;
            QusapModularVisualRig rig = player.Rig;

            inputFixture.Press(keyboard.aKey);
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            yield return new WaitForEndOfFrame();
            Assert.That(player.Combat.FacingDirection, Is.EqualTo(-1));
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(
                player.Facing.OrientationPivot.localEulerAngles.y,
                player.Facing.LeftFacingYaw)), Is.LessThan(0.01f));

            inputFixture.Press(keyboard.leftShiftKey);
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(player.Root.GetComponent<QusapDashMotor>().IsDashing, Is.True);
            Assert.That(player.Root.GetComponentInChildren<QusapModularVisualRig>(), Is.SameAs(rig));
            Assert.That(weaponPresenter.EquippedVisual, Is.SameAs(weaponVisual));
            Assert.That(weaponPresenter.WeaponSocket.childCount, Is.EqualTo(1));

            inputFixture.Release(keyboard.leftShiftKey);
            inputFixture.Release(keyboard.aKey);
            InputSystem.Update();
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

            inputFixture.Press(gamepad.buttonNorth);
            InputSystem.Update();
            yield return null;
            yield return new WaitForFixedUpdate();
            Assert.That(playerTwo.Combat.CurrentAttackVariant, Is.EqualTo(QusapAttackVariant.WeaponLight));
            Assert.That(playerTwo.Root.GetComponent<QusapEquippedWeaponPresenter>().EquippedVisual,
                Is.Not.Null);
            inputFixture.Release(gamepad.buttonNorth);
            InputSystem.Update();
        }

        [Test]
        public void WeaponBootstrapKeepsOneVisibleSwordAndDistinctStableIdentities()
        {
            QusapWeaponMatchBootstrap bootstrap = Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
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
        public void ProceduralBodyAttackMovesOneFootClearlyWithoutMovingPhysicalRoot()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Vector3 rootPosition = player.Root.transform.position;
            Vector3 leftStart = player.Rig.FootPivotLeft.position;
            Vector3 rightStart = player.Rig.FootPivotRight.position;
            var context = new QusapCombatVisualContext(
                1001, QusapCombatCommand.BodyAttack, null, -1, false, 1,
                QusapAttackPhase.Active, 0.68f);

            Assert.That(player.Procedural.Present(context), Is.True);

            float leftTravel = Vector3.Distance(leftStart, player.Rig.FootPivotLeft.position);
            float rightTravel = Vector3.Distance(rightStart, player.Rig.FootPivotRight.position);
            Assert.That(leftTravel, Is.GreaterThan(0.2f));
            Assert.That(leftTravel, Is.GreaterThan(rightTravel * 2f));
            Assert.That(player.Root.transform.position, Is.EqualTo(rootPosition));
        }

        [Test]
        public void CombatPublishesImmutableContextAndCancellationReason()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Assert.That(player.Combat.TryStartAttack(QusapAttackType.WeakKick), Is.True);
            Assert.That(player.Combat.TryGetCombatVisualContext(
                out QusapCombatVisualContext context), Is.True);
            Assert.That(context.AttackExecutionId, Is.GreaterThan(0));
            Assert.That(context.Command, Is.EqualTo(QusapCombatCommand.BodyAttack));
            Assert.That(context.Phase, Is.EqualTo(QusapAttackPhase.Startup));
            Assert.That(context.CapturedFacing, Is.EqualTo(player.Combat.AttackDirection));
            player.Combat.CancelAttack();
            Assert.That(player.Combat.TryGetCombatVisualContext(out _), Is.False);
            Assert.That(player.Combat.LastCombatVisualContext.AttackExecutionId,
                Is.EqualTo(context.AttackExecutionId));
            Assert.That(player.Combat.LastCombatVisualContext.CancellationReason,
                Is.EqualTo(QusapCombatVisualCancellationReason.Cancelled));
        }

        [Test]
        public void LightCutsAreComplementaryAndStrongArcIsLarger()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Transform sword = player.Root.GetComponent<QusapEquippedWeaponPresenter>()
                .EquippedVisual.transform;
            Quaternion neutral = sword.localRotation;

            player.Procedural.Present(new QusapCombatVisualContext(
                1010, QusapCombatCommand.WeaponLight, null, 0, false, 1,
                QusapAttackPhase.Active, 0.62f));
            float firstAngle = Quaternion.Angle(neutral, sword.localRotation);
            Quaternion firstRotation = sword.localRotation;
            player.Procedural.Present(new QusapCombatVisualContext(
                1011, QusapCombatCommand.WeaponLight, null, 1, false, 1,
                QusapAttackPhase.Active, 0.62f));
            float secondAngle = Quaternion.Angle(neutral, sword.localRotation);
            Quaternion secondRotation = sword.localRotation;
            player.Procedural.Present(new QusapCombatVisualContext(
                1012, QusapCombatCommand.WeaponStrong, null, -1, false, 1,
                QusapAttackPhase.Active, 0.62f));
            float strongAngle = Quaternion.Angle(neutral, sword.localRotation);

            Assert.That(Quaternion.Angle(firstRotation, secondRotation), Is.GreaterThan(50f));
            Assert.That(firstAngle, Is.GreaterThan(45f));
            Assert.That(secondAngle, Is.GreaterThan(45f));
            Assert.That(strongAngle, Is.GreaterThan(firstAngle + 20f));
        }

        [Test]
        public void HeadbuttAndLaunchFinisherMoveVisualOnly()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Vector3 rootPosition = player.Root.transform.position;
            Vector3 bodyStart = player.Rig.BodyPivot.position;
            Vector3 footStart = player.Rig.FootPivotLeft.position;
            player.Procedural.Present(new QusapCombatVisualContext(
                1020, QusapCombatCommand.Headbutt, null, -1, false, 1,
                QusapAttackPhase.Active, 0.68f));
            Assert.That(Vector3.Distance(bodyStart, player.Rig.BodyPivot.position),
                Is.GreaterThan(0.2f));

            player.Procedural.Present(new QusapCombatVisualContext(
                1021, QusapCombatCommand.BodyAttack, QusapComboId.Launch, 2, true, 1,
                QusapAttackPhase.Active, 0.68f));
            Assert.That(player.Rig.FootPivotLeft.position.y,
                Is.GreaterThan(footStart.y + 0.2f));
            Assert.That(player.Root.transform.position, Is.EqualTo(rootPosition));
        }

        [Test]
        public void CancellationAndWeaponChangeReturnEveryOwnedTransformToNeutral()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Vector3 body = player.Rig.BodyPivot.localPosition;
            Quaternion left = player.Rig.FootPivotLeft.localRotation;
            Transform sword = player.Root.GetComponent<QusapEquippedWeaponPresenter>()
                .EquippedVisual.transform;
            Vector3 swordPosition = sword.localPosition;
            Quaternion swordRotation = sword.localRotation;
            player.Procedural.Present(new QusapCombatVisualContext(
                1030, QusapCombatCommand.WeaponStrong, null, -1, false, 1,
                QusapAttackPhase.Active, 0.68f));
            player.Procedural.CancelExecution(
                1030, QusapCombatVisualCancellationReason.Disarmed);

            Assert.That(player.Rig.BodyPivot.localPosition, Is.EqualTo(body));
            Assert.That(player.Rig.FootPivotLeft.localRotation, Is.EqualTo(left));
            Assert.That(sword.localPosition, Is.EqualTo(swordPosition));
            Assert.That(sword.localRotation, Is.EqualTo(swordRotation));
        }

        [TestCase(QusapProceduralCombatMotionId.BodyAttack)]
        [TestCase(QusapProceduralCombatMotionId.WeaponLightFirst)]
        [TestCase(QusapProceduralCombatMotionId.WeaponLightSecond)]
        [TestCase(QusapProceduralCombatMotionId.WeaponStrong)]
        [TestCase(QusapProceduralCombatMotionId.Headbutt)]
        [TestCase(QusapProceduralCombatMotionId.DamageBodyAttack)]
        [TestCase(QusapProceduralCombatMotionId.DamageFinisher)]
        [TestCase(QusapProceduralCombatMotionId.DisarmFinisher)]
        [TestCase(QusapProceduralCombatMotionId.LaunchWeaponLight)]
        [TestCase(QusapProceduralCombatMotionId.LaunchFinisher)]
        public void EveryConfiguredChoreographyProducesVisibleRigidMotion(
            QusapProceduralCombatMotionId motionId)
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Transform sword = player.Root.GetComponent<QusapEquippedWeaponPresenter>()
                .EquippedVisual.transform;
            Vector3 body = player.Rig.BodyPivot.localPosition;
            Vector3 left = player.Rig.FootPivotLeft.localPosition;
            Vector3 right = player.Rig.FootPivotRight.localPosition;
            Quaternion weapon = sword.localRotation;
            QusapCombatVisualContext context = ContextForMotion(
                1100 + (ulong)motionId, motionId, 1);

            Assert.That(player.Procedural.Present(context), Is.True);
            bool moved = player.Rig.BodyPivot.localPosition != body
                || player.Rig.FootPivotLeft.localPosition != left
                || player.Rig.FootPivotRight.localPosition != right
                || sword.localRotation != weapon;
            Assert.That(moved, Is.True, motionId.ToString());
            Assert.That(player.Rig.BodyPivot.localScale, Is.EqualTo(Vector3.one));
            Assert.That(player.Rig.FootPivotLeft.localScale, Is.EqualTo(Vector3.one));
            Assert.That(player.Rig.FootPivotRight.localScale, Is.EqualTo(Vector3.one));
        }

        [TestCase(-1)]
        [TestCase(1)]
        public void BothFacingsUseCapturedDirectionWithoutNegativeScale(int facing)
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Assert.That(player.Procedural.Present(ContextForMotion(
                (ulong)(1200 + facing + 1),
                QusapProceduralCombatMotionId.BodyAttack,
                facing)), Is.True);
            Assert.That(player.Procedural.CombatFacingCapturePivot.localScale,
                Is.EqualTo(Vector3.one));
            Assert.That(player.Rig.GetComponentsInChildren<Transform>(true)
                .All(item => item.localScale.x > 0f
                    && item.localScale.y > 0f
                    && item.localScale.z > 0f), Is.True);
        }

        [TestCase(QusapProceduralCombatMotionId.BodyAttack)]
        [TestCase(QusapProceduralCombatMotionId.WeaponStrong)]
        [TestCase(QusapProceduralCombatMotionId.DisarmFinisher)]
        [TestCase(QusapProceduralCombatMotionId.LaunchFinisher)]
        public void ChoreographyNeverChangesRigidbodyColliderOrHitbox(
            QusapProceduralCombatMotionId motionId)
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Rigidbody body = player.Root.GetComponent<Rigidbody>();
            CapsuleCollider collider = player.Root.GetComponent<CapsuleCollider>();
            QusapAttackHitbox hitbox = player.Root.GetComponentInChildren<QusapAttackHitbox>(true);
            Vector3 rootPosition = body.position;
            float height = collider.height;
            Vector3 center = collider.center;
            Vector3 hitboxPosition = hitbox.transform.localPosition;
            Quaternion hitboxRotation = hitbox.transform.localRotation;
            Vector3 hitboxScale = hitbox.transform.localScale;

            player.Procedural.Present(ContextForMotion(
                1300 + (ulong)motionId, motionId, 1));

            Assert.That(body.position, Is.EqualTo(rootPosition));
            Assert.That(collider.height, Is.EqualTo(height));
            Assert.That(collider.center, Is.EqualTo(center));
            Assert.That(hitbox.transform.localPosition, Is.EqualTo(hitboxPosition));
            Assert.That(hitbox.transform.localRotation, Is.EqualTo(hitboxRotation));
            Assert.That(hitbox.transform.localScale, Is.EqualTo(hitboxScale));
        }

        [Test]
        public void OneHundredPresentedAttacksReturnExactlyToNeutral()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            Vector3 body = player.Rig.BodyPivot.localPosition;
            Vector3 left = player.Rig.FootPivotLeft.localPosition;
            Quaternion right = player.Rig.FootPivotRight.localRotation;
            for (ulong id = 2000; id < 2100; id++)
            {
                player.Procedural.Present(ContextForMotion(
                    id, QusapProceduralCombatMotionId.BodyAttack, 1));
                player.Procedural.CancelExecution(
                    id, QusapCombatVisualCancellationReason.Completed);
            }
            Assert.That(player.Rig.BodyPivot.localPosition, Is.EqualTo(body));
            Assert.That(player.Rig.FootPivotLeft.localPosition, Is.EqualTo(left));
            Assert.That(player.Rig.FootPivotRight.localRotation, Is.EqualTo(right));
        }

        [Test]
        public void TwoPlayersPresentSimultaneouslyWithoutSharedState()
        {
            PlayerView first = Player(QusapLocalPlayerSlot.Player1Keyboard);
            PlayerView second = Player(QusapLocalPlayerSlot.Player2Gamepad);
            first.Procedural.Present(ContextForMotion(
                3001, QusapProceduralCombatMotionId.BodyAttack, 1));
            second.Procedural.Present(ContextForMotion(
                4001, QusapProceduralCombatMotionId.Headbutt, -1));
            Assert.That(first.Procedural.ActiveExecutionId, Is.EqualTo(3001));
            Assert.That(second.Procedural.ActiveExecutionId, Is.EqualTo(4001));
            Assert.That(first.Procedural.CurrentMotionId,
                Is.EqualTo(QusapProceduralCombatMotionId.BodyAttack));
            Assert.That(second.Procedural.CurrentMotionId,
                Is.EqualTo(QusapProceduralCombatMotionId.Headbutt));
        }

        [Test]
        public void ProceduralPresentationCreatesNoExtraRendererOrWeaponInstance()
        {
            PlayerView player = Player(QusapLocalPlayerSlot.Player1Keyboard);
            QusapEquippedWeaponPresenter weapon =
                player.Root.GetComponent<QusapEquippedWeaponPresenter>();
            GameObject instance = weapon.EquippedVisual;
            int rendererCount = player.Root.GetComponentsInChildren<Renderer>(true).Length;
            player.Procedural.Present(ContextForMotion(
                5001, QusapProceduralCombatMotionId.DamageFinisher, 1));
            Assert.That(weapon.EquippedVisual, Is.SameAs(instance));
            Assert.That(weapon.WeaponSocket.childCount, Is.EqualTo(1));
            Assert.That(player.Root.GetComponentsInChildren<Renderer>(true).Length,
                Is.EqualTo(rendererCount));
            Assert.That(player.Root.GetComponentsInChildren<Animator>(false), Is.Empty);
        }

        [Test]
        public void TwoPlayersCanPresentIndependentExecutions()
        {
            PlayerView first = Player(QusapLocalPlayerSlot.Player1Keyboard);
            PlayerView second = Player(QusapLocalPlayerSlot.Player2Gamepad);
            Vector3 secondNeutral = second.Rig.BodyPivot.localPosition;
            first.Procedural.Present(new QusapCombatVisualContext(
                1040, QusapCombatCommand.BodyAttack, null, -1, false, 1,
                QusapAttackPhase.Active, 0.68f));

            Assert.That(first.Procedural.ActiveExecutionId, Is.EqualTo(1040));
            Assert.That(second.Procedural.ActiveExecutionId, Is.Not.EqualTo(1040));
            Assert.That(second.Rig.BodyPivot.localPosition, Is.EqualTo(secondNeutral));
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
                Assert.That(player.Root.GetComponent<QusapWeaponAttackVisualPresenter>().enabled, Is.False);
                Assert.That(player.Root.GetComponent<QusapModularCombatVisualPresenter>().enabled, Is.True);
                Assert.That(player.Root.GetComponentsInChildren<QusapHurtbox>(true), Has.Length.EqualTo(1));
                Assert.That(player.Root.GetComponentsInChildren<QusapAttackHitbox>(true), Has.Length.EqualTo(1));
            }
        }

        private static PlayerView[] Players()
        {
            return Object.FindObjectsByType<QusapInputReader>(FindObjectsInactive.Include)
                .Select(input => new PlayerView(input.gameObject))
                .OrderBy(player => player.Input.LocalPlayerSlot)
                .ToArray();
        }

        private static string[] CaptureInputDevices()
        {
            return InputSystem.devices
                .Select(device => $"{device.GetType().Name}:id={device.deviceId}:"
                    + $"layout={device.layout}:native={device.native}")
                .ToArray();
        }

        private static string CurrentInputDevices()
        {
            string keyboardCurrent = Keyboard.current != null
                ? $"Keyboard.current={Keyboard.current.deviceId}"
                : "Keyboard.current=none";
            string gamepadCurrent = Gamepad.current != null
                ? $"Gamepad.current={Gamepad.current.deviceId}"
                : "Gamepad.current=none";
            return $"{keyboardCurrent}, {gamepadCurrent}";
        }

        private static QusapCombatVisualContext ContextForMotion(
            ulong executionId,
            QusapProceduralCombatMotionId motionId,
            int facing)
        {
            QusapCombatCommand command = QusapCombatCommand.BodyAttack;
            QusapComboId? combo = null;
            int step = -1;
            bool finisher = false;
            switch (motionId)
            {
                case QusapProceduralCombatMotionId.WeaponLightFirst:
                    command = QusapCombatCommand.WeaponLight;
                    step = 0;
                    break;
                case QusapProceduralCombatMotionId.WeaponLightSecond:
                    command = QusapCombatCommand.WeaponLight;
                    step = 1;
                    break;
                case QusapProceduralCombatMotionId.WeaponStrong:
                    command = QusapCombatCommand.WeaponStrong;
                    break;
                case QusapProceduralCombatMotionId.Headbutt:
                    command = QusapCombatCommand.Headbutt;
                    break;
                case QusapProceduralCombatMotionId.DamageBodyAttack:
                    combo = QusapComboId.Damage;
                    step = 2;
                    break;
                case QusapProceduralCombatMotionId.DamageFinisher:
                    command = QusapCombatCommand.WeaponStrong;
                    combo = QusapComboId.Damage;
                    step = 3;
                    finisher = true;
                    break;
                case QusapProceduralCombatMotionId.DisarmFinisher:
                    command = QusapCombatCommand.Headbutt;
                    combo = QusapComboId.Disarm;
                    step = 2;
                    finisher = true;
                    break;
                case QusapProceduralCombatMotionId.LaunchWeaponLight:
                    command = QusapCombatCommand.WeaponLight;
                    combo = QusapComboId.Launch;
                    step = 1;
                    break;
                case QusapProceduralCombatMotionId.LaunchFinisher:
                    combo = QusapComboId.Launch;
                    step = 2;
                    finisher = true;
                    break;
            }

            return new QusapCombatVisualContext(
                executionId,
                command,
                combo,
                step,
                finisher,
                facing,
                QusapAttackPhase.Active,
                0.62f);
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
                Procedural = root.GetComponent<QusapModularCombatVisualPresenter>();
            }

            public GameObject Root { get; }
            public QusapInputReader Input { get; }
            public QusapCombatController Combat { get; }
            public QusapModularVisualRig Rig { get; }
            public QusapModularFacingPresenter Facing { get; }
            public QusapModularCombatVisualPresenter Procedural { get; }
        }
    }
}
#endif
