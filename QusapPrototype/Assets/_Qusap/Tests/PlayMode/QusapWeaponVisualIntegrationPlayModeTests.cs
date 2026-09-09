using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Qusap.Tests
{
    public sealed class QusapWeaponVisualIntegrationPlayModeTests
    {
        private readonly List<GameObject> matchRoots = new();
        private readonly List<PlayerRig> players = new();
        private readonly List<GameObject> visualPrefabs = new();
        private QusapWeaponVisualCatalog catalog;

        [SetUp]
        public void SetUp()
        {
            GameObject blue = CreateVisualPrefab("BlueVisual");
            GameObject purple = CreateVisualPrefab("PurpleVisual");
            GameObject white = CreateVisualPrefab("WhiteVisual");
            catalog = ScriptableObject.CreateInstance<QusapWeaponVisualCatalog>();
            catalog.Configure(
                new QusapWeaponVisualEntry(
                    QusapWeaponVisualCatalog.BlueDefinitionId,
                    "Blue Sword",
                    blue),
                new QusapWeaponVisualEntry(
                    QusapWeaponVisualCatalog.PurpleDefinitionId,
                    "Purple Sword",
                    purple),
                new QusapWeaponVisualEntry(
                    QusapWeaponVisualCatalog.WhiteDefinitionId,
                    "White Sword",
                    white));
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = matchRoots.Count - 1; i >= 0; i--)
            {
                if (matchRoots[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(matchRoots[i]);
                }
            }

            for (int i = players.Count - 1; i >= 0; i--)
            {
                players[i].Dispose();
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

            matchRoots.Clear();
            players.Clear();
            visualPrefabs.Clear();
        }

        [Test]
        public void PlayerOneReceivesBlueSword()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon,
                Is.SameAs(match.Bootstrap.PlayerOneWeapon));
            Assert.That(match.Bootstrap.PlayerOneWeapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.BlueDefinitionId));
        }

        [Test]
        public void PlayerTwoReceivesPurpleSword()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerTwo.Equipment.EquippedWeapon,
                Is.SameAs(match.Bootstrap.PlayerTwoWeapon));
            Assert.That(match.Bootstrap.PlayerTwoWeapon.Definition.Id,
                Is.EqualTo(QusapWeaponVisualCatalog.PurpleDefinitionId));
        }

        [Test]
        public void InitialWeaponIdsAreDifferent()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.Bootstrap.PlayerOneWeapon.InstanceId,
                Is.Not.EqualTo(match.Bootstrap.PlayerTwoWeapon.InstanceId));
        }

        [Test]
        public void RepeatedInitializationDoesNotDuplicateWeapons()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance first = match.Bootstrap.PlayerOneWeapon;
            QusapWeaponInstance second = match.Bootstrap.PlayerTwoWeapon;

            Assert.That(match.Bootstrap.TryInitialize(), Is.True);
            Assert.That(match.Bootstrap.PlayerOneWeapon, Is.SameAs(first));
            Assert.That(match.Bootstrap.PlayerTwoWeapon, Is.SameAs(second));
            Assert.That(match.PlayerOne.Presenter.WeaponSocket.childCount, Is.EqualTo(1));
            Assert.That(match.PlayerTwo.Presenter.WeaponSocket.childCount, Is.EqualTo(1));
        }

        [Test]
        public void EachPlayerShowsOneVisualPrefab()
        {
            MatchRig match = CreateMatch();
            AssertSingleEquippedVisual(match.PlayerOne);
            AssertSingleEquippedVisual(match.PlayerTwo);
        }

        [Test]
        public void DisplayedPrefabMatchesLogicalDefinition()
        {
            MatchRig match = CreateMatch();
            Assert.That(catalog.TryGetEntry(
                match.PlayerOne.Equipment.EquippedWeapon.Definition.Id,
                out QusapWeaponVisualEntry blue), Is.True);
            Assert.That(catalog.TryGetEntry(
                match.PlayerTwo.Equipment.EquippedWeapon.Definition.Id,
                out QusapWeaponVisualEntry purple), Is.True);
            Assert.That(match.PlayerOne.Presenter.DisplayedVisualPrefab,
                Is.SameAs(blue.VisualPrefab));
            Assert.That(match.PlayerTwo.Presenter.DisplayedVisualPrefab,
                Is.SameAs(purple.VisualPrefab));
        }

        [Test]
        public void EquippedVisualHasNoRigidbodyColliderOrHitbox()
        {
            MatchRig match = CreateMatch();
            AssertPassiveVisual(match.PlayerOne.Presenter.EquippedVisual);
            AssertPassiveVisual(match.PlayerTwo.Presenter.EquippedVisual);
        }

        [Test]
        public void EquippedVisualFollowsPlayer()
        {
            MatchRig match = CreateMatch();
            Transform visual = match.PlayerOne.Presenter.EquippedVisual.transform;
            Vector3 localPosition = visual.localPosition;
            Vector3 offsetBefore = visual.position - match.PlayerOne.Root.transform.position;

            match.PlayerOne.Root.transform.position += new Vector3(3.5f, -1.25f, 0f);

            Assert.That(visual.localPosition, Is.EqualTo(localPosition));
            Assert.That(Vector3.Distance(
                    visual.position - match.PlayerOne.Root.transform.position,
                    offsetBefore),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void FacingChangeMovesWeaponToOtherSide()
        {
            MatchRig match = CreateMatch();
            QusapEquippedWeaponPresenter presenter = match.PlayerOne.Presenter;
            presenter.ApplyFacing(1);
            float rightX = presenter.WeaponSocket.localPosition.x;
            presenter.ApplyFacing(-1);
            float leftX = presenter.WeaponSocket.localPosition.x;

            Assert.That(rightX, Is.GreaterThan(0f));
            Assert.That(leftX, Is.LessThan(0f));
            Assert.That(Mathf.Abs(leftX), Is.EqualTo(Mathf.Abs(rightX)));
        }

        [Test]
        public void FacingChangeReusesRendererAndDoesNotDuplicateVisual()
        {
            MatchRig match = CreateMatch();
            QusapEquippedWeaponPresenter presenter = match.PlayerOne.Presenter;
            Renderer renderer = presenter.PrimaryRenderer;
            ulong rendererId = EntityId.ToULong(renderer.GetEntityId());
            GameObject visual = presenter.EquippedVisual;

            presenter.ApplyFacing(-1);
            presenter.ApplyFacing(1);
            presenter.ApplyFacing(-1);

            Assert.That(presenter.EquippedVisual, Is.SameAs(visual));
            Assert.That(presenter.PrimaryRenderer, Is.SameAs(renderer));
            Assert.That(
                EntityId.ToULong(presenter.PrimaryRenderer.GetEntityId()),
                Is.EqualTo(rendererId));
            Assert.That(presenter.WeaponSocket.childCount, Is.EqualTo(1));
        }

        [Test]
        public void SuccessfulDisarmEmptiesDefenderSlot()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            Assert.That(match.PlayerTwo.Equipment.HasWeapon, Is.False);
            Assert.That(match.PlayerTwo.Equipment.EquippedWeapon, Is.Null);
        }

        [Test]
        public void SuccessfulDisarmHidesEquippedWeapon()
        {
            MatchRig match = CreateMatch();
            GameObject equippedVisual = match.PlayerTwo.Presenter.EquippedVisual;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);

            Assert.That(match.PlayerTwo.Presenter.EquippedVisual, Is.Null);
            Assert.That(match.PlayerTwo.Presenter.DisplayedWeapon, Is.Null);
            Assert.That(equippedVisual.activeSelf, Is.False);
        }

        [Test]
        public void SuccessfulDisarmCreatesOneDroppedRepresentation()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));
            Assert.That(match.Bootstrap.DroppedWeapons[0].VisualInstance, Is.Not.Null);
        }

        [Test]
        public void DroppedInstanceIdMatchesDefenderWeapon()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance defenderWeapon = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);

            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            Assert.That(dropped.Weapon, Is.SameAs(defenderWeapon));
            Assert.That(dropped.InstanceId, Is.EqualTo(defenderWeapon.InstanceId));
        }

        [Test]
        public void DroppedInstanceIsFreeAndAttackerDoesNotReceiveIt()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance attackerWeapon = match.PlayerOne.Equipment.EquippedWeapon;
            QusapWeaponInstance defenderWeapon = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);

            Assert.That(defenderWeapon.IsFree, Is.True);
            Assert.That(defenderWeapon.OwnerEntityId, Is.Null);
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(attackerWeapon));
        }

        [Test]
        public void RepeatingSameDisarmDoesNotDuplicateDroppedRepresentation()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView first = match.Bootstrap.DroppedWeapons[0];

            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.False);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));
            Assert.That(match.Bootstrap.DroppedWeapons[0], Is.SameAs(first));
        }

        [UnityTest]
        public IEnumerator SuccessfulParryPreservesWeapon()
        {
            MatchRig match = CreateMatch(true);
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            GameObject visual = match.PlayerTwo.Presenter.EquippedVisual;
            ulong revision = match.PlayerTwo.Equipment.Revision;
            yield return ArmDisarmFinisher(match.PlayerOne, match.PlayerTwo);

            match.PlayerOne.Combat.UpdateFinisherDefense(
                match.PlayerOne.Combat.ParryWindowOpensAt);
            double parryTimestamp = match.PlayerOne.Combat.ParryWindowOpensAt
                + (match.PlayerOne.Combat.ParryWindowClosesAt
                    - match.PlayerOne.Combat.ParryWindowOpensAt) / 2d;
            match.PlayerTwo.Input.EnqueueCombatCommand(
                QusapCombatCommand.Parry,
                parryTimestamp);
            yield return new WaitForFixedUpdate();

            Assert.That(match.PlayerOne.Combat.FinisherDefensePhase,
                Is.EqualTo(QusapFinisherDefensePhase.Parried));
            AssertWeaponUnchanged(match, weapon, visual, revision, "successful parry");
        }

        [UnityTest]
        public IEnumerator WhiffPreservesWeapon()
        {
            MatchRig match = CreateMatch(true);
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            GameObject visual = match.PlayerTwo.Presenter.EquippedVisual;
            ulong revision = match.PlayerTwo.Equipment.Revision;
            yield return ArmDisarmFinisher(match.PlayerOne, match.PlayerTwo);

            match.PlayerTwo.Root.transform.position += Vector3.right * 100f;
            match.PlayerOne.Combat.UpdateFinisherDefense(
                match.PlayerOne.Combat.ParryWindowOpensAt);
            match.PlayerOne.Combat.UpdateFinisherDefense(
                match.PlayerOne.Combat.ParryWindowClosesAt + 0.001d);
            yield return new WaitForFixedUpdate();

            Assert.That(match.PlayerOne.Combat.LastFinisherResolution.HasValue, Is.True);
            Assert.That(match.PlayerOne.Combat.LastFinisherResolution.Value.Outcome,
                Is.EqualTo(QusapFinisherResolutionOutcome.Whiffed));
            AssertWeaponUnchanged(match, weapon, visual, revision, "whiff");
        }

        [Test]
        public void RejectedSelfDisarmPreservesWeapon()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            GameObject visual = match.PlayerTwo.Presenter.EquippedVisual;

            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerTwo.Combat), Is.False);
            Assert.That(match.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(match.PlayerTwo.Presenter.EquippedVisual, Is.SameAs(visual));
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero);
        }

        [Test]
        public void UnarmedDefenderDoesNotCreateDroppedObject()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerTwo.Equipment.TryDrop(out QusapWeaponInstance dropped, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));
            Assert.That(dropped.IsFree, Is.True);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero,
                "A normal drop is not an authoritative Disarm transition.");

            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.False);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero);
        }

        [Test]
        public void DroppedWeaponDoesNotCauseDamagePushOrBlocking()
        {
            MatchRig match = CreateMatch();
            float damage = match.PlayerTwo.Receiver.TotalDamageReceived;
            Vector3 playerOnePosition = match.PlayerOne.Root.transform.position;
            Vector3 playerTwoPosition = match.PlayerTwo.Root.transform.position;
            Vector3 playerOneVelocity = match.PlayerOne.Body.linearVelocity;
            Vector3 playerTwoVelocity = match.PlayerTwo.Body.linearVelocity;

            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);

            AssertPassiveVisual(dropped.VisualInstance);
            Assert.That(dropped.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(dropped.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(dropped.GetComponentsInChildren<QusapAttackHitbox>(true), Is.Empty);
            Assert.That(match.PlayerTwo.Receiver.TotalDamageReceived, Is.EqualTo(damage));
            Assert.That(match.PlayerOne.Root.transform.position, Is.EqualTo(playerOnePosition));
            Assert.That(match.PlayerTwo.Root.transform.position, Is.EqualTo(playerTwoPosition));
            Assert.That(match.PlayerOne.Body.linearVelocity, Is.EqualTo(playerOneVelocity));
            Assert.That(match.PlayerTwo.Body.linearVelocity, Is.EqualTo(playerTwoVelocity));
            Assert.That(dropped.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void DroppedWeaponRemainsInTwoPointFiveDPlaneAndSettles()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            float planeZ = dropped.PlaneZ;

            for (int i = 0; i < 4; i++)
            {
                dropped.Advance(QusapDroppedWeaponView.DefaultDuration / 4f);
                Assert.That(dropped.transform.position.z, Is.EqualTo(planeZ));
            }

            Vector3 settledPosition = dropped.transform.position;
            dropped.Advance(10f);
            Assert.That(dropped.IsSettled, Is.True);
            Assert.That(dropped.transform.position, Is.EqualTo(settledPosition));
            Assert.That(dropped.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void DisableAndEnableComponentsDoesNotDuplicateInstances()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance playerOneWeapon = match.Bootstrap.PlayerOneWeapon;
            QusapWeaponInstance playerTwoWeapon = match.Bootstrap.PlayerTwoWeapon;
            GameObject playerOneVisual = match.PlayerOne.Presenter.EquippedVisual;
            GameObject playerTwoVisual = match.PlayerTwo.Presenter.EquippedVisual;

            match.Bootstrap.enabled = false;
            match.PlayerOne.Presenter.enabled = false;
            match.PlayerTwo.Presenter.enabled = false;
            match.PlayerOne.Equipment.enabled = false;
            match.PlayerTwo.Equipment.enabled = false;
            match.PlayerOne.Equipment.enabled = true;
            match.PlayerTwo.Equipment.enabled = true;
            match.PlayerOne.Presenter.enabled = true;
            match.PlayerTwo.Presenter.enabled = true;
            match.Bootstrap.enabled = true;

            Assert.That(match.Bootstrap.TryInitialize(), Is.True);
            Assert.That(match.Bootstrap.PlayerOneWeapon, Is.SameAs(playerOneWeapon));
            Assert.That(match.Bootstrap.PlayerTwoWeapon, Is.SameAs(playerTwoWeapon));
            Assert.That(match.PlayerOne.Presenter.EquippedVisual, Is.SameAs(playerOneVisual));
            Assert.That(match.PlayerTwo.Presenter.EquippedVisual, Is.SameAs(playerTwoVisual));
            Assert.That(match.PlayerOne.Presenter.WeaponSocket.childCount, Is.EqualTo(1));
            Assert.That(match.PlayerTwo.Presenter.WeaponSocket.childCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator UnarmedPlayerAutomaticallyPicksSettledWeaponWithoutSideEffects()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerOne.Equipment.TryDrop(out _, out _),
                Is.EqualTo(QusapWeaponOperationResult.Success));
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            float damageBefore = match.PlayerOne.Receiver.TotalDamageReceived;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            GameObject expectedVisualPrefab = dropped.VisualPrefab;
            match.PlayerOne.Root.transform.position = dropped.transform.position;
            Vector3 positionBefore = match.PlayerOne.Root.transform.position;
            Vector3 velocityBefore = match.PlayerOne.Body.linearVelocity;
            ulong instanceId = weapon.InstanceId;

            yield return null;

            Assert.That(match.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon.InstanceId,
                Is.EqualTo(instanceId));
            Assert.That(weapon.OwnerEntityId,
                Is.EqualTo(match.PlayerOne.Equipment.OwnerEntityId));
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero);
            Assert.That(dropped == null || !dropped.gameObject.activeSelf, Is.True);
            AssertSingleEquippedVisual(match.PlayerOne);
            Assert.That(match.PlayerOne.Presenter.DisplayedVisualPrefab,
                Is.SameAs(expectedVisualPrefab));
            AssertPassiveVisual(match.PlayerOne.Presenter.EquippedVisual);
            Assert.That(match.PlayerOne.Root.transform.position, Is.EqualTo(positionBefore));
            Assert.That(match.PlayerOne.Body.linearVelocity, Is.EqualTo(velocityBefore));
            Assert.That(match.PlayerOne.Receiver.TotalDamageReceived, Is.EqualTo(damageBefore));
            Assert.That(match.PlayerOne.Combat.IsAttacking, Is.False);
        }

        [Test]
        public void PickupWaitsForDroppedVisualToSettle()
        {
            MatchRig match = CreateMatch();
            match.PlayerOne.Equipment.TryDrop(out _, out _);
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            match.PlayerOne.Root.transform.position = dropped.transform.position;

            Assert.That(match.Bootstrap.ProcessPickups(dropped.DroppedAt), Is.Zero);
            Assert.That(match.PlayerOne.Equipment.HasWeapon, Is.False);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));

            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = dropped.transform.position;
            Assert.That(match.Bootstrap.ProcessPickups(dropped.DroppedAt), Is.EqualTo(1));
        }

        [Test]
        public void ArmedPlayerNeitherSwapsNorBlocksEligibleUnarmedPlayer()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance armedWeapon = match.PlayerOne.Equipment.EquippedWeapon;
            QusapWeaponInstance droppedInstance = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = dropped.transform.position;
            match.PlayerTwo.Root.transform.position =
                dropped.transform.position + Vector3.right * 0.25f;

            Assert.That(match.Bootstrap.ProcessPickups(
                dropped.DroppedAt + match.Bootstrap.PreviousOwnerPickupLockout),
                Is.EqualTo(1));
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(armedWeapon));
            Assert.That(match.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(droppedInstance));
            AssertExactlyOneOwnedEquippedRepresentation(
                match,
                match.PlayerOne,
                armedWeapon);
            AssertExactlyOneOwnedEquippedRepresentation(
                match,
                match.PlayerTwo,
                droppedInstance);
        }

        [Test]
        public void FormerOwnerCannotRecoverUntilExactLockoutBoundary()
        {
            MatchRig match = CreateMatch();
            match.PlayerOne.Root.transform.position += Vector3.left * 100f;
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerTwo.Root.transform.position = dropped.transform.position;

            Assert.That(match.Bootstrap.ProcessPickups(
                dropped.DroppedAt + match.Bootstrap.PreviousOwnerPickupLockout - 0.001d),
                Is.Zero);
            Assert.That(match.PlayerTwo.Equipment.HasWeapon, Is.False);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));

            Assert.That(match.Bootstrap.ProcessPickups(
                dropped.DroppedAt + match.Bootstrap.PreviousOwnerPickupLockout),
                Is.EqualTo(1));
            Assert.That(match.PlayerTwo.Equipment.EquippedWeapon, Is.SameAs(weapon));
        }

        [Test]
        public void TwoPlayersCannotClaimSameDroppedInstance()
        {
            MatchRig match = CreateMatch();
            match.PlayerOne.Equipment.TryDrop(out _, out _);
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position =
                dropped.transform.position + Vector3.left * 0.1f;
            match.PlayerTwo.Root.transform.position =
                dropped.transform.position + Vector3.right * 0.2f;

            Assert.That(match.Bootstrap.ProcessPickups(
                dropped.DroppedAt + match.Bootstrap.PreviousOwnerPickupLockout),
                Is.EqualTo(1));
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(match.PlayerTwo.Equipment.HasWeapon, Is.False);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero);
        }

        [Test]
        public void RejectedEquipLeavesDroppedRepresentationAvailable()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance armedWeapon = match.PlayerOne.Equipment.EquippedWeapon;
            QusapWeaponInstance droppedInstance = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = dropped.transform.position;

            Assert.That(match.Bootstrap.TryConfirmPickup(
                match.PlayerOne.Equipment,
                dropped), Is.False);
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(armedWeapon));
            Assert.That(droppedInstance.IsFree, Is.True);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));
            Assert.That(match.Bootstrap.DroppedWeapons[0], Is.SameAs(dropped));
            Assert.That(dropped.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void RepeatedPickupCannotDuplicateLogicalOrVisualInstance()
        {
            MatchRig match = CreateMatch();
            Assert.That(match.PlayerOne.Equipment.TryDrop(
                out QusapWeaponInstance releasedWeapon,
                out _), Is.EqualTo(QusapWeaponOperationResult.Success));
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = dropped.transform.position;
            double timestamp = dropped.DroppedAt + 2d;
            Assert.That(match.Bootstrap.ProcessPickups(timestamp), Is.EqualTo(1));
            ulong revision = match.PlayerOne.Equipment.Revision;
            GameObject equippedVisual = match.PlayerOne.Presenter.EquippedVisual;

            Assert.That(match.Bootstrap.ProcessPickups(timestamp), Is.Zero);
            Assert.That(match.Bootstrap.TryPickup(
                match.PlayerOne.Equipment, dropped, timestamp), Is.False);
            Assert.That(match.PlayerOne.Equipment.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(match.PlayerOne.Equipment.Revision, Is.EqualTo(revision));
            Assert.That(match.PlayerOne.Presenter.EquippedVisual, Is.SameAs(equippedVisual));
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero);
            AssertExactlyOneOwnedEquippedRepresentation(
                match,
                match.PlayerOne,
                weapon);
            Assert.That(releasedWeapon.IsFree, Is.True);
            Assert.That(CountLogicalOwners(match, releasedWeapon.InstanceId), Is.Zero);
            Assert.That(CountWeaponRepresentations(match, releasedWeapon.InstanceId), Is.Zero);
        }

        [Test]
        public void DropPickupAndSecondDisarmPreserveIdentity()
        {
            MatchRig match = CreateMatch();
            match.PlayerOne.Equipment.TryDrop(out _, out _);
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            ulong instanceId = weapon.InstanceId;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView firstDrop = match.Bootstrap.DroppedWeapons[0];
            firstDrop.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = firstDrop.transform.position;
            Assert.That(match.Bootstrap.ProcessPickups(firstDrop.DroppedAt), Is.EqualTo(1));

            Assert.That(match.PlayerOne.Equipment.TryDisarm(match.PlayerTwo.Combat), Is.True);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));
            QusapDroppedWeaponView secondDrop = match.Bootstrap.DroppedWeapons[0];
            Assert.That(secondDrop, Is.Not.SameAs(firstDrop));
            Assert.That(secondDrop.Weapon, Is.SameAs(weapon));
            Assert.That(secondDrop.InstanceId, Is.EqualTo(instanceId));
            Assert.That(weapon.IsFree, Is.True);
        }

        [Test]
        public void DisabledOrPausedCoordinatorDoesNotPickup()
        {
            MatchRig match = CreateMatch();
            match.PlayerOne.Equipment.TryDrop(out _, out _);
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = dropped.transform.position;
            double timestamp = dropped.DroppedAt + 2d;

            match.Bootstrap.enabled = false;
            Assert.That(match.Bootstrap.ProcessPickups(timestamp), Is.Zero);
            match.Bootstrap.enabled = true;

            float previousTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0f;
                Assert.That(match.Bootstrap.ProcessPickups(timestamp), Is.Zero);
            }
            finally
            {
                Time.timeScale = previousTimeScale;
            }

            Assert.That(match.PlayerOne.Equipment.HasWeapon, Is.False);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.EqualTo(1));
            Assert.That(dropped.gameObject.activeSelf, Is.True);
        }

        [Test]
        public void DestroyingMatchAfterPickupReleasesCurrentOwnershipAndRecords()
        {
            MatchRig match = CreateMatch();
            match.PlayerOne.Equipment.TryDrop(out _, out _);
            QusapWeaponInstance weapon = match.PlayerTwo.Equipment.EquippedWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            dropped.Advance(QusapDroppedWeaponView.DefaultDuration);
            match.PlayerOne.Root.transform.position = dropped.transform.position;
            Assert.That(match.Bootstrap.ProcessPickups(dropped.DroppedAt), Is.EqualTo(1));
            GameObject root = match.Bootstrap.gameObject;

            UnityEngine.Object.DestroyImmediate(root);
            matchRoots.Remove(root);

            Assert.That(weapon.IsFree, Is.True);
            Assert.That(match.PlayerOne.Equipment.HasWeapon, Is.False);
            Assert.That(root == null, Is.True);
        }

        [Test]
        public void DestroyingMatchCleansRepresentationsAndLogicalOwnership()
        {
            MatchRig match = CreateMatch();
            QusapWeaponInstance playerOneWeapon = match.Bootstrap.PlayerOneWeapon;
            QusapWeaponInstance playerTwoWeapon = match.Bootstrap.PlayerTwoWeapon;
            Assert.That(match.PlayerTwo.Equipment.TryDisarm(match.PlayerOne.Combat), Is.True);
            QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[0];
            GameObject root = match.Bootstrap.gameObject;

            UnityEngine.Object.DestroyImmediate(root);
            matchRoots.Remove(root);

            Assert.That(root == null, Is.True);
            Assert.That(dropped == null, Is.True);
            Assert.That(playerOneWeapon.IsFree, Is.True);
            Assert.That(playerTwoWeapon.IsFree, Is.True);
        }

        private MatchRig CreateMatch(bool useCombatInput = false)
        {
            PlayerRig playerOne = CreatePlayer(
                "PlayerOne",
                new Vector3(-2f, 1f, 0f),
                useCombatInput);
            PlayerRig playerTwo = CreatePlayer(
                "PlayerTwo",
                new Vector3(2f, 1f, 0f),
                useCombatInput);
            playerOne.Presenter.ApplyFacing(1);
            playerTwo.Presenter.ApplyFacing(-1);

            GameObject root = new("WeaponMatchBootstrap_Test");
            matchRoots.Add(root);
            QusapWeaponMatchBootstrap bootstrap = root.AddComponent<QusapWeaponMatchBootstrap>();
            bootstrap.Configure(
                catalog,
                playerOne.Equipment,
                playerOne.Presenter,
                playerTwo.Equipment,
                playerTwo.Presenter);
            Assert.That(bootstrap.TryInitialize(), Is.True);
            return new MatchRig(bootstrap, playerOne, playerTwo);
        }

        private PlayerRig CreatePlayer(
            string name,
            Vector3 position,
            bool useCombatInput)
        {
            PlayerRig player = new(name, position, catalog, useCombatInput);
            players.Add(player);
            return player;
        }

        private GameObject CreateVisualPrefab(string name)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = name;
            Collider collider = visual.GetComponent<Collider>();
            UnityEngine.Object.DestroyImmediate(collider);
            visual.transform.localScale = new Vector3(0.12f, 1.8f, 0.08f);
            visual.SetActive(false);
            visualPrefabs.Add(visual);
            return visual;
        }

        private static void AssertSingleEquippedVisual(PlayerRig player)
        {
            Assert.That(player.Presenter.EquippedVisual, Is.Not.Null);
            Assert.That(player.Presenter.EquippedVisual.activeSelf, Is.True);
            Assert.That(player.Presenter.WeaponSocket.childCount, Is.EqualTo(1));
            Assert.That(player.Presenter.DisplayedWeapon,
                Is.SameAs(player.Equipment.EquippedWeapon));
        }

        private static void AssertExactlyOneOwnedEquippedRepresentation(
            MatchRig match,
            PlayerRig expectedOwner,
            QusapWeaponInstance weapon)
        {
            Assert.That(expectedOwner.Equipment.EquippedWeapon, Is.SameAs(weapon));
            Assert.That(weapon.OwnerEntityId, Is.EqualTo(expectedOwner.Equipment.OwnerEntityId));
            Assert.That(expectedOwner.Presenter.DisplayedWeapon, Is.SameAs(weapon));
            Assert.That(expectedOwner.Presenter.EquippedVisual, Is.Not.Null);
            Assert.That(expectedOwner.Presenter.EquippedVisual.activeInHierarchy, Is.True);
            Assert.That(CountActiveSocketChildren(expectedOwner.Presenter), Is.EqualTo(1));
            Assert.That(CountActiveSocketChildren(match.PlayerOne.Presenter),
                Is.LessThanOrEqualTo(1));
            Assert.That(CountActiveSocketChildren(match.PlayerTwo.Presenter),
                Is.LessThanOrEqualTo(1));
            Assert.That(CountLogicalOwners(match, weapon.InstanceId), Is.EqualTo(1));
            Assert.That(CountWeaponRepresentations(match, weapon.InstanceId), Is.EqualTo(1));
            Assert.That(CountDroppedRepresentations(match, weapon.InstanceId), Is.Zero);
        }

        private static int CountActiveSocketChildren(QusapEquippedWeaponPresenter presenter)
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

        private static int CountLogicalOwners(MatchRig match, ulong instanceId)
        {
            int count = 0;
            if (match.PlayerOne.Equipment.EquippedWeapon?.InstanceId == instanceId)
            {
                count++;
            }

            if (match.PlayerTwo.Equipment.EquippedWeapon?.InstanceId == instanceId)
            {
                count++;
            }

            return count;
        }

        private static int CountWeaponRepresentations(MatchRig match, ulong instanceId)
        {
            int count = CountEquippedRepresentation(match.PlayerOne, instanceId)
                + CountEquippedRepresentation(match.PlayerTwo, instanceId);
            return count + CountDroppedRepresentations(match, instanceId);
        }

        private static int CountEquippedRepresentation(PlayerRig player, ulong instanceId)
        {
            return player.Presenter.DisplayedWeapon?.InstanceId == instanceId
                && player.Presenter.EquippedVisual != null
                && player.Presenter.EquippedVisual.activeInHierarchy
                ? 1
                : 0;
        }

        private static int CountDroppedRepresentations(MatchRig match, ulong instanceId)
        {
            int count = 0;
            for (int i = 0; i < match.Bootstrap.DroppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = match.Bootstrap.DroppedWeapons[i];
                if (dropped != null
                    && dropped.InstanceId == instanceId
                    && dropped.gameObject.activeInHierarchy)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertPassiveVisual(GameObject visual)
        {
            Assert.That(visual, Is.Not.Null);
            Assert.That(visual.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(visual.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(visual.GetComponentsInChildren<QusapAttackHitbox>(true), Is.Empty);
        }

        private static void AssertWeaponUnchanged(
            MatchRig match,
            QusapWeaponInstance weapon,
            GameObject visual,
            ulong revision,
            string outcome)
        {
            Assert.That(match.PlayerTwo.Equipment.EquippedWeapon,
                Is.SameAs(weapon), outcome);
            Assert.That(match.PlayerTwo.Equipment.Revision, Is.EqualTo(revision), outcome);
            Assert.That(match.PlayerTwo.Presenter.EquippedVisual,
                Is.SameAs(visual), outcome);
            Assert.That(match.Bootstrap.DroppedWeaponCount, Is.Zero, outcome);
        }

        private static IEnumerator ArmDisarmFinisher(
            PlayerRig attacker,
            PlayerRig defender)
        {
            QusapComboDefinition definition = null;
            IReadOnlyList<QusapComboDefinition> definitions =
                QusapComboDefinition.CreateDefaultDefinitions();
            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i].ComboId == QusapComboId.Disarm)
                {
                    definition = definitions[i];
                    break;
                }
            }

            Assert.That(definition, Is.Not.Null);
            double timestamp = 10d;
            for (int i = 0; i < definition.StepCount - 1; i++)
            {
                if (i > 0)
                {
                    QusapComboStep step = definition.GetStep(i);
                    timestamp += (step.MinimumDelay + step.MaximumDelay) / 2d;
                }

                attacker.Input.EnqueueCombatCommand(
                    definition.GetStep(i).Command,
                    timestamp);
                yield return new WaitForFixedUpdate();

                int frames = 0;
                while (attacker.Combat.CurrentPhase != QusapAttackPhase.Active
                    && frames++ < 64)
                {
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(attacker.Combat.CurrentPhase,
                    Is.EqualTo(QusapAttackPhase.Active));
                attacker.Combat.NotifyAttackHit(defender.Receiver);

                frames = 0;
                while (attacker.Combat.IsAttacking && frames++ < 64)
                {
                    yield return new WaitForFixedUpdate();
                }

                Assert.That(attacker.Combat.IsAttacking, Is.False);
            }

            QusapComboStep finalStep = definition.GetStep(definition.StepCount - 1);
            timestamp += (finalStep.MinimumDelay + finalStep.MaximumDelay) / 2d;
            attacker.Input.EnqueueCombatCommand(finalStep.Command, timestamp);
            yield return new WaitForFixedUpdate();
            Assert.That(attacker.Combat.HasArmedFinisher, Is.True);
            Assert.That(attacker.Combat.ArmedFinisherCombo,
                Is.EqualTo(QusapComboId.Disarm));
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

        private sealed class PlayerRig : IDisposable
        {
            public PlayerRig(
                string name,
                Vector3 position,
                QusapWeaponVisualCatalog catalog,
                bool useCombatInput)
            {
                Root = new GameObject(name);
                Root.SetActive(false);
                Root.transform.position = position;
                Body = Root.AddComponent<Rigidbody>();
                Body.useGravity = false;
                Body.constraints = RigidbodyConstraints.FreezeAll;
                Input = useCombatInput
                    ? Root.AddComponent<QusapInputReader>()
                    : Root.AddComponent<QusapWeaponVisualTestInputReader>();
                Root.AddComponent<QusapGroundSensor>();
                Root.AddComponent<QusapWallSensor>();
                Root.AddComponent<QusapHorizontalMotor>();
                Root.AddComponent<QusapVerticalMotor>();
                Root.AddComponent<QusapDashMotor>();
                Receiver = Root.AddComponent<QusapHitReceiver>();
                Root.AddComponent<QusapHitstunController>();
                GameObject hitboxObject = new("AttackHitbox");
                hitboxObject.transform.SetParent(Root.transform, false);
                hitboxObject.AddComponent<QusapAttackHitbox>();
                Combat = Root.AddComponent<QusapCombatController>();
                Equipment = Root.AddComponent<QusapWeaponEquipment>();
                GameObject socketObject = new("WeaponSocket");
                socketObject.transform.SetParent(Root.transform, false);
                Presenter = Root.AddComponent<QusapEquippedWeaponPresenter>();
                Presenter.Configure(Equipment, Combat, catalog, socketObject.transform);
                if (useCombatInput)
                {
                    LogAssert.Expect(
                        LogType.Error,
                        "QusapInputReader requires the configured Gameplay actions in its InputActionAsset.");
                }

                Root.SetActive(true);
                Assert.That(Equipment.TryInitialize(Combat), Is.True);
                Assert.That(Presenter.TryInitialize(), Is.True);
            }

            public GameObject Root { get; }
            public Rigidbody Body { get; }
            public QusapInputReader Input { get; }
            public QusapHitReceiver Receiver { get; }
            public QusapCombatController Combat { get; }
            public QusapWeaponEquipment Equipment { get; }
            public QusapEquippedWeaponPresenter Presenter { get; }

            public void Dispose()
            {
                if (Root != null)
                {
                    UnityEngine.Object.DestroyImmediate(Root);
                }
            }
        }
    }
}
