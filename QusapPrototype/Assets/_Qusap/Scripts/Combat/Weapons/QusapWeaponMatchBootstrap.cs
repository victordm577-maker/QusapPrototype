using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapWeaponMatchBootstrap : MonoBehaviour
    {
        [SerializeField] private QusapWeaponVisualCatalog catalog;
        [SerializeField] private QusapWeaponEquipment playerOneEquipment;
        [SerializeField] private QusapEquippedWeaponPresenter playerOnePresenter;
        [SerializeField] private QusapWeaponEquipment playerTwoEquipment;
        [SerializeField] private QusapEquippedWeaponPresenter playerTwoPresenter;
        [SerializeField] private float droppedStartHeight = 0.35f;
        [SerializeField] private float droppedStartForwardOffset = 0.95f;
        [SerializeField] private float droppedOutwardDistance =
            QusapDroppedWeaponView.DefaultOutwardDistance;
        [SerializeField] private float droppedFallDistance =
            QusapDroppedWeaponView.DefaultFallDistance;
        [SerializeField] private float droppedDuration = QusapDroppedWeaponView.DefaultDuration;
        [SerializeField] private float pickupRadius =
            QusapWeaponPickupResolver.DefaultPickupRadius;
        [SerializeField] private float previousOwnerPickupLockout =
            (float)QusapWeaponPickupResolver.DefaultPreviousOwnerLockout;

        private readonly QusapWeaponIdGenerator idGenerator = new();
        private readonly List<QusapDroppedWeaponView> droppedWeapons = new();
        private readonly HashSet<ulong> representedDroppedIds = new();
        private readonly List<RuntimePickupCandidate> pickupCandidates = new(16);
        private QusapWeaponPickupResolver pickupResolver;
        private bool subscribed;
        private bool initialized;
        private bool destroying;

        public bool IsInitialized => initialized;
        public QusapWeaponVisualCatalog Catalog => catalog;
        public QusapWeaponEquipment PlayerOneEquipment => playerOneEquipment;
        public QusapEquippedWeaponPresenter PlayerOnePresenter => playerOnePresenter;
        public QusapWeaponEquipment PlayerTwoEquipment => playerTwoEquipment;
        public QusapEquippedWeaponPresenter PlayerTwoPresenter => playerTwoPresenter;
        public QusapWeaponInstance PlayerOneWeapon { get; private set; }
        public QusapWeaponInstance PlayerTwoWeapon { get; private set; }
        public IReadOnlyList<QusapDroppedWeaponView> DroppedWeapons => droppedWeapons;
        public int DroppedWeaponCount => droppedWeapons.Count;
        public float PickupRadius => pickupResolver?.PickupRadius
            ?? QusapWeaponPickupResolver.NormalizePickupRadius(pickupRadius);
        public double PreviousOwnerPickupLockout =>
            pickupResolver?.PreviousOwnerLockout
            ?? QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(
                previousOwnerPickupLockout);

        private void Update()
        {
            if (initialized && Time.timeScale > 0f)
            {
                ProcessPickups(Time.timeAsDouble);
            }
        }

        private void Start()
        {
            TryInitialize();
        }

        private void OnDestroy()
        {
            destroying = true;
            Unsubscribe();
            ReleaseEquippedWeapon(playerOneEquipment);
            ReleaseEquippedWeapon(playerTwoEquipment);
            for (int i = droppedWeapons.Count - 1; i >= 0; i--)
            {
                QusapDroppedWeaponView dropped = droppedWeapons[i];
                if (dropped != null)
                {
                    if (Application.isPlaying)
                    {
                        Destroy(dropped.gameObject);
                    }
                    else
                    {
                        DestroyImmediate(dropped.gameObject);
                    }
                }
            }

            droppedWeapons.Clear();
            representedDroppedIds.Clear();
            pickupCandidates.Clear();
            pickupResolver?.ResetTime();
            pickupResolver = null;
            initialized = false;
        }

        private void OnValidate()
        {
            pickupRadius = QusapWeaponPickupResolver.NormalizePickupRadius(
                pickupRadius);
            previousOwnerPickupLockout = (float)
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(
                    previousOwnerPickupLockout);
        }

        public void Configure(
            QusapWeaponVisualCatalog configuredCatalog,
            QusapWeaponEquipment configuredPlayerOneEquipment,
            QusapEquippedWeaponPresenter configuredPlayerOnePresenter,
            QusapWeaponEquipment configuredPlayerTwoEquipment,
            QusapEquippedWeaponPresenter configuredPlayerTwoPresenter)
        {
            if (initialized)
            {
                return;
            }

            catalog = configuredCatalog;
            playerOneEquipment = configuredPlayerOneEquipment;
            playerOnePresenter = configuredPlayerOnePresenter;
            playerTwoEquipment = configuredPlayerTwoEquipment;
            playerTwoPresenter = configuredPlayerTwoPresenter;
        }

        public void ConfigurePickup(
            float configuredPickupRadius,
            double configuredPreviousOwnerLockout)
        {
            if (initialized)
            {
                return;
            }

            pickupRadius = QusapWeaponPickupResolver.NormalizePickupRadius(
                configuredPickupRadius);
            previousOwnerPickupLockout = (float)
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(
                    configuredPreviousOwnerLockout);
            pickupResolver = new QusapWeaponPickupResolver(
                pickupRadius,
                previousOwnerPickupLockout);
        }

        public bool TryInitialize()
        {
            if (initialized)
            {
                return true;
            }

            pickupRadius = QusapWeaponPickupResolver.NormalizePickupRadius(
                pickupRadius);
            previousOwnerPickupLockout = (float)
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(
                    previousOwnerPickupLockout);
            pickupResolver = new QusapWeaponPickupResolver(
                pickupRadius,
                previousOwnerPickupLockout);

            if (!ValidateConfiguration()
                || !playerOnePresenter.TryInitialize()
                || !playerTwoPresenter.TryInitialize()
                || !catalog.TryGetDefinition(
                    QusapWeaponVisualCatalog.BlueDefinitionId,
                    out QusapWeaponDefinition blueDefinition)
                || !catalog.TryGetDefinition(
                    QusapWeaponVisualCatalog.PurpleDefinitionId,
                    out QusapWeaponDefinition purpleDefinition)
                || !catalog.TryGetEntry(QusapWeaponVisualCatalog.WhiteDefinitionId, out _))
            {
                return false;
            }

            Subscribe();
            QusapWeaponInstance playerOneWeapon = new(idGenerator.Next(), blueDefinition);
            QusapWeaponInstance playerTwoWeapon = new(idGenerator.Next(), purpleDefinition);
            QusapWeaponOperationResult playerOneResult = playerOneEquipment.TryEquip(
                playerOneWeapon,
                out _);
            if (playerOneResult != QusapWeaponOperationResult.Success)
            {
                Unsubscribe();
                return false;
            }

            QusapWeaponOperationResult playerTwoResult = playerTwoEquipment.TryEquip(
                playerTwoWeapon,
                out _);
            if (playerTwoResult != QusapWeaponOperationResult.Success)
            {
                playerOneEquipment.TryDrop(out _, out _);
                Unsubscribe();
                return false;
            }

            PlayerOneWeapon = playerOneWeapon;
            PlayerTwoWeapon = playerTwoWeapon;
            initialized = true;
            return true;
        }

        public int ProcessPickups(double currentTimestamp)
        {
            if (!initialized
                || !isActiveAndEnabled
                || Time.timeScale <= 0f
                || pickupResolver == null
                || !pickupResolver.TryAcceptTimestamp(currentTimestamp))
            {
                return 0;
            }

            pickupCandidates.Clear();
            AddCandidatesForPlayer(playerOneEquipment, currentTimestamp);
            AddCandidatesForPlayer(playerTwoEquipment, currentTimestamp);
            pickupCandidates.Sort(RuntimePickupCandidateComparer.Instance);

            int completed = 0;
            for (int i = 0; i < pickupCandidates.Count; i++)
            {
                RuntimePickupCandidate candidate = pickupCandidates[i];
                if (TryPickup(
                    candidate.Equipment,
                    candidate.DroppedWeapon,
                    currentTimestamp))
                {
                    completed++;
                }
            }

            pickupCandidates.Clear();
            return completed;
        }

        internal bool TryPickup(
            QusapWeaponEquipment equipment,
            QusapDroppedWeaponView droppedWeapon,
            double currentTimestamp)
        {
            if (!initialized
                || !isActiveAndEnabled
                || Time.timeScale <= 0f
                || !IsRegisteredAndActive(equipment)
                || droppedWeapon == null
                || !droppedWeapons.Contains(droppedWeapon)
                || pickupResolver == null
                || !pickupResolver.TryCreateCandidate(
                    CreatePlayerSnapshot(equipment),
                    droppedWeapon.CreatePickupSnapshot(),
                    currentTimestamp,
                    out _))
            {
                return false;
            }

            return TryConfirmPickup(equipment, droppedWeapon);
        }

        internal bool TryConfirmPickup(
            QusapWeaponEquipment equipment,
            QusapDroppedWeaponView droppedWeapon)
        {
            if (!initialized
                || !isActiveAndEnabled
                || Time.timeScale <= 0f
                || !IsRegisteredAndActive(equipment)
                || droppedWeapon == null
                || droppedWeapon.IsClaimed
                || droppedWeapon.Weapon == null
                || !droppedWeapon.Weapon.IsFree
                || !droppedWeapons.Contains(droppedWeapon))
            {
                return false;
            }

            QusapWeaponInstance weapon = droppedWeapon.Weapon;
            QusapWeaponOperationResult result = equipment.TryEquip(weapon, out _);
            if (result != QusapWeaponOperationResult.Success
                || equipment.EquippedWeapon != weapon
                || weapon.OwnerEntityId != equipment.OwnerEntityId)
            {
                return false;
            }

            droppedWeapon.MarkClaimed();
            droppedWeapons.Remove(droppedWeapon);
            representedDroppedIds.Remove(weapon.InstanceId);
            DestroyObject(droppedWeapon.gameObject);
            return true;
        }

        private bool ValidateConfiguration()
        {
            return catalog != null
                && playerOneEquipment != null
                && playerOnePresenter != null
                && playerTwoEquipment != null
                && playerTwoPresenter != null
                && playerOneEquipment != playerTwoEquipment
                && playerOnePresenter != playerTwoPresenter
                && playerOneEquipment.gameObject == playerOnePresenter.gameObject
                && playerTwoEquipment.gameObject == playerTwoPresenter.gameObject;
        }

        private void Subscribe()
        {
            if (subscribed)
            {
                return;
            }

            playerOneEquipment.WeaponTransitioned += HandlePlayerOneTransition;
            playerTwoEquipment.WeaponTransitioned += HandlePlayerTwoTransition;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            if (playerOneEquipment != null)
            {
                playerOneEquipment.WeaponTransitioned -= HandlePlayerOneTransition;
            }

            if (playerTwoEquipment != null)
            {
                playerTwoEquipment.WeaponTransitioned -= HandlePlayerTwoTransition;
            }

            subscribed = false;
        }

        private void HandlePlayerOneTransition(QusapWeaponTransition transition)
        {
            HandleTransition(playerOnePresenter, transition);
        }

        private void HandlePlayerTwoTransition(QusapWeaponTransition transition)
        {
            HandleTransition(playerTwoPresenter, transition);
        }

        private void HandleTransition(
            QusapEquippedWeaponPresenter ownerPresenter,
            QusapWeaponTransition transition)
        {
            if (destroying
                || transition.Type != QusapWeaponTransitionType.Disarmed
                || transition.Weapon == null
                || !transition.Weapon.IsFree
                || representedDroppedIds.Contains(transition.Weapon.InstanceId)
                || !catalog.TryGetEntry(
                    transition.Weapon.Definition.Id,
                    out QusapWeaponVisualEntry entry))
            {
                return;
            }

            int direction = ownerPresenter.DisplayedFacingDirection < 0 ? -1 : 1;
            Vector3 ownerPosition = ownerPresenter.transform.position;
            Vector3 origin = ownerPosition + new Vector3(
                direction * droppedStartForwardOffset,
                droppedStartHeight,
                0f);
            origin.z = ownerPosition.z;

            GameObject droppedObject = new($"DroppedWeapon_{transition.Weapon.InstanceId}");
            droppedObject.transform.SetParent(transform, true);
            QusapDroppedWeaponView droppedView =
                droppedObject.AddComponent<QusapDroppedWeaponView>();
            if (!droppedView.Initialize(
                    transition.Weapon,
                    entry.VisualPrefab,
                    origin,
                    direction,
                    droppedDuration,
                    droppedOutwardDistance,
                    droppedFallDistance,
                    transition.PreviousOwnerEntityId,
                    Time.timeAsDouble))
            {
                DestroyObject(droppedObject);
                return;
            }

            representedDroppedIds.Add(transition.Weapon.InstanceId);
            droppedWeapons.Add(droppedView);
            int requiredCandidateCapacity = droppedWeapons.Count * 2;
            if (pickupCandidates.Capacity < requiredCandidateCapacity)
            {
                pickupCandidates.Capacity = requiredCandidateCapacity;
            }
        }

        private void AddCandidatesForPlayer(
            QusapWeaponEquipment equipment,
            double currentTimestamp)
        {
            if (!IsRegisteredAndActive(equipment))
            {
                return;
            }

            QusapWeaponPickupPlayerSnapshot player = CreatePlayerSnapshot(equipment);
            for (int i = 0; i < droppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = droppedWeapons[i];
                if (dropped != null
                    && pickupResolver.TryCreateCandidate(
                        player,
                        dropped.CreatePickupSnapshot(),
                        currentTimestamp,
                        out QusapWeaponPickupCandidate candidate))
                {
                    pickupCandidates.Add(new RuntimePickupCandidate(
                        equipment,
                        dropped,
                        candidate));
                }
            }
        }

        private bool IsRegisteredAndActive(QusapWeaponEquipment equipment)
        {
            return equipment != null
                && (equipment == playerOneEquipment || equipment == playerTwoEquipment)
                && equipment.isActiveAndEnabled
                && equipment.gameObject.activeInHierarchy;
        }

        private static QusapWeaponPickupPlayerSnapshot CreatePlayerSnapshot(
            QusapWeaponEquipment equipment)
        {
            return new QusapWeaponPickupPlayerSnapshot(
                equipment.OwnerEntityId,
                equipment.isActiveAndEnabled && equipment.gameObject.activeInHierarchy,
                equipment.HasWeapon,
                equipment.transform.position);
        }

        private static void ReleaseEquippedWeapon(QusapWeaponEquipment equipment)
        {
            if (equipment != null
                && equipment.EquippedWeapon != null)
            {
                equipment.TryDrop(out _, out _);
            }
        }

        private static void DestroyObject(GameObject target)
        {
            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private readonly struct RuntimePickupCandidate
        {
            public RuntimePickupCandidate(
                QusapWeaponEquipment equipment,
                QusapDroppedWeaponView droppedWeapon,
                QusapWeaponPickupCandidate logicalCandidate)
            {
                Equipment = equipment;
                DroppedWeapon = droppedWeapon;
                LogicalCandidate = logicalCandidate;
            }

            public QusapWeaponEquipment Equipment { get; }
            public QusapDroppedWeaponView DroppedWeapon { get; }
            public QusapWeaponPickupCandidate LogicalCandidate { get; }
        }

        private sealed class RuntimePickupCandidateComparer
            : IComparer<RuntimePickupCandidate>
        {
            public static readonly RuntimePickupCandidateComparer Instance = new();

            public int Compare(
                RuntimePickupCandidate left,
                RuntimePickupCandidate right)
            {
                if (QusapWeaponPickupResolver.IsPreferred(
                    left.LogicalCandidate,
                    right.LogicalCandidate))
                {
                    return -1;
                }

                return QusapWeaponPickupResolver.IsPreferred(
                    right.LogicalCandidate,
                    left.LogicalCandidate)
                    ? 1
                    : 0;
            }
        }
    }
}
