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

        private readonly QusapWeaponIdGenerator idGenerator = new();
        private readonly List<QusapDroppedWeaponView> droppedWeapons = new();
        private readonly HashSet<ulong> representedDroppedIds = new();
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

        private void Start()
        {
            TryInitialize();
        }

        private void OnDestroy()
        {
            destroying = true;
            Unsubscribe();
            ReleaseEquippedWeapon(playerOneEquipment, PlayerOneWeapon);
            ReleaseEquippedWeapon(playerTwoEquipment, PlayerTwoWeapon);
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
            initialized = false;
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

        public bool TryInitialize()
        {
            if (initialized)
            {
                return true;
            }

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
                    droppedFallDistance))
            {
                DestroyObject(droppedObject);
                return;
            }

            representedDroppedIds.Add(transition.Weapon.InstanceId);
            droppedWeapons.Add(droppedView);
        }

        private static void ReleaseEquippedWeapon(
            QusapWeaponEquipment equipment,
            QusapWeaponInstance weapon)
        {
            if (equipment != null
                && weapon != null
                && equipment.EquippedWeapon == weapon)
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
    }
}
