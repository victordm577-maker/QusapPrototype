using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(BoxCollider))]
    public class QusapOneWayPlatform : MonoBehaviour
    {
        [SerializeField] private BoxCollider solidCollider;
        [SerializeField] private float enterTolerance = 0.08f;
        [SerializeField] private float exitTolerance = 0.12f;

        private readonly Dictionary<Rigidbody, TrackedCharacter> trackedCharacters = new();
        private BoxCollider detectionTrigger;

        private sealed class TrackedCharacter
        {
            public CapsuleCollider CapsuleCollider;
            public int OverlapCount;
            public bool IsCollisionIgnored;
        }

        private void Awake()
        {
            detectionTrigger = GetComponent<BoxCollider>();

            if (detectionTrigger == null)
            {
                Debug.LogError(
                    "QusapOneWayPlatform necesita un BoxCollider en PassThroughSensor para detectar personajes.",
                    this);
                enabled = false;
                return;
            }

            if (!detectionTrigger.isTrigger)
            {
                Debug.LogError(
                    "El BoxCollider de PassThroughSensor debe tener Is Trigger activado.",
                    this);
                enabled = false;
                return;
            }

            if (solidCollider == null)
            {
                Debug.LogError(
                    "QusapOneWayPlatform necesita que Solid Collider esté asignado en el Inspector.",
                    this);
                enabled = false;
                return;
            }

            if (solidCollider.isTrigger)
            {
                Debug.LogError(
                    "Solid Collider debe ser un collider sólido y no puede tener Is Trigger activado.",
                    this);
                enabled = false;
            }
        }

        private void OnValidate()
        {
            enterTolerance = Mathf.Max(enterTolerance, 0f);
            exitTolerance = Mathf.Max(exitTolerance, enterTolerance);
        }

        private void FixedUpdate()
        {
            if (solidCollider == null)
            {
                return;
            }

            float platformTop = solidCollider.bounds.max.y;

            foreach (TrackedCharacter character in trackedCharacters.Values)
            {
                if (character.CapsuleCollider == null)
                {
                    continue;
                }

                float characterBottom = character.CapsuleCollider.bounds.min.y;

                if (!character.IsCollisionIgnored
                    && characterBottom < platformTop - enterTolerance)
                {
                    SetCollisionIgnored(character, true);
                }
                else if (character.IsCollisionIgnored
                    && characterBottom >= platformTop + exitTolerance)
                {
                    SetCollisionIgnored(character, false);
                }
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Rigidbody characterBody = other.attachedRigidbody;

            if (characterBody == null)
            {
                return;
            }

            if (trackedCharacters.TryGetValue(characterBody, out TrackedCharacter character))
            {
                character.OverlapCount++;
                return;
            }

            CapsuleCollider capsuleCollider = characterBody.GetComponent<CapsuleCollider>();

            if (capsuleCollider == null)
            {
                return;
            }

            character = new TrackedCharacter
            {
                CapsuleCollider = capsuleCollider,
                OverlapCount = 1
            };

            trackedCharacters.Add(characterBody, character);
            UpdateCollisionStateImmediately(character);
        }

        private void OnTriggerExit(Collider other)
        {
            Rigidbody characterBody = other.attachedRigidbody;

            if (characterBody == null
                || !trackedCharacters.TryGetValue(characterBody, out TrackedCharacter character))
            {
                return;
            }

            character.OverlapCount--;

            if (character.OverlapCount > 0)
            {
                return;
            }

            SetCollisionIgnored(character, false);
            trackedCharacters.Remove(characterBody);
        }

        private void OnDisable()
        {
            RestoreAllCollisions();
        }

        private void OnDestroy()
        {
            RestoreAllCollisions();
        }

        private void UpdateCollisionStateImmediately(TrackedCharacter character)
        {
            if (solidCollider == null || character.CapsuleCollider == null)
            {
                return;
            }

            float platformTop = solidCollider.bounds.max.y;
            float characterBottom = character.CapsuleCollider.bounds.min.y;

            if (characterBottom < platformTop - enterTolerance)
            {
                SetCollisionIgnored(character, true);
            }
        }

        private void SetCollisionIgnored(TrackedCharacter character, bool shouldIgnore)
        {
            if (character.IsCollisionIgnored == shouldIgnore
                || character.CapsuleCollider == null
                || solidCollider == null)
            {
                return;
            }

            Physics.IgnoreCollision(character.CapsuleCollider, solidCollider, shouldIgnore);
            character.IsCollisionIgnored = shouldIgnore;
        }

        private void RestoreAllCollisions()
        {
            foreach (TrackedCharacter character in trackedCharacters.Values)
            {
                SetCollisionIgnored(character, false);
            }

            trackedCharacters.Clear();
        }
    }
}
