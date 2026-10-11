using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Qusap
{
    public sealed class QusapRaidWorldLootHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidWorldLootPlayground scene;
        private Text header, inventory, stash, action;
        private readonly Text[] caches = new Text[3];
        public Canvas Canvas { get; private set; }
        public void Configure(QusapRaidWorldLootPlayground playground) => scene = playground;
        private void Start()
        {
            var root = new GameObject("WorldLoot_Diagnostic_Canvas", typeof(Canvas), typeof(CanvasScaler)); root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            header = Label(root.transform, "Raid_Context", new Vector2(12, -10), new Vector2(1256, 77), 15);
            for (int i = 0; i < 3; i++) caches[i] = Label(root.transform, "Cache_" + i, new Vector2(12 + i * 422, -94), new Vector2(412, 237), 12);
            inventory = Label(root.transform, "Backpack_And_Pocket", new Vector2(12, -453), new Vector2(754, 199), 13);
            stash = Label(root.transform, "Persistent_Stash", new Vector2(774, -453), new Vector2(494, 199), 12);
            action = Label(root.transform, "Action", new Vector2(12, -658), new Vector2(1256, 54), 16);
        }
        private static Text Label(Transform parent, string name, Vector2 offset, Vector2 size, int font)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image)); panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = offset; rect.sizeDelta = size;
            panel.GetComponent<Image>().color = new Color(.02f, .025f, .045f, .92f);
            var child = new GameObject("Text", typeof(RectTransform), typeof(Text)); child.transform.SetParent(panel.transform, false);
            var textRect = child.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = new Vector2(8, 4); textRect.offsetMax = new Vector2(-8, -4);
            var text = child.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = font; text.color = Color.white; return text;
        }
        private static string Item(QusapLootSnapshot item) => item.DefinitionId + " [" + item.Rarity + "] x" + item.Quantity + "\n" + item.LootInstanceId;
        private void Update()
        {
            if (scene?.Authority == null) return;
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.digit1Key.wasPressedThisFrame) scene.Select(0); if (k.digit2Key.wasPressedThisFrame) scene.Select(1); if (k.digit3Key.wasPressedThisFrame) scene.Select(2);
                if (k.jKey.wasPressedThisFrame) scene.SelectedItem = 0; if (k.kKey.wasPressedThisFrame) scene.SelectedItem = 1; if (k.lKey.wasPressedThisFrame) scene.SelectedItem = 2;
                if (k.oKey.wasPressedThisFrame) scene.OpenSelected(); if (k.tKey.wasPressedThisFrame) scene.TakeSelected(); if (k.yKey.wasPressedThisFrame) scene.TakeSelected(1);
                if (k.bKey.wasPressedThisFrame) scene.MoveLastToPocket(); if (k.hKey.wasPressedThisFrame) scene.Consume(); if (k.vKey.wasPressedThisFrame) scene.DiagnosticDamage();
            }
            var profile = scene.Loadout.Profile;
            header.text = "RAID WORLD LOOT / VISUALES DIAGNÓSTICOS — 1/2/3 seleccionar | O abrir | T stack | Y unidad | B bolsillo | H curar\nRaidId: " + scene.Authority.RaidId
                + " | Seed: " + scene.Seed + "\nConfirmadas " + scene.Authority.ConfirmedTransfers + " | Rechazos " + scene.Authority.RejectedRequests + " | Duplicados " + scene.Authority.DuplicateInstances
                + " | Schema " + profile.SchemaVersion + " Revision " + profile.Revision + " Escrituras " + profile.Storage.Writes;
            for (int i = 0; i < caches.Length; i++)
            {
                var snap = scene.Containers[i].Snapshot; var text = new StringBuilder();
                text.AppendLine((scene.SelectedContainer == i ? "> " : "") + snap.TableId + " — " + snap.State + " v" + snap.Version);
                text.AppendLine("SpawnPointId: " + snap.SpawnPointId + " | generación " + snap.GenerationCount);
                for (int n = 0; n < snap.Items.Count; n++) { text.AppendLine("Roll " + n + (scene.SelectedContainer == i && scene.SelectedItem == n ? " [seleccionado]" : "")); text.AppendLine(Item(snap.Items[n])); }
                if (snap.State == QusapWorldLootState.Unopened) text.AppendLine("Sin contenido generado. Abrir: O");
                if (snap.State == QusapWorldLootState.Depleted) text.AppendLine("No quedan unidades transferibles. No se regenera.");
                caches[i].text = text.ToString();
            }
            var bag = scene.LocalPlayer.ReadInventorySnapshot(); var inventoryText = new StringBuilder("BACKPACK / 6 SLOTS | EQUIPO: " + (bag.EquippedWeapon.HasValue ? bag.EquippedWeapon.Value.DefinitionId + (bag.EquippedWeapon.Value.RaidLoaner ? " [RaidLoaner]" : "") : "vacío") + "\n");
            for (int i = 0; i < bag.Backpack.Count; i++) inventoryText.AppendLine("[" + i + "] " + (bag.Backpack[i].HasValue ? Item(bag.Backpack[i].Value).Replace("\n", " | ") : "vacío"));
            inventoryText.AppendLine("BOLSILLO / 1 SLOT: " + (bag.SecurePocket.HasValue ? Item(bag.SecurePocket.Value) : "vacío — nunca automático")); inventory.text = inventoryText.ToString();
            var persistent = profile.ReadStashSnapshot(profile.ProfileId); stash.text = "STASH PERSISTENTE / " + persistent.Count + " instancias\n" + string.Join("\n", persistent.Select(i => Item(i)));
            action.text = scene.Action + "\nJ/K/L seleccionar roll | V daño diagnóstico 25 | extracción: entrar en el volumen existente";
        }
    }
}
