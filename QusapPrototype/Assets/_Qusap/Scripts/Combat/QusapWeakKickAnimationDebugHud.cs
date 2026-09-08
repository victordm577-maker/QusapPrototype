using System.Linq;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapWeakKickAnimationDebugHud : MonoBehaviour
    {
        private static readonly string[] StateNames =
        {
            "Qusap_Idle", "Qusap_Run", "Qusap_JumpRise", "Qusap_Fall", "Qusap_Land",
            "Qusap_WallSlide", "Qusap_WallJump", "Qusap_Dash",
            "Qusap_WeakKickGround", "Qusap_WeakKickAir",
            "Qusap_StrongKickGround", "Qusap_StrongKickAir"
        };

        [SerializeField] private QusapCombatController[] players;
        [SerializeField] private Vector2 screenPosition = new(420f, 16f);

        public void Configure(QusapCombatController[] combatPlayers)
        {
            players = combatPlayers;
        }

        private void OnGUI()
        {
            if (players == null || players.Length == 0)
                return;

            GUILayout.BeginArea(
                new Rect(screenPosition.x, screenPosition.y, 440f, 42f + players.Length * 116f),
                GUI.skin.box);
            GUILayout.Label("Qusap Weak Kick Animation");
            for (int index = 0; index < players.Length; index++)
            {
                QusapCombatController combat = players[index];
                if (combat == null)
                    continue;

                Animator animator = combat.transform.Find("PlayerVisual")?.GetComponent<Animator>();
                QusapCombatController target = players.Length > 1
                    ? players[(index + 1) % players.Length] : combat;
                QusapHitstunController hitstun = target != null
                    ? target.GetComponent<QusapHitstunController>() : null;
                bool combatAnimating = ReadBool(animator, "CombatAnimating");
                int attackVariant = ReadInt(animator, "AttackVariant");
                GUILayout.Label(
                    $"P{index + 1}  Animator: {CurrentState(animator)}\n" +
                    $"CombatAnimating: {combatAnimating}  AttackVariant: {attackVariant} " +
                    $"({combat.CurrentAttackVariant})\n" +
                    $"Phase: {combat.CurrentPhase}  Target hitstun: " +
                    $"{(hitstun != null && hitstun.IsInHitstun ? hitstun.TimeRemaining : 0f):0.000}s");
            }
            GUILayout.EndArea();
        }

        private static bool ReadBool(Animator animator, string parameter)
        {
            return IsReady(animator) && animator.parameters.Any(item =>
                    item.name == parameter && item.type == AnimatorControllerParameterType.Bool)
                && animator.GetBool(parameter);
        }

        private static int ReadInt(Animator animator, string parameter)
        {
            return IsReady(animator) && animator.parameters.Any(item =>
                    item.name == parameter && item.type == AnimatorControllerParameterType.Int)
                ? animator.GetInteger(parameter) : 0;
        }

        private static string CurrentState(Animator animator)
        {
            if (!IsReady(animator) || animator.layerCount == 0)
                return "No Animator";
            AnimatorStateInfo info = animator.IsInTransition(0)
                ? animator.GetNextAnimatorStateInfo(0)
                : animator.GetCurrentAnimatorStateInfo(0);
            return StateNames.FirstOrDefault(info.IsName) ?? $"hash {info.shortNameHash}";
        }

        private static bool IsReady(Animator animator)
        {
            return animator != null && animator.enabled && animator.gameObject.activeInHierarchy
                && animator.runtimeAnimatorController != null;
        }
    }
}
