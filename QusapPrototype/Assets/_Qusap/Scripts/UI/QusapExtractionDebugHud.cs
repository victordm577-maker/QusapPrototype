using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    [DefaultExecutionOrder(900)]
    public sealed class QusapExtractionDebugHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        private Text[] panels;
        public Canvas Canvas { get; private set; }
        public string DiagnosticAction { get; set; } = "Zona verde: permanecer 5 s. A/D: P1. Gamepad: P2.";
        private Text action;
        public void Configure(QusapRaidBootstrap bootstrap) => raid = bootstrap;
        private void Start()
        {
            var obj = new GameObject("Extraction_UGUI", typeof(Canvas), typeof(CanvasScaler));
            obj.transform.SetParent(transform, false); Canvas = obj.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = obj.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            panels = raid.Participants.Select((p, i) => Label(obj.transform, p.ParticipantId,
                new Vector2(i == 0 ? 0 : 1, 1), new Vector2(i == 0 ? 12 : -12, -12), new Vector2(340, 340), 17)).ToArray();
            action = Label(obj.transform, "Action", new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1000, 90), 22);
        }
        private static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image)); box.transform.SetParent(parent, false);
            var rect = box.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset; rect.sizeDelta = size; box.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.93f);
            var obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(box.transform, false);
            var tr = obj.GetComponent<RectTransform>(); tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10, 8); tr.offsetMax = new Vector2(-10, -8);
            var text = obj.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.color = Color.white; text.horizontalOverflow = HorizontalWrapMode.Wrap; return text;
        }
        public string PlayerLabel(int index) => panels[index].text;
        private static string Item(QusapLootSnapshot? item) => item.HasValue ? item.Value.DisplayName + " [" + item.Value.LootInstanceId.Split('/').Last() + "]" : "vacío";
        private void Update()
        {
            if (panels == null) return;
            for (int i = 0; i < panels.Length; i++)
            {
                var player = raid.Participants[i]; var inventory = player.ReadInventorySnapshot();
                var extraction = player.GetComponent<QusapRaidExtraction>(); var timer = extraction.Countdown;
                panels[i].text = $"{player.ParticipantId}: {extraction.Status} | Vida {player.Receiver.CurrentHealth:0}\n"
                    + $"Progreso {timer.Progress:P0} | Restante {timer.Remaining:0.00} s\nÚltima cancelación: {timer.Cancellation}\n"
                    + $"Mochila: {string.Join(", ", inventory.Backpack.Where(x => x.HasValue).Select(Item))}\n"
                    + $"Bolsillo: {Item(inventory.SecurePocket)}\nArma: {Item(inventory.EquippedWeapon)}\n"
                    + $"Stash: {string.Join(", ", player.ReadStashSnapshot().Select(x => Item(x)))}\n"
                    + $"Transferencias: {timer.TransfersResolved}\nContenedores: {raid.Session.Containers.Count(c => c.ParticipantId == player.ParticipantId)}\n"
                    + $"Rechazo: {extraction.LastRejection}";
            }
            action.text = DiagnosticAction;
        }
    }
}
