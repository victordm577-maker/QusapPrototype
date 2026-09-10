using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapEquippedWeaponPresenter : MonoBehaviour
    {
        public static readonly Vector3 DefaultSocketOffset = new(1.15f, 0.25f, -0.35f);
        public static readonly Vector3 DefaultSocketEulerAngles = new(0f, 0f, -12f);
        public static readonly Vector3 DefaultVisualScale = Vector3.one * 0.70f;

        [SerializeField] private QusapWeaponEquipment equipment;
        [SerializeField] private QusapCombatController combatController;
        [SerializeField] private QusapWeaponVisualCatalog catalog;
        [SerializeField] private Transform weaponSocket;
        [SerializeField] private Vector3 socketOffset = new(1.15f, 0.25f, -0.35f);
        [SerializeField] private Vector3 socketEulerAngles = new(0f, 0f, -12f);
        [SerializeField] private Vector3 visualScale = new(0.70f, 0.70f, 0.70f);

        private bool initialized;
        private QusapWeaponInstance displayedWeapon;
        private GameObject equippedVisual;
        private GameObject displayedVisualPrefab;
        private int displayedFacingDirection = 1;

        public bool IsInitialized => initialized;
        public QusapWeaponEquipment Equipment => equipment;
        public QusapCombatController CombatController => combatController;
        public QusapWeaponVisualCatalog Catalog => catalog;
        public QusapWeaponInstance DisplayedWeapon => displayedWeapon;
        public GameObject EquippedVisual => equippedVisual;
        public GameObject DisplayedVisualPrefab => displayedVisualPrefab;
        public Transform WeaponSocket => weaponSocket;
        public Vector3 SocketOffset => socketOffset;
        public Vector3 SocketEulerAngles => socketEulerAngles;
        public Vector3 VisualScale => visualScale;
        public int DisplayedFacingDirection => displayedFacingDirection;
        public Renderer PrimaryRenderer => equippedVisual != null
            ? equippedVisual.GetComponentInChildren<Renderer>(true)
            : null;

        private void Awake()
        {
            TryInitialize();
        }

        private void Start()
        {
            RefreshPresentation();
        }

        private void LateUpdate()
        {
            if (initialized && combatController != null)
            {
                ApplyFacing(combatController.FacingDirection);
            }
        }

        private void OnDestroy()
        {
            if (equipment != null)
            {
                equipment.WeaponTransitioned -= HandleWeaponTransitioned;
            }

            ClearVisual();
            initialized = false;
        }

        public void Configure(
            QusapWeaponEquipment configuredEquipment,
            QusapCombatController configuredCombatController,
            QusapWeaponVisualCatalog configuredCatalog,
            Transform configuredSocket)
        {
            if (initialized)
            {
                return;
            }

            equipment = configuredEquipment;
            combatController = configuredCombatController;
            catalog = configuredCatalog;
            weaponSocket = configuredSocket;
        }

        public void ConfigureTransform(
            Vector3 configuredSocketOffset,
            Vector3 configuredSocketEulerAngles,
            Vector3 configuredVisualScale)
        {
            if (!IsFinite(configuredSocketOffset)
                || !IsFinite(configuredSocketEulerAngles)
                || !IsFinite(configuredVisualScale)
                || configuredVisualScale.x <= 0f
                || configuredVisualScale.y <= 0f
                || configuredVisualScale.z <= 0f)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(configuredVisualScale),
                    "Weapon presentation transforms must be finite and visual scale must be positive.");
            }

            socketOffset = configuredSocketOffset;
            socketEulerAngles = configuredSocketEulerAngles;
            visualScale = configuredVisualScale;
            if (initialized)
            {
                ApplyFacing(displayedFacingDirection);
                ApplyVisualTransform();
            }
        }

        public bool TryInitialize()
        {
            if (initialized)
            {
                return true;
            }

            equipment ??= GetComponent<QusapWeaponEquipment>();
            combatController ??= GetComponent<QusapCombatController>();
            if (equipment == null
                || combatController == null
                || catalog == null
                || weaponSocket == null
                || equipment.gameObject != gameObject
                || combatController.gameObject != gameObject
                || !weaponSocket.IsChildOf(transform)
                || !equipment.TryInitialize(combatController))
            {
                return false;
            }

            equipment.WeaponTransitioned += HandleWeaponTransitioned;
            initialized = true;
            ApplyFacing(combatController.FacingDirection);
            RefreshPresentation();
            return true;
        }

        public void RefreshPresentation()
        {
            if (!initialized && !TryInitialize())
            {
                return;
            }

            ApplyFacing(combatController.FacingDirection);
            if (equipment.HasWeapon)
            {
                ShowWeapon(equipment.EquippedWeapon);
            }
            else
            {
                ClearVisual();
            }
        }

        public void ApplyFacing(int authoritativeFacingDirection)
        {
            if (weaponSocket == null)
            {
                return;
            }

            displayedFacingDirection = authoritativeFacingDirection < 0 ? -1 : 1;
            weaponSocket.localPosition = new Vector3(
                Mathf.Abs(socketOffset.x) * displayedFacingDirection,
                socketOffset.y,
                socketOffset.z);
            weaponSocket.localRotation = Quaternion.Euler(
                socketEulerAngles.x,
                socketEulerAngles.y,
                -Mathf.Abs(socketEulerAngles.z) * displayedFacingDirection);
            weaponSocket.localScale = Vector3.one;
        }

        private void HandleWeaponTransitioned(QusapWeaponTransition transition)
        {
            if (transition.Type == QusapWeaponTransitionType.Equipped)
            {
                ShowWeapon(transition.Weapon);
                return;
            }

            if ((transition.Type == QusapWeaponTransitionType.Dropped
                    || transition.Type == QusapWeaponTransitionType.Disarmed
                    || transition.Type == QusapWeaponTransitionType.VoluntarySwapThrow)
                && displayedWeapon == transition.Weapon)
            {
                ClearVisual();
            }
        }

        private void ShowWeapon(QusapWeaponInstance weapon)
        {
            if (weapon == null
                || !catalog.TryGetEntry(weapon.Definition.Id, out QusapWeaponVisualEntry entry))
            {
                ClearVisual();
                return;
            }

            if (displayedWeapon == weapon
                && equippedVisual != null
                && displayedVisualPrefab == entry.VisualPrefab)
            {
                return;
            }

            ClearVisual();
            equippedVisual = Instantiate(entry.VisualPrefab, weaponSocket);
            equippedVisual.name = $"Equipped_{entry.VisualPrefab.name}";
            equippedVisual.SetActive(true);
            displayedWeapon = weapon;
            displayedVisualPrefab = entry.VisualPrefab;
            ApplyVisualTransform();
        }

        private void ApplyVisualTransform()
        {
            if (equippedVisual == null)
            {
                return;
            }

            equippedVisual.transform.localPosition = Vector3.zero;
            equippedVisual.transform.localRotation = Quaternion.identity;
            equippedVisual.transform.localScale = visualScale;
        }

        private void ClearVisual()
        {
            displayedWeapon = null;
            displayedVisualPrefab = null;
            if (equippedVisual == null)
            {
                return;
            }

            GameObject visualToDestroy = equippedVisual;
            equippedVisual = null;
            visualToDestroy.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(visualToDestroy);
            }
            else
            {
                DestroyImmediate(visualToDestroy);
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }
    }
}
