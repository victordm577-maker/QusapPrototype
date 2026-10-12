using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(900)]
    public sealed class QusapDeathLootSafePosition : MonoBehaviour
    {
        private Rigidbody body;
        private QusapHitReceiver receiver;
        private int groundMask, solidMask;
        private float radius, planeZ;
        private bool configured;
        public Vector3? LastSafePosition { get; private set; }
        public void Configure(int ground, int solid, float clearance, float plane)
        {
            body = GetComponent<Rigidbody>(); receiver = GetComponent<QusapHitReceiver>();
            groundMask = ground; solidMask = solid; radius = clearance; planeZ = plane; configured = true; Capture();
        }
        public void Capture()
        {
            if (!configured || body == null || receiver == null || receiver.IsHealthDepleted || receiver.IsGameplayRetired) return;
            if (QusapDeathLootPlacementResolver.TryResolve(body.position, null, groundMask, solidMask, radius, planeZ, out _)) LastSafePosition = body.position;
        }
        private void FixedUpdate() => Capture();
    }
}
