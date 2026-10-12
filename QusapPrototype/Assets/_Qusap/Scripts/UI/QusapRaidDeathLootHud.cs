using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // Read-only ownership projections. Buttons submit commands to existing gameplay APIs through the diagnostic host.
    public sealed class QusapRaidDeathLootHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidDeathLootContainerPlayground playground;
        private Text victim, container, custody, prompt;
        public Canvas Canvas { get; private set; }
        public void Configure(QusapRaidDeathLootContainerPlayground host) => playground = host;
        private void Start()
        {
            var root = new GameObject("DeathLootDiagnosticCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform); Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay; Canvas.sortingOrder = 220;
            var scale = root.GetComponent<CanvasScaler>(); scale.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scale.referenceResolution = new(1280, 720);
            var left = Panel(root.transform, "Victim", new(0, .54f), new(.5f, 1));
            var right = Panel(root.transform, "DeathContainer", new(.5f, .54f), Vector2.one);
            victim = Text(left, "VictimOwnership", 15, Vector2.zero, Vector2.one);
            container = Text(right, "ContainerOwnership", 15, new(0, .14f), Vector2.one);
            var footer = Panel(root.transform, "Custody", Vector2.zero, new(1, .22f));
            custody = Text(footer, "CustodyTrace", 17, Vector2.zero, Vector2.one);
            Button(right, "<", 0, () => Command(new(false, -1)));
            Button(right, ">", 1, () => Command(new(false, 1)));
            Button(right, "Tomar 1", 2, () => Command(new(false, takeOne: true)));
            Button(right, "Stack", 3, () => Command(new(false, takeStack: true)));
            Button(right, "Consumir", 4, () => playground.ConsumeRivalHealing());
            Button(right, "Cerrar", 5, () => Command(new(false, close: true)));
            prompt = Text(root.transform, "WorldHoldPrompt", 18, Vector2.zero, Vector2.zero);
            prompt.rectTransform.sizeDelta = new(420, 90); prompt.rectTransform.pivot = new(.5f, 0); prompt.alignment = TextAnchor.MiddleCenter;
        }
        private static Transform Panel(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = new(5, 4); rect.offsetMax = new(-5, -4);
            go.GetComponent<Image>().color = new(.025f, .04f, .065f, .95f); return go.transform;
        }
        private static Text Text(Transform parent, string name, int size, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size;
            text.color = Color.white; text.supportRichText = true; text.raycastTarget = false;
            text.rectTransform.anchorMin = min; text.rectTransform.anchorMax = max; text.rectTransform.offsetMin = new(9, 5); text.rectTransform.offsetMax = new(-9, -5); return text;
        }
        private static void Button(Transform parent, string name, int index, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchorMin = new(index / 6f, 0); rect.anchorMax = new((index + 1) / 6f, .13f); rect.offsetMin = new(3, 3); rect.offsetMax = new(-3, -3);
            go.GetComponent<Image>().color = new(.15f, .22f, .29f); go.GetComponent<Button>().onClick.AddListener(action);
            var label = Text(go.transform, "Label", 15, Vector2.zero, Vector2.one); label.alignment = TextAnchor.MiddleCenter; label.text = name;
        }
        private void Command(QusapLootInteractionCommand command)
        { if (playground.Interactors.Length > 1 && playground.Interactors[1].isActiveAndEnabled) playground.Interactors[1].QueueCommand(command); }
        private static string Row(QusapLootSnapshot item) => $"{item.LootInstanceId}  x{item.Quantity}\n  {item.DefinitionId} → {item.Location}/{item.HolderId ?? "--"}";
        private static string Owner(string value) => value.Replace("Equipped/", "Equipped ").Replace("Backpack/", "Backpack ").Replace("Stash/memory/", "Stash ")
            .Replace("DeathContainer/death/death-demo-001/", "Death ");
        private void LateUpdate()
        {
            if (playground.Owner == null || playground.InitialVictim == null) return;
            var p1 = playground.Raid.Participants.Single(p => p.ParticipantId == "P1");
            var p2 = playground.Raid.Participants.Single(p => p.ParticipantId == "P2");
            var ledger = playground.Raid.Session.World;
            var v = new StringBuilder();
            v.AppendLine($"<b>RAID DEATH LOOT FOUNDATION — DIAGNÓSTICO</b>\nRaidId: {ledger.RaidId} | P1 / memory/P1 | HP {p1.Receiver.CurrentHealth:0}");
            v.AppendLine($"Estado P1: {p1.State.Status} | {(p1.Receiver.IsEliminated ? "ELIMINADO · sin interacción/prompts" : "activo · F interactuar")}");
            v.AppendLine("<b>Inventario inicial de víctima (IDs originales)</b>");
            if (playground.InitialVictim.EquippedWeapon.HasValue) v.AppendLine(Row(playground.InitialVictim.EquippedWeapon.Value));
            foreach (var item in playground.InitialVictim.Backpack.Where(i => i.HasValue))
                v.AppendLine($"{item.Value.LootInstanceId} x{item.Value.Quantity} · {item.Value.DefinitionId}{(item.Value.RaidLoaner ? " [RAID LOANER]" : "")}");
            v.AppendLine($"<color=#9AF3CD>Bolsillo seguro: {playground.Pocket.LootInstanceId} x{playground.Pocket.Quantity}\n  {playground.Pocket.DefinitionId} → {playground.Pocket.Location}/{playground.Pocket.HolderId}</color>");
            v.AppendLine($"RaidLoaner: {playground.Loaner.Location} | Retired {playground.Loaner.Weapon.IsRetired} | destruidos {ledger.LoanersDestroyed}");
            v.AppendLine($"Consumido: {playground.Consumed.LootInstanceId} x{playground.Consumed.Quantity} → {playground.Consumed.Location}");
            victim.text = v.ToString();
            var c = new StringBuilder(); var projection = playground.Presentation.Projections.LastOrDefault();
            c.AppendLine($"<b>P2 / memory/P2</b> | HP {p2.Receiver.CurrentHealth:0} | {playground.Session.Status}");
            var interaction = playground.Interactors[1]; var bag = p2.ReadInventorySnapshot();
            c.AppendLine($"Mochila {bag.Backpack.Count(i => i.HasValue)}/{bag.Backpack.Count} | D-pad ↑ · hold {interaction.Progress.Elapsed:0.00}/0.45 s");
            if (projection == null) c.AppendLine("Contenedor aún no existe. P2: BodyAttack real [botón West].");
            else
            {
                var state = projection.View.Snapshot;
                c.AppendLine($"Víctima {projection.Record.ParticipantId} | EliminationOrdinal {projection.Record.EliminationOrdinal}\nDeathContainerId: {projection.Record.ContainerId}");
                c.AppendLine($"Posición {projection.Placement.Position.ToString("F2")} · {projection.Placement.Kind}");
                c.AppendLine($"{state.State} | versión {state.Version} | sin tabla/RNG | generación {state.GenerationCount}");
                foreach (var item in state.Items) c.AppendLine((item.LootInstanceId == interaction.SelectedInstanceId ? "> " : "  ") + Row(item));
                c.AppendLine("Bolsillo seguro y RaidLoaner: EXCLUIDOS");
            }
            c.AppendLine("Resultado: " + QusapRaidLootInteractionHud.Result(interaction.LastReply));
            container.text = c.ToString();
            var sword = playground.Sword;
            custody.text = $"<b>{playground.EvidenceAction}</b>\nInstanceId {sword.LootInstanceId} | CanonicalItemId {sword.DefinitionId} | x{sword.Quantity}\n"
                + "Custodia: " + string.Join(" → ", playground.Custody(sword.LootInstanceId).Select(Owner))
                + $"\nActual: {sword.Location}/{sword.HolderId} | Stash P1: {p1.ReadStashSnapshot().Count} | Stash P2: {p2.ReadStashSnapshot().Count}"
                + $"\nConfirmaciones {playground.Owner.ConfirmedTransfers} | Rechazos {playground.Rejections} | Perdidos {ledger.DeathLootDestroyed} | Duplicados {playground.Owner.DuplicateInstances} | Extracción derecha: 5 s";
            var target = interaction.isActiveAndEnabled ? interaction.Sensor.Target : null;
            prompt.gameObject.SetActive(target != null && Camera.main != null);
            if (target != null && Camera.main != null)
            {
                var point = Camera.main.WorldToViewportPoint(target.transform.position + Vector3.up * .7f);
                prompt.rectTransform.anchoredPosition = new(point.x * 1280, point.y * 720);
                int bars = Mathf.RoundToInt(interaction.Progress.Normalized * 10);
                prompt.text = $"<b>DEATH LOOT · {target.SpawnPointId}</b>\nP2 [D-pad ↑] 0,45 s\n[{new string('|', bars)}{new string('.', 10 - bars)}]";
                prompt.color = new(1, .8f, .35f);
            }
        }
    }
}
