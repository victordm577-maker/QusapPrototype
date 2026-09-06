using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapAirCombatDebugHud : MonoBehaviour
    {
        [SerializeField] private QusapCombatController[] players;
        [SerializeField] private Vector2 screenPosition = new(16f, 16f);

        public void Configure(QusapCombatController[] combatPlayers)
        {
            players = combatPlayers;
        }

        private void OnGUI()
        {
            if (players == null || players.Length == 0)
            {
                return;
            }

            float height = 26f + players.Length * 92f;
            GUILayout.BeginArea(new Rect(screenPosition.x, screenPosition.y, 390f, height), GUI.skin.box);
            GUILayout.Label("Qusap Air Combat Mechanics");

            for (int index = 0; index < players.Length; index++)
            {
                QusapCombatController combat = players[index];
                if (combat == null)
                {
                    continue;
                }

                QusapGroundSensor ground = combat.GetComponent<QusapGroundSensor>();
                QusapDashMotor dash = combat.GetComponent<QusapDashMotor>();
                GUILayout.Label(
                    $"P{index + 1}  Grounded: {ground != null && ground.IsGrounded}  " +
                    $"Dashing: {dash != null && dash.IsDashing}\n" +
                    $"Attack: {combat.CurrentAttackVariant}  Phase: {combat.CurrentPhase}\n" +
                    $"Dive: {(combat.DiveHeadbuttAvailable ? "Available" : "Consumed")}");
            }

            GUILayout.EndArea();
        }
    }
}
