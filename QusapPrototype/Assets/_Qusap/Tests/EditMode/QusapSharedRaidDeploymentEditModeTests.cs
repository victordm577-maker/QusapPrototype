using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapSharedRaidDeploymentEditModeTests
    {
        private sealed class FaultFiles : QusapMemoryProfileFiles
        {
            public bool Fail; public Action DuringWrite;
            public override void WriteClosedAndFlushed(string path, string contents)
            { base.WriteClosedAndFlushed(path, contents); DuringWrite?.Invoke(); if (Fail) throw new IOException("Injected second profile failure after staging"); }
        }
        private QusapRaidLootCatalog catalog;
        private QusapPersistentStashRepository[] repositories;
        private FaultFiles[] files;
        private GameObject[] roots;
        private QusapRaidDeploymentAuthority[] deployments;
        private QusapSharedRaidContext context;
        private ulong nativeSequence;
        [SetUp] public void Setup()
        {
            catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            files = new[] { new FaultFiles(), new FaultFiles() };
            repositories = Enumerable.Range(0, 2).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(), "QusapSharedMemoryOnly", Guid.NewGuid().ToString("N")), catalog.Definitions, "shared-profile-" + i, files[i])).ToArray();
            roots = Enumerable.Range(0, 2).Select(i => new GameObject("SharedPlayer" + i)).ToArray();
            deployments = new QusapRaidDeploymentAuthority[2];
            for (int i = 0; i < 2; i++)
            {
                roots[i].SetActive(false); var combat = roots[i].AddComponent<QusapCombatController>();
                var equipment = roots[i].AddComponent<QusapWeaponEquipment>(); Assert.That(equipment.TryInitialize(combat), Is.True);
                deployments[i] = Make(i, "P" + (i + 1));
            }
            context = new QusapSharedRaidContext("P1", "P2");
        }
        private QusapRaidDeploymentAuthority Make(int index, string participant, QusapPersistentStashRepository repository = null)
            => new(repository ?? repositories[index], catalog, participant, roots[index].GetComponent<QusapWeaponEquipment>(), d => new QusapWeaponInstance(++nativeSequence, d.WeaponDefinition));
        private QusapLootInstance Seed(int index, string id, int quantity = 1)
        {
            Assert.That(catalog.TryResolve(id, out var definition), Is.True);
            using var session = new QusapRaidSession("seed-" + Guid.NewGuid().ToString("N"), repositories[index]);
            var inventory = session.Register("seed", repositories[index].ProfileId);
            var weapon = definition.Category == QusapLootCategory.Weapon ? new QusapWeaponInstance(++nativeSequence, definition.WeaponDefinition) : null;
            var item = session.World.Create(definition, "isolated shared test", weapon, quantity);
            Assert.That(inventory.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            Assert.That(session.SettleExtraction(inventory), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private void Select(int index, QusapLootInstance weapon = null, params QusapLootInstance[] consumables)
        { Assert.That(deployments[index].PrepareLoadout(weapon?.LootInstanceId, consumables.Select(i => new QusapLoadoutStack(i.LootInstanceId, i.Quantity)).ToArray()), Is.True); }
        private void Enlist() { foreach (var deployment in deployments) Assert.That(context.Add(deployment), Is.True, context.LastResult); }
        private void Commit() { Enlist(); Assert.That(context.CommitDeployment(), Is.True, context.LastResult); }
        private string Bytes(int index) => files[index].Exists(repositories[index].Storage.MainPath) ? files[index].Read(repositories[index].Storage.MainPath) : null;
        [TearDown] public void Cleanup()
        { context.Dispose(); foreach (var deployment in deployments) deployment.Dispose(); foreach (var root in roots) UnityEngine.Object.DestroyImmediate(root); }

        [Test] public void BothParticipantsShareExactlyOneRaidSessionAndLedgerWithIndependentProfiles()
        {
            Select(0); Select(1); Commit(); Assert.That(context.StartRunning(), Is.True);
            Assert.That(deployments[0].RaidId, Is.EqualTo(context.RaidId)); Assert.That(deployments[1].RaidId, Is.EqualTo(context.RaidId));
            Assert.That(deployments.All(d => ReferenceEquals(d.Session, context.Session)), Is.True);
            Assert.That(deployments[0].Session.World, Is.SameAs(deployments[1].Session.World));
            Assert.That(repositories[0], Is.Not.SameAs(repositories[1])); Assert.That(repositories[0].ProfileId, Is.Not.EqualTo(repositories[1].ProfileId));
            Assert.That(context.Session.ParticipantCount, Is.EqualTo(2));
        }
        [Test] public void ExactSelectionsWithdrawOnceFromTheirOwnProfilesAndRetainInstanceIdentity()
        {
            var sword1 = Seed(0, "raid_purple_sword"); var heal1 = Seed(0, "raid_minor_heal", 2);
            var sword2 = Seed(1, "raid_basic_sword"); var heal2 = Seed(1, "raid_major_heal"); var leftover = Seed(1, "raid_rare_relic");
            Select(0, sword1, heal1); Select(1, sword2, heal2); var revisions = repositories.Select(p => p.Revision).ToArray(); Commit();
            for (int i = 0; i < 2; i++) Assert.That(repositories[i].Revision, Is.EqualTo(revisions[i] + 1));
            Assert.That(repositories[0].ReadStashSnapshot(repositories[0].ProfileId), Is.Empty);
            Assert.That(repositories[1].ReadStashSnapshot(repositories[1].ProfileId).Single().LootInstanceId, Is.EqualTo(leftover.LootInstanceId));
            Assert.That(deployments[0].Inventory.ReadInventorySnapshot().Backpack.Single(i => i.HasValue).Value.LootInstanceId, Is.EqualTo(heal1.LootInstanceId));
            Assert.That(deployments[1].Inventory.ReadInventorySnapshot().Backpack.Single(i => i.HasValue).Value.LootInstanceId, Is.EqualTo(heal2.LootInstanceId));
            Assert.That(deployments[0].WeaponAdapter.CurrentWeapon, Is.SameAs(sword1.Weapon)); Assert.That(deployments[1].WeaponAdapter.CurrentWeapon, Is.SameAs(sword2.Weapon));
            foreach (var item in new[] { sword1, heal1, sword2, heal2 }) Assert.That(context.Session.World.Get(item.LootInstanceId), Is.SameAs(item));
            Assert.That(context.CommitDeployment(), Is.False); for (int i = 0; i < 2; i++) Assert.That(repositories[i].Revision, Is.EqualTo(revisions[i] + 1));
        }
        [Test] public void EachLoanerIsUniqueAndBelongsOnlyToItsParticipant()
        {
            Select(0); Select(1); Commit(); var loans = context.Session.World.Instances.Where(i => i.RaidLoaner).ToArray();
            Assert.That(loans.Length, Is.EqualTo(2)); Assert.That(loans.Select(i => i.HolderId), Is.EquivalentTo(new[] { "P1", "P2" }));
            Assert.That(loans.Select(i => i.LootInstanceId).Distinct().Count(), Is.EqualTo(2));
            Assert.That(loans.Select(i => i.Weapon.InstanceId).Distinct().Count(), Is.EqualTo(2));
            for (int i = 0; i < 2; i++) Assert.That(loans.Single(l => l.HolderId == deployments[i].ParticipantId).Weapon, Is.SameAs(deployments[i].WeaponAdapter.CurrentWeapon));
        }
        [Test] public void DuplicateParticipantIsRejectedWithoutAnyProfileOrLedgerMutation()
        {
            Select(0); Select(1); Assert.That(context.Add(deployments[0]), Is.True);
            using var duplicate = Make(1, "P1"); duplicate.PrepareLoadout(); Assert.That(context.Add(duplicate), Is.False);
            Assert.That(context.Deployments.Count, Is.EqualTo(1)); Assert.That(context.Session, Is.Null); Assert.That(repositories.All(r => !r.HasActiveDeployment), Is.True);
        }
        [Test] public void DuplicateProfileObjectAndDuplicateProfileIdentityAreBothRejected()
        {
            Assert.That(context.Add(deployments[0]), Is.True);
            using var sameRepository = Make(1, "P2", repositories[0]); Assert.That(context.Add(sameRepository), Is.False);
            var sameIdentity = new QusapPersistentStashRepository(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), catalog.Definitions, repositories[0].ProfileId, new QusapMemoryProfileFiles());
            using var duplicateId = Make(1, "P2", sameIdentity); Assert.That(context.Add(duplicateId), Is.False);
            Assert.That(context.Deployments.Count, Is.EqualTo(1)); Assert.That(repositories[0].HasActiveDeployment, Is.False);
        }
        [Test] public void RequiredParticipantMissingPreventsCommitAndStartWithoutMutation()
        {
            Select(0); context.Add(deployments[0]); Assert.That(context.CommitDeployment(), Is.False); Assert.That(context.StartRunning(), Is.False);
            Assert.That(context.Session, Is.Null); Assert.That(repositories[0].HasActiveDeployment, Is.False);
        }
        [TestCase(false)] [TestCase(true)] public void FailureOfSecondDeploymentRestoresExactFirstProfileAndLeavesNoOrphansAndAllowsRetry(bool ownSword)
        {
            var sword = ownSword ? Seed(0, "raid_purple_sword") : null; var a = Seed(0, "raid_minor_heal", 2); var b = Seed(1, "raid_major_heal");
            Select(0, sword, a); Select(1, null, b); Enlist();
            var bytes = new[] { Bytes(0), Bytes(1) }; var revisions = repositories.Select(r => r.Revision).ToArray(); var writes = repositories.Select(r => r.Storage.Writes).ToArray();
            QusapRaidSession aborted = null; QusapLootInstance[] prior = null;
            files[1].DuringWrite = () => { aborted = deployments[0].Session; prior = aborted.World.Instances.ToArray(); Assert.That(context.CommitDeployment(), Is.False); };
            files[1].Fail = true; Assert.That(context.CommitDeployment(), Is.False);
            Assert.That(context.Session, Is.Null); Assert.That(aborted.ParticipantCount, Is.Zero); Assert.That(aborted.World.Instances, Is.Empty);
            Assert.That(deployments.All(d => d.Session == null && d.Inventory == null && d.Status == QusapDeploymentStatus.None), Is.True);
            foreach (var loan in prior.Where(i => i.RaidLoaner)) Assert.That(loan.Weapon.IsRetired && loan.Weapon.IsFree, Is.True);
            for (int i = 0; i < 2; i++)
            { Assert.That(Bytes(i), Is.EqualTo(bytes[i])); Assert.That(repositories[i].Revision, Is.EqualTo(revisions[i])); Assert.That(repositories[i].Storage.Writes, Is.EqualTo(writes[i]));
                Assert.That(repositories[i].HasActiveDeployment, Is.False); Assert.That(repositories[i].Storage.TemporaryExists, Is.False); Assert.That(roots[i].GetComponent<QusapWeaponEquipment>().HasWeapon, Is.False); }
            Assert.That(a.Location, Is.EqualTo(QusapLootLocation.Stash)); Assert.That(b.Location, Is.EqualTo(QusapLootLocation.Stash));
            files[1].Fail = false; files[1].DuringWrite = null; Assert.That(context.CommitDeployment(), Is.True, context.LastResult); Assert.That(context.StartRunning(), Is.True);
            Assert.That(context.Session.World.Get(a.LootInstanceId), Is.SameAs(a)); Assert.That(context.Session.World.Get(b.LootInstanceId), Is.SameAs(b));
        }
        [Test] public void CancellationBeforeRunningRestoresExactProfilesAndIsIdempotent()
        {
            var a = Seed(0, "raid_minor_heal"); Select(0, null, a); Select(1); string before = Bytes(0); long revision = repositories[0].Revision; Commit();
            var loans = context.Session.World.Instances.Where(i => i.RaidLoaner).ToArray(); Assert.That(context.CancelDeployment(), Is.True); Assert.That(context.CancelDeployment(), Is.True);
            Assert.That(Bytes(0), Is.EqualTo(before)); Assert.That(repositories[0].Revision, Is.EqualTo(revision)); Assert.That(a.Location, Is.EqualTo(QusapLootLocation.Stash));
            Assert.That(loans.All(i => i.Weapon.IsRetired && i.Weapon.IsFree), Is.True); Assert.That(repositories.All(r => !r.HasActiveDeployment), Is.True);
            Assert.That(context.CommitDeployment(), Is.True); Assert.That(context.StartRunning(), Is.True); Assert.That(context.CancelDeployment(), Is.False);
        }
        [Test] public void FailureWhileStartingSecondParticipantAlsoRestoresBothDeployments()
        {
            var a = Seed(0, "raid_minor_heal"); var b = Seed(1, "raid_major_heal"); Select(0, null, a); Select(1, null, b);
            var bytes = new[] { Bytes(0), Bytes(1) }; Commit(); files[1].Fail = true;
            Assert.That(context.StartRunning(), Is.False); Assert.That(context.Session, Is.Null);
            Assert.That(Bytes(0), Is.EqualTo(bytes[0])); Assert.That(Bytes(1), Is.EqualTo(bytes[1]));
            Assert.That(repositories.All(r => !r.HasActiveDeployment), Is.True); files[1].Fail = false;
            Assert.That(context.CommitDeployment(), Is.True); Assert.That(context.StartRunning(), Is.True);
        }
        [Test] public void SettlementsRouteToCorrectProfilesAndFinishIsIdempotent()
        {
            var a = Seed(0, "raid_minor_heal"); var b = Seed(1, "raid_major_heal"); Select(0, null, a); Select(1, null, b); Commit(); context.StartRunning();
            Assert.That(context.Session.SettleExtraction(deployments[0].Inventory, deployments[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
            Assert.That(deployments[1].Inventory.Status, Is.EqualTo(QusapRaidInventoryStatus.Winner));
            Assert.That(context.Session.SettleExtraction(deployments[1].Inventory, deployments[1].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            var revisions = repositories.Select(r => r.Revision).ToArray(); context.FinishSession(); context.FinishSession();
            Assert.That(repositories[0].ReadStashSnapshot(repositories[0].ProfileId).Single().LootInstanceId, Is.EqualTo(a.LootInstanceId));
            Assert.That(repositories[1].ReadStashSnapshot(repositories[1].ProfileId).Single().LootInstanceId, Is.EqualTo(b.LootInstanceId));
            Assert.That(repositories.Select(r => r.Revision), Is.EqualTo(revisions)); Assert.That(repositories.All(r => !r.HasActiveDeployment), Is.True);
            Assert.That(context.Session.World.LoanersDestroyed, Is.EqualTo(2)); Assert.That(context.Session.IsFinished, Is.True);
        }
        [Test] public void CancellationRejectsReentrantCommandsWhileRestoringBothProfiles()
        {
            var sword = Seed(0, "raid_purple_sword"); Select(0, sword); Select(1); Commit(); int rejected = 0;
            roots[0].GetComponent<QusapWeaponEquipment>().WeaponTransitioned += transition =>
            { if (transition.Type == QusapWeaponTransitionType.Dropped) { Assert.That(context.CancelDeployment(), Is.False); Assert.That(context.CommitDeployment(), Is.False); rejected++; } };
            Assert.That(context.CancelDeployment(), Is.True); Assert.That(rejected, Is.EqualTo(1));
            Assert.That(sword.Location, Is.EqualTo(QusapLootLocation.Stash)); Assert.That(repositories.All(r => !r.HasActiveDeployment), Is.True); Assert.That(context.Session, Is.Null);
        }
        [Test] public void CombinedProfilesAndLiveWorldContainNoDuplicateOwnership()
        {
            var a = Seed(0, "raid_purple_sword"); var b = Seed(1, "raid_minor_heal", 2); Select(0, a); Select(1, null, b); Commit(); context.StartRunning();
            var live = context.Session.World.Instances.Where(i => i.Location != QusapLootLocation.Stash && i.Location != QusapLootLocation.Consumed && i.Location != QusapLootLocation.Destroyed);
            var ids = live.Select(i => i.LootInstanceId).Concat(repositories.SelectMany(r => r.ReadStashSnapshot(r.ProfileId)).Select(i => i.LootInstanceId)).ToArray();
            Assert.That(ids.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(ids.Length));
            Assert.That(deployments.All(d => !d.CommitDeployment()), Is.True); Assert.That(context.Session.ParticipantCount, Is.EqualTo(2));
        }
        [Test] public void OneDeploymentCannotBeEnlistedInTwoContexts()
        {
            Select(0); Select(1); Enlist(); using var other = new QusapSharedRaidContext("P1");
            Assert.That(other.Add(deployments[0]), Is.False); Assert.That(other.Session, Is.Null);
            Assert.That(context.CommitDeployment(), Is.True); Assert.That(context.Session.ParticipantCount, Is.EqualTo(2));
        }
        [Test] public void AlreadyActiveProfileIsRejectedWithoutCancellingItsExistingSinglePlayerRaid()
        {
            Select(0); Assert.That(deployments[0].CommitDeployment(), Is.True);
            var original = deployments[0].Session; using var second = Make(1, "P2", repositories[0]);
            Assert.That(second.PrepareLoadout(), Is.False); Assert.That(context.Add(second), Is.False);
            Assert.That(context.CommitDeployment(), Is.False); Assert.That(context.Session, Is.Null);
            Assert.That(original.IsFinished, Is.False); Assert.That(repositories[0].HasActiveDeployment, Is.True); Assert.That(deployments[0].WeaponAdapter.CurrentWeapon, Is.Not.Null);
        }
    }
}
