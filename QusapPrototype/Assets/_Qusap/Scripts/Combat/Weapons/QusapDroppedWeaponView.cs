using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapDroppedWeaponView : MonoBehaviour
    {
        public const float DefaultDuration = 0.40f;
        public const float DefaultOutwardDistance = 0.55f;
        public const float DefaultFallDistance = 0.70f;
        public const float DefaultRestingTilt = 68f;

        private QusapWeaponInstance weapon;
        private GameObject visualPrefab;
        private GameObject visualInstance;
        private Vector3 startPosition;
        private Vector3 endPosition;
        private Quaternion startRotation;
        private Quaternion endRotation;
        private float duration;
        private float elapsed;
        private float planeZ;
        private bool initialized;

        public QusapWeaponInstance Weapon => weapon;
        public ulong InstanceId => weapon?.InstanceId ?? 0;
        public GameObject VisualPrefab => visualPrefab;
        public GameObject VisualInstance => visualInstance;
        public bool IsInitialized => initialized;
        public bool IsSettled => initialized && elapsed >= duration;
        public float PlaneZ => planeZ;

        private void Update()
        {
            if (initialized && !IsSettled)
            {
                Advance(Time.deltaTime);
            }
        }

        public bool Initialize(
            QusapWeaponInstance droppedWeapon,
            GameObject droppedVisualPrefab,
            Vector3 origin,
            int outwardDirection,
            float animationDuration = DefaultDuration,
            float outwardDistance = DefaultOutwardDistance,
            float fallDistance = DefaultFallDistance)
        {
            if (initialized
                || droppedWeapon == null
                || !droppedWeapon.IsFree
                || droppedVisualPrefab == null
                || !IsFinite(origin)
                || !float.IsFinite(animationDuration)
                || animationDuration <= 0f
                || !float.IsFinite(outwardDistance)
                || outwardDistance < 0f
                || !float.IsFinite(fallDistance)
                || fallDistance < 0f)
            {
                return false;
            }

            int direction = outwardDirection < 0 ? -1 : 1;
            weapon = droppedWeapon;
            visualPrefab = droppedVisualPrefab;
            duration = animationDuration;
            planeZ = origin.z;
            startPosition = origin;
            endPosition = origin + new Vector3(
                direction * outwardDistance,
                -fallDistance,
                0f);
            startRotation = Quaternion.identity;
            endRotation = Quaternion.Euler(0f, 0f, -DefaultRestingTilt * direction);
            elapsed = 0f;
            transform.SetPositionAndRotation(startPosition, startRotation);
            transform.localScale = Vector3.one;

            visualInstance = Instantiate(visualPrefab, transform);
            visualInstance.name = $"Dropped_{visualPrefab.name}_{weapon.InstanceId}";
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;
            visualInstance.SetActive(true);
            initialized = true;
            return true;
        }

        public void Advance(float deltaTime)
        {
            if (!initialized
                || IsSettled
                || !float.IsFinite(deltaTime)
                || deltaTime <= 0f)
            {
                return;
            }

            elapsed = Mathf.Min(elapsed + deltaTime, duration);
            float normalized = elapsed / duration;
            float eased = normalized * normalized * (3f - 2f * normalized);
            Vector3 position = Vector3.LerpUnclamped(startPosition, endPosition, eased);
            position.y += Mathf.Sin(normalized * Mathf.PI) * 0.18f;
            position.z = planeZ;
            transform.SetPositionAndRotation(
                position,
                Quaternion.SlerpUnclamped(startRotation, endRotation, eased));
        }

        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x)
                && float.IsFinite(value.y)
                && float.IsFinite(value.z);
        }
    }
}
