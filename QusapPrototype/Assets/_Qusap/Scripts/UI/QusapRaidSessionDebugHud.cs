using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Qusap
{
    [DefaultExecutionOrder(900)]
    public sealed class QusapRaidSessionDebugHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidSessionObserver session;
        private Text header, summary, action;
        private Text[] players;
        public Canvas Canvas { get; private set; }
        public string DiagnosticAction { get; set; } = "ENTER: iniciar sesión. A/D: P1. Gamepad: P2. Zona verde: extracción de 5 s.";
        public void Configure(QusapRaidSessionObserver observer) => session = observer;
        private void Start()
        {
            var root = new GameObject("Session_UGUI", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false); Canvas = root.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            header = Label(root.transform, "Session", new Vector2(0.5f, 1), new Vector2(0, -10), new Vector2(560, 125), 20);
            players = session.Raid.Participants.Select((p, i) => Label(root.transform, p.ParticipantId,
                new Vector2(i == 0 ? 0 : 1, 1), new Vector2(i == 0 ? 10 : -10, -10), new Vector2(335, 220), 18)).ToArray();
            summary = Label(root.transform, "FinalResult", new Vector2(0.5f, 0), new Vector2(0, 85), new Vector2(640, 145), 20);
            action = Label(root.transform, "Action", new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(1200, 65), 21);
        }
        private static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image)); box.transform.SetParent(parent, false);
            var rect = box.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset; rect.sizeDelta = size; box.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.93f);
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(box.transform, false);
            var textRect = obj.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(10, 8); textRect.offsetMax = new Vector2(-10, -8);
            var text = obj.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.color = Color.white; return text;
        }
        private void Update()
        {
            if (header == null || session.Authority == null) return;
            var model = session.Authority;
            if (Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame) session.StartSession();
            header.text = $"SessionId: {model.SessionId}\nSesión: {model.Status} | Registrados: {model.ParticipantCount}/{model.ParticipantLimit}\n"
                + $"Active: {model.ActiveCount} | Extracted: {model.ExtractedCount} | Eliminated: {model.EliminatedCount}\n"
                + $"SessionFinished: {model.SessionFinishedEvents} | Rechazos: {model.RejectedSignals}";
            for (int i = 0; i < players.Length; i++)
            {
                var player = session.Raid.Participants[i]; model.TryGetOutcome(player.ParticipantId, out var outcome);
                var extraction = player.GetComponent<QusapRaidExtraction>();
                var inventory = player.ReadInventorySnapshot();
                players[i].text = $"{player.ParticipantId}: {outcome} | Vida: {player.Receiver.CurrentHealth:0}\n"
                    + $"Extracción: {extraction.Status}\nRestante: {extraction.Countdown.Remaining:0.00} s\n"
                    + $"Mochila: {inventory.Backpack.Count(x => x.HasValue)} | Bolsillo: {(inventory.SecurePocket.HasValue ? 1 : 0)}\n"
                    + $"Stash: {player.ReadStashSnapshot().Count} | Transferencias: {extraction.Countdown.TransfersResolved}\n"
                    + $"Contenedores: {session.Raid.Session.Containers.Count(c => c.ParticipantId == player.ParticipantId)}";
            }
            var result = model.FinalResult;
            summary.text = result == null ? $"Resultado final pendiente\nÚltimo rechazo: {model.LastRejection}"
                : $"RESULTADO FINAL INMUTABLE ({result.ParticipantCount})\n"
                    + string.Join(" | ", result.Participants.Select(p => p.ParticipantId + ": " + p.Outcome))
                    + $"\nExtraídos: {result.ExtractedCount} | Eliminados: {result.EliminatedCount}\n"
                    + $"Motivo: {result.Reason}\nÚltimo rechazo: {model.LastRejection}";
            action.text = DiagnosticAction;
        }
    }
}
