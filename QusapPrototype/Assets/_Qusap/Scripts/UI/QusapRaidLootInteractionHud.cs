using System.Text;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // Presentation sends commands and reads projections only; it has no inventory/profile mutation API.
    public sealed class QusapRaidLootInteractionHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidLootInteractionPlayground playground;
        private readonly Text[] panels = new Text[2];
        private readonly Text[] prompts = new Text[2];
        private Text counters;
        public Canvas Canvas { get; private set; }
        public string EvidenceAction { get; set; } = "Acercarse | mantener Interact 0,45 s | seleccionar y tomar";
        public void Configure(QusapRaidLootInteractionPlayground root) => playground = root;
        private void Start()
        {
            var root = new GameObject("LootInteractionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); root.transform.SetParent(transform);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 200;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new(1280, 720);
            for (int p = 0; p < 2; p++)
            {
                int index = p;
                var background = new GameObject("PlayerPanel", typeof(RectTransform), typeof(Image)); background.transform.SetParent(root.transform, false);
                var rect = background.GetComponent<RectTransform>(); rect.anchorMin = new(p * .5f, .57f); rect.anchorMax = new((p + 1) * .5f, 1); rect.offsetMin = new(5, 0); rect.offsetMax = new(-5, -5);
                background.GetComponent<Image>().color = new(.025f, .045f, .075f, .94f);
                panels[p] = MakeText(background.transform, "PlayerDiagnostics", 15, new(0, .15f), Vector2.one);
                MakeButton(background.transform, "<", 0, () => Command(index, new(false, -1)));
                MakeButton(background.transform, ">", 1, () => Command(index, new(false, 1)));
                MakeButton(background.transform, "Tomar 1", 2, () => Command(index, new(false, takeOne: true)));
                MakeButton(background.transform, "Stack", 3, () => Command(index, new(false, takeStack: true)));
                MakeButton(background.transform, "Cerrar", 4, () => Command(index, new(false, close: true)));
                prompts[p] = MakeText(root.transform, "WorldPrompt", 17, Vector2.zero, Vector2.zero);
                var promptRect = prompts[p].rectTransform; promptRect.sizeDelta = new(320, 80); promptRect.pivot = new(.5f, 0);
                prompts[p].alignment = TextAnchor.MiddleCenter;
            }
            counters = MakeText(root.transform, "Counters", 19, Vector2.zero, new(1, .14f)); counters.alignment = TextAnchor.MiddleCenter;
        }
        private void Command(int index, QusapLootInteractionCommand command)
        { if (index < playground.Interactors.Length) playground.Interactors[index].QueueCommand(command); }
        private static Text MakeText(Transform parent, string name, int size, Vector2 min, Vector2 max)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Text)); item.transform.SetParent(parent, false);
            var text = item.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size;
            text.color = Color.white; text.supportRichText = true; text.raycastTarget = false;
            text.rectTransform.anchorMin = min; text.rectTransform.anchorMax = max; text.rectTransform.offsetMin = new(10, 4); text.rectTransform.offsetMax = new(-10, -6);
            return text;
        }
        private static void MakeButton(Transform parent, string label, int slot, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new(slot / 5f, 0); rect.anchorMax = new((slot + 1) / 5f, .14f); rect.offsetMin = new(5, 5); rect.offsetMax = new(-5, -5);
            go.GetComponent<Image>().color = new(.15f, .23f, .3f); go.GetComponent<Button>().onClick.AddListener(action);
            var text = MakeText(go.transform, "Label", 16, Vector2.zero, Vector2.one); text.text = label; text.alignment = TextAnchor.MiddleCenter;
        }
        public static string Reason(QusapLootInteractionRejection reason) => reason switch
        {
            QusapLootInteractionRejection.OutOfRange => "Fuera de rango",
            QusapLootInteractionRejection.NoLineOfSight => "Sin línea de visión",
            QusapLootInteractionRejection.Incapacitated => "Jugador incapacitado",
            QusapLootInteractionRejection.SessionNotRunning => "Raid Session no está Running",
            QusapLootInteractionRejection.Released => "Botón liberado",
            QusapLootInteractionRejection.TargetChanged => "Objetivo cambiado",
            QusapLootInteractionRejection.Depleted => "Vacío",
            QusapLootInteractionRejection.InvalidTarget => "Objetivo no válido",
            QusapLootInteractionRejection.Closed => "Cerrado por jugador",
            _ => "--"
        };
        public static string Result(QusapWorldLootReply reply) => reply == null ? "--" : reply.Result switch
        {
            QusapWorldLootResult.Full => "Mochila llena",
            QusapWorldLootResult.StaleRequest => "Solicitud obsoleta (StaleRequest)",
            QusapWorldLootResult.OutOfReach => "Fuera de rango / sin línea de visión",
            QusapWorldLootResult.Blocked => "Jugador incapacitado / sesión detenida",
            QusapWorldLootResult.Success => "Confirmado x" + reply.ConfirmedQuantity,
            _ => reply.Result.ToString()
        };
        public static string State(QusapWorldLootState state) => state == QusapWorldLootState.Unopened ? "Cerrado" : state == QusapWorldLootState.Opened ? "Abierto" : "Vacío";
        private static string Rarity(QusapLootRarity rarity) => rarity switch
        { QusapLootRarity.Uncommon => "#72DB91", QusapLootRarity.Rare => "#72B7FF", QusapLootRarity.Epic => "#D99BFF", QusapLootRarity.Legendary => "#FFD572", _ => "#EEEEEE" };
        private void LateUpdate()
        {
            if (Canvas == null || playground.Owner == null) return;
            for (int p = 0; p < panels.Length && p < playground.Interactors.Length; p++)
            {
                var player = playground.Interactors[p]; var sensor = player.Sensor; var target = sensor.Target;
                var diagnostic = target != null ? target : sensor.NearestDiagnostic;
                var snapshot = player.Observed ?? diagnostic?.Snapshot;
                var text = new StringBuilder();
                text.AppendLine($"<b>{player.PlayerId}</b>  InteractorId: {player.InteractorId}  [{player.BindingLabel}]");
                text.AppendLine($"Objetivo: {target?.name ?? "--"}  SpawnPointId: {diagnostic?.SpawnPointId ?? "--"}");
                text.AppendLine($"Distancia: {sensor.Distance:0.00}/{sensor.Range:0.00}  LOS: {(sensor.LineOfSight ? "SÍ" : "NO")}  Hold: {player.Progress.Elapsed:0.00}/{player.Progress.Duration:0.00}");
                text.AppendLine($"Estado: {(player.Progress.IsHolding ? "Abriendo" : snapshot != null ? State(snapshot.State) : "--")}  ContainerVersion: {snapshot?.Version.ToString() ?? "--"}");
                text.AppendLine("InstanceId seleccionado: " + (player.SelectedInstanceId ?? "--"));
                text.AppendLine($"Resultado: {Result(player.LastReply)} | {Reason(player.LastRejection)}");
                var inventory = playground.Raid.Participants.FirstOrDefault(i => i.ParticipantId == player.PlayerId)?.ReadInventorySnapshot();
                if (inventory != null) text.AppendLine($"Mochila: {inventory.Backpack.Count(i => i.HasValue)}/{inventory.Backpack.Count} slots · {inventory.Backpack.Where(i => i.HasValue).Sum(i => i.Value.Quantity)} unidades · Bolsillo: {(inventory.SecurePocket.HasValue ? "ocupado" : "vacío")}");
                if (player.PresentationOpen)
                    foreach (var item in player.Observed.Items)
                        text.AppendLine($"{(item.LootInstanceId == player.SelectedInstanceId ? ">" : " ")} <color={Rarity(item.Rarity)}>{item.DisplayName} [{item.Rarity}] x{item.Quantity}</color>\n  {item.LootInstanceId}");
                panels[p].text = text.ToString();
                prompts[p].gameObject.SetActive(target != null);
                if (target != null && Camera.main != null)
                {
                    var screen = Camera.main.WorldToViewportPoint(target.transform.position + Vector3.up * 1.3f);
                    bool shared = playground.Interactors.Length > 1 && playground.Interactors[0].Sensor.Target == playground.Interactors[1].Sensor.Target;
                    prompts[p].rectTransform.anchoredPosition = new(screen.x * 1280 + (shared ? (p == 0 ? -110 : 110) : 0), screen.y * 720);
                    int bars = Mathf.RoundToInt(player.Progress.Normalized * 10);
                    prompts[p].text = $"<b>{player.PlayerId} → {target.SpawnPointId}</b>\n{(player.Progress.IsHolding ? "Abriendo" : State(target.Snapshot.State))} [{(p == 0 ? "F" : "D-pad ↑")}]\n[{new string('|', bars)}{new string('.', 10 - bars)}]";
                    prompts[p].color = p == 0 ? new(.4f, .85f, 1) : new(1, .75f, .35f);
                }
            }
            counters.text = $"{EvidenceAction}\nAperturas: {playground.OpeningCount} | Confirmaciones: {playground.Owner.ConfirmedTransfers} | Rechazos autoridad: {playground.Rejections} | Duplicados: {playground.Owner.DuplicateInstances}\n{playground.Session.Status} · Sin perfil · F: Interact diagnóstico · E: WeaponSwapThrow nativo";
        }
    }
}
