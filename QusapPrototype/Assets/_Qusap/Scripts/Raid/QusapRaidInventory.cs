using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapRaidInventory : MonoBehaviour
    {
        [SerializeField] private string participantId;
        [SerializeField] private string profileId;
        [SerializeField, Min(1)] private int backpackCapacity = 6;
        private QusapRaidSession session;
        public QusapRaidInventoryState State { get; private set; }
        public QusapRaidWeaponAdapter WeaponAdapter { get; private set; }
        public QusapHitReceiver Receiver { get; private set; }
        public string ParticipantId => participantId;
        public string ProfileId => profileId;
        public int BackpackCapacity => backpackCapacity;
        public QusapRaidSession Session => session;
        public bool CanOperate => State != null && State.CanOperate && isActiveAndEnabled
            && Receiver != null && !Receiver.IsHealthDepleted;
        private void Awake() => Receiver = GetComponent<QusapHitReceiver>();
        private void OnEnable()
        {
            Receiver ??= GetComponent<QusapHitReceiver>();
            if (Receiver == null) return;
            Receiver.HealthDepleted += Depleted; Receiver.Eliminated += Eliminated;
        }
        private void OnDisable()
        {
            if (Receiver != null) { Receiver.HealthDepleted -= Depleted; Receiver.Eliminated -= Eliminated; }
        }
        public void Configure(string participant, string profile, int capacity = 6)
        { if (State != null) throw new System.InvalidOperationException("Inventory already initialized."); participantId = participant; profileId = profile; backpackCapacity = capacity; }
        public void Initialize(QusapRaidSession raid, IReadOnlyList<QusapLootDefinition> definitions)
        {
            if (State != null) return;
            session = raid; State = raid.Register(participantId, profileId, backpackCapacity);
            WeaponAdapter = new QusapRaidWeaponAdapter(raid.World, State, GetComponent<QusapWeaponEquipment>(), definitions);
            if (Receiver.IsHealthDepleted) State.BlockOperations();
            if (Receiver.IsEliminated) RetryDeathSettlement();
        }
        private void Depleted(QusapHealthChange _) => State?.BlockOperations();
        private void Eliminated(QusapHitReceiver _) { State?.BlockOperations(); RetryDeathSettlement(); }
        public QusapLootResult RetryDeathSettlement() => State != null && Receiver.IsEliminated
            ? session.SettleDeath(State, WeaponAdapter) : QusapLootResult.Blocked;
        public QusapLootResult TryPickup(QusapLootPickup pickup) => CanOperate && pickup != null && pickup.CanReach(this)
            ? State.TryPickup(pickup.InstanceId) : QusapLootResult.Blocked;
        public QusapLootResult TryMoveToSecurePocket(string id) => CanOperate ? State.TryMoveToSecurePocket(id) : QusapLootResult.Blocked;
        public QusapLootResult TryMoveToBackpack(string id) => CanOperate ? State.TryMoveToBackpack(id) : QusapLootResult.Blocked;
        public QusapLootResult TryConsume(string id) => CanOperate ? State.TryConsume(id, Receiver.Heal) : QusapLootResult.Blocked;
        public QusapLootResult TryTakeFromDeathContainer(QusapDeathLootContainer container, string id) => CanOperate
            && container != null && container.CanReach(this) ? State.TryTakeFromDeathContainer(container.ContainerId, id) : QusapLootResult.Blocked;
        public QusapLootResult TryEquipWeaponFromContainer(QusapDeathLootContainer container, string id) => CanOperate
            && container != null && container.CanReach(this) ? WeaponAdapter.TryEquipWeaponFromContainer(container.ContainerId, id) : QusapLootResult.Blocked;
        public QusapLootResult TryStoreEquippedWeapon() => CanOperate ? WeaponAdapter.TryStoreEquippedWeapon() : QusapLootResult.Blocked;
        public QusapRaidInventorySnapshot ReadInventorySnapshot() => State?.ReadInventorySnapshot();
        public IReadOnlyList<QusapLootSnapshot> ReadStashSnapshot() => session.Stash.ReadStashSnapshot(profileId);
        private void OnDestroy() { WeaponAdapter?.Dispose(); State?.Dispose(); }
    }
}
