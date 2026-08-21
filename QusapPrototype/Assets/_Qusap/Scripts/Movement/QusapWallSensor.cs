using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(CapsuleCollider))]
    public class QusapWallSensor : MonoBehaviour
    {
        [SerializeField] private float wallNormalThreshold = 0.7f;

        private readonly Dictionary<Collider, int> wallContacts = new();

        public bool IsTouchingWall => wallContacts.Count > 0;
        public int WallSide { get; private set; }
        public Collider CurrentWallCollider { get; private set; }

        private void OnValidate()
        {
            wallNormalThreshold = Mathf.Clamp01(wallNormalThreshold);
        }

        private void OnDisable()
        {
            wallContacts.Clear();
            WallSide = 0;
            CurrentWallCollider = null;
        }

        private void OnCollisionStay(Collision collision)
        {
            if (IsOneWaySolid(collision.collider))
            {
                wallContacts.Remove(collision.collider);
                RefreshWallSide();
                return;
            }

            float strongestHorizontalNormal = 0f;
            int detectedWallSide = 0;

            foreach (ContactPoint contact in collision.contacts)
            {
                float horizontalNormal = Mathf.Abs(contact.normal.x);

                if (horizontalNormal < wallNormalThreshold || horizontalNormal <= strongestHorizontalNormal)
                {
                    continue;
                }

                strongestHorizontalNormal = horizontalNormal;
                detectedWallSide = contact.normal.x < 0f ? 1 : -1;
            }

            if (detectedWallSide == 0)
            {
                wallContacts.Remove(collision.collider);
            }
            else
            {
                wallContacts[collision.collider] = detectedWallSide;
            }

            RefreshWallSide();
        }

        private void OnCollisionExit(Collision collision)
        {
            wallContacts.Remove(collision.collider);
            RefreshWallSide();
        }

        private void RefreshWallSide()
        {
            WallSide = 0;
            CurrentWallCollider = null;

            foreach (KeyValuePair<Collider, int> contact in wallContacts)
            {
                if (contact.Key == null)
                {
                    continue;
                }

                CurrentWallCollider = contact.Key;
                WallSide = contact.Value;
                break;
            }
        }

        private static bool IsOneWaySolid(Collider candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            QusapOneWayPlatform oneWayPlatform =
                candidate.GetComponentInChildren<QusapOneWayPlatform>(true);

            return oneWayPlatform != null
                && oneWayPlatform.SolidCollider == candidate;
        }
    }
}
