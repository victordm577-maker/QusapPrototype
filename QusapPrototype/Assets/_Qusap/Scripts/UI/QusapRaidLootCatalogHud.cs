using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Qusap
{
    public sealed class QusapRaidLootCatalogHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidLootCatalogPlayground playground;
        [SerializeField] private QusapRaidSessionObserver observer;
        private Text selected, inventory, rolls, action;
        public Canvas Canvas { get; private set; }
        public void Configure(QusapRaidLootCatalogPlayground scene, QusapRaidSessionObserver session)
        { playground = scene; observer = session; }
        private void Start()
        {
            var root = new GameObject("RaidLootCatalog_UGUI", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false); Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            selected = Label(root.transform, "Selected", new Vector2(0, 1), new Vector2(12, -12), new Vector2(435, 260), 15);
            rolls = Label(root.transform, "Rolls", new Vector2(1, 1), new Vector2(-12, -12), new Vector2(390, 260), 14);
            inventory = Label(root.transform, "Inventory", new Vector2(1, 0), new Vector2(-12, 95), new Vector2(390, 300), 14);
            action = Label(root.transform, "Action", new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1256, 75), 19);
        }
        private static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image)); box.transform.SetParent(parent, false);
            var rect = box.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = offset; rect.sizeDelta = size;
            box.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.92f);
            var child = new GameObject("Text", typeof(RectTransform), typeof(Text)); child.transform.SetParent(box.transform, false);
            var tr = child.GetComponent<RectTransform>(); tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(9, 7); tr.offsetMax = new Vector2(-9, -7);
            var text = child.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = fontSize; text.color = Color.white; return text;
        }
        private static string Item(QusapLootSnapshot item) => "#" + item.LootInstanceId.Substring(item.LootInstanceId.LastIndexOf('/') + 1) + " " + item.DefinitionId + " x" + item.Quantity + " (" + item.Rarity + ")";
        private void Update()
        {
            if (playground?.Selected == null || selected == null || observer.Raid.Participants[0].State == null) return;
            var player = observer.Raid.Participants[0]; var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.enterKey.wasPressedThisFrame) observer.StartSession();
                if (keyboard.gKey.wasPressedThisFrame) playground.GenerateWorldLoot(playground.Seed);
                if (keyboard.nKey.wasPressedThisFrame) playground.Preview(unchecked(playground.Seed + 1));
                if (keyboard.tabKey.wasPressedThisFrame) playground.Select(playground.Catalog.Definitions[(playground.Catalog.Definitions.ToList().IndexOf(playground.Selected) + 1) % playground.Catalog.Definitions.Count].DefinitionId);
                if (keyboard.hKey.wasPressedThisFrame && player.CanOperate)
                {
                    var item = player.ReadInventorySnapshot().Backpack.FirstOrDefault(i => i.HasValue && i.Value.Category == QusapLootCategory.Consumable);
                    if (item.HasValue) playground.Action = "Consumir: " + player.TryConsume(item.Value.LootInstanceId);
                }
                if (keyboard.rKey.wasPressedThisFrame && player.CanOperate)
                {
                    var relic = player.ReadInventorySnapshot().Backpack.FirstOrDefault(i => i.HasValue && i.Value.Category == QusapLootCategory.Relic);
                    if (relic.HasValue) playground.Action = "Bolsillo: " + player.TryMoveToSecurePocket(relic.Value.LootInstanceId);
                }
                if (keyboard.bKey.wasPressedThisFrame && player.CanOperate)
                {
                    var weapon = player.ReadInventorySnapshot().Backpack.FirstOrDefault(i => i.HasValue && i.Value.Weapon != null);
                    if (weapon.HasValue) playground.Action = "Equipar: " + player.WeaponAdapter.TryEquipWeaponFromBackpack(weapon.Value.LootInstanceId);
                    else playground.Action = "Guardar arma: " + player.TryStoreEquippedWeapon();
                }
                if (keyboard.lKey.wasPressedThisFrame) playground.ReloadEvidenceProfile();
            }
            var d = playground.Selected;
            selected.text = "RAID LOOT CATALOG | Validacion: " + playground.ValidationErrors.Count + " errores\n"
                + d.DefinitionId + "\n" + d.DisplayName + "\nCategory: " + (d.Category == QusapLootCategory.Material ? "Material" : d.Category.ToString()) + " | Rarity: " + d.Rarity
                + "\nQuantity: " + SelectedQuantity(player, d.DefinitionId) + " | MaxStack: " + d.MaxStack + " | BaseValue: " + d.BaseValue
                + (d.Category == QusapLootCategory.Consumable ? "\nHealAmount: " + d.HealAmount + " | ConsumeOnUse: " + d.ConsumeOnUse : "")
                + (d.Category == QusapLootCategory.Weapon ? "\nWeaponDefinitionId: " + d.WeaponDefinitionId : "")
                + "\nBackpack: " + d.CanEnterBackpack + " | Pocket: " + d.CanEnterSecurePocket
                + "\nDropOnDeath: " + d.CanDropOnDeath + " | Stash: " + d.CanPersistInStash
                + "\nPickup: " + d.WorldPickupPresentationId + "\nTags: " + string.Join(", ", d.Tags)
                + (playground.ValidationErrors.Count == 0 ? "" : "\n" + string.Join("\n", playground.ValidationErrors));
            rolls.text = playground.Table.LootTableId + "\nSeed: " + playground.Seed + " | misma seed: " + playground.SameSeedMatches
                + "\nOtra seed difiere: " + playground.DifferentSeedDiffers + "\n" + string.Join("\n", playground.LastRoll.Select(r => r.ToString()))
                + "\nCommon gris / Uncommon verde / Rare azul\nEpic morado / Legendary dorado";
            var bag = player.ReadInventorySnapshot(); var profile = playground.Reloaded ?? observer.Raid.GetComponent<QusapLocalProfilePersistence>().Repository;
            var stash = profile.ReadStashSnapshot(profile.ProfileId);
            inventory.text = "P1 Health: " + player.Receiver.CurrentHealth + "/" + player.Receiver.MaxHealth + " | Slots: " + bag.Backpack.Count(i => i.HasValue) + "/6"
                + "\n" + string.Join("\n", bag.Backpack.Where(i => i.HasValue).Select(i => Item(i.Value)))
                + "\nBolsillo 1: " + (bag.SecurePocket.HasValue ? Item(bag.SecurePocket.Value) : "vacio")
                + "\nSTASH evidencia: " + stash.Count + " | Revision: " + profile.Revision + "\n" + string.Join("\n", stash.Select(Item))
                + "\nDuplicados: " + (stash.Count - stash.Select(i => i.LootInstanceId).Distinct().Count());
            var extraction = player.GetComponent<QusapRaidExtraction>();
            action.text = playground.Action + "\nP1: " + extraction.Status + " | Extraccion: " + extraction.Countdown.Remaining.ToString("0.00") + "s | Sin multiplicadores de rareza";
        }
        private static int SelectedQuantity(QusapRaidInventory player, string definition)
        {
            var snapshot = player.ReadInventorySnapshot();
            return snapshot.Backpack.Where(i => i.HasValue && i.Value.DefinitionId == definition).Select(i => i.Value.Quantity)
                .Concat(snapshot.SecurePocket.HasValue && snapshot.SecurePocket.Value.DefinitionId == definition ? new[] { snapshot.SecurePocket.Value.Quantity } : Array.Empty<int>())
                .Concat(snapshot.EquippedWeapon.HasValue && snapshot.EquippedWeapon.Value.DefinitionId == definition ? new[] { snapshot.EquippedWeapon.Value.Quantity } : Array.Empty<int>())
                .Concat(player.ReadStashSnapshot().Where(i => i.DefinitionId == definition).Select(i => i.Quantity))
                .Concat(player.Session.World.Instances.Where(i => i.DefinitionId == definition && i.Location == QusapLootLocation.World).Select(i => i.Quantity)).FirstOrDefault();
        }
    }
}
