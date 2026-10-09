using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(600)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QusapRaidInventory))]
    public sealed class QusapRaidExtraction : MonoBehaviour
    {
        private QusapRaidInventory inventory;
        private QusapRespawnController recovery;
        private QusapExtractionVolume zone;
        private bool resolving;
        private QusapRaidSession session;
        public QusapExtractionCountdown Countdown { get; } = new();
        public QusapExtractionStatus Status => Countdown.Status;
        public QusapRaidInventory Inventory => inventory;
        public string LastRejection { get; private set; } = "None";
        private void Awake()
        { inventory = GetComponent<QusapRaidInventory>(); recovery = GetComponent<QusapRespawnController>(); }
        private void Start() { session = inventory.Session; session.ExtractionSettled += Settled; }
        private void OnDestroy() { if (session != null) session.ExtractionSettled -= Settled; }
        private void Settled(QusapRaidInventoryState state)
        {
            if (state != inventory.State) return;
            Retire(); Countdown.Complete(); zone = null;
        }
        private void OnEnable()
        {
            var receiver = GetComponent<QusapHitReceiver>();
            receiver.HealthChanged += HealthChanged;
            receiver.HealthDepleted += Depleted;
            receiver.Eliminated += Eliminated;
            if (recovery != null) recovery.RecoveryCompleted += Recovered;
        }
        private void OnDisable()
        {
            var receiver = GetComponent<QusapHitReceiver>();
            receiver.HealthChanged -= HealthChanged; receiver.HealthDepleted -= Depleted; receiver.Eliminated -= Eliminated;
            if (recovery != null) recovery.RecoveryCompleted -= Recovered;
            Countdown.Cancel(QusapExtractionCancellation.PlayerDisabled); zone = null;
        }
        private void HealthChanged(QusapHealthChange change)
        {
            if (change.TotalDamage > change.PreviousDamage)
                Countdown.Cancel(inventory.Receiver.IsHealthDepleted ? QusapExtractionCancellation.Eliminated : QusapExtractionCancellation.Damage);
        }
        private void Depleted(QusapHealthChange _) { Countdown.Eliminate(); zone = null; }
        private void Eliminated(QusapHitReceiver _) { Countdown.Eliminate(); zone = null; }
        private void Recovered(QusapRecoveryCause cause)
        {
            if (cause == QusapRecoveryCause.TechnicalRecovery)
            { Countdown.Cancel(QusapExtractionCancellation.TechnicalRecovery); zone = null; }
        }
        public bool TryBegin(QusapExtractionVolume volume)
        {
            if (!isActiveAndEnabled || resolving || inventory == null || !inventory.CanOperate
                || volume == null || !volume.Available || !volume.Contains(this)
                || !Countdown.Begin(volume.RequiredSeconds, true))
            { LastRejection = "Blocked: " + Status; return false; }
            zone = volume; LastRejection = "None"; return true;
        }
        public void Leave(QusapExtractionVolume volume, QusapExtractionCancellation reason)
        {
            if (zone != volume) return;
            Countdown.Cancel(reason); zone = null;
        }
        private void Update()
        {
            if (Status != QusapExtractionStatus.Extracting) return;
            if (!inventory.CanOperate) { Countdown.Eliminate(); zone = null; return; }
            if (zone == null || !zone.Available) { Countdown.Cancel(QusapExtractionCancellation.ZoneDisabled); zone = null; return; }
            if (!zone.Contains(this)) { Leave(zone, QusapExtractionCancellation.LeftVolume); return; }
            Countdown.Advance(Time.deltaTime, Resolve);
        }
        private QusapLootResult Resolve()
        {
            if (resolving || !inventory.CanOperate || zone == null || !zone.Available || !zone.Contains(this))
                return QusapLootResult.Blocked;
            resolving = true;
            try
            {
                var result = inventory.Session.SettleExtraction(inventory.State, inventory.WeaponAdapter);
                return result;
            }
            finally { resolving = false; }
        }
        private void Retire()
        {
            // No elimination API or death callback is involved.
            inventory.Receiver.RetireFromRaid();
            GetComponent<QusapInputReader>()?.SetExternalGameplayInputBlocked(true);
            var combat = GetComponent<QusapCombatController>();
            if (combat != null) combat.CombatAllowed = false;
            GetComponent<QusapHitstunController>()?.ResetHitstun();
            GetComponent<QusapDashMotor>()?.ResetDashState();
            Block(combat); Block(GetComponent<QusapHitstunController>());
            Block(GetComponent<QusapHorizontalMotor>()); Block(GetComponent<QusapVerticalMotor>());
            Block(GetComponent<QusapDashMotor>()); Block(GetComponent<QusapInputReader>());
            Block(recovery); Block(GetComponent<QusapWeaponEquipment>());
            foreach (var hitbox in GetComponentsInChildren<QusapAttackHitbox>(true)) hitbox.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var renderer in GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (var animator in GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
                body.detectCollisions = false; body.isKinematic = true;
            }
        }
        private static void Block(Behaviour component) { if (component != null) component.enabled = false; }
    }
}
