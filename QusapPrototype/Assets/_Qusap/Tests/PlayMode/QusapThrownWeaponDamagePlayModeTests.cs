using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapThrownWeaponDamagePlayModeTests
    {
        private readonly List<GameObject> roots = new();
        private readonly List<GameObject> visualPrefabs = new();
        private QusapWeaponVisualCatalog catalog;
        private ulong nextPressId;

        [SetUp]
        public void SetUp()
        {
            Time.timeScale = 1f;
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
        }

        [Test]
        public void ForwardSwapThrowHitsRival()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f);

            thrown.Advance(0.45f);

            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
            Assert.That(thrown.ConfirmedImpactCount, Is.EqualTo(1));
            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon,
                Is.SameAs(rig.Bootstrap.PlayerTwoWeapon));
        }

        [Test]
        public void UpSwapThrowHitsRivalInVerticalPath()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Up);
            PlaceAlongUp(thrown, rig.PlayerTwo);

            thrown.Advance(0.35f);

            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
            Assert.That(thrown.ConfirmedTarget, Is.SameAs(rig.PlayerTwo.Receiver));
        }

        [Test]
        public void ImpactIsAttributedToCapturedThrower()
        {
            MatchRig rig = CreateMatch();
            QusapHitInfo received = default;
            rig.PlayerTwo.Receiver.HitReceived += hit => received = hit;
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f);

            thrown.Advance(0.45f);

            Assert.That(received.Source, Is.SameAs(rig.PlayerOne.Combat));
            Assert.That(thrown.ThrowerEntityId,
                Is.EqualTo(rig.PlayerOne.Equipment.OwnerEntityId));
            Assert.That(thrown.ThrowId, Is.Not.Zero);
        }

        [Test]
        public void ReleasedWeaponPreservesExactInstanceId()
        {
            MatchRig rig = CreateMatch();
            ulong originalId = rig.PlayerOne.Equipment.EquippedWeapon.InstanceId;

            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);

            Assert.That(thrown.InstanceId, Is.EqualTo(originalId));
            Assert.That(thrown.AttackState.WeaponInstanceId, Is.EqualTo(originalId));
            Assert.That(thrown.Weapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.BlueDefinitionId));
        }

        [Test]
        public void ThrownInstanceRemainsFreeInFlightAndAfterSettling()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);

            Assert.That(thrown.Weapon.IsFree, Is.True);
            Assert.That(thrown.Weapon.OwnerEntityId, Is.Null);
            thrown.Advance(1f);

            Assert.That(thrown.IsSettled, Is.True);
            Assert.That(thrown.Weapon.IsFree, Is.True);
            Assert.That(thrown.Weapon.OwnerEntityId, Is.Null);
        }

        [Test]
        public void RivalReceivesExactlyOneDamagePacket()
        {
            MatchRig rig = CreateMatch();
            int receivedCount = 0;
            rig.PlayerTwo.Receiver.HitReceived += _ => receivedCount++;
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f);

            thrown.Advance(0.45f);
            thrown.Advance(0.05f);

            Assert.That(receivedCount, Is.EqualTo(1));
            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
        }

        [Test]
        public void RivalEntersHitstunExactlyOnce()
        {
            MatchRig rig = CreateMatch();
            int hitstunCount = 0;
            rig.PlayerTwo.Hitstun.HitstunStarted += () => hitstunCount++;
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f);

            thrown.Advance(0.45f);
            thrown.Advance(0.05f);

            Assert.That(hitstunCount, Is.EqualTo(1));
            Assert.That(rig.PlayerTwo.Hitstun.IsInHitstun, Is.True);
            Assert.That(rig.PlayerTwo.Hitstun.TimeRemaining,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultHitstunDuration));
        }

        [Test]
        public void ForwardKnockbackUsesCapturedFacing()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(
                rig,
                QusapWeaponThrowDirection.Forward,
                -1);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f, -1);

            thrown.Advance(0.45f);

            Assert.That(rig.PlayerTwo.Body.linearVelocity.x,
                Is.EqualTo(-QusapThrownWeaponAttackProfile.DefaultHorizontalKnockback));
            Assert.That(rig.PlayerTwo.Body.linearVelocity.y,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultVerticalKnockback));
        }

        [Test]
        public void VerticalKnockbackIsPredominantlyUpward()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Up);
            PlaceAlongUp(thrown, rig.PlayerTwo);

            thrown.Advance(0.35f);

            Vector3 velocity = rig.PlayerTwo.Body.linearVelocity;
            Assert.That(velocity.y,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultHorizontalKnockback));
            Assert.That(Mathf.Abs(velocity.x),
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultVerticalKnockback));
            Assert.That(velocity.y, Is.GreaterThan(Mathf.Abs(velocity.x)));
        }

        [Test]
        public void FacingChangeAfterPressDoesNotChangeTrajectoryOrKnockback()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(
                rig,
                QusapWeaponThrowDirection.Forward,
                -1);
            float startX = thrown.transform.position.x;
            rig.PlayerOne.Presenter.ApplyFacing(1);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f, -1);

            thrown.Advance(0.45f);

            Assert.That(thrown.CapturedFacingDirection, Is.EqualTo(-1));
            Assert.That(thrown.transform.position.x, Is.LessThan(startX));
            Assert.That(rig.PlayerTwo.Body.linearVelocity.x, Is.LessThan(0f));
        }

        [Test]
        public void SweptQueryHitsWhenOneFrameTravelExceedsWeaponSize()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            Vector3 start = thrown.transform.position;
            PlaceAlongForward(thrown, rig.PlayerTwo, 2.5f);

            thrown.Advance(0.59f);

            Assert.That(Vector3.Distance(start, thrown.transform.position),
                Is.GreaterThan(2f));
            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
        }

        [Test]
        public void RepeatedOverlappingUpdatesDoNotDuplicateImpact()
        {
            MatchRig rig = CreateMatch();
            int receivedCount = 0;
            rig.PlayerTwo.Receiver.HitReceived += _ => receivedCount++;
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            rig.PlayerTwo.Root.transform.position = thrown.transform.position;
            Physics.SyncTransforms();

            thrown.Advance(0.02f);
            thrown.Advance(0.02f);
            thrown.Advance(0.02f);

            Assert.That(receivedCount, Is.EqualTo(1));
            Assert.That(thrown.AttackState.ImpactConsumed, Is.True);
            Assert.That(thrown.IsOffensive, Is.False);
        }

        [Test]
        public void TwoTargetsInTrajectoryDoNotBothReceiveDamage()
        {
            MatchRig rig = CreateMatch();
            PlayerRig farther = CreatePlayer("FartherTarget", new Vector3(20f, 1f, 0f));
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, rig.PlayerTwo, 1f);
            PlaceAlongForward(thrown, farther, 2.5f);

            thrown.Advance(0.59f);

            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
            Assert.That(farther.Receiver.TotalDamageReceived, Is.Zero);
            Assert.That(thrown.ConfirmedImpactCount, Is.EqualTo(1));
        }

        [Test]
        public void EqualDistanceTargetsChooseLowestEntityId()
        {
            MatchRig rig = CreateMatch();
            PlayerRig other = CreatePlayer("TiedTarget", new Vector3(20f, 1f, 0f));
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            Vector3 tiedPosition = thrown.transform.position + new Vector3(2f, 0f, 0f);
            rig.PlayerTwo.Root.transform.position = tiedPosition;
            other.Root.transform.position = tiedPosition;
            Physics.SyncTransforms();
            PlayerRig expected = rig.PlayerTwo.EntityId < other.EntityId
                ? rig.PlayerTwo
                : other;
            PlayerRig rejected = rig.PlayerTwo.EntityId < other.EntityId
                ? other
                : rig.PlayerTwo;

            thrown.Advance(0.5f);

            Assert.That(expected.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
            Assert.That(rejected.Receiver.TotalDamageReceived, Is.Zero);
        }

        [Test]
        public void ThrowerCannotHitItself()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            rig.PlayerTwo.Root.transform.position = new Vector3(30f, 1f, 0f);
            rig.PlayerOne.Root.transform.position = thrown.transform.position
                + new Vector3(2f, 0f, 0f);
            Physics.SyncTransforms();

            thrown.Advance(0.45f);

            Assert.That(rig.PlayerOne.Receiver.TotalDamageReceived, Is.Zero);
            Assert.That(thrown.ConfirmedImpactCount, Is.Zero);
            Assert.That(thrown.AttackState.ImpactConsumed, Is.False);
        }

        [Test]
        public void DisarmDropTouchingPlayerIsAlwaysHarmless()
        {
            MatchRig rig = CreateMatch();
            ulong disarmedId = rig.PlayerOne.Equipment.EquippedWeapon.InstanceId;
            Assert.That(rig.PlayerOne.Equipment.TryDisarm(rig.PlayerTwo.Combat), Is.True);
            QusapDroppedWeaponView dropped = FindDropped(rig, disarmedId);
            rig.PlayerTwo.Root.transform.position = dropped.transform.position;
            Physics.SyncTransforms();

            dropped.Advance(1f);

            Assert.That(dropped.ReleaseType, Is.EqualTo(QusapWeaponReleaseType.Disarmed));
            Assert.That(dropped.IsOffensive, Is.False);
            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived, Is.Zero);
            Assert.That(rig.PlayerTwo.Hitstun.IsInHitstun, Is.False);
            Assert.That(rig.PlayerTwo.Body.linearVelocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void MissedThrowSettlesAndCanBePickedUp()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            rig.PlayerTwo.Root.transform.position = new Vector3(30f, 1f, 0f);
            Physics.SyncTransforms();
            thrown.Advance(1f);

            AssertPickupByPlayerTwo(rig, thrown);
        }

        [Test]
        public void ImpactedThrowSettlesAndCanBePickedUp()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f);
            thrown.Advance(0.45f);
            thrown.Advance(1f);

            Assert.That(thrown.AttackState.ImpactConsumed, Is.True);
            AssertPickupByPlayerTwo(rig, thrown);
        }

        [Test]
        public void PickupAfterImpactPreservesIdentityWithoutDuplicateRepresentation()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            ulong thrownId = thrown.InstanceId;
            PlaceAlongForward(thrown, rig.PlayerTwo, 2f);
            thrown.Advance(1f);

            AssertPickupByPlayerTwo(rig, thrown);

            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon.InstanceId,
                Is.EqualTo(thrownId));
            Assert.That(CountRepresentations(rig, thrownId), Is.EqualTo(1));
            Assert.That(CountOwners(rig, thrownId), Is.EqualTo(1));
        }

        [Test]
        public void WeaponVisualsContainNoPhysicalBodyColliderOrPersistentHitbox()
        {
            MatchRig rig = CreateMatch();
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);

            Assert.That(thrown.GetComponentInChildren<Rigidbody>(true), Is.Null);
            Assert.That(thrown.GetComponentInChildren<Collider>(true), Is.Null);
            Assert.That(thrown.GetComponentInChildren<QusapAttackHitbox>(true), Is.Null);
            Assert.That(rig.PlayerOne.Presenter.EquippedVisual
                .GetComponentInChildren<Rigidbody>(true), Is.Null);
            Assert.That(rig.PlayerOne.Presenter.EquippedVisual
                .GetComponentInChildren<Collider>(true), Is.Null);
        }

        [Test]
        public void FailedThrowerValidationLeavesSwapAtomic()
        {
            MatchRig rig = CreateMatch();
            QusapWeaponInstance original = rig.PlayerOne.Equipment.EquippedWeapon;
            QusapWeaponInstance replacement = rig.Bootstrap.InitialWhiteWeapon;
            UnityEngine.Object.DestroyImmediate(rig.PlayerOne.Combat);

            bool swapped = TrySwap(
                rig,
                rig.PlayerOne,
                QusapWeaponThrowDirection.Forward,
                1);

            Assert.That(swapped, Is.False);
            Assert.That(rig.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(original));
            Assert.That(original.OwnerEntityId,
                Is.EqualTo(rig.PlayerOne.Equipment.OwnerEntityId));
            Assert.That(replacement.IsFree, Is.True);
            Assert.That(FindDropped(rig, replacement.InstanceId).IsClaimed, Is.False);
        }

        [Test]
        public void InvalidCandidateIsIgnoredAndLaterValidTargetCanBeHit()
        {
            MatchRig rig = CreateMatch();
            PlayerRig invalid = CreatePlayer("InvalidTarget", new Vector3(20f, 1f, 0f));
            invalid.Receiver.AcceptsHits = false;
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            PlaceAlongForward(thrown, invalid, 1f);
            PlaceAlongForward(thrown, rig.PlayerTwo, 2.5f);

            thrown.Advance(0.59f);

            Assert.That(invalid.Receiver.TotalDamageReceived, Is.Zero);
            Assert.That(rig.PlayerTwo.Receiver.TotalDamageReceived,
                Is.EqualTo(QusapThrownWeaponAttackProfile.DefaultDamage));
        }

        [Test]
        public void PreviousSwapPickupAndDisarmBehaviorStillWorks()
        {
            MatchRig rig = CreateMatch();
            QusapWeaponInstance original = rig.PlayerOne.Equipment.EquippedWeapon;
            QusapDroppedWeaponView thrown = Swap(rig, QusapWeaponThrowDirection.Forward);
            rig.PlayerTwo.Root.transform.position = new Vector3(30f, 1f, 0f);
            Physics.SyncTransforms();
            thrown.Advance(1f);

            AssertPickupByPlayerTwo(rig, thrown);
            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(original));
            Assert.That(rig.PlayerTwo.Equipment.TryDisarm(rig.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView disarmed = FindDropped(rig, original.InstanceId);
            Assert.That(disarmed.ReleaseType, Is.EqualTo(QusapWeaponReleaseType.Disarmed));
            Assert.That(disarmed.IsOffensive, Is.False);
        }

        private MatchRig CreateMatch()
        {
            PlayerRig playerOne = CreatePlayer("ThrownPlayerOne", new Vector3(-4f, 1f, 0f));
            PlayerRig playerTwo = CreatePlayer("ThrownPlayerTwo", new Vector3(4f, 1f, 0f));
            GameObject root = new("ThrownWeaponBootstrap_Test");
            roots.Add(root);
            QusapWeaponMatchBootstrap bootstrap = root.AddComponent<QusapWeaponMatchBootstrap>();
            bootstrap.Configure(
                catalog,
                playerOne.Equipment,
                playerOne.Presenter,
                playerTwo.Equipment,
                playerTwo.Presenter);
            bootstrap.ConfigureInitialWhiteWeapon(true, playerOne.Root.transform.position);
            bootstrap.ConfigureThrownWeaponAttack(
                new QusapThrownWeaponAttackProfile(),
                1 << playerOne.Root.layer);
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
            body.constraints = RigidbodyConstraints.FreezeRotation
                | RigidbodyConstraints.FreezePositionZ;
            CapsuleCollider collider = root.AddComponent<CapsuleCollider>();
            collider.radius = 0.5f;
            collider.height = 2f;
            QusapInputReader input = root.AddComponent<QusapWeaponVisualTestInputReader>();
            root.AddComponent<QusapGroundSensor>();
            root.AddComponent<QusapWallSensor>();
            root.AddComponent<QusapHorizontalMotor>();
            root.AddComponent<QusapVerticalMotor>();
            root.AddComponent<QusapDashMotor>();
            QusapHitReceiver receiver = root.AddComponent<QusapHitReceiver>();
            QusapHitstunController hitstun = root.AddComponent<QusapHitstunController>();
            root.AddComponent<QusapHurtbox>();
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
            return new PlayerRig(
                root,
                input,
                body,
                combat,
                receiver,
                hitstun,
                equipment,
                presenter);
        }

        private QusapWeaponVisualEntry Entry(string id, string displayName)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = $"{displayName}ThrownVisual";
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.SetActive(false);
            visualPrefabs.Add(visual);
            return new QusapWeaponVisualEntry(id, $"{displayName} Sword", visual);
        }

        private QusapDroppedWeaponView Swap(
            MatchRig rig,
            QusapWeaponThrowDirection direction,
            int facing = 1)
        {
            Assert.That(TrySwap(rig, rig.PlayerOne, direction, facing), Is.True);
            return FindVoluntaryThrow(rig);
        }

        private bool TrySwap(
            MatchRig rig,
            PlayerRig player,
            QusapWeaponThrowDirection direction,
            int facing)
        {
            double now = Now(rig) + 0.01d;
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

        private static void PlaceAlongForward(
            QusapDroppedWeaponView thrown,
            PlayerRig target,
            float distance,
            int facing = 1)
        {
            target.Root.transform.position = thrown.transform.position
                + new Vector3(distance * facing, 0f, 0f);
            Physics.SyncTransforms();
        }

        private static void PlaceAlongUp(
            QusapDroppedWeaponView thrown,
            PlayerRig target)
        {
            target.Root.transform.position = thrown.transform.position
                + new Vector3(0.2f, 2.2f, 0f);
            Physics.SyncTransforms();
        }

        private static void AssertPickupByPlayerTwo(
            MatchRig rig,
            QusapDroppedWeaponView thrown)
        {
            Assert.That(thrown.IsSettled, Is.True);
            Assert.That(rig.PlayerTwo.Equipment.TryDrop(out _, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));
            rig.PlayerTwo.Root.transform.position = thrown.transform.position;
            Physics.SyncTransforms();

            int pickups = rig.Bootstrap.ProcessPickups(Now(rig) + 2d);

            Assert.That(pickups, Is.EqualTo(1));
            Assert.That(rig.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(thrown.Weapon));
            Assert.That(thrown.gameObject.activeSelf, Is.False);
        }

        private static QusapDroppedWeaponView FindVoluntaryThrow(MatchRig rig)
        {
            for (int i = 0; i < rig.Bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = rig.Bootstrap.DroppedWeapons[i];
                if (dropped != null
                    && dropped.ReleaseType == QusapWeaponReleaseType.VoluntarySwapThrow)
                {
                    return dropped;
                }
            }

            Assert.Fail("Expected a voluntary thrown-weapon representation.");
            return null;
        }

        private static QusapDroppedWeaponView FindDropped(MatchRig rig, ulong id)
        {
            for (int i = 0; i < rig.Bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = rig.Bootstrap.DroppedWeapons[i];
                if (dropped != null && dropped.InstanceId == id)
                {
                    return dropped;
                }
            }

            Assert.Fail($"Expected dropped representation for instance {id}.");
            return null;
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

        private static int CountRepresentations(MatchRig rig, ulong id)
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
                Rigidbody body,
                QusapCombatController combat,
                QusapHitReceiver receiver,
                QusapHitstunController hitstun,
                QusapWeaponEquipment equipment,
                QusapEquippedWeaponPresenter presenter)
            {
                Root = root;
                Input = input;
                Body = body;
                Combat = combat;
                Receiver = receiver;
                Hitstun = hitstun;
                Equipment = equipment;
                Presenter = presenter;
            }

            public GameObject Root { get; }
            public QusapInputReader Input { get; }
            public Rigidbody Body { get; }
            public QusapCombatController Combat { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapHitstunController Hitstun { get; }
            public QusapWeaponEquipment Equipment { get; }
            public QusapEquippedWeaponPresenter Presenter { get; }
            public ulong EntityId => EntityIdUtility(Combat);

            private static ulong EntityIdUtility(QusapCombatController combat)
            {
                return UnityEngine.EntityId.ToULong(combat.GetEntityId());
            }
        }
    }
}
