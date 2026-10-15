using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Qusap
{
    // Scene-only observation. Never sends input, arms finishers or changes eligibility/timing.
    [DefaultExecutionOrder(1200)]
    public sealed class QusapRaidIntegratedParryDiagnostic : MonoBehaviour
    {
        public sealed class PlayerObservation
        {
            public QusapRaidInventory Actor { get; }
            public QusapCombatController Combat { get; }
            public QusapInputReader Input { get; }
            public InputAction Action { get; }
            public Gamepad PairedPad { get; }
            public Gamepad ObservedPad => PairedPad ?? (Input.LocalPlayerSlot == QusapLocalPlayerSlot.Player2Gamepad && Gamepad.all.Count > 0 ? Gamepad.all[0] : null);
            public int TriggerPresses { get; private set; }
            public int TriggerReleases { get; private set; }
            public int ActionPerformed { get; private set; }
            public int CommandsForwarded { get; private set; }
            public int AttemptsAccepted { get; private set; }
            public int AttemptsReported { get; private set; }
            public int Successes { get; private set; }
            public int Failures { get; private set; }
            public string LastOutcome { get; private set; } = "Sin intento";
            public string LastResolution { get; private set; } = "Sin resolución";
            public float TriggerValue => ObservedPad?.leftTrigger.ReadValue() ?? 0;
            public float PressPoint => ObservedPad?.leftTrigger.pressPointOrDefault ?? InputSystem.settings.defaultButtonPressPoint;
            public bool TriggerHeld => ObservedPad?.leftTrigger.isPressed == true;
            public double LastPressTimestamp { get; private set; }

            internal PlayerObservation(QusapRaidInventory actor)
            {
                Actor = actor; Combat = actor.GetComponent<QusapCombatController>(); Input = actor.GetComponent<QusapInputReader>();
                // Same instance identity used by QusapInputReader for its private runtime clone.
                string suffix = "_" + Input.LocalPlayerSlot + "_" + Input.GetEntityId();
                var actions = Resources.FindObjectsOfTypeAll<InputActionAsset>().SingleOrDefault(a => a.name.EndsWith(suffix, StringComparison.Ordinal));
                Action = actions?.FindAction("Gameplay/Parry");
                PairedPad = actions?.devices?.OfType<Gamepad>().FirstOrDefault();
                if (Action != null) Action.performed += Performed;
                Input.ParryPressed += Forwarded;
                Combat.ParryAttemptAccepted += Accepted;
                Combat.ParryAttemptFinished += Finished;
                Combat.ParrySucceeded += Succeeded;
                Combat.ParryFailed += Failed;
            }
            private void Performed(InputAction.CallbackContext context) { ActionPerformed++; LastPressTimestamp = context.time; }
            private void Forwarded(QusapCombatCommandPress press) { CommandsForwarded++; LastPressTimestamp = press.Timestamp; }
            private void Accepted(QusapCombatCommandPress press) => AttemptsAccepted++;
            private void Finished(QusapParryAttemptFeedback result) { AttemptsReported++; LastOutcome = Explain(result.Outcome); }
            private void Succeeded(QusapCombatController attacker, QusapComboId combo) { Successes++; LastResolution = "Éxito resuelto · " + combo; }
            private void Failed(QusapParryAttemptOutcome outcome) { Failures++; LastResolution = "Fallo resuelto · " + Explain(outcome); }
            internal void SampleTrigger()
            {
                if (ObservedPad == null) return;
                if (ObservedPad.leftTrigger.wasPressedThisFrame) TriggerPresses++;
                if (ObservedPad.leftTrigger.wasReleasedThisFrame) TriggerReleases++;
            }
            internal void Unbind()
            {
                if (Action != null) Action.performed -= Performed;
                if (Input != null) Input.ParryPressed -= Forwarded;
                if (Combat == null) return;
                Combat.ParryAttemptAccepted -= Accepted; Combat.ParryAttemptFinished -= Finished;
                Combat.ParrySucceeded -= Succeeded; Combat.ParryFailed -= Failed;
            }
            public string CurrentBlock
            {
                get
                {
                    if (!Input.isActiveAndEnabled) return "Lector desactivado / participante retirado";
                    if (Input.LocalPlayerSlot == QusapLocalPlayerSlot.Player2Gamepad && PairedPad == null) return "Gamepad NO emparejado: conectar antes de Play y reiniciar Play";
                    if (Actor.GetComponent<QusapHitstunController>().IsInHitstun) return "Hitstun: no elegible";
                    if (Input.IsGameplayInputBlocked) return "Entrada bloqueada (preparación o retiro)";
                    if (!Combat.isActiveAndEnabled || !Combat.CombatAllowed) return "Combate desactivado";
                    if (Combat.IsAttacking) return "Ataque propio: no elegible";
                    if (Actor.GetComponent<QusapDashMotor>().IsDashing) return "Dash: no elegible";
                    if (Combat.IsParryAttemptRecovering) return "Recuperación (cada nueva pulsación procesada la reinicia)";
                    return "Sin bloqueo de estado; todavía requiere finalizador y oportunidad válida";
                }
            }
        }

        public PlayerObservation[] Players { get; private set; } = Array.Empty<PlayerObservation>();
        public bool Visible { get; set; }
        private GUIStyle text, title;
        public void Bind(QusapRaidInventory[] actors)
        {
            foreach (var player in Players) player.Unbind();
            Players = actors.Select(a => new PlayerObservation(a)).ToArray();
        }
        private void Update()
        {
            if (Keyboard.current?.f8Key.wasPressedThisFrame == true) Visible = !Visible;
            foreach (var player in Players) player.SampleTrigger();
        }
        public static string Explain(QusapParryAttemptOutcome outcome) => outcome switch
        {
            QusapParryAttemptOutcome.Success => "Aceptado dentro de ventana; éxito",
            QusapParryAttemptOutcome.NoIncomingFinisher => "Sin finalizador entrante; golpes normales no admiten parry",
            QusapParryAttemptOutcome.TooEarly => "Demasiado pronto; oportunidad del finalizador consumida",
            QusapParryAttemptOutcome.TooLate => "Demasiado tarde",
            QusapParryAttemptOutcome.OnRecovery => "Rechazado: recuperación de 0,50 s desde la última pulsación",
            QusapParryAttemptOutcome.AlreadyAttempted => "Rechazado: ya se intentó contra este finalizador",
            QusapParryAttemptOutcome.Ineligible => "Rechazado: ataque propio, dash, hitstun o combate desactivado",
            QusapParryAttemptOutcome.DuplicateOrStalePressIgnored => "Pulsación duplicada o antigua ignorada",
            QusapParryAttemptOutcome.InvalidTimestampIgnored => "Timestamp inválido ignorado",
            _ => "Sin resultado"
        };
        public string IncomingDescription(int index)
        {
            var defender = Players[index]; var attacker = Players[1 - index].Combat;
            if (!attacker.HasArmedFinisher || attacker.ArmedFinisherTarget != defender.Actor.Receiver)
                return attacker.IsAttacking ? "Golpe normal " + attacker.CurrentAttackVariant + " · NO elegible para parry" : "Sin ataque elegible entrante";
            bool available = defender.Combat.TryGetCurrentParryCue(out _);
            bool insideWindow = InputState.currentTime >= attacker.ParryWindowOpensAt && InputState.currentTime <= attacker.ParryWindowClosesAt;
            return "Finalizador " + attacker.ArmedFinisherCombo + " · " + attacker.FinisherDefensePhase
                + " · ventana temporal " + (insideWindow ? "ABIERTA" : "cerrada")
                + " · intento disponible " + (available ? "SÍ" : "NO")
                + "\nVentana InputState " + attacker.ParryWindowOpensAt.ToString("F3") + "–" + attacker.ParryWindowClosesAt.ToString("F3")
                + " · ahora " + InputState.currentTime.ToString("F3");
        }
        private void OnGUI()
        {
            if (!Visible || Players.Length != 2) return;
            text ??= new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true };
            title ??= new GUIStyle(text) { fontStyle = FontStyle.Bold, fontSize = 17 };
            var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new(Screen.width / 1280f, Screen.height / 720f, 1));
            var previousColor = GUI.color; GUI.color = new(.02f, .03f, .04f, 1);
            GUI.DrawTexture(new(12, 110, 845, 435), Texture2D.whiteTexture); GUI.color = previousColor;
            GUI.Box(new(12, 110, 845, 435), "");
            GUI.Label(new(24, 119, 820, 28), "DIAGNÓSTICO PARRY · F8 cerrar · sólo finalizadores, no bloqueo de golpes normales", title);
            for (int i = 0; i < Players.Length; i++)
            {
                var p = Players[i]; float y = 154 + i * 185;
                string device = (p.ObservedPad == null ? "sin gamepad" : p.ObservedPad.displayName + " #" + p.ObservedPad.deviceId)
                    + " · emparejado " + (p.PairedPad != null ? "SÍ" : "NO");
                GUI.Label(new(24, y, 820, 35), "P" + (i + 1) + " · " + device + " · LT " + p.TriggerValue.ToString("F2") + " / umbral " + p.PressPoint.ToString("F2")
                    + " · mantenido " + p.TriggerHeld + " · pulsaciones/liberaciones " + p.TriggerPresses + "/" + p.TriggerReleases, text);
                GUI.Label(new(24, y + 31, 820, 25), "Acción performed " + p.ActionPerformed + " · comando recibido " + p.CommandsForwarded
                    + " · aceptaciones " + p.AttemptsAccepted + " · resultados " + p.AttemptsReported + " · timestamp LT " + p.LastPressTimestamp.ToString("F3"), text);
                GUI.Label(new(24, y + 55, 820, 52), "Estado actual: " + p.CurrentBlock + "\nÚltimo resultado: " + p.LastOutcome, text);
                GUI.Label(new(24, y + 111, 820, 42), IncomingDescription(i), text);
                GUI.Label(new(24, y + 156, 820, 24), p.LastResolution + " · éxitos/fallos " + p.Successes + "/" + p.Failures, text);
            }
            GUI.matrix = previous;
        }
        private void OnDestroy() { foreach (var player in Players) player.Unbind(); }
    }
}
