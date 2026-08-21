using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(-500)]
    [DisallowMultipleComponent]
    public sealed class QusapCombatArenaController : MonoBehaviour
    {
        [Header("Players")]
        [SerializeField] private QusapInputReader playerOne;
        [SerializeField] private QusapInputReader playerTwo;
        [SerializeField] private Vector3 playerOneStartPosition = new(-5f, 1.1f, 0f);
        [SerializeField] private Vector3 playerTwoStartPosition = new(5f, 1.1f, 0f);

        [Header("Provisional Body Contact")]
        [SerializeField] private bool ignorePhysicalCollisionBetweenPlayers = true;

        private readonly List<ColliderPair> ignoredColliderPairs = new();

        public QusapInputReader PlayerOne => playerOne;
        public QusapInputReader PlayerTwo => playerTwo;
        public Vector3 PlayerOneStartPosition => playerOneStartPosition;
        public Vector3 PlayerTwoStartPosition => playerTwoStartPosition;

        private void Awake()
        {
            PlacePlayer(playerOne, playerOneStartPosition);
            PlacePlayer(playerTwo, playerTwoStartPosition);
        }

        private void Start()
        {
            if (ignorePhysicalCollisionBetweenPlayers)
            {
                IgnorePlayerBodyCollisions();
            }
        }

        private void OnDisable()
        {
            RestorePlayerBodyCollisions();
        }

        public void Configure(
            QusapInputReader firstPlayer,
            QusapInputReader secondPlayer,
            Vector3 firstStartPosition,
            Vector3 secondStartPosition,
            bool ignorePlayerCollision)
        {
            playerOne = firstPlayer;
            playerTwo = secondPlayer;
            playerOneStartPosition = firstStartPosition;
            playerTwoStartPosition = secondStartPosition;
            ignorePhysicalCollisionBetweenPlayers = ignorePlayerCollision;
        }

        private static void PlacePlayer(QusapInputReader player, Vector3 position)
        {
            if (player == null)
            {
                return;
            }

            player.transform.SetPositionAndRotation(position, Quaternion.identity);
            Rigidbody playerBody = player.GetComponent<Rigidbody>();
            if (playerBody != null)
            {
                playerBody.position = position;
                playerBody.rotation = Quaternion.identity;
            }
        }

        private void IgnorePlayerBodyCollisions()
        {
            RestorePlayerBodyCollisions();
            if (playerOne == null || playerTwo == null)
            {
                return;
            }

            Collider[] playerOneColliders = playerOne.GetComponentsInChildren<Collider>(true);
            Collider[] playerTwoColliders = playerTwo.GetComponentsInChildren<Collider>(true);

            foreach (Collider firstCollider in playerOneColliders)
            {
                if (!IsPhysicalBodyCollider(firstCollider))
                {
                    continue;
                }

                foreach (Collider secondCollider in playerTwoColliders)
                {
                    if (!IsPhysicalBodyCollider(secondCollider))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(firstCollider, secondCollider, true);
                    ignoredColliderPairs.Add(new ColliderPair(firstCollider, secondCollider));
                }
            }
        }

        private void RestorePlayerBodyCollisions()
        {
            foreach (ColliderPair pair in ignoredColliderPairs)
            {
                if (pair.First != null && pair.Second != null)
                {
                    Physics.IgnoreCollision(pair.First, pair.Second, false);
                }
            }

            ignoredColliderPairs.Clear();
        }

        private static bool IsPhysicalBodyCollider(Collider targetCollider)
        {
            return targetCollider != null && targetCollider.enabled && !targetCollider.isTrigger;
        }

        private readonly struct ColliderPair
        {
            public ColliderPair(Collider first, Collider second)
            {
                First = first;
                Second = second;
            }

            public Collider First { get; }
            public Collider Second { get; }
        }
    }
}
