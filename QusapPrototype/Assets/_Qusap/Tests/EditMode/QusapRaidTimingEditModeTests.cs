using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapRaidTimingEditModeTests
    {
        private sealed class FaultFiles : QusapMemoryProfileFiles
        {
            public bool Fail, AfterStage; public Action DuringWrite;
            public override void WriteClosedAndFlushed(string path, string content)
            {
                DuringWrite?.Invoke();
                if (Fail && !AfterStage) throw new IOException("Timing rejection before staging");
                base.WriteClosedAndFlushed(path, content);
                if (Fail) throw new IOException("Timing rejection after staging");
            }
            public void RecoverMemoryTemporary(string path)
            {
                var contents = (System.Collections.Generic.Dictionary<string, string>)typeof(QusapMemoryProfileFiles)
                    .GetField("contents", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(this);
                Assert.That(contents.ContainsKey(path), Is.True); contents.Remove(path);
            }
        }
        private QusapRaidLootCatalog catalog;
        private QusapSharedRaidContext context;
        private QusapRaidDeploymentAuthority[] players;
        private QusapPersistentStashRepository[] profiles;
        private FaultFiles[] files;
        private GameObject[] roots;
        private ulong native;
        [SetUp] public void Setup()
        {
            catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            Create(2, new QusapRaidTimingSettings(10, 3));
        }
        private void Create(int count, QusapRaidTimingSettings settings)
        {
            Cleanup();
            files = Enumerable.Range(0, count).Select(_ => new FaultFiles()).ToArray();
            profiles = Enumerable.Range(0, count).Select(i => new QusapPersistentStashRepository(
                Path.Combine(Path.GetTempPath(), "QusapTimingMemoryTests", Guid.NewGuid().ToString("N")),
                catalog.Definitions, "timing-profile-" + i, files[i])).ToArray();
            roots = Enumerable.Range(0, count).Select(i => new GameObject("TimingPlayer" + i)).ToArray();
            foreach (var root in roots)
            { root.SetActive(false); var combat = root.AddComponent<QusapCombatController>(); Assert.That(root.AddComponent<QusapWeaponEquipment>().TryInitialize(combat), Is.True); }
            context = new QusapSharedRaidContext(settings, Enumerable.Range(1, count).Select(i => "P" + i).ToArray());
            players = Enumerable.Range(0, count).Select(i => new QusapRaidDeploymentAuthority(profiles[i], catalog, "P" + (i + 1),
                roots[i].GetComponent<QusapWeaponEquipment>(), d => new QusapWeaponInstance(++native, d.WeaponDefinition))).ToArray();
            foreach (var player in players) { Assert.That(player.PrepareLoadout(), Is.True); Assert.That(context.Add(player), Is.True); }
        }
        private void Start() { Assert.That(context.CommitDeployment(), Is.True); Assert.That(context.StartRunning(), Is.True); }
        private QusapLootInstance Give(int index, string id = "raid_fragment")
        {
            Assert.That(catalog.TryResolve(id, out var definition), Is.True);
            var item = context.Session.World.Create(definition, "Timing test cargo");
            Assert.That(players[index].Inventory.TryPickup(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item;
        }
        private QusapLootInstance Pocket(int index)
        { var item = Give(index, "raid_rare_relic"); Assert.That(players[index].Inventory.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Success)); return item; }
        private QusapLootResult Extract(int index) => context.Session.SettleExtraction(players[index].Inventory, players[index].WeaponAdapter);
        private void Kill(int index)
        { players[index].Inventory.BlockOperations(); Assert.That(context.Session.SettleDeath(players[index].Inventory, players[index].WeaponAdapter), Is.EqualTo(QusapLootResult.Success)); }
        [TearDown] public void Cleanup()
        { context?.Dispose(); context = null; if (roots != null) foreach (var root in roots) UnityEngine.Object.DestroyImmediate(root); roots = null; }
        [Test] public void NormalSettingsAre480And180AndClockStartsOnlyAfterSharedRosterRunning()
        {
            Create(2, new QusapRaidTimingSettings());
            Assert.That(context.Session, Is.Null); Assert.That(context.CommitDeployment(), Is.True);
            var clock = context.Session.Timing;
            Assert.That(clock.Settings.DurationSeconds, Is.EqualTo(480)); Assert.That(clock.Settings.ExtractionOpeningSeconds, Is.EqualTo(180));
            Assert.That(clock.HasStarted || clock.IsRunning, Is.False);
            Assert.That(context.Session.AdvanceTime(100), Is.False); Assert.That(clock.ElapsedSeconds, Is.Zero);
            Assert.That(Extract(0), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(context.StartRunning(), Is.True);
            Assert.That(context.Session.Competition.RosterClosed && context.Session.Competition.SharedDeploymentCompleted, Is.True);
            Assert.That(clock.HasStarted && clock.IsRunning, Is.True); Assert.That(clock.ElapsedSeconds, Is.Zero);
        }
        [TestCase(0,0)] [TestCase(-1,0)] [TestCase(10,-1)] [TestCase(10,10)] [TestCase(10,11)]
        [TestCase(double.NaN,0)] [TestCase(double.PositiveInfinity,0)] [TestCase(10,double.NaN)] [TestCase(10,double.PositiveInfinity)]
        public void InvalidTimingConfigurationIsRejected(double duration, double opening)
            => Assert.Throws<ArgumentOutOfRangeException>(() => new QusapRaidTimingSettings(duration, opening));
        [TestCase(double.NaN)] [TestCase(double.PositiveInfinity)] [TestCase(-1)]
        public void InvalidAdvanceCannotChangeTheClock(double seconds)
        { Start(); Assert.That(context.Session.AdvanceTime(seconds), Is.False); Assert.That(context.Session.Timing.ElapsedSeconds, Is.Zero); }
        [Test] public void ExtractionApiIsBlockedBeforeExactOpeningAndAccruesNoPriorTime()
        {
            Start(); var cargo = Give(0); long revision = profiles[0].Revision;
            Assert.That(context.Session.AdvanceTime(2.999), Is.True); Assert.That(Extract(0), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.Backpack)); Assert.That(profiles[0].Revision, Is.EqualTo(revision));
            Assert.That(context.Session.Timing.ExtractionStepSeconds, Is.Zero);
            Assert.That(context.Session.AdvanceTime(.0010000001), Is.True); Assert.That(context.Session.CanExtract, Is.True);
            Assert.That(context.Session.Timing.ExtractionStepSeconds, Is.LessThan(.00000001));
            Assert.That(Extract(0), Is.EqualTo(QusapLootResult.Success)); Assert.That(profiles[0].HasTriumphHorn, Is.False);
            Assert.That(context.Session.Competition.WinnerId, Is.EqualTo("P2")); Assert.That(profiles[1].HasTriumphHorn, Is.True);
        }
        [TestCase(0)] [TestCase(4)]
        public void LastActiveWinsBeforeAndAfterOpeningAndStopsClock(double elapsed)
        {
            Start(); context.Session.AdvanceTime(elapsed); Kill(0);
            Assert.That(context.Session.Competition.WinnerId, Is.EqualTo("P2")); Assert.That(context.Session.Timing.IsRunning, Is.False);
            Assert.That(context.Session.Timing.ElapsedSeconds, Is.EqualTo(elapsed)); Assert.That(context.Session.AdvanceTime(100), Is.False);
            Assert.That(context.Session.Competition.FinalResult.Reason, Is.EqualTo(QusapRaidSessionFinishReason.LastSurvivor));
        }
        [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void CollectiveExpirationPreservesOnlyPocketsNeverWinnerForTwoThroughEight(int count)
        {
            Create(count, new QusapRaidTimingSettings(10, 3)); Start();
            var pockets = Enumerable.Range(0,count).Select(Pocket).ToArray();
            var cargo = Enumerable.Range(0,count).Select(i => Give(i)).ToArray();
            var revisions = profiles.Select(p => p.Revision).ToArray();
            int batches = 0;
            context.Session.ExpirationStarted += batch =>
            {
                batches++; Assert.That(batch.Count, Is.EqualTo(count));
                Assert.That(players.All(p => !p.Inventory.CanOperate), Is.True);
                Assert.That(context.Session.Competition.ExpirationStarted, Is.True);
                Assert.That(Extract(0), Is.EqualTo(QusapLootResult.Blocked));
            };
            context.Session.AdvanceTime(10);
            Assert.That(batches, Is.EqualTo(1)); Assert.That(context.Session.IsFinished, Is.True);
            Assert.That(context.Status, Is.EqualTo(QusapDeploymentStatus.Resolved));
            Assert.That(context.Session.Competition.WinnerId, Is.Null);
            Assert.That(context.Session.Competition.FinalResult.Reason, Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(context.Session.Competition.EliminatedCount, Is.EqualTo(count));
            Assert.That(context.Session.World.LoanersDestroyed, Is.EqualTo(count));
            Assert.That(cargo.All(i => i.Location == QusapLootLocation.Destroyed), Is.True);
            for(int i=0;i<count;i++)
            {
                Assert.That(profiles[i].HasTriumphHorn, Is.False);
                Assert.That(profiles[i].ReadStashSnapshot(profiles[i].ProfileId).Single().LootInstanceId, Is.EqualTo(pockets[i].LootInstanceId));
                Assert.That(profiles[i].Revision, Is.EqualTo(revisions[i]+1)); Assert.That(profiles[i].HasActiveDeployment, Is.False);
            }
        }
        [Test] public void TerminalExtractionAndEliminationArePreservedWhenRemainingActivesExpire()
        {
            Create(4, new QusapRaidTimingSettings(10, 3)); Start(); var first = Give(0); Pocket(1); Pocket(2); Pocket(3);
            context.Session.AdvanceTime(3); Assert.That(Extract(0), Is.EqualTo(QusapLootResult.Success)); Kill(1);
            var revisions = profiles.Select(p => p.Revision).ToArray(); context.Session.AdvanceTime(7);
            Assert.That(context.Session.Competition.FinalResult.ExtractedCount, Is.EqualTo(1));
            Assert.That(context.Session.Competition.FinalResult.EliminatedCount, Is.EqualTo(3));
            Assert.That(profiles[0].ReadStashSnapshot(profiles[0].ProfileId).Single().LootInstanceId, Is.EqualTo(first.LootInstanceId));
            Assert.That(profiles[0].Revision, Is.EqualTo(revisions[0])); Assert.That(profiles[1].Revision, Is.EqualTo(revisions[1]));
            Assert.That(profiles.All(p => !p.HasTriumphHorn), Is.True);
        }
        [Test] public void ExactDeadlineTakesPriorityOverNewExtractionAndPocketMovement()
        {
            Start(); var item = Give(0); context.Session.AdvanceTime(9.999);
            Assert.That(context.Session.CanExtract, Is.True); context.Session.AdvanceTime(.001000001);
            Assert.That(context.Session.CanExtract, Is.False); Assert.That(Extract(0), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(players[0].Inventory.TryMoveToSecurePocket(item.LootInstanceId), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(context.Session.Competition.WinnerId, Is.Null);
        }
        [Test] public void RepeatExpirationAndFinishAreIdempotentAndTerminalStateCannotRevive()
        {
            Start(); Pocket(0); Pocket(1); context.Session.AdvanceTime(1000);
            var revisions = profiles.Select(p => p.Revision).ToArray();
            context.Session.AdvanceTime(1000); context.Session.FinishSession(); context.FinishSession();
            Assert.That(context.Session.Timing.ElapsedSeconds, Is.EqualTo(10)); Assert.That(context.Session.Timing.IsRunning, Is.False);
            Assert.That(profiles.Select(p=>p.Revision), Is.EqualTo(revisions)); Assert.That(context.Session.Competition.SessionFinishedEvents, Is.EqualTo(1));
            Assert.That(context.Session.Competition.TryResolve("P1",QusapRaidParticipantOutcome.Active), Is.False);
        }
        [TestCase(false)] [TestCase(true)]
        public void RejectedProfileStaysFencedWithNoPartialPocketOrHornAndRetriesSafely(bool afterStaging)
        {
            Start(); var one = Pocket(0); var two = Pocket(1); var cargo = Give(1);
            long firstRevision=profiles[0].Revision, secondRevision=profiles[1].Revision;
            bool mainExisted=files[1].Exists(profiles[1].Storage.MainPath);
            string old=mainExisted?files[1].Read(profiles[1].Storage.MainPath):null;
            files[1].Fail=true; files[1].AfterStage=afterStaging;
            context.Session.AdvanceTime(10);
            Assert.That(context.Session.IsFinished, Is.False); Assert.That(context.Session.Competition.ActiveCount, Is.EqualTo(1));
            Assert.That(context.Session.Competition.WinnerId, Is.Null); Assert.That(context.Session.Competition.EvaluateVictory(), Is.False);
            Assert.That(profiles[0].Revision, Is.EqualTo(firstRevision+1)); Assert.That(profiles[1].Revision, Is.EqualTo(secondRevision));
            Assert.That(files[1].Exists(profiles[1].Storage.MainPath), Is.EqualTo(mainExisted));
            if(mainExisted) Assert.That(files[1].Read(profiles[1].Storage.MainPath), Is.EqualTo(old));
            Assert.That(two.Location, Is.EqualTo(QusapLootLocation.SecurePocket)); Assert.That(cargo.Location, Is.EqualTo(QusapLootLocation.Backpack));
            Assert.That(players[1].Inventory.CanOperate, Is.False); Assert.That(Extract(1), Is.EqualTo(QusapLootResult.Blocked));
            Assert.That(profiles.All(p=>!p.HasTriumphHorn), Is.True);
            files[1].Fail=false;
            if(afterStaging)
            {
                string rejected=files[1].Read(profiles[1].Storage.TemporaryPath);
                context.Session.AdvanceTime(0); Assert.That(context.Session.IsFinished, Is.False);
                Assert.That(files[1].Read(profiles[1].Storage.TemporaryPath), Is.EqualTo(rejected));
                files[1].RecoverMemoryTemporary(profiles[1].Storage.TemporaryPath);
            }
            context.Session.AdvanceTime(0); Assert.That(context.Session.IsFinished, Is.True);
            Assert.That(profiles[0].Revision, Is.EqualTo(firstRevision+1)); Assert.That(profiles[1].Revision, Is.EqualTo(secondRevision+1));
            Assert.That(profiles[1].ReadStashSnapshot(profiles[1].ProfileId).Single().LootInstanceId, Is.EqualTo(two.LootInstanceId));
            Assert.That(context.Session.Competition.WinnerId, Is.Null); Assert.That(context.Session.Competition.SessionFinishedEvents, Is.EqualTo(1));
        }
        [Test] public void SaveCallbackCannotReenterClockOrEvadeAnExpirationBatch()
        {
            Start(); Pocket(0); Pocket(1); int callbacks=0;
            files[0].DuringWrite=()=> {callbacks++; Assert.That(context.Session.AdvanceTime(0), Is.False); Assert.That(Extract(1), Is.EqualTo(QusapLootResult.Blocked));};
            context.Session.AdvanceTime(10);
            Assert.That(callbacks, Is.EqualTo(1)); Assert.That(context.Session.Competition.WinnerId, Is.Null);
            Assert.That(context.Session.IsFinished, Is.True);
        }
        [Test] public void HornConfirmedInAnEarlierRaidSurvivesExpirationWithoutAnotherGrant()
        {
            Start(); Kill(0); Assert.That(profiles[1].HasTriumphHorn, Is.True); string oldId=context.RaidId;
            context.Dispose(); context=new QusapSharedRaidContext(new QusapRaidTimingSettings(10,3),"P1","P2");
            players=Enumerable.Range(0,2).Select(i=>new QusapRaidDeploymentAuthority(profiles[i],catalog,"P"+(i+1),
                roots[i].GetComponent<QusapWeaponEquipment>(),d=>new QusapWeaponInstance(++native,d.WeaponDefinition))).ToArray();
            foreach(var player in players){Assert.That(player.PrepareLoadout(),Is.True);Assert.That(context.Add(player),Is.True);}
            Start();Assert.That(context.RaidId,Is.Not.EqualTo(oldId));context.Session.AdvanceTime(10);
            Assert.That(context.Session.Competition.WinnerId,Is.Null);Assert.That(profiles[1].HasTriumphHorn,Is.True);
            Assert.That(profiles[1].TriumphHornGrantsPerformed,Is.EqualTo(1));Assert.That(profiles[1].TriumphHornGrantCount,Is.EqualTo(1));
        }
        [Test] public void UnconfirmedWinnerAtEarlierSaveFailureCannotWinAfterDeadline()
        {
            Start();Pocket(1);files[1].Fail=true;Kill(0);
            Assert.That(context.Session.Competition.WinnerId,Is.Null);Assert.That(profiles[1].HasTriumphHorn,Is.False);
            context.Session.AdvanceTime(10);files[1].Fail=false;context.Session.FinishSession();
            Assert.That(context.Session.IsFinished,Is.False);Assert.That(context.Session.Competition.WinnerId,Is.Null);
            context.Session.AdvanceTime(0);Assert.That(context.Session.IsFinished,Is.True);
            Assert.That(context.Session.Competition.FinalResult.Reason,Is.EqualTo(QusapRaidSessionFinishReason.Expired));
            Assert.That(profiles[1].HasTriumphHorn,Is.False);Assert.That(context.Session.Competition.EliminatedCount,Is.EqualTo(2));
        }
        [Test] public void EmptyWinnerSaveCannotReenterClockWhenThereAreNoReservedInventoryItems()
        {
            Start();Assert.That(roots[1].GetComponent<QusapWeaponEquipment>().TryDrop(out _,out _),Is.EqualTo(QusapWeaponOperationResult.Success));
            int callbacks=0;
            files[1].DuringWrite=()=>{callbacks++;Assert.That(context.Session.AdvanceTime(10),Is.False);};
            Kill(0);
            Assert.That(callbacks,Is.EqualTo(1));Assert.That(context.Session.Timing.ElapsedSeconds,Is.Zero);
            Assert.That(context.Session.Competition.WinnerId,Is.EqualTo("P2"));Assert.That(profiles[1].HasTriumphHorn,Is.True);
        }
    }
}
