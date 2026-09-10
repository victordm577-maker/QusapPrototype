using System;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapWeaponEquipment : MonoBehaviour, IQusapDisarmable
    {
        private QusapCombatController ownerController;
        private QusapWeaponEquipmentState state;

        public event Action<QusapWeaponTransition> WeaponTransitioned;

        public bool IsInitialized => state != null;
        public bool HasWeapon => EnsureInitialized() && state.HasWeapon;
        public QusapWeaponInstance EquippedWeapon => EnsureInitialized()
            ? state.EquippedWeapon
            : null;
        public ulong OwnerEntityId => EnsureInitialized() ? state.OwnerEntityId : 0;
        public ulong Revision => EnsureInitialized() ? state.Revision : 0;
        public bool CanBeDisarmed => isActiveAndEnabled
            && EnsureInitialized()
            && state.HasWeapon;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnDestroy()
        {
            if (state != null && state.HasWeapon)
            {
                state.TryDrop(out _, out _);
            }

            state = null;
            ownerController = null;
            WeaponTransitioned = null;
        }

        public bool TryInitialize()
        {
            return TryInitialize(GetComponent<QusapCombatController>());
        }

        public bool TryInitialize(QusapCombatController controller)
        {
            if (controller == null || controller.gameObject != gameObject)
            {
                return false;
            }

            ulong entityId = EntityId.ToULong(controller.GetEntityId());
            if (entityId == 0)
            {
                return false;
            }

            if (state != null)
            {
                return ownerController == controller && state.OwnerEntityId == entityId;
            }

            ownerController = controller;
            state = new QusapWeaponEquipmentState(entityId);
            return true;
        }

        public QusapWeaponOperationResult TryEquip(
            QusapWeaponInstance weapon,
            out QusapWeaponTransition transition)
        {
            transition = default;
            if (!EnsureInitialized())
            {
                return QusapWeaponOperationResult.InvalidOwner;
            }

            QusapWeaponOperationResult result = state.TryEquip(weapon, out transition);
            if (result == QusapWeaponOperationResult.Success)
            {
                WeaponTransitioned?.Invoke(transition);
            }

            return result;
        }

        public QusapWeaponOperationResult TryDrop(
            out QusapWeaponInstance weapon,
            out QusapWeaponTransition transition)
        {
            weapon = null;
            transition = default;
            if (!EnsureInitialized())
            {
                return QusapWeaponOperationResult.InvalidOwner;
            }

            QusapWeaponOperationResult result = state.TryDrop(out weapon, out transition);
            if (result == QusapWeaponOperationResult.Success)
            {
                WeaponTransitioned?.Invoke(transition);
            }

            return result;
        }

        public QusapWeaponOperationResult TryDisarm(
            ulong sourceEntityId,
            out QusapWeaponInstance weapon,
            out QusapWeaponTransition transition)
        {
            weapon = null;
            transition = default;
            if (!EnsureInitialized())
            {
                return QusapWeaponOperationResult.InvalidOwner;
            }

            QusapWeaponOperationResult result = state.TryDisarm(
                sourceEntityId,
                out weapon,
                out transition);
            if (result == QusapWeaponOperationResult.Success)
            {
                WeaponTransitioned?.Invoke(transition);
            }

            return result;
        }

        public QusapWeaponOperationResult TryVoluntarySwap(
            QusapWeaponInstance expectedEquippedWeapon,
            QusapWeaponInstance replacementWeapon,
            out QusapWeaponSwapTransition swapTransition)
        {
            swapTransition = default;
            if (!EnsureInitialized())
            {
                return QusapWeaponOperationResult.InvalidOwner;
            }

            QusapWeaponOperationResult result = state.TryVoluntarySwap(
                expectedEquippedWeapon,
                replacementWeapon,
                out swapTransition);
            if (result == QusapWeaponOperationResult.Success)
            {
                WeaponTransitioned?.Invoke(swapTransition.ReleaseTransition);
                WeaponTransitioned?.Invoke(swapTransition.EquipTransition);
            }

            return result;
        }

        public bool TryDisarm(QusapCombatController source)
        {
            if (!isActiveAndEnabled || source == null)
            {
                return false;
            }

            ulong sourceEntityId = EntityId.ToULong(source.GetEntityId());
            return TryDisarm(sourceEntityId, out _, out _)
                == QusapWeaponOperationResult.Success;
        }

        private bool EnsureInitialized()
        {
            return state != null || TryInitialize();
        }
    }
}
