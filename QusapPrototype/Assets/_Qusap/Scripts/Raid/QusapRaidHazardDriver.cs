using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QusapRaidBootstrap),typeof(QusapRaidTimingDriver))]
    public sealed class QusapRaidHazardDriver : MonoBehaviour
    {
        private QusapRaidBootstrap raid;
        public QusapRaidHazard Hazard { get; private set; }
        private void Awake() => raid=GetComponent<QusapRaidBootstrap>();
        public void Bind(QusapRaidHazardSettings settings)
        {
            raid ??= GetComponent<QusapRaidBootstrap>();
            if(Hazard != null) throw new System.InvalidOperationException("Hazard already bound");
            Hazard=new QusapRaidHazard(raid.Session,settings,raid.Participants,this);
        }
        private void Update()
        {
            if(Hazard == null || raid.Session.IsFinished) return;
            // Existing per-profile retry API, including its durable .tmp rejection policy.
            // The timing driver has already applied expiration before this Update.
            if(!raid.Session.Timing.HasExpired)
                foreach(var actor in raid.Participants)
                    if(actor.Receiver.IsEliminated && actor.State.Status == QusapRaidInventoryStatus.SettlementPending)
                        actor.RetryDeathSettlement();
            Hazard.Process();
        }
    }
}
