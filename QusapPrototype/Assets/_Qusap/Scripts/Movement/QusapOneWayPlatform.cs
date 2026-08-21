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
        [SerializeField] private float standingTolerance = 0.12f;

        private readonly Dictionary<Rigidbody, PlayerState> trackedPlayers = new();
        private BoxCollider detectionTrigger;

        public Collider SolidCollider => solidCollider;

        private sealed class PlayerState
        {
            public Rigidbody Rigidbody;
            public CapsuleCollider CapsuleCollider;
            public QusapInputReader InputReader;
            public bool CollisionIgnored;
            public bool DroppingDown;
            public bool InsideTrigger;
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
            standingTolerance = Mathf.Max(standingTolerance, 0f);
        }

        private void FixedUpdate()
        {
            if (solidCollider == null)
            {
                return;
            }

            float platformTop = solidCollider.bounds.max.y;
            List<Rigidbody> playersToRemove = null;

            foreach (KeyValuePair<Rigidbody, PlayerState> entry in trackedPlayers)
            {
                PlayerState player = entry.Value;

                if (player.Rigidbody == null || player.CapsuleCollider == null)
                {
                    SetCollisionIgnored(player, false);
                    playersToRemove ??= new List<Rigidbody>();
                    playersToRemove.Add(entry.Key);
                    continue;
                }

                SyncCollisionIgnored(player);

                float playerBottom = player.CapsuleCollider.bounds.min.y;
                float playerTop = player.CapsuleCollider.bounds.max.y;

                if (player.DroppingDown)
                {
                    SetCollisionIgnored(player, true);

                    if (playerTop < platformTop - exitTolerance)
                    {
                        player.DroppingDown = false;
                    }
                }

                if (!player.DroppingDown)
                {
                    bool isStanding = Mathf.Abs(playerBottom - platformTop) <= standingTolerance
                        && Mathf.Abs(player.Rigidbody.linearVelocity.y) <= 0.3f
                        && !player.CollisionIgnored;

                    if (isStanding
                        && player.InputReader != null
                        && player.InputReader.ConsumeDropPressed())
                    {
                        BeginDrop(player);
                    }
                    else if (!player.CollisionIgnored
                        && playerBottom < platformTop - enterTolerance
                        && player.Rigidbody.linearVelocity.y > 0f)
                    {
                        SetCollisionIgnored(player, true);
                    }
                    else if (player.CollisionIgnored
                        && playerBottom >= platformTop + exitTolerance)
                    {
                        SetCollisionIgnored(player, false);
                    }
                }

                if (!player.DroppingDown && !player.InsideTrigger)
                {
                    SetCollisionIgnored(player, false);
                    playersToRemove ??= new List<Rigidbody>();
                    playersToRemove.Add(entry.Key);
                }
            }

            if (playersToRemove == null)
            {
                return;
            }

            foreach (Rigidbody characterBody in playersToRemove)
            {
                trackedPlayers.Remove(characterBody);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            RegisterOrRefreshPlayer(other);
        }

        private void OnTriggerStay(Collider other)
        {
            RegisterOrRefreshPlayer(other);
        }

        private void OnTriggerExit(Collider other)
        {
            Rigidbody characterBody = other.attachedRigidbody;

            if (characterBody == null
                || !trackedPlayers.TryGetValue(characterBody, out PlayerState player))
            {
                return;
            }

            player.InsideTrigger = false;

            if (player.DroppingDown)
            {
                return;
            }

            SetCollisionIgnored(player, false);
            trackedPlayers.Remove(characterBody);
        }

        private void OnDisable()
        {
            RestoreAllCollisions();
        }

        private void OnDestroy()
        {
            RestoreAllCollisions();
        }

        private void RegisterOrRefreshPlayer(Collider other)
        {
            Rigidbody characterBody = other.attachedRigidbody;

            if (characterBody == null)
            {
                return;
            }

            if (trackedPlayers.TryGetValue(characterBody, out PlayerState player))
            {
                player.InsideTrigger = true;
                return;
            }

            CapsuleCollider capsuleCollider = characterBody.GetComponent<CapsuleCollider>();

            if (capsuleCollider == null)
            {
                return;
            }

            player = new PlayerState
            {
                Rigidbody = characterBody,
                CapsuleCollider = capsuleCollider,
                InputReader = characterBody.GetComponent<QusapInputReader>(),
                InsideTrigger = true
            };

            trackedPlayers.Add(characterBody, player);

            float platformTop = solidCollider.bounds.max.y;
            float playerBottom = capsuleCollider.bounds.min.y;

            if (playerBottom < platformTop - enterTolerance
                && characterBody.linearVelocity.y > 0f)
            {
                SetCollisionIgnored(player, true);
            }
        }

        private void BeginDrop(PlayerState player)
        {
            player.DroppingDown = true;
            SetCollisionIgnored(player, true);

            Vector3 velocity = player.Rigidbody.linearVelocity;
            velocity.y = Mathf.Min(velocity.y, -2f);
            velocity.z = 0f;
            player.Rigidbody.linearVelocity = velocity;
            player.Rigidbody.WakeUp();
        }

        private void SetCollisionIgnored(PlayerState player, bool ignored)
        {
            if (player.CapsuleCollider == null || solidCollider == null)
            {
                player.CollisionIgnored = false;
                return;
            }

            Physics.IgnoreCollision(player.CapsuleCollider, solidCollider, ignored);
            player.CollisionIgnored = Physics.GetIgnoreCollision(
                player.CapsuleCollider,
                solidCollider);
        }

        private void SyncCollisionIgnored(PlayerState player)
        {
            if (player.CapsuleCollider == null || solidCollider == null)
            {
                player.CollisionIgnored = false;
                return;
            }

            player.CollisionIgnored = Physics.GetIgnoreCollision(
                player.CapsuleCollider,
                solidCollider);
        }

        private void RestoreAllCollisions()
        {
            foreach (PlayerState player in trackedPlayers.Values)
            {
                SetCollisionIgnored(player, false);
            }

            trackedPlayers.Clear();
        }
    }
}
