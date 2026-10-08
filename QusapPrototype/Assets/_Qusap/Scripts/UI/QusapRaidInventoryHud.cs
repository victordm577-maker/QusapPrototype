using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    // UI selections are local presentation state. All inventory data is read from authorities.
    [DefaultExecutionOrder(800)]
    public sealed class QusapRaidInventoryHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidBootstrap raid;
        private sealed class Panel
        {
            public QusapRaidInventory Player;
            public Text Summary, Pocket, Stash, Container, Result;
            public Text[] Slots;
            public string SelectedItem, SelectedCargo;
        }
        private Panel[] panels;
        private Font font;
        private bool dirty;
        public Canvas Canvas { get; private set; }
        public int RefreshCount { get; private set; }
        public void Configure(QusapRaidBootstrap bootstrap) => raid = bootstrap;
        public string PlayerLabel(int index) => panels[index].Summary.text;
        public string BackpackLabel(int index, int slot) => panels[index].Slots[slot].text;
        public string PocketLabel(int index) => panels[index].Pocket.text;
        public string StashLabel(int index) => panels[index].Stash.text;
        public string ContainerLabel(int index) => panels[index].Container.text;

        private void Start()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("Raid_UGUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            panels = raid.Participants.Select((p, i) => Build(p, i, root.transform)).ToArray();
            raid.Session.World.Changed += Changed;
            foreach (var p in panels)
            {
                p.Player.State.InventoryChanged += Changed;
                p.Player.Receiver.HealthChanged += HealthChanged;
            }
            Changed(); Refresh();
        }
        private Panel Build(QusapRaidInventory player, int index, Transform parent)
        {
            var panel = new GameObject(player.ParticipantId + "_InventoryPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(index == 0 ? 0 : 1, 1);
            rect.pivot = new Vector2(index == 0 ? 0 : 1, 1);
            rect.anchoredPosition = new Vector2(index == 0 ? 12 : -12, -12); rect.sizeDelta = new Vector2(320, 630);
            panel.GetComponent<Image>().color = new Color(0.025f, 0.04f, 0.07f, 0.90f);
            var p = new Panel { Player = player, Slots = new Text[player.BackpackCapacity] };
            p.Summary = Label(panel.transform, "Status", 8, 8, 304, 78, 18);
            for (int slot = 0; slot < p.Slots.Length; slot++)
            {
                int s = slot;
                p.Slots[slot] = Button(panel.transform, "Slot_" + slot, 8, 90 + slot * 29, 304, 27, () =>
                { p.SelectedItem = player.ReadInventorySnapshot().Backpack[s]?.LootInstanceId; Changed(); });
            }
            int y = 90 + p.Slots.Length * 29;
            p.Pocket = Button(panel.transform, "SecurePocket", 8, y, 304, 32, () =>
            { p.SelectedItem = player.ReadInventorySnapshot().SecurePocket?.LootInstanceId; Changed(); });
            Button(panel.transform, "A seguro", 8, y + 37, 96, 28, () => Command(p, () => player.TryMoveToSecurePocket(p.SelectedItem)));
            Button(panel.transform, "A mochila", 108, y + 37, 100, 28, () => Command(p, () => player.TryMoveToBackpack(p.SelectedItem)));
            Button(panel.transform, "Consumir", 212, y + 37, 100, 28, () => Command(p, () => player.TryConsume(p.SelectedItem)));
            Button(panel.transform, "Guardar espada en mochila", 8, y + 69, 304, 28, () => Command(p, player.TryStoreEquippedWeapon));
            p.Stash = Label(panel.transform, "Stash", 8, y + 102, 304, 50, 15);
            Button(panel.transform, "Contenedor / siguiente objeto", 8, y + 157, 304, 26, () =>
            {
                var view = SelectedContainer(p);
                var cargo = view?.ReadSnapshot();
                if (cargo != null && cargo.Count > 0)
                { int current = Array.FindIndex(cargo.ToArray(), i => i.LootInstanceId == p.SelectedCargo); p.SelectedCargo = cargo[(current + 1) % cargo.Count].LootInstanceId; }
                Changed();
            });
            p.Container = Label(panel.transform, "Container", 8, y + 188, 304, 105, 14);
            Button(panel.transform, "Saquear", 8, y + 297, 148, 28, () => Command(p, () => player.TryTakeFromDeathContainer(SelectedContainer(p), p.SelectedCargo)));
            Button(panel.transform, "Equipar espada", 160, y + 297, 152, 28, () => Command(p, () => player.TryEquipWeaponFromContainer(SelectedContainer(p), p.SelectedCargo)));
            p.Result = Label(panel.transform, "Result", 8, y + 329, 304, 26, 14);
            return p;
        }
        private QusapDeathLootContainer SelectedContainer(Panel p)
        {
            // Prefer a reachable live view. Selection is a UI concern, not inventory ownership.
            return raid.ContainerViews.FirstOrDefault(v => v != null && v.isActiveAndEnabled && v.CanReach(p.Player))
                ?? raid.ContainerViews.FirstOrDefault(v => v != null && v.isActiveAndEnabled);
        }
        private void Command(Panel p, Func<QusapLootResult> operation)
        { p.Result.text = operation().ToString(); Changed(); }
        private Text Label(Transform parent, string name, float x, float y, float w, float h, int size)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
            var rt = obj.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var text = obj.GetComponent<Text>(); text.font = font; text.fontSize = size; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false; return text;
        }
        private Text Button(Transform parent, string name, float x, float y, float w, float h, UnityEngine.Events.UnityAction click)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image)); obj.transform.SetParent(parent, false);
            var rt = obj.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y); rt.sizeDelta = new Vector2(w, h);
            var image = obj.GetComponent<Image>(); image.color = new Color(0.12f, 0.20f, 0.29f, 1);
            var label = Label(image.transform, name + "_Text", 5, 0, w - 10, h, 14); label.text = name;
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(click);
            return label;
        }
        private void Changed() => dirty = true;
        private void HealthChanged(QusapHealthChange _) => Changed();
        private void LateUpdate() { if (dirty && panels != null) Refresh(); }
        private void Refresh()
        {
            dirty = false; RefreshCount++;
            foreach (var p in panels)
            {
                var data = p.Player.ReadInventorySnapshot();
                string active = p.Player.Receiver.IsEliminated ? "ELIMINADO" : data.Status == QusapRaidInventoryStatus.Active ? "ACTIVO" : "BLOQUEADO / PENDIENTE";
                p.Summary.text = $"{data.ParticipantId}  |  {active}\nVida {p.Player.Receiver.CurrentHealth:0} / {p.Player.Receiver.MaxHealth:0}\nEspada: {data.EquippedWeapon?.DisplayName ?? "desarmado"}";
                for (int i = 0; i < data.Backpack.Count; i++)
                    p.Slots[i].text = $"{i + 1}. {(p.SelectedItem != null && data.Backpack[i]?.LootInstanceId == p.SelectedItem ? "> " : "")}{data.Backpack[i]?.DisplayName ?? "vacío"}";
                p.Pocket.text = "SEGURO [1]: " + (data.SecurePocket?.DisplayName ?? "vacío");
                var stash = p.Player.ReadStashSnapshot();
                p.Stash.text = "STASH EN MEMORIA (esta sesión)\n" + (stash.Count == 0 ? "vacío" : string.Join(", ", stash.Select(i => i.DisplayName)));
                var view = SelectedContainer(p); var cargo = view?.ReadSnapshot();
                if (cargo != null && cargo.Count > 0 && !cargo.Any(i => i.LootInstanceId == p.SelectedCargo)) p.SelectedCargo = cargo[0].LootInstanceId;
                p.Container.text = view == null ? "CONTENEDOR: ninguno" : "CONTENEDOR " + view.Record.ParticipantId + "\n"
                    + string.Join("\n", cargo.Select(i => (i.LootInstanceId == p.SelectedCargo ? "> " : "  ") + i.DisplayName));
            }
        }
        private void OnDestroy()
        {
            if (raid != null && raid.Session != null) raid.Session.World.Changed -= Changed;
            if (panels == null) return;
            foreach (var p in panels)
            { p.Player.State.InventoryChanged -= Changed; if (p.Player.Receiver != null) p.Player.Receiver.HealthChanged -= HealthChanged; }
        }
    }
}
