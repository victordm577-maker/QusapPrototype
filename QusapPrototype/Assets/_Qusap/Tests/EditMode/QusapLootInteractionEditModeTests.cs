using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Qusap.Tests
{
    public sealed class QusapLootInteractionEditModeTests
    {
        private readonly List<UnityEngine.Object> temporary = new();
        private QusapWorldLootContainerView View(string id)
        { var go = new GameObject(id); temporary.Add(go); var view = go.AddComponent<QusapWorldLootContainerView>(); view.Configure(id, null); return view; }
        [TearDown] public void Cleanup() { foreach (var obj in temporary) UnityEngine.Object.DestroyImmediate(obj); temporary.Clear(); }
        [Test] public void NearestTargetWinsRegardlessOfCandidateOrder()
        {
            var far = View("a"); var near = View("z");
            Assert.That(QusapLootTargetSelector.Select(new[] { new QusapLootCandidate(far, 2, true), new QusapLootCandidate(near, 1, true) }, null, 2.75f, .2f), Is.SameAs(near));
        }
        [Test] public void EqualDistancesUseOrdinalSpawnPointId()
        {
            var a = View("A"); var b = View("a");
            Assert.That(QusapLootTargetSelector.Select(new[] { new QusapLootCandidate(b, 1, true), new QusapLootCandidate(a, 1, true) }, null, 2.75f, .2f), Is.SameAs(a));
        }
        [Test] public void HysteresisRetainsOnlyExistingTargetAndDoesNotAcquireOutsideRange()
        {
            var a = View("a"); var candidates = new[] { new QusapLootCandidate(a, 2.85f, true) };
            Assert.That(QusapLootTargetSelector.Select(candidates, null, 2.75f, .2f), Is.Null);
            Assert.That(QusapLootTargetSelector.Select(candidates, a, 2.75f, .2f), Is.SameAs(a));
            Assert.That(QusapLootTargetSelector.Select(new[] { new QusapLootCandidate(a, 2.96f, true) }, a, 2.75f, .2f), Is.Null);
            Assert.That(QusapLootTargetSelector.Select(new[] { new QusapLootCandidate(a, 2, false) }, a, 2.75f, .2f), Is.Null);
        }
        [Test] public void CompletedHoldFiresOnceAndRequiresReleaseForNextAttempt()
        {
            var clock = new QusapLootInteractionProgress();
            Assert.That(clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, .44f), Is.False);
            Assert.That(clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, .01f), Is.True);
            Assert.That(clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, 1), Is.False);
            clock.Advance(false, "cache", true, QusapLootInteractionRejection.None, 0);
            Assert.That(clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, .45f), Is.True);
        }
        [TestCase(QusapLootInteractionRejection.OutOfRange)]
        [TestCase(QusapLootInteractionRejection.NoLineOfSight)]
        [TestCase(QusapLootInteractionRejection.Incapacitated)]
        [TestCase(QusapLootInteractionRejection.SessionNotRunning)]
        [TestCase(QusapLootInteractionRejection.InvalidTarget)]
        public void CancellationClearsProgressAndRequiresAReleasedButton(QusapLootInteractionRejection reason)
        {
            var clock = new QusapLootInteractionProgress(); clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, .2f);
            Assert.That(clock.Advance(true, "cache", true, reason, .1f), Is.False); Assert.That(clock.IsHolding, Is.False); Assert.That(clock.Elapsed, Is.Zero);
            Assert.That(clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, 1), Is.False);
        }
        [Test] public void ReleaseAndTargetChangeCancelWithoutRestartingSamePress()
        {
            var clock = new QusapLootInteractionProgress(); clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, .2f);
            clock.Advance(false, "cache", true, QusapLootInteractionRejection.None, .2f); Assert.That(clock.LastCancellation, Is.EqualTo(QusapLootInteractionRejection.Released));
            clock.Advance(true, "cache", true, QusapLootInteractionRejection.None, .2f);
            Assert.That(clock.Advance(true, "other", true, QusapLootInteractionRejection.None, .45f), Is.False);
            Assert.That(clock.LastCancellation, Is.EqualTo(QusapLootInteractionRejection.TargetChanged));
        }
        [Test] public void ConfigurableDurationAndDepletedContainersCannotStartOpening()
        {
            var clock = new QusapLootInteractionProgress(.8f);
            Assert.That(clock.Advance(true, "empty", false, QusapLootInteractionRejection.None, 1), Is.False); Assert.That(clock.IsHolding, Is.False);
            Assert.That(clock.Advance(true, "closed", true, QusapLootInteractionRejection.None, .45f), Is.False);
            Assert.That(clock.Advance(true, "closed", true, QusapLootInteractionRejection.None, .35f), Is.True);
        }
        [Test] public void DiagnosticSceneIsExcludedAndEverySerializedGuidResolves()
        {
            const string path = "Assets/_Qusap/Scenes/RaidWorldLootInteractionPlayground.unity";
            Assert.That(EditorBuildSettings.scenes.Any(s => s.path == path), Is.False);
            Assert.That(File.Exists(path), Is.True);
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"guid: ([0-9a-f]{32})"))
                if (!match.Groups[1].Value.StartsWith("0000000000000000")) Assert.That(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value), Is.Not.Empty, match.Value);
            StringAssert.DoesNotContain("QusapLocalProfilePersistence", File.ReadAllText(path));
        }
        [Test] public void InteractorAndHudHaveNoStashProfileOrPocketMutationPath()
        {
            foreach (string path in new[] { "Assets/_Qusap/Scripts/Raid/QusapLootInteractor.cs", "Assets/_Qusap/Scripts/UI/QusapRaidLootInteractionHud.cs" })
            {
                string source = File.ReadAllText(path);
                foreach (string forbidden in new[] { "TryMoveToSecurePocket", "ReadStash", "PersistentStash", "LocalProfilePersistence", "File.Write", "TryPickup" }) StringAssert.DoesNotContain(forbidden, source);
            }
        }
        [Test] public void InputAuditUsesFreeFBridgeAndPreservesNativeE()
        {
            string controls = File.ReadAllText("Assets/_Qusap/Settings/QusapControls.inputactions");
            StringAssert.DoesNotContain("\"name\": \"Interact\"", controls);
            StringAssert.Contains("\"path\": \"<Keyboard>/e\"", controls);
            StringAssert.Contains("\"action\": \"WeaponSwapThrow\"", controls);
            string bridge = File.ReadAllText("Assets/_Qusap/Scripts/Raid/QusapLootInteractionInputBridge.cs");
            StringAssert.Contains("fKey.isPressed", bridge); StringAssert.DoesNotContain("eKey.isPressed", bridge); StringAssert.Contains("dpad.up.isPressed", bridge);
            StringAssert.DoesNotContain("InputActionAsset", bridge);
        }
        [Test] public void RealConcurrentSameVersionHasOneWinnerAndNoLoserBackpackMutation()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<QusapRaidLootCatalog>("Assets/_Qusap/Settings/RaidLootCatalog/RaidLootCatalog.asset");
            var weighted = ScriptableObject.CreateInstance<QusapRaidLootTable>(); temporary.Add(weighted); weighted.Configure("fixture", new QusapLootTableEntry("raid_fragment", 100, 3, 3));
            var table = ScriptableObject.CreateInstance<QusapWorldLootTable>(); temporary.Add(table); table.Configure(weighted, 2, 2);
            using var session = new QusapRaidSession("interaction-concurrency");
            var first = session.Register("any/player", "memory/one"); var second = session.Register("any/rival", "memory/two");
            var authority = new QusapLocalWorldLootAuthority(session, catalog, 42); authority.RegisterSpawn("cache", table);
            Parallel.Invoke(() => authority.RequestOpen(new(first.ParticipantId, "cache")), () => authority.RequestOpen(new(second.ParticipantId, "cache")));
            var snapshot = authority.ReadContainer("cache"); Assert.That(snapshot.GenerationCount, Is.EqualTo(1));
            string instance = snapshot.Items[0].LootInstanceId;
            var a = new QusapWorldLootTakeRequest(first.ParticipantId, "cache", instance, 1, snapshot.Version);
            var b = new QusapWorldLootTakeRequest(second.ParticipantId, "cache", instance, 1, snapshot.Version);
            QusapWorldLootReply r1 = null, r2 = null;
            Parallel.Invoke(() => r1 = authority.RequestTake(a), () => r2 = authority.RequestTake(b));
            Assert.That(new[] { r1.Result, r2.Result }, Is.EquivalentTo(new[] { QusapWorldLootResult.Success, QusapWorldLootResult.StaleRequest }));
            var winner = r1.Confirmed ? first : second; var loser = r1.Confirmed ? second : first;
            Assert.That(winner.ReadInventorySnapshot().Backpack.Where(i => i.HasValue).Sum(i => i.Value.Quantity), Is.EqualTo(1));
            Assert.That(loser.ReadInventorySnapshot().Backpack.All(i => !i.HasValue), Is.True); Assert.That(authority.DuplicateInstances, Is.Zero);
            Assert.That(authority.ReadContainer("cache").Items[0].Quantity, Is.EqualTo(2));
            // Container-wide versions: independent items succeed when each uses the current version.
            var current = authority.ReadContainer("cache");
            Assert.That(authority.RequestTake(new(second.ParticipantId, "cache", current.Items[1].LootInstanceId, 3, current.Version)).Confirmed, Is.True);
        }
    }
}
