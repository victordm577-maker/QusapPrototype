using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Qusap.Tests
{
    public sealed class QusapRaidSessionEditModeTests
    {
        [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)]
        [TestCase(5)] [TestCase(6)] [TestCase(7)] [TestCase(8)]
        public void RegistersConfiguredCapacityAndStartsExactlyOnce(int count)
        {
            var s = new QusapRaidSessionAuthority("stable-session", count);
            for (int i = 1; i <= count; i++) Assert.That(s.Register("P" + i), Is.True);
            Assert.That(s.Status, Is.EqualTo(QusapRaidSessionStatus.Waiting));
            Assert.That(s.Register("overflow"), Is.False); Assert.That(s.ParticipantCount, Is.EqualTo(count));
            Assert.That(s.StartSession(), Is.True); Assert.That(s.Status, Is.EqualTo(QusapRaidSessionStatus.Running));
            Assert.That(s.ActiveCount, Is.EqualTo(count)); Assert.That(s.StartSession(), Is.False);
            Assert.That(s.Register("later"), Is.False); Assert.That(s.SessionId, Is.EqualTo("stable-session"));
        }
        [TestCase(0)] [TestCase(9)] [TestCase(-1)]
        public void InvalidCapacityCannotConstructAuthority(int capacity)
        { Assert.Throws<ArgumentOutOfRangeException>(() => new QusapRaidSessionAuthority("session", capacity)); }
        [TestCase(null)] [TestCase("")] [TestCase(" ")]
        public void EmptyIdsAreRejected(string id)
        {
            Assert.Throws<ArgumentException>(() => new QusapRaidSessionAuthority(id));
            var s = new QusapRaidSessionAuthority("session"); Assert.That(s.Register(id), Is.False); Assert.That(s.ParticipantCount, Is.Zero);
        }
        [Test] public void DuplicateIdCannotReplaceParticipantAndNinthIsRejected()
        {
            var s = new QusapRaidSessionAuthority("session"); Assert.That(s.Register("P1"), Is.True);
            Assert.That(s.Register("P1"), Is.False); Assert.That(s.LastRejection, Is.EqualTo(QusapRaidSessionRejection.DuplicateParticipant));
            for (int i = 2; i <= 8; i++) s.Register("P" + i);
            Assert.That(s.Register("P9"), Is.False); Assert.That(s.ParticipantCount, Is.EqualTo(8));
        }
        [Test] public void EmptyStartAndWaitingResolutionCannotChangeSession()
        {
            var s = new QusapRaidSessionAuthority("session"); Assert.That(s.StartSession(), Is.False);
            s.Register("P1"); Assert.That(s.TryResolve("P1", QusapRaidParticipantOutcome.Extracted), Is.False);
            Assert.That(s.ActiveCount, Is.EqualTo(1)); Assert.That(s.Status, Is.EqualTo(QusapRaidSessionStatus.Waiting));
            Assert.That(s.FinalResult, Is.Null); Assert.That(s.SessionFinishedEvents, Is.Zero);
        }
        [TestCase(QusapRaidParticipantOutcome.Extracted)] [TestCase(QusapRaidParticipantOutcome.Eliminated)]
        public void IndividualResolutionLeavesLastActiveRunningWithoutWinner(QusapRaidParticipantOutcome outcome)
        {
            var s = Two(); Assert.That(s.TryResolve("P1", outcome), Is.True);
            Assert.That(s.TryGetOutcome("P2", out var remaining), Is.True); Assert.That(remaining, Is.EqualTo(QusapRaidParticipantOutcome.Active));
            Assert.That(s.ActiveCount, Is.EqualTo(1)); Assert.That(s.Status, Is.EqualTo(QusapRaidSessionStatus.Running));
            Assert.That(s.FinalResult, Is.Null); Assert.That(s.SessionFinishedEvents, Is.Zero);
        }
        [TestCase(QusapRaidParticipantOutcome.Extracted, QusapRaidParticipantOutcome.Extracted)]
        [TestCase(QusapRaidParticipantOutcome.Extracted, QusapRaidParticipantOutcome.Eliminated)]
        [TestCase(QusapRaidParticipantOutcome.Eliminated, QusapRaidParticipantOutcome.Eliminated)]
        [TestCase(QusapRaidParticipantOutcome.Eliminated, QusapRaidParticipantOutcome.Extracted)]
        public void RepeatedOrContradictorySignalsDoNotChangeResolvedParticipant(QusapRaidParticipantOutcome first, QusapRaidParticipantOutcome second)
        {
            var s = Two(); s.TryResolve("P1", first); Assert.That(s.TryResolve("P1", second), Is.False);
            s.TryGetOutcome("P1", out var actual); Assert.That(actual, Is.EqualTo(first)); Assert.That(s.ActiveCount, Is.EqualTo(1));
            Assert.That(s.RejectedSignals, Is.EqualTo(1)); Assert.That(s.LastRejection, Is.EqualTo(QusapRaidSessionRejection.AlreadyResolved));
        }
        [Test] public void UnknownAndInvalidOutcomesDoNotResolveAnyParticipant()
        {
            var s = Two(); Assert.That(s.TryResolve("unknown", QusapRaidParticipantOutcome.Extracted), Is.False);
            Assert.That(s.TryResolve(null, QusapRaidParticipantOutcome.Extracted), Is.False);
            Assert.That(s.TryResolve("P1", QusapRaidParticipantOutcome.Active), Is.False);
            Assert.That(s.TryResolve("P1", (QusapRaidParticipantOutcome)99), Is.False);
            Assert.That(s.ActiveCount, Is.EqualTo(2)); Assert.That(s.RejectedSignals, Is.EqualTo(4));
        }
        [Test] public void FinalSnapshotIsDetachedReadOnlySortedAndUnchangedAfterLateSignals()
        {
            var s = Two(); var before = s.ReadParticipantsSnapshot(); int calls = 0; QusapRaidSessionResult published = null;
            s.SessionFinished += result => { calls++; published = result; };
            s.TryResolve("P2", QusapRaidParticipantOutcome.Eliminated); s.TryResolve("P1", QusapRaidParticipantOutcome.Extracted);
            var final = s.FinalResult;
            Assert.That(s.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(final, Is.SameAs(published));
            Assert.That(final.SessionId, Is.EqualTo("session")); Assert.That(final.ParticipantCount, Is.EqualTo(2));
            Assert.That(final.ExtractedCount, Is.EqualTo(1)); Assert.That(final.EliminatedCount, Is.EqualTo(1));
            Assert.That(final.Reason, Is.EqualTo(QusapRaidSessionFinishReason.AllParticipantsResolved));
            CollectionAssert.AreEqual(new[] { "P1", "P2" }, final.Participants.Select(p => p.ParticipantId));
            Assert.That(before.All(p => p.Outcome == QusapRaidParticipantOutcome.Active), Is.True);
            var mutableView = (IList<QusapRaidParticipantResult>)final.Participants;
            Assert.Throws<NotSupportedException>(() => mutableView.RemoveAt(0));
            Assert.Throws<NotSupportedException>(() => mutableView[0] = before[0]);
            foreach (var id in new[] { "P1", "P2" }) foreach (var outcome in new[] { QusapRaidParticipantOutcome.Extracted, QusapRaidParticipantOutcome.Eliminated })
                Assert.That(s.TryResolve(id, outcome), Is.False);
            Assert.That(s.StartSession(), Is.False); Assert.That(s.Register("P3"), Is.False);
            Assert.That(s.FinalResult, Is.SameAs(final)); Assert.That(calls, Is.EqualTo(1)); Assert.That(s.SessionFinishedEvents, Is.EqualTo(1));
            Assert.That(final.Participants[0].Outcome, Is.EqualTo(QusapRaidParticipantOutcome.Extracted));
        }
        [Test] public void FinishedIsSealedBeforeReentrantNotification()
        {
            var s = new QusapRaidSessionAuthority("one"); s.Register("P1"); s.StartSession(); int calls = 0;
            s.SessionFinished += result =>
            {
                calls++; Assert.That(s.Status, Is.EqualTo(QusapRaidSessionStatus.Finished)); Assert.That(s.FinalResult, Is.SameAs(result));
                Assert.That(s.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated), Is.False); Assert.That(s.StartSession(), Is.False);
            };
            s.TryResolve("P1", QusapRaidParticipantOutcome.Extracted); Assert.That(calls, Is.EqualTo(1)); Assert.That(s.RejectedSignals, Is.EqualTo(1));
        }
        [Test] public void EightParticipantsAll256CombinationsAndThreeOrdersProduceDeterministicResults()
        {
            string[] ids = Enumerable.Range(1, 8).Select(i => "P" + i).ToArray();
            int[][] orders = { Enumerable.Range(0, 8).ToArray(), Enumerable.Range(0, 8).Reverse().ToArray(), new[] { 3, 7, 0, 5, 1, 6, 2, 4 } };
            for (int mask = 0; mask < 256; mask++)
            {
                string expected = null;
                foreach (var order in orders)
                {
                    var s = new QusapRaidSessionAuthority("eight"); foreach (var i in order) s.Register(ids[i]); s.StartSession(); int calls = 0;
                    s.SessionFinished += _ => calls++;
                    for (int resolved = 0; resolved < 8; resolved++)
                    {
                        int i = order[resolved]; var outcome = (mask & (1 << i)) == 0 ? QusapRaidParticipantOutcome.Extracted : QusapRaidParticipantOutcome.Eliminated;
                        Assert.That(s.TryResolve(ids[i], outcome), Is.True);
                        Assert.That(s.ActiveCount, Is.EqualTo(7 - resolved));
                        Assert.That(s.Status, Is.EqualTo(resolved == 7 ? QusapRaidSessionStatus.Finished : QusapRaidSessionStatus.Running));
                        Assert.That(calls, Is.EqualTo(resolved == 7 ? 1 : 0));
                    }
                    var final = s.FinalResult;
                    Assert.That(final.ExtractedCount + final.EliminatedCount, Is.EqualTo(8));
                    CollectionAssert.AreEqual(ids, final.Participants.Select(p => p.ParticipantId));
                    string actual = string.Join(";", final.Participants.Select(p => p.ParticipantId + ":" + p.Outcome));
                    if (expected != null) Assert.That(actual, Is.EqualTo(expected)); expected = actual;
                    Assert.That(s.TryResolve("P1", QusapRaidParticipantOutcome.Eliminated), Is.False); Assert.That(calls, Is.EqualTo(1));
                }
            }
        }
        private static QusapRaidSessionAuthority Two()
        { var s = new QusapRaidSessionAuthority("session"); s.Register("P2"); s.Register("P1"); s.StartSession(); return s; }
    }
}
