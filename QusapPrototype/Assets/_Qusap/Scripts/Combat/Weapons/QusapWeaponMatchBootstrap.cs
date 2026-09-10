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
        [SerializeField] private bool spawnInitialWhiteWeapon;
        [SerializeField] private Vector3 initialWhiteWeaponPosition =
            new(0f, 0.35f, 0f);
        [SerializeField] private float forwardThrowDistance = 4.5f;
        [SerializeField] private float forwardThrowArcHeight = 1.2f;
        [SerializeField] private float forwardThrowDuration = 0.65f;
        [SerializeField] private float upThrowHorizontalDistance = 0.8f;
        [SerializeField] private float upThrowArcHeight = 4.0f;
        [SerializeField] private float upThrowDuration = 0.70f;

        private readonly QusapWeaponIdGenerator idGenerator = new();
        private readonly List<QusapDroppedWeaponView> droppedWeapons = new();
        private readonly HashSet<ulong> representedDroppedIds = new();
        private readonly List<RuntimePickupCandidate> pickupCandidates = new(16);
        private QusapWeaponPickupResolver pickupResolver;
        private bool subscribed;
        private bool initialized;
        private bool destroying;
        private QusapInputReader playerOneInput;
        private QusapInputReader playerTwoInput;

        public bool IsInitialized => initialized;
        public QusapWeaponVisualCatalog Catalog => catalog;
        public QusapWeaponEquipment PlayerOneEquipment => playerOneEquipment;
        public QusapEquippedWeaponPresenter PlayerOnePresenter => playerOnePresenter;
        public QusapWeaponEquipment PlayerTwoEquipment => playerTwoEquipment;
        public QusapEquippedWeaponPresenter PlayerTwoPresenter => playerTwoPresenter;
        public QusapWeaponInstance PlayerOneWeapon { get; private set; }
        public QusapWeaponInstance PlayerTwoWeapon { get; private set; }
        public QusapWeaponInstance InitialWhiteWeapon { get; private set; }
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
                ProcessSwapInput(playerOneEquipment, playerOneInput);
                ProcessSwapInput(playerTwoEquipment, playerTwoInput);
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
            playerOneInput = null;
            playerTwoInput = null;
            InitialWhiteWeapon = null;
            initialized = false;
        }

        private void OnValidate()
        {
            pickupRadius = QusapWeaponPickupResolver.NormalizePickupRadius(
                pickupRadius);
            previousOwnerPickupLockout = (float)
                QusapWeaponPickupResolver.NormalizePreviousOwnerLockout(
                    previousOwnerPickupLockout);
            NormalizeThrowProfiles();
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

        public void ConfigureInitialWhiteWeapon(bool enabled, Vector3 position)
        {
            if (initialized || !IsFinite(position))
            {
                return;
            }

            spawnInitialWhiteWeapon = enabled;
            initialWhiteWeaponPosition = position;
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
            NormalizeThrowProfiles();

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
            playerOneInput = playerOneEquipment.GetComponent<QusapInputReader>();
            playerTwoInput = playerTwoEquipment.GetComponent<QusapInputReader>();
            if (spawnInitialWhiteWeapon
                && (!catalog.TryGetDefinition(
                        QusapWeaponVisualCatalog.WhiteDefinitionId,
                        out QusapWeaponDefinition whiteDefinition)
                    || !TryCreateInitialDroppedWeapon(
                        new QusapWeaponInstance(idGenerator.Next(), whiteDefinition),
                        initialWhiteWeaponPosition,
                        Time.timeAsDouble)))
            {
                playerTwoEquipment.TryDrop(out _, out _);
                playerOneEquipment.TryDrop(out _, out _);
                Unsubscribe();
                return false;
            }

            initialized = true;
            return true;
        }

        public bool TryProcessVoluntarySwap(
            QusapWeaponEquipment equipment,
            QusapWeaponSwapThrowPress press,
            double currentTimestamp)
        {
            if (!initialized
                || !isActiveAndEnabled
                || Time.timeScale <= 0f
                || press.PressId == 0
                || !IsFinite(currentTimestamp)
                || !IsRegisteredAndActive(equipment)
                || !equipment.HasWeapon
                || IsSwapThrowInProgress(equipment.OwnerEntityId)
                || !IsInputEnabled(equipment)
                || pickupResolver == null)
            {
                return false;
            }

            QusapDroppedWeaponView selected = null;
            QusapWeaponPickupCandidate selectedCandidate = default;
            bool hasSelected = false;
            QusapWeaponPickupPlayerSnapshot player = CreatePlayerSnapshot(equipment);
            for (int i = 0; i < droppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = droppedWeapons[i];
                if (dropped == null
                    || !pickupResolver.TryCreateSwapCandidate(
                        player,
                        dropped.CreatePickupSnapshot(),
                        currentTimestamp,
                        out QusapWeaponPickupCandidate candidate)
                    || (hasSelected
                        && !QusapWeaponPickupResolver.IsPreferred(
                            candidate,
                            selectedCandidate)))
                {
                    continue;
                }

                selected = dropped;
                selectedCandidate = candidate;
                hasSelected = true;
            }

            if (!hasSelected
                || selected == null
                || !selected.TryReserve(equipment.OwnerEntityId))
            {
                return false;
            }

            QusapWeaponInstance original = equipment.EquippedWeapon;
            QusapWeaponInstance replacement = selected.Weapon;
            QusapEquippedWeaponPresenter presenter = GetPresenter(equipment);
            if (original == null
                || replacement == null
                || !replacement.IsFree
                || representedDroppedIds.Contains(original.InstanceId)
                || presenter == null
                || !catalog.TryGetEntry(
                    original.Definition.Id,
                    out QusapWeaponVisualEntry releasedEntry))
            {
                selected.ReleaseReservation(equipment.OwnerEntityId);
                return false;
            }

            Vector3 origin = presenter.WeaponSocket != null
                ? presenter.WeaponSocket.position
                : presenter.transform.position;
            origin.z = presenter.transform.position.z;
            if (!IsFinite(origin))
            {
                selected.ReleaseReservation(equipment.OwnerEntityId);
                return false;
            }

            QusapWeaponOperationResult result = equipment.TryVoluntarySwap(
                original,
                replacement,
                out QusapWeaponSwapTransition swap);
            if (result != QusapWeaponOperationResult.Success)
            {
                selected.ReleaseReservation(equipment.OwnerEntityId);
                return false;
            }

            selected.MarkClaimed();
            droppedWeapons.Remove(selected);
            representedDroppedIds.Remove(replacement.InstanceId);
            DestroyObject(selected.gameObject);

            QusapDroppedWeaponView thrown = CreateDroppedObject(swap.ReleasedWeapon);
            if (thrown == null
                || !thrown.InitializeVoluntaryThrow(
                    swap.ReleasedWeapon,
                    releasedEntry.VisualPrefab,
                    origin,
                    press.Direction,
                    press.FacingDirection,
                    GetThrowProfile(press.Direction),
                    equipment.OwnerEntityId,
                    currentTimestamp))
            {
                if (thrown != null)
                {
                    DestroyObject(thrown.gameObject);
                }

                Debug.LogError(
                    $"Committed weapon swap could not represent released instance {swap.ReleasedWeapon.InstanceId}.",
                    this);
                return false;
            }

            RegisterDropped(thrown);
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

        private void ProcessSwapInput(
            QusapWeaponEquipment equipment,
            QusapInputReader input)
        {
            if (input != null
                && input.TryConsumeWeaponSwapThrow(out QusapWeaponSwapThrowPress press))
            {
                TryProcessVoluntarySwap(equipment, press, Time.timeAsDouble);
            }
        }

        private bool TryCreateInitialDroppedWeapon(
            QusapWeaponInstance weapon,
            Vector3 position,
            double timestamp)
        {
            if (weapon == null
                || !weapon.IsFree
                || !IsFinite(position)
                || !catalog.TryGetEntry(
                    weapon.Definition.Id,
                    out QusapWeaponVisualEntry entry))
            {
                return false;
            }

            QusapDroppedWeaponView dropped = CreateDroppedObject(weapon);
            if (dropped == null
                || !dropped.InitializeSettled(
                    weapon,
                    entry.VisualPrefab,
                    position,
                    timestamp))
            {
                if (dropped != null)
                {
                    DestroyObject(dropped.gameObject);
                }

                return false;
            }

            RegisterDropped(dropped);
            InitialWhiteWeapon = weapon;
            return true;
        }

        private QusapDroppedWeaponView CreateDroppedObject(QusapWeaponInstance weapon)
        {
            if (weapon == null
                || representedDroppedIds.Contains(weapon.InstanceId))
            {
                return null;
            }

            GameObject droppedObject = new($"DroppedWeapon_{weapon.InstanceId}");
            droppedObject.transform.SetParent(transform, true);
            return droppedObject.AddComponent<QusapDroppedWeaponView>();
        }

        private void RegisterDropped(QusapDroppedWeaponView dropped)
        {
            representedDroppedIds.Add(dropped.InstanceId);
            droppedWeapons.Add(dropped);
            int requiredCandidateCapacity = droppedWeapons.Count * 2;
            if (pickupCandidates.Capacity < requiredCandidateCapacity)
            {
                pickupCandidates.Capacity = requiredCandidateCapacity;
            }
        }

        private bool IsSwapThrowInProgress(ulong ownerEntityId)
        {
            for (int i = 0; i < droppedWeapons.Count; i++)
            {
                QusapDroppedWeaponView dropped = droppedWeapons[i];
                if (dropped != null
                    && dropped.ReleaseType == QusapWeaponReleaseType.VoluntarySwapThrow
                    && dropped.PreviousOwnerEntityId == ownerEntityId
                    && !dropped.IsSettled)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInputEnabled(QusapWeaponEquipment equipment)
        {
            QusapInputReader input = equipment == playerOneEquipment
                ? playerOneInput
                : equipment == playerTwoEquipment
                    ? playerTwoInput
                    : null;
            return input != null && input.isActiveAndEnabled;
        }

        private QusapEquippedWeaponPresenter GetPresenter(
            QusapWeaponEquipment equipment)
        {
            return equipment == playerOneEquipment
                ? playerOnePresenter
                : equipment == playerTwoEquipment
                    ? playerTwoPresenter
                    : null;
        }

        private QusapWeaponThrowTrajectoryProfile GetThrowProfile(
            QusapWeaponThrowDirection direction)
        {
            return direction == QusapWeaponThrowDirection.Up
                ? new QusapWeaponThrowTrajectoryProfile(
                    upThrowHorizontalDistance,
                    upThrowArcHeight,
                    upThrowDuration,
                    0.8f,
                    4.0f,
                    0.70f)
                : new QusapWeaponThrowTrajectoryProfile(
                    forwardThrowDistance,
                    forwardThrowArcHeight,
                    forwardThrowDuration,
                    4.5f,
                    1.2f,
                    0.65f);
        }

        private void NormalizeThrowProfiles()
        {
            QusapWeaponThrowTrajectoryProfile forward =
                new QusapWeaponThrowTrajectoryProfile(
                    forwardThrowDistance,
                    forwardThrowArcHeight,
                    forwardThrowDuration,
                    4.5f,
                    1.2f,
                    0.65f);
            forwardThrowDistance = forward.HorizontalDistance;
            forwardThrowArcHeight = forward.ArcHeight;
            forwardThrowDuration = forward.Duration;

            QusapWeaponThrowTrajectoryProfile up =
                new QusapWeaponThrowTrajectoryProfile(
                    upThrowHorizontalDistance,
                    upThrowArcHeight,
                    upThrowDuration,
                    0.8f,
                    4.0f,
                    0.70f);
            upThrowHorizontalDistance = up.HorizontalDistance;
            upThrowArcHeight = up.ArcHeight;
            upThrowDuration = up.Duration;
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

            QusapDroppedWeaponView droppedView = CreateDroppedObject(transition.Weapon);
            if (droppedView == null
                || !droppedView.Initialize(
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
                if (droppedView != null)
                {
                    DestroyObject(droppedView.gameObject);
                }

                return;
            }

            RegisterDropped(droppedView);
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

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
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
