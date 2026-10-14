using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    // Deadline first in Update: later input, loot and extraction cannot evade the latched expiry.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QusapRaidBootstrap))]
    public sealed class QusapRaidTimingDriver : MonoBehaviour
    {
        private QusapRaidBootstrap raid;
        private QusapRaidSession bound;
        private void Awake() { raid = GetComponent<QusapRaidBootstrap>(); }
        private void Update()
        {
            if (raid.Session != bound)
            {
                if (bound != null) { bound.ExpirationStarted -= RetireExpired; bound.DeathSettled -= RetireSettled; }
                bound = raid.Session;
                if (bound != null) { bound.ExpirationStarted += RetireExpired; bound.DeathSettled += RetireSettled; }
            }
            bound?.AdvanceTime(Time.deltaTime);
        }
        private void RetireExpired(IReadOnlyList<QusapRaidInventoryState> participants)
        {
            foreach (var state in participants)
            {
                foreach (var actor in raid.Participants)
                {
                    if (actor.State != state) continue;
                    actor.Receiver.TryReceiveEnvironmentDamage(actor.Receiver.CurrentHealth, this);
                    var elimination = actor.GetComponent<QusapPlayerElimination>();
                    if (elimination == null) elimination = actor.gameObject.AddComponent<QusapPlayerElimination>();
                    elimination.Apply();
                    actor.GetComponent<QusapRaidExtraction>()?.Countdown.Eliminate();
                }
            }
        }
        private void RetireSettled(QusapDeathLootRecord record)
        {
            foreach (var actor in raid.Participants)
                if (actor.ParticipantId == record.ParticipantId) actor.Receiver.RetireFromRaid();
        }
        private void OnDestroy() { if (bound != null) { bound.ExpirationStarted -= RetireExpired; bound.DeathSettled -= RetireSettled; } }
    }
}
