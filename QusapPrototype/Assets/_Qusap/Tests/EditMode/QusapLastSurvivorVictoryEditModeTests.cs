using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapLastSurvivorVictoryEditModeTests
    {
        private sealed class FaultFiles : QusapMemoryProfileFiles
        {
            public bool Fail, FailAfterStaging; public Action DuringWrite;
            public override void WriteClosedAndFlushed(string path, string contents)
            {
                DuringWrite?.Invoke(); if (Fail) throw new IOException("Injected failure before staging");
                base.WriteClosedAndFlushed(path, contents); if (FailAfterStaging) throw new IOException("Injected failure after staging");
            }
            public void PreserveAndRemoveRejectedTemporary(string path)
            {
                // Diagnostic recovery only: exact rejected temporary in an isolated memory store.
                var values = (System.Collections.Generic.Dictionary<string, string>)typeof(QusapMemoryProfileFiles)
                    .GetField("contents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this);
                Assert.That(values.ContainsKey(path), Is.True); values.Remove(path);
            }
        }
        private QusapRaidLootCatalog catalog;
        private QusapPersistentStashRepository[] profiles;
        private FaultFiles[] files;
        private GameObject[] roots;
        private QusapSharedRaidContext context;
        private QusapRaidDeploymentAuthority[] players;
        private ulong nativeId;
        [SetUp] public void Setup()
        {
            catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            files = new[] { new FaultFiles(), new FaultFiles() };
            profiles = Enumerable.Range(0, 2).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(), "QusapVictoryMemoryTests", Guid.NewGuid().ToString("N")), catalog.Definitions, "victory-" + i, files[i])).ToArray();
            roots = Enumerable.Range(0, 2).Select(i => new GameObject("VictoryPlayer" + i)).ToArray();
            foreach (var root in roots)
            { root.SetActive(false); var combat = root.AddComponent<QusapCombatController>(); Assert.That(root.AddComponent<QusapWeaponEquipment>().TryInitialize(combat), Is.True); }
            Prepare();
        }
        private void Prepare()
        {
            context = new QusapSharedRaidContext("P1", "P2");
            players = Enumerable.Range(0, 2).Select(i => new QusapRaidDeploymentAuthority(profiles[i], catalog, "P" + (i + 1),
                roots[i].GetComponent<QusapWeaponEquipment>(), d => new QusapWeaponInstance(++nativeId, d.WeaponDefinition))).ToArray();
            foreach (var player in players) { Assert.That(player.PrepareLoadout(), Is.True); Assert.That(context.Add(player), Is.True); }
        }
        private void Start() { Assert.That(context.CommitDeployment(), Is.True); Assert.That(context.StartRunning(), Is.True); }
        private QusapLootInstance Give(int player, string id, int quantity = 1)
        {
            Assert.That(catalog.TryResolve(id, out var definition), Is.True);
            var native = definition.Category == QusapLootCategory.Weapon ? new QusapWeaponInstance(++nativeId, definition.WeaponDefinition) : null;
            var item = context.Session.World.Create(definition, "Victory test " + Guid.NewGuid().ToString("N"), native, quantity);
            Assert.That(players[player].Inventory.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private void Extract(int index) => Assert.That(context.Session.SettleExtraction(players[index].Inventory, players[index].WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
        private void Kill(int index) { players[index].Inventory.BlockOperations(); Assert.That(context.Session.SettleDeath(players[index].Inventory, players[index].WeaponAdapter), Is.EqualTo(QusapLootResult.Success)); }
        [TearDown] public void Cleanup() { context.Dispose(); foreach (var root in roots) UnityEngine.Object.DestroyImmediate(root); }
        private static QusapRaidSessionAuthority Authority(int count, Func<string, bool> commit = null, bool close = true, bool start = true)
        {
            var a = new QusapRaidSessionAuthority("authority-test"); var ids = Enumerable.Range(1, count).Select(i => "P" + i).ToArray();
            foreach (string id in ids) Assert.That(a.Register(id), Is.True);
            if (close) Assert.That((bool)typeof(QusapRaidSessionAuthority).GetMethod("CompleteSharedDeployment", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(a, new object[] { ids, commit ?? new Func<string, bool>(_ => true) }), Is.True);
            if (start) Assert.That(a.StartSession(), Is.True); return a;
        }
        [Test] public void NoWinnerDuringDeploymentOrBeforeSharedRunning()
        {
            Assert.That(context.Session, Is.Null); Assert.That(context.CommitDeployment(), Is.True);
            Assert.That(context.Session.Competition, Is.Null); Assert.That(context.Session.IsFinished, Is.False);
            Assert.That(context.Session.SettleExtraction(players[0].Inventory, players[0].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(profiles.All(p => !p.HasTriumphHorn), Is.True);
            var a = Authority(2, start: false); Assert.That(a.EvaluateVictory(), Is.False); Assert.That(a.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated), Is.False);
            Assert.That(a.WinnerId, Is.Null);
        }
        [Test] public void NoWinnerBeforeRosterIsClosedAndLegacyFlowRemainsCompatible()
        {
            var a = Authority(2, close: false); a.TryResolve("P1", QusapRaidParticipantOutcome.Extracted);
            Assert.That(a.RosterClosed, Is.False); Assert.That(a.EvaluateVictory(), Is.False); Assert.That(a.WinnerId, Is.Null);
            a.TryResolve("P2", QusapRaidParticipantOutcome.Extracted); Assert.That(a.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(a.FinalResult.ExtractedCount, Is.EqualTo(2));
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void RosterTwoThroughEightElectsExactlyOneWinnerAfterSevenOrFewerTerminals(int count)
        {
            int commits = 0; var a = Authority(count, _ => { commits++; return true; });
            Assert.That(a.Register("extra"), Is.False);
            for (int i = 1; i < count; i++)
            {
                Assert.That(a.TryResolve("P" + i, i % 2 == 0 ? QusapRaidParticipantOutcome.Extracted : QusapRaidParticipantOutcome.Eliminated), Is.True);
                if (i < count - 1) Assert.That(a.WinnerId, Is.Null);
            }
            Assert.That(a.WinnerId, Is.EqualTo("P" + count)); Assert.That(commits, Is.EqualTo(1)); Assert.That(a.ActiveCount, Is.Zero);
            Assert.That(a.FinalResult.Participants.Count(p => p.Outcome == QusapRaidParticipantOutcome.Winner), Is.EqualTo(1));
        }
        [TestCase(QusapRaidParticipantOutcome.Extracted)] [TestCase(QusapRaidParticipantOutcome.Eliminated)]
        public void RemainingActiveWinsAfterOnlyExtractionsOrOnlyEliminations(QusapRaidParticipantOutcome outcome)
        {
            var a = Authority(8); for (int i = 1; i <= 7; i++) a.TryResolve("P" + i, outcome);
            Assert.That(a.WinnerId, Is.EqualTo("P8")); Assert.That(a.FinalResult.Reason, Is.EqualTo(QusapRaidSessionFinishReason.LastSurvivor));
        }
        [Test] public void ZeroActiveFinishesWithoutWinnerIfLastActiveResolvesWhileWinnerWriteWasRejected()
        {
            var a = Authority(2, _ => false); a.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated);
            Assert.That(a.Status, Is.EqualTo(QusapRaidSessionStatus.Running)); a.TryResolve("P2", QusapRaidParticipantOutcome.Extracted);
            Assert.That(a.ActiveCount, Is.Zero); Assert.That(a.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(a.WinnerId, Is.Null); Assert.That(a.SessionFinishedEvents, Is.EqualTo(1));
        }
        [Test] public void SharedAuthorityRejectsUnsettledSignalsAndSimultaneousDepletionEndsWithoutWinner()
        {
            Start(); var a = context.Session.Competition;
            Assert.That(a.TryResolve("P1", QusapRaidParticipantOutcome.Extracted), Is.False);
            Assert.That(a.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated), Is.False); Assert.That(a.ActiveCount, Is.EqualTo(2));
            foreach (var player in players) player.Inventory.BlockOperations();
            Kill(0); Assert.That(a.Status, Is.EqualTo(QusapRaidSessionStatus.Running)); Kill(1);
            Assert.That(a.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(a.WinnerId, Is.Null);
            Assert.That(context.Status, Is.EqualTo(QusapDeploymentStatus.Resolved)); Assert.That(profiles.All(p => !p.HasTriumphHorn), Is.True);
        }
        [Test] public void TerminalOutcomesCannotBeReactivatedOrPromotedByCallerAndRepeatedEvaluationIsIdempotent()
        {
            int count = 0; var a = Authority(3, _ => { count++; return true; });
            a.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated);
            Assert.That(a.TryResolve("P1", QusapRaidParticipantOutcome.Active), Is.False);
            Assert.That(a.TryResolve("P1", QusapRaidParticipantOutcome.Extracted), Is.False);
            Assert.That(a.TryResolve("P2", QusapRaidParticipantOutcome.Winner), Is.False);
            a.TryResolve("P2", QusapRaidParticipantOutcome.Extracted);
            for (int i = 0; i < 5; i++) Assert.That(a.EvaluateVictory(), Is.False);
            foreach (string id in new[] { "P1", "P2", "P3" }) Assert.That(a.TryResolve(id, QusapRaidParticipantOutcome.Active), Is.False);
            Assert.That(a.TryGetOutcome("P1", out var outcome) && outcome == QusapRaidParticipantOutcome.Eliminated, Is.True);
            Assert.That(count, Is.EqualTo(1)); Assert.That(a.SessionFinishedEvents, Is.EqualTo(1));
        }
        [TestCase(false)] [TestCase(true)] public void WinnerKeepsAllValidLootAndPocketDiscardsLoanerAndCommitsHornOnce(bool elimination)
        {
            Start(); var cargo = Give(1, "raid_fragment", 3); var sword = Give(1, "raid_purple_sword"); var heal = Give(1, "raid_minor_heal", 2); var pocket = Give(1, "raid_rare_relic");
            Assert.That(players[1].Inventory.TryMoveToSecurePocket(pocket.LootInstanceId), Is.EqualTo(QusapLootResult.Success));
            var loan = context.Session.World.Instances.Single(i => i.RaidLoaner && i.HolderId == "P2"); long before = profiles[1].Revision;
            if (elimination) Kill(0); else Extract(0);
            var stash = profiles[1].ReadStashSnapshot(profiles[1].ProfileId);
            Assert.That(stash.Select(i => i.LootInstanceId), Is.EquivalentTo(new[] { cargo, sword, heal, pocket }.Select(i => i.LootInstanceId)));
            Assert.That(stash.Single(i => i.LootInstanceId == heal.LootInstanceId).Quantity, Is.EqualTo(2));
            Assert.That(loan.Location, Is.EqualTo(QusapLootLocation.Destroyed)); Assert.That(loan.Weapon.IsRetired, Is.True);
            Assert.That(profiles[1].Revision, Is.EqualTo(before + 1)); Assert.That(profiles[1].HasTriumphHorn && profiles[1].IsNextModeUnlocked, Is.True);
            Assert.That(profiles[1].TriumphHornGrantCount, Is.EqualTo(1)); Assert.That(profiles[1].TriumphHornGrantsPerformed, Is.EqualTo(1));
            Assert.That(players[1].Inventory.Status, Is.EqualTo(QusapRaidInventoryStatus.Winner)); Assert.That(players[1].Inventory.CanOperate, Is.False);
            Assert.That(players.All(p => p.Status == QusapDeploymentStatus.Resolved), Is.True); Assert.That(context.Status, Is.EqualTo(QusapDeploymentStatus.Resolved));
            Assert.That(profiles.All(p => !p.HasActiveDeployment), Is.True);
            context.Session.FinishSession(); context.FinishSession(); context.Session.Competition.EvaluateVictory();
            Assert.That(profiles[1].Revision, Is.EqualTo(before + 1)); Assert.That(context.Session.Competition.SessionFinishedEvents, Is.EqualTo(1));
            Assert.That(context.Session.SettleExtraction(players[1].Inventory, players[1].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(context.Session.SettleDeath(players[1].Inventory, players[1].WeaponAdapter), Is.EqualTo(QusapLootResult.Blocked));
        }
        [Test] public void SecondWinAndSubsequentDeathKeepExactlyOnePermanentHornAcrossReload()
        {
            Start(); Extract(0); string firstRaid = context.RaidId; long revision = profiles[1].Revision; context.Dispose();
            var loaded = new QusapPersistentStashRepository(profiles[1].Storage.DirectoryPath, catalog.Definitions, files: files[1]);
            Assert.That(loaded.HasTriumphHorn, Is.True); Assert.That(loaded.Revision, Is.EqualTo(revision));
            Prepare(); Start(); Assert.That(context.RaidId, Is.Not.EqualTo(firstRaid)); Extract(0);
            Assert.That(profiles[1].TriumphHornGrantsPerformed, Is.EqualTo(1)); Assert.That(profiles[1].TriumphHornGrantCount, Is.EqualTo(1)); context.Dispose();
            Prepare(); Start(); Give(1, "raid_fragment"); Kill(1);
            Assert.That(profiles[1].HasTriumphHorn, Is.True); Assert.That(profiles[1].TriumphHornGrantCount, Is.EqualTo(1));
        }
        [Test] public void HornIsPrimitiveProgressionAndNeverLootOrInstanceId()
        {
            Start(); Extract(0); Assert.That(typeof(QusapProfileContent).GetField("TriumphHornUnlocked").FieldType, Is.EqualTo(typeof(bool)));
            Assert.That(catalog.Definitions.Any(d => d.DefinitionId.Contains("triumph")), Is.False);
            Assert.That(context.Session.World.Instances.Any(i => i.DefinitionId.Contains("triumph")), Is.False);
            Assert.That(context.Session.Containers.SelectMany(c => c.InitialTransferredSnapshot).Any(i => i.DefinitionId.Contains("triumph")), Is.False);
            Assert.That(profiles[1].ReadStashSnapshot(profiles[1].ProfileId), Is.Empty);
            string bytes = files[1].Read(profiles[1].Storage.MainPath); Assert.That(bytes, Does.Contain("\"TriumphHornUnlocked\":true"));
            Assert.That(bytes, Does.Not.Contain("\"InstanceId\""));
            var reload = new QusapPersistentStashRepository(profiles[1].Storage.DirectoryPath, catalog.Definitions, files: files[1]);
            long revision = reload.Revision; Assert.That(reload.TryCommitVictory(firstKey(), reload.ProfileId, Array.Empty<QusapLootInstance>()), Is.True);
            Assert.That(reload.Revision, Is.EqualTo(revision));
            string firstKey() => context.RaidId + "/winner/P2";
        }
        [Test] public void RejectedWinnerWritePublishesNoPartialLootOrHornAndFinishSafelyRetries()
        {
            Start(); var cargo = Give(1, "raid_fragment", 4); var pocket = Give(1, "raid_rare_relic"); players[1].Inventory.TryMoveToSecurePocket(pocket.LootInstanceId);
            var weapon = players[1].WeaponAdapter.CurrentWeapon; long before = profiles[1].Revision; files[1].Fail = true;
            files[1].DuringWrite = () => Assert.That(context.Session.Competition.EvaluateVictory(), Is.False);
            Extract(0); var a = context.Session.Competition;
            Assert.That(a.Status, Is.EqualTo(QusapRaidSessionStatus.Running)); Assert.That(a.WinnerId, Is.Null); Assert.That(a.ActiveCount, Is.EqualTo(1));
            Assert.That(context.Session.IsFinished, Is.False); Assert.That(profiles[1].Revision, Is.EqualTo(before)); Assert.That(profiles[1].HasTriumphHorn, Is.False);
            Assert.That(profiles[1].ReadStashSnapshot(profiles[1].ProfileId), Is.Empty); Assert.That(profiles[1].Storage.MainExists, Is.False);
            Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(pocket.Location, Is.EqualTo(QusapLootLocation.SecurePocket));
            Assert.That(players[1].WeaponAdapter.CurrentWeapon, Is.SameAs(weapon)); Assert.That(players[1].Status, Is.EqualTo(QusapDeploymentStatus.Running));
            files[1].Fail = false; files[1].DuringWrite = null; context.Session.FinishSession(); context.Session.FinishSession();
            Assert.That(a.WinnerId, Is.EqualTo("P2")); Assert.That(context.Session.IsFinished, Is.True); Assert.That(profiles[1].Revision, Is.EqualTo(before + 1));
            Assert.That(profiles[1].ReadStashSnapshot(profiles[1].ProfileId).Count, Is.EqualTo(2)); Assert.That(profiles[1].TriumphHornGrantsPerformed, Is.EqualTo(1));
        }
        [TestCase(1)] [TestCase(2)] public void OneMigrationPreservesAllPreviousDataAndCanonicalWireBytes(int schema)
        {
            Start(); var item = Give(0, "raid_minor_heal", 2); Extract(0);
            var codec = new QusapProfileCodec(catalog.Definitions);
            Assert.That(codec.TryDecode(files[0].Read(profiles[0].Storage.MainPath), out var saved, out _, out _), Is.True);
            saved.Content.SchemaVersion = schema; string oldBytes = QusapProfileCodec.Encode(QusapProfileCodec.Seal(saved.Content));
            var oldFiles = new QusapMemoryProfileFiles(); string path = Path.Combine(Path.GetTempPath(), "QusapOldVictory", Guid.NewGuid().ToString("N"));
            oldFiles.WriteClosedAndFlushed(Path.Combine(path, QusapProfileFileStore.FileName), oldBytes);
            var migrated = new QusapPersistentStashRepository(path, catalog.Definitions, files: oldFiles);
            Assert.That(migrated.SchemaVersion, Is.EqualTo(3)); Assert.That(migrated.HasTriumphHorn, Is.False); Assert.That(migrated.Storage.Writes, Is.Zero);
            Assert.That(oldFiles.Read(migrated.Storage.MainPath), Is.EqualTo(oldBytes)); Assert.That(migrated.Revision, Is.EqualTo(saved.Content.Revision));
            Assert.That(migrated.NextItemInstanceId, Is.EqualTo(saved.Content.NextItemInstanceId)); Assert.That(migrated.NextNativeWeaponInstanceId, Is.EqualTo(saved.Content.NextNativeWeaponInstanceId));
            Assert.That(migrated.ReadStashSnapshot(migrated.ProfileId).Single().LootInstanceId, Is.EqualTo(item.LootInstanceId));
            Assert.That(migrated.ReadStashSnapshot(migrated.ProfileId).Single().Quantity, Is.EqualTo(2));
            Assert.That(codec.TryDecode(oldBytes, out var old, out _, out _), Is.True); Assert.That(QusapProfileCodec.Encode(old), Is.EqualTo(oldBytes));
            old.Content.SchemaVersion = 3; string current = QusapProfileCodec.Encode(QusapProfileCodec.Seal(old.Content));
            Assert.That(codec.TryDecode(current, out var roundtrip, out _, out _), Is.True); Assert.That(QusapProfileCodec.Encode(roundtrip), Is.EqualTo(current));
            Assert.That(roundtrip.Content.Stash.Select(i => JsonUtility.ToJson(i)), Is.EqualTo(saved.Content.Stash.Select(i => JsonUtility.ToJson(i))));
            Assert.That(roundtrip.Content.Settlements.Select(r => r.Fingerprint), Is.EqualTo(saved.Content.Settlements.Select(r => r.Fingerprint)));
        }
        [Test] public void FailedStagingPreservesTemporaryEvidenceAndRequiresControlledRecoveryBeforeRetry()
        {
            Start(); var cargo = Give(1, "raid_fragment"); files[1].FailAfterStaging = true; Extract(0);
            Assert.That(profiles[1].Storage.TemporaryExists, Is.True); string temporary = files[1].Read(profiles[1].Storage.TemporaryPath);
            Assert.That(profiles[1].HasTriumphHorn, Is.False); Assert.That(profiles[1].Revision, Is.Zero); Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.Backpack));
            Assert.That(context.Session.Competition.WinnerId, Is.Null); Assert.That(context.Session.IsFinished, Is.False);
            files[1].FailAfterStaging = false; context.Session.FinishSession(); Assert.That(context.Session.IsFinished, Is.False);
            Assert.That(files[1].Read(profiles[1].Storage.TemporaryPath), Is.EqualTo(temporary));
            files[1].PreserveAndRemoveRejectedTemporary(profiles[1].Storage.TemporaryPath); context.Session.FinishSession();
            Assert.That(context.Session.Competition.WinnerId, Is.EqualTo("P2")); Assert.That(profiles[1].Revision, Is.EqualTo(1));
            Assert.That(profiles[1].TriumphHornGrantsPerformed, Is.EqualTo(1)); Assert.That(profiles[1].ReadStashSnapshot(profiles[1].ProfileId).Single().LootInstanceId, Is.EqualTo(cargo.LootInstanceId));
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void RealSharedDeploymentSupportsTwoThroughEightAndSevenExtractionsElectLastProfile(int count)
        {
            string[] ids = Enumerable.Range(1, count).Select(i => "roster/P" + i).ToArray();
            using var shared = new QusapSharedRaidContext(ids);
            var actors = new GameObject[count]; var deployed = new QusapRaidDeploymentAuthority[count]; var stores = new QusapPersistentStashRepository[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    actors[i] = new GameObject(ids[i]); actors[i].SetActive(false); var combat = actors[i].AddComponent<QusapCombatController>();
                    var equipment = actors[i].AddComponent<QusapWeaponEquipment>(); Assert.That(equipment.TryInitialize(combat), Is.True);
                    stores[i] = new QusapPersistentStashRepository(Path.Combine(Path.GetTempPath(), "QusapEightVictory", Guid.NewGuid().ToString("N")), catalog.Definitions, ids[i], new QusapMemoryProfileFiles());
                    deployed[i] = new QusapRaidDeploymentAuthority(stores[i], catalog, ids[i], equipment, d => new QusapWeaponInstance(++nativeId, d.WeaponDefinition));
                    Assert.That(deployed[i].PrepareLoadout(), Is.True); Assert.That(shared.Add(deployed[i]), Is.True);
                }
                Assert.That(shared.CommitDeployment(), Is.True); Assert.That(shared.Session.Competition, Is.Null);
                Assert.That(shared.StartRunning(), Is.True); Assert.That(shared.Session.Competition.ParticipantCount, Is.EqualTo(count));
                for (int i = 0; i < count - 1; i++) Assert.That(shared.Session.SettleExtraction(deployed[i].Inventory, deployed[i].WeaponAdapter), Is.EqualTo(QusapLootResult.Success));
                Assert.That(shared.Session.Competition.WinnerId, Is.EqualTo(ids[count - 1])); Assert.That(stores.Count(p => p.HasTriumphHorn), Is.EqualTo(1));
                Assert.That(shared.Session.Competition.SessionFinishedEvents, Is.EqualTo(1)); Assert.That(shared.Session.World.LoanersDestroyed, Is.EqualTo(count));
            }
            finally { shared.Dispose(); foreach (var actor in actors) if (actor != null) UnityEngine.Object.DestroyImmediate(actor); }
        }
        [Test] public void SchemaTwoManifestRecoveryDataAndCountersSurviveCanonicalSchemaThreeMigration()
        {
            Start(); var item = Give(0, "raid_minor_heal", 2); Extract(0); var codec = new QusapProfileCodec(catalog.Definitions);
            Assert.That(codec.TryDecode(files[0].Read(profiles[0].Storage.MainPath), out var doc, out _, out _), Is.True);
            var c = doc.Content; c.SchemaVersion = 2; c.RecoveryReason = "existing reason";
            c.ActiveDeploymentManifest = new QusapActiveDeploymentManifest { DeploymentId = "old-deployment", RaidId = "old-raid", State = QusapDeploymentStatus.Running,
                CreatedAtUtc = c.SavedAtUtc, Items = c.Stash, WeaponInstanceId = string.Empty };
            c.Stash = System.Array.Empty<QusapProfileItem>(); c.Settlements = System.Array.Empty<QusapProfileReceipt>();
            string oldBytes = QusapProfileCodec.Encode(QusapProfileCodec.Seal(c));
            Assert.That(codec.TryDecode(oldBytes, out var loaded, out _, out _), Is.True); Assert.That(QusapProfileCodec.Encode(loaded), Is.EqualTo(oldBytes));
            string manifest = JsonUtility.ToJson(loaded.Content.ActiveDeploymentManifest); loaded.Content.SchemaVersion = 3;
            string migrated = QusapProfileCodec.Encode(QusapProfileCodec.Seal(loaded.Content));
            Assert.That(codec.TryDecode(migrated, out var current, out _, out _), Is.True);
            Assert.That(JsonUtility.ToJson(current.Content.ActiveDeploymentManifest), Is.EqualTo(manifest)); Assert.That(current.Content.RecoveryReason, Is.EqualTo("existing reason"));
            Assert.That(current.Content.TriumphHornUnlocked, Is.False); Assert.That(current.Content.NextItemInstanceId, Is.EqualTo(c.NextItemInstanceId));
            Assert.That(current.Content.ActiveDeploymentManifest.Items.Single().InstanceId, Is.EqualTo(item.LootInstanceId));
        }
    }
}
