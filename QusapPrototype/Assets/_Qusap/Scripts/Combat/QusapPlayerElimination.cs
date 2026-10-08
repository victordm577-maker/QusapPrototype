using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    // Terminal player blocking only; live movement/physics and inventory are unchanged.
    [DisallowMultipleComponent]
    public sealed class QusapPlayerElimination : MonoBehaviour
    {
        private readonly List<(Behaviour component, bool enabled)> behaviours = new();
        private readonly List<(Collider collider, bool enabled)> colliders = new();
        private Rigidbody body;
        private bool wasKinematic, detectedCollisions, combatWasAllowed;
        private bool applied;

        public void Apply()
        {
            if (applied) return;
            applied = true;
            var combat = GetComponent<QusapCombatController>();
            combatWasAllowed = combat == null || combat.CombatAllowed;
            GetComponent<QusapHitstunController>()?.ResetHitstun();
            GetComponent<QusapDashMotor>()?.ResetDashState();
            if (combat != null) combat.CombatAllowed = false;
            GetComponent<QusapInputReader>()?.SetExternalGameplayInputBlocked(true);

            Block(GetComponent<QusapCombatController>()); // OnDisable resets parry gate too.
            Block(GetComponent<QusapHitstunController>());
            Block(GetComponent<QusapHorizontalMotor>());
            Block(GetComponent<QusapVerticalMotor>());
            Block(GetComponent<QusapDashMotor>());
            Block(GetComponent<QusapInputReader>());
            Block(GetComponent<QusapRespawnController>());
            Block(GetComponent<QusapWeaponEquipment>()); // Retain inventory; no posthumous pickup.

            foreach (var collider in GetComponentsInChildren<Collider>(true))
            {
                colliders.Add((collider, collider.enabled));
                collider.enabled = false;
            }
            body = GetComponent<Rigidbody>();
            if (body != null)
            {
                wasKinematic = body.isKinematic;
                detectedCollisions = body.detectCollisions;
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.detectCollisions = false;
                body.isKinematic = true;
            }
        }

        private void Block(Behaviour component)
        {
            if (component == null) return;
            behaviours.Add((component, component.enabled));
            component.enabled = false;
        }

        public void RestoreForNewSession()
        {
            if (!applied) return;
            if (body != null)
            {
                body.isKinematic = wasKinematic;
                body.detectCollisions = detectedCollisions;
            }
            foreach (var entry in colliders)
                if (entry.collider != null) entry.collider.enabled = entry.enabled;
            GetComponent<QusapInputReader>()?.SetExternalGameplayInputBlocked(false);
            var combat = GetComponent<QusapCombatController>();
            if (combat != null) combat.CombatAllowed = combatWasAllowed;
            foreach (var entry in behaviours)
                if (entry.component != null) entry.component.enabled = entry.enabled;
            behaviours.Clear();
            colliders.Clear();
            applied = false;
        }
    }
}
