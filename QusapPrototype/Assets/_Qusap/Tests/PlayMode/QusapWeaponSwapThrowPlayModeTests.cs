using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Qusap.Tests
{
    public sealed class QusapWeaponSwapThrowPlayModeTests
    {
        private readonly List<GameObject> roots = new();
        private readonly List<GameObject> visualPrefabs = new();
        private readonly List<InputDevice> addedInputDevices = new();
        private QusapWeaponVisualCatalog catalog;
        private ulong nextPressId;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
            for (int i = addedInputDevices.Count - 1; i >= 0; i--)
            {
                if (addedInputDevices[i] != null && addedInputDevices[i].added)
                {
                    InputSystem.RemoveDevice(addedInputDevices[i]);
                }
            }

            catalog = ScriptableObject.CreateInstance<QusapWeaponVisualCatalog>();
            catalog.Configure(
                Entry(QusapWeaponVisualCatalog.BlueDefinitionId, "Blue"),
                Entry(QusapWeaponVisualCatalog.PurpleDefinitionId, "Purple"),
                Entry(QusapWeaponVisualCatalog.WhiteDefinitionId, "White"));
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            for (int i = roots.Count - 1; i >= 0; i--)
            {
                if (roots[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(roots[i]);
                }
            }

            for (int i = visualPrefabs.Count - 1; i >= 0; i--)
            {
                if (visualPrefabs[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualPrefabs[i]);
                }
            }

            if (catalog != null)
            {
                UnityEngine.Object.DestroyImmediate(catalog);
            }

            roots.Clear();
            visualPrefabs.Clear();
            addedInputDevices.Clear();
        }

        [Test]
        public void PlayerOneEIntentSwapsBlueForWhite()
        {
            MatchRig rig = CreateMatch(true, false);

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.True);

            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon,
                Is.SameAs(rig.Bootstrap.InitialWhiteWeapon));
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.WhiteDefinitionId));
        }

        [Test]
        public void PlayerTwoRightTriggerIntentSwapsPurpleForWhite()
        {
            MatchRig rig = CreateMatch(true, true);

            Assert.That(Swap(rig, rig.PlayerTwo, QusapWeaponThrowDirection.Forward), Is.True);

            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon,
                Is.SameAs(rig.Bootstrap.InitialWhiteWeapon));
            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.WhiteDefinitionId));
        }

        [Test]
        public void ArmedPlayersDoNotAutomaticallyCollectInitialWhiteWeapon()
        {
            MatchRig rig = CreateMatch(true, false);
            QusapWeaponInstance playerOne = rig.PlayerOne.Equipment.EquippedWeapon;
            QusapWeaponInstance playerTwo = rig.PlayerTwo.Equipment.EquippedWeapon;

            int pickups = rig.Bootstrap.ProcessPickups(Now(rig) + 0.01d);

            Assert.That(pickups, Is.Zero);
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(playerOne));
            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(playerTwo));
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
        }

        [Test]
        public void ForwardSwapCreatesStrongForwardThrow()
        {
            MatchRig rig = CreateMatch(true, false);
            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);
            Vector3 start = thrown.transform.position;

            thrown.Advance(0.325f);

            Assert.That(thrown.transform.position.x - start.x, Is.GreaterThan(2f));
            Assert.That(thrown.transform.position.y - start.y, Is.GreaterThan(0.7f));
            Assert.That(thrown.ThrowDirection, Is.EqualTo(QusapWeaponThrowDirection.Forward));
        }

        [Test]
        public void UpSwapCreatesHighMostlyVerticalThrow()
        {
            MatchRig rig = CreateMatch(true, false);
            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Up);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);
            Vector3 start = thrown.transform.position;

            thrown.Advance(0.35f);

            Assert.That(Mathf.Abs(thrown.transform.position.x - start.x), Is.LessThan(0.5f));
            Assert.That(thrown.transform.position.y - start.y, Is.GreaterThan(3.5f));
            Assert.That(thrown.ThrowDirection, Is.EqualTo(QusapWeaponThrowDirection.Up));
        }

        [TestCase(QusapWeaponThrowDirection.Forward, 0.65f)]
        [TestCase(QusapWeaponThrowDirection.Up, 0.70f)]
        public void StrongThrowSettlesOnlyAfterConfiguredDuration(
            QusapWeaponThrowDirection direction,
            float duration)
        {
            MatchRig rig = CreateMatch(true, false);
            Swap(rig, rig.PlayerOne, direction);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);

            thrown.Advance(duration - 0.01f);
            Assert.That(thrown.IsSettled, Is.False);
            thrown.Advance(0.02f);
            Assert.That(thrown.IsSettled, Is.True);
        }

        [Test]
        public void CapturedFacingControlsThrowAfterPresenterFacesElsewhere()
        {
            MatchRig rig = CreateMatch(true, false);
            rig.PlayerOne.Presenter.ApplyFacing(1);

            Assert.That(Swap(
                rig,
                rig.PlayerOne,
                QusapWeaponThrowDirection.Forward,
                -1), Is.True);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);
            float startX = thrown.transform.position.x;
            thrown.Advance(1f);

            Assert.That(thrown.CapturedFacingDirection, Is.EqualTo(-1));
            Assert.That(thrown.transform.position.x, Is.LessThan(startX));
        }

        [Test]
        public void SwapPreservesOriginalAndReplacementInstanceIds()
        {
            MatchRig rig = CreateMatch(true, false);
            ulong originalId = rig.PlayerOne.Equipment.EquippedWeapon.InstanceId;
            ulong replacementId = rig.Bootstrap.InitialWhiteWeapon.InstanceId;

            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);

            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon.InstanceId,
                Is.EqualTo(replacementId));
            Assert.That(FindVoluntaryThrow(rig).InstanceId, Is.EqualTo(originalId));
        }

        [Test]
        public void SwapPreservesDefinitionsAndColors()
        {
            MatchRig rig = CreateMatch(true, false);

            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);

            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.WhiteDefinitionId));
            Assert.That(FindVoluntaryThrow(rig).Weapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.BlueDefinitionId));
        }

        [Test]
        public void SwapLeavesExactlyOneLogicalOwnerPerInstance()
        {
            MatchRig rig = CreateMatch(true, false);
            QusapWeaponInstance original = rig.PlayerOne.Equipment.EquippedWeapon;
            QusapWeaponInstance replacement = rig.Bootstrap.InitialWhiteWeapon;

            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);

            Assert.That(original.OwnerEntityId, Is.Null);
            Assert.That(replacement.OwnerEntityId,
                Is.EqualTo(rig.PlayerOne.Equipment.OwnerEntityId));
            Assert.That(CountOwners(rig, original.InstanceId), Is.Zero);
            Assert.That(CountOwners(rig, replacement.InstanceId), Is.EqualTo(1));
        }

        [Test]
        public void SwapLeavesAtMostOneEquippedVisualPerPlayer()
        {
            MatchRig rig = CreateMatch(true, false);

            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);

            Assert.That(ActiveSocketChildren(rig.PlayerOne.Presenter), Is.EqualTo(1));
            Assert.That(ActiveSocketChildren(rig.PlayerTwo.Presenter), Is.EqualTo(1));
            Assert.That(rig.PlayerOne.Presenter.DisplayedWeapon,
                Is.SameAs(rig.PlayerOne.Equipment.EquippedWeapon));
        }

        [Test]
        public void CollectedInstanceHasNoDroppedRepresentation()
        {
            MatchRig rig = CreateMatch(true, false);
            ulong collectedId = rig.Bootstrap.InitialWhiteWeapon.InstanceId;

            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);

            Assert.That(CountDropped(rig, collectedId), Is.Zero);
            Assert.That(CountRepresentations(rig, collectedId), Is.EqualTo(1));
        }

        [Test]
        public void ReleasedInstanceHasExactlyOneDroppedRepresentation()
        {
            MatchRig rig = CreateMatch(true, false);
            ulong releasedId = rig.PlayerOne.Equipment.EquippedWeapon.InstanceId;

            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);

            Assert.That(CountDropped(rig, releasedId), Is.EqualTo(1));
            Assert.That(CountRepresentations(rig, releasedId), Is.EqualTo(1));
        }

        [Test]
        public void VoluntaryThrowIsPassiveAndKeepsGameplayPlane()
        {
            MatchRig rig = CreateMatch(true, false);
            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Up);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);
            float z = thrown.transform.position.z;

            thrown.Advance(0.35f);

            Assert.That(thrown.transform.position.z, Is.EqualTo(z).Within(0.0001f));
            Assert.That(thrown.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(thrown.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(thrown.GetComponentsInChildren<QusapAttackHitbox>(true), Is.Empty);
        }

        [Test]
        public void NoNearbyWeaponRejectsWithoutMutatingPlayer()
        {
            MatchRig rig = CreateMatch(true, false);
            QusapWeaponInstance original = rig.PlayerOne.Equipment.EquippedWeapon;
            ulong revision = rig.PlayerOne.Equipment.Revision;
            rig.PlayerOne.Root.transform.position += Vector3.left * 10f;

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(original));
            Assert.That(rig.PlayerOne.Equipment.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void UnarmedPlayerCannotVoluntarilySwap()
        {
            MatchRig rig = CreateMatch(true, false);
            rig.PlayerOne.Equipment.TryDisarm(
                rig.PlayerTwo.Equipment.OwnerEntityId, out _, out _);

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
        }

        [Test]
        public void InactivePlayerCannotVoluntarilySwap()
        {
            MatchRig rig = CreateMatch(true, false);
            rig.PlayerOne.Root.SetActive(false);

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
        }

        [Test]
        public void DisabledInputCannotVoluntarilySwap()
        {
            MatchRig rig = CreateMatch(true, false);
            rig.PlayerOne.Input.enabled = false;

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
        }

        [Test]
        public void PausedMatchCannotVoluntarilySwap()
        {
            MatchRig rig = CreateMatch(true, false);
            Time.timeScale = 0f;

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
        }

        [Test]
        public void ReservedGroundWeaponRejectsWithoutMutation()
        {
            MatchRig rig = CreateMatch(true, false);
            QusapWeaponInstance original = rig.PlayerOne.Equipment.EquippedWeapon;
            QusapDroppedWeaponView white = FindDropped(
                rig, rig.Bootstrap.InitialWhiteWeapon.InstanceId);
            Assert.That(white.TryReserve(999), Is.True);

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(original));
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
        }

        [Test]
        public void RepeatedIntentDuringFlightCannotCreateSecondSwap()
        {
            MatchRig rig = CreateMatch(true, false);
            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.True);
            QusapWeaponInstance afterFirst = rig.PlayerOne.Equipment.EquippedWeapon;

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Up), Is.False);
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(afterFirst));
            Assert.That(rig.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));
        }

        [Test]
        public void CompetingPlayerCannotCollectAlreadyReservedOrCommittedInstance()
        {
            MatchRig rig = CreateMatch(true, false);
            rig.PlayerTwo.Root.transform.position = rig.PlayerOne.Root.transform.position;

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.True);
            Assert.That(Swap(rig, rig.PlayerTwo, QusapWeaponThrowDirection.Forward), Is.False);
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon,
                Is.SameAs(rig.Bootstrap.InitialWhiteWeapon));
            Assert.That(CountOwners(rig, rig.Bootstrap.InitialWhiteWeapon.InstanceId),
                Is.EqualTo(1));
        }

        [Test]
        public void EventsObserveOnlyCommittedReplacementState()
        {
            MatchRig rig = CreateMatch(true, false);
            QusapWeaponInstance replacement = rig.Bootstrap.InitialWhiteWeapon;
            int eventCount = 0;
            Action<QusapWeaponTransition> handler = _ =>
            {
                eventCount++;
                Assert.That(rig.PlayerOne.Equipment.EquippedWeapon,
                    Is.SameAs(replacement));
                Assert.That(replacement.OwnerEntityId,
                    Is.EqualTo(rig.PlayerOne.Equipment.OwnerEntityId));
            };
            rig.PlayerOne.Equipment.WeaponTransitioned += handler;

            Assert.That(Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward), Is.True);
            rig.PlayerOne.Equipment.WeaponTransitioned -= handler;
            Assert.That(eventCount, Is.EqualTo(2));
        }

        [Test]
        public void StrongThrowCannotBePickedUpInFlight()
        {
            MatchRig rig = CreateMatch(true, false);
            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);
            rig.PlayerTwo.Equipment.TryDisarm(
                rig.PlayerOne.Equipment.OwnerEntityId, out _, out _);
            rig.PlayerTwo.Root.transform.position = thrown.transform.position;

            int pickups = rig.Bootstrap.ProcessPickups(Now(rig) + 0.1d);

            Assert.That(pickups, Is.Zero);
            Assert.That(rig.PlayerTwo.Equipment.HasWeapon, Is.False);
            Assert.That(thrown.Weapon.IsFree, Is.True);
        }

        [Test]
        public void StrongThrowBecomesPickupEligibleAfterSettling()
        {
            MatchRig rig = CreateMatch(true, false);
            Swap(rig, rig.PlayerOne, QusapWeaponThrowDirection.Forward);
            QusapDroppedWeaponView thrown = FindVoluntaryThrow(rig);
            rig.PlayerTwo.Equipment.TryDisarm(
                rig.PlayerOne.Equipment.OwnerEntityId, out _, out _);
            thrown.Advance(1f);
            rig.PlayerTwo.Root.transform.position = thrown.transform.position;

            int pickups = rig.Bootstrap.ProcessPickups(Now(rig) + 2d);

            Assert.That(pickups, Is.EqualTo(1));
            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(thrown.Weapon));
            Assert.That(CountRepresentations(rig, thrown.InstanceId), Is.EqualTo(1));
        }

        [Test]
        public void InitialWhiteWeaponUsesNextIdAndStartsFreeAndSettled()
        {
            MatchRig rig = CreateMatch(true, false);
            QusapDroppedWeaponView white = FindDropped(
                rig, rig.Bootstrap.InitialWhiteWeapon.InstanceId);

            Assert.That(rig.Bootstrap.PlayerOneWeapon.InstanceId, Is.EqualTo(1UL));
            Assert.That(rig.Bootstrap.PlayerTwoWeapon.InstanceId, Is.EqualTo(2UL));
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.InstanceId, Is.EqualTo(3UL));
            Assert.That(rig.Bootstrap.InitialWhiteWeapon.IsFree, Is.True);
            Assert.That(white.IsSettled, Is.True);
            Assert.That(white.ReleaseType, Is.EqualTo(QusapWeaponReleaseType.InitialSpawn));
        }

        [UnityTest]
        public IEnumerator CombatPlaygroundSpawnsCenteredWhiteWeaponWithoutAutoEquip()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode(
                "Assets/_Qusap/Scenes/CombatPlayground.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;

            QusapWeaponMatchBootstrap bootstrap =
                UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.IsInitialized, Is.True);
            Assert.That(bootstrap.InitialWhiteWeapon, Is.Not.Null);
            Assert.That(bootstrap.InitialWhiteWeapon.InstanceId, Is.EqualTo(3UL));
            Assert.That(bootstrap.InitialWhiteWeapon.IsFree, Is.True);
            QusapDroppedWeaponView white = FindDropped(
                bootstrap, bootstrap.InitialWhiteWeapon.InstanceId);
            Assert.That(white.IsSettled, Is.True);
            Assert.That(white.transform.position.x, Is.EqualTo(0f).Within(0.001f));
            Assert.That(bootstrap.PlayerOneEquipment.HasWeapon, Is.True);
            Assert.That(bootstrap.PlayerTwoEquipment.HasWeapon, Is.True);
#else
            yield return null;
            Assert.Ignore("CombatPlayground scene verification requires the Unity Editor.");
#endif
        }

        [UnityTest]
        public IEnumerator CombatPlaygroundPlayerOneActuallySwapsWithE()
        {
#if UNITY_EDITOR
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                keyboard = InputSystem.AddDevice<Keyboard>();
                addedInputDevices.Add(keyboard);
            }

            EditorSceneManager.LoadSceneInPlayMode(
                "Assets/_Qusap/Scenes/CombatPlayground.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            QusapWeaponMatchBootstrap bootstrap =
                UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            QusapDroppedWeaponView white = FindDropped(
                bootstrap, bootstrap.InitialWhiteWeapon.InstanceId);
            bootstrap.PlayerOneEquipment.transform.position = white.transform.position;
            bootstrap.PlayerOneEquipment.GetComponent<Rigidbody>().position =
                white.transform.position;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.E));
            InputSystem.Update();
            Assert.That(
                bootstrap.PlayerOneEquipment.GetComponent<QusapInputReader>()
                    .HasPendingWeaponSwapThrow,
                Is.True);
            yield return null;
            yield return null;

            Assert.That(bootstrap.PlayerOneEquipment.EquippedWeapon,
                Is.SameAs(bootstrap.InitialWhiteWeapon));
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
#else
            yield return null;
            Assert.Ignore("Authoritative keyboard integration requires the Unity Editor.");
#endif
        }

        [UnityTest]
        public IEnumerator CombatPlaygroundPlayerTwoActuallySwapsWithRightTrigger()
        {
#if UNITY_EDITOR
            Gamepad gamepad;
            if (Gamepad.all.Count == 0)
            {
                gamepad = InputSystem.AddDevice<Gamepad>();
                addedInputDevices.Add(gamepad);
            }
            else
            {
                gamepad = Gamepad.all[0];
            }
            EditorSceneManager.LoadSceneInPlayMode(
                "Assets/_Qusap/Scenes/CombatPlayground.unity",
                new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            QusapWeaponMatchBootstrap bootstrap =
                UnityEngine.Object.FindAnyObjectByType<QusapWeaponMatchBootstrap>();
            QusapDroppedWeaponView white = FindDropped(
                bootstrap, bootstrap.InitialWhiteWeapon.InstanceId);
            bootstrap.PlayerTwoEquipment.transform.position = white.transform.position;
            bootstrap.PlayerTwoEquipment.GetComponent<Rigidbody>().position =
                white.transform.position;

            InputSystem.QueueStateEvent(gamepad, new GamepadState
            {
                rightTrigger = 1f
            });
            InputSystem.Update();
            Assert.That(
                bootstrap.PlayerTwoEquipment.GetComponent<QusapInputReader>()
                    .HasPendingWeaponSwapThrow,
                Is.True);
            yield return null;
            yield return null;

            Assert.That(bootstrap.PlayerTwoEquipment.EquippedWeapon,
                Is.SameAs(bootstrap.InitialWhiteWeapon));
            InputSystem.QueueStateEvent(gamepad, new GamepadState());
#else
            yield return null;
            Assert.Ignore("Authoritative gamepad integration requires the Unity Editor.");
#endif
        }

        private bool Swap(
            MatchRig rig,
            PlayerRig player,
            QusapWeaponThrowDirection direction,
            int facing = 1)
        {
            double now = Now(rig) + 0.001d;
            QusapWeaponSwapThrowPress press = new(++nextPressId, now, direction, facing);
            return rig.Bootstrap.TryProcessVoluntarySwap(player.Equipment, press, now);
        }

        private static double Now(MatchRig rig)
        {
            double now = Time.timeAsDouble;
            for (int i = 0; i < rig.Bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = rig.Bootstrap.DroppedWeapons[i];
                if (dropped != null)
                {
                    now = Math.Max(now, dropped.DroppedAt);
                }
            }

            return now;
        }

        private MatchRig CreateMatch(bool initialWhite, bool whiteNearPlayerTwo)
        {
            PlayerRig playerOne = CreatePlayer("SwapPlayerOne", new Vector3(-2f, 1f, 0f));
            PlayerRig playerTwo = CreatePlayer("SwapPlayerTwo", new Vector3(2f, 1f, 0f));
            GameObject root = new("WeaponSwapBootstrap_Test");
            roots.Add(root);
            QusapWeaponMatchBootstrap bootstrap = root.AddComponent<QusapWeaponMatchBootstrap>();
            bootstrap.Configure(
                catalog,
                playerOne.Equipment,
                playerOne.Presenter,
                playerTwo.Equipment,
                playerTwo.Presenter);
            Vector3 position = whiteNearPlayerTwo
                ? playerTwo.Root.transform.position
                : playerOne.Root.transform.position;
            bootstrap.ConfigureInitialWhiteWeapon(initialWhite, position);
            Assert.That(bootstrap.TryInitialize(), Is.True);
            return new MatchRig(bootstrap, playerOne, playerTwo);
        }

        private PlayerRig CreatePlayer(string name, Vector3 position)
        {
            GameObject root = new(name);
            roots.Add(root);
            root.SetActive(false);
            root.transform.position = position;
            Rigidbody body = root.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeAll;
            QusapInputReader input = root.AddComponent<QusapWeaponVisualTestInputReader>();
            root.AddComponent<QusapGroundSensor>();
            root.AddComponent<QusapWallSensor>();
            root.AddComponent<QusapHorizontalMotor>();
            root.AddComponent<QusapVerticalMotor>();
            root.AddComponent<QusapDashMotor>();
            root.AddComponent<QusapHitReceiver>();
            root.AddComponent<QusapHitstunController>();
            GameObject hitbox = new("AttackHitbox");
            hitbox.transform.SetParent(root.transform, false);
            hitbox.AddComponent<QusapAttackHitbox>();
            QusapCombatController combat = root.AddComponent<QusapCombatController>();
            QusapWeaponEquipment equipment = root.AddComponent<QusapWeaponEquipment>();
            GameObject socket = new("WeaponSocket");
            socket.transform.SetParent(root.transform, false);
            QusapEquippedWeaponPresenter presenter =
                root.AddComponent<QusapEquippedWeaponPresenter>();
            presenter.Configure(equipment, combat, catalog, socket.transform);
            root.SetActive(true);
            Assert.That(equipment.TryInitialize(combat), Is.True);
            Assert.That(presenter.TryInitialize(), Is.True);
            return new PlayerRig(root, input, equipment, presenter);
        }

        private QusapWeaponVisualEntry Entry(string id, string displayName)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = $"{displayName}Visual";
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.SetActive(false);
            visualPrefabs.Add(visual);
            return new QusapWeaponVisualEntry(id, $"{displayName} Sword", visual);
        }

        private static QusapDroppedWeaponView FindVoluntaryThrow(MatchRig rig)
        {
            for (int i = 0; i < rig.Bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = rig.Bootstrap.DroppedWeapons[i];
                if (dropped.ReleaseType == QusapWeaponReleaseType.VoluntarySwapThrow)
                {
                    return dropped;
                }
            }

            Assert.Fail("Expected one voluntary swap throw representation.");
            return null;
        }

        private static QusapDroppedWeaponView FindDropped(MatchRig rig, ulong id)
        {
            return FindDropped(rig.Bootstrap, id);
        }

        private static QusapDroppedWeaponView FindDropped(
            QusapWeaponMatchBootstrap bootstrap,
            ulong id)
        {
            for (int i = 0; i < bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = bootstrap.DroppedWeapons[i];
                if (dropped != null && dropped.InstanceId == id)
                {
                    return dropped;
                }
            }

            Assert.Fail($"Expected dropped representation for instance {id}.");
            return null;
        }

        private static int ActiveSocketChildren(QusapEquippedWeaponPresenter presenter)
        {
            int count = 0;
            for (int i = 0; i < presenter.WeaponSocket.childCount; i++)
            {
                if (presenter.WeaponSocket.GetChild(i).gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountOwners(MatchRig rig, ulong id)
        {
            int count = 0;
            if (rig.PlayerOne.Equipment.EquippedWeapon?.InstanceId == id)
            {
                count++;
            }

            if (rig.PlayerTwo.Equipment.EquippedWeapon?.InstanceId == id)
            {
                count++;
            }

            return count;
        }

        private static int CountDropped(MatchRig rig, ulong id)
        {
            int count = 0;
            for (int i = 0; i < rig.Bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = rig.Bootstrap.DroppedWeapons[i];
                if (dropped != null
                    && dropped.InstanceId == id
                    && dropped.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRepresentations(MatchRig rig, ulong id)
        {
            int count = CountDropped(rig, id);
            if (rig.PlayerOne.Presenter.DisplayedWeapon?.InstanceId == id
                && rig.PlayerOne.Presenter.EquippedVisual != null
                && rig.PlayerOne.Presenter.EquippedVisual.activeInHierarchy)
            {
                count++;
            }

            if (rig.PlayerTwo.Presenter.DisplayedWeapon?.InstanceId == id
                && rig.PlayerTwo.Presenter.EquippedVisual != null
                && rig.PlayerTwo.Presenter.EquippedVisual.activeInHierarchy)
            {
                count++;
            }

            return count;
        }

        private readonly struct MatchRig
        {
            public MatchRig(
                QusapWeaponMatchBootstrap bootstrap,
                PlayerRig playerOne,
                PlayerRig playerTwo)
            {
                Bootstrap = bootstrap;
                PlayerOne = playerOne;
                PlayerTwo = playerTwo;
            }

            public QusapWeaponMatchBootstrap Bootstrap { get; }
            public PlayerRig PlayerOne { get; }
            public PlayerRig PlayerTwo { get; }
        }

        private readonly struct PlayerRig
        {
            public PlayerRig(
                GameObject root,
                QusapInputReader input,
                QusapWeaponEquipment equipment,
                QusapEquippedWeaponPresenter presenter)
            {
                Root = root;
                Input = input;
                Equipment = equipment;
                Presenter = presenter;
            }

            public GameObject Root { get; }
            public QusapInputReader Input { get; }
            public QusapWeaponEquipment Equipment { get; }
            public QusapEquippedWeaponPresenter Presenter { get; }
        }
    }
}
