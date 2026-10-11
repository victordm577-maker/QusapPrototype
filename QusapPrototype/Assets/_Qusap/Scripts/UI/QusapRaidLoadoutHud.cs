using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Qusap
{
    public sealed class QusapRaidLoadoutHud : MonoBehaviour
    {
        [SerializeField] private QusapRaidLoadoutPlayground playground;
        private Text profile, raid, action;
        public Canvas Canvas { get; private set; }
        public void Configure(QusapRaidLoadoutPlayground scene) => playground = scene;
        private void Start()
        {
            var root = new GameObject("Loadout_Diagnostic_Canvas", typeof(Canvas), typeof(CanvasScaler)); root.transform.SetParent(transform, false);
            Canvas = root.GetComponent<Canvas>(); Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1280, 720);
            profile = Label(root.transform, "Profile", new Vector2(0, 1), new Vector2(12, -12), new Vector2(540, 330), 14);
            raid = Label(root.transform, "Raid", new Vector2(1, 1), new Vector2(-12, -12), new Vector2(680, 345), 13);
            action = Label(root.transform, "Action", new Vector2(.5f, 0), new Vector2(0, 12), new Vector2(1256, 88), 18);
        }
        private static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int font)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image)); box.transform.SetParent(parent, false);
            var rect = box.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = anchor; rect.anchoredPosition = offset; rect.sizeDelta = size;
            box.GetComponent<Image>().color = new Color(.02f, .03f, .05f, .93f);
            var child = new GameObject("Text", typeof(RectTransform), typeof(Text)); child.transform.SetParent(box.transform, false);
            var textRect = child.GetComponent<RectTransform>(); textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one; textRect.offsetMin = new Vector2(9, 7); textRect.offsetMax = new Vector2(-9, -7);
            var text = child.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = font; text.color = Color.white; return text;
        }
        private static string Item(QusapLootSnapshot i) => i.LootInstanceId + "\n" + i.DefinitionId + " x" + i.Quantity + " @" + i.Location + (i.RaidLoaner ? " [RaidLoaner]" : "");
        private void Update()
        {
            var deployment = playground?.Deployment; if (deployment == null || profile == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) playground.SelectOwned();
                if (keyboard.digit2Key.wasPressedThisFrame) playground.SelectLoaner();
                if (keyboard.tKey.wasPressedThisFrame) playground.ToggleConsumable("raid_healing");
                if (keyboard.mKey.wasPressedThisFrame) playground.ToggleConsumable("raid_major_heal");
                if (keyboard.pKey.wasPressedThisFrame) playground.Prepare();
                if (keyboard.cKey.wasPressedThisFrame) playground.Commit();
                if (keyboard.enterKey.wasPressedThisFrame) playground.Run();
                if (keyboard.xKey.wasPressedThisFrame) { deployment.CancelDeployment(); playground.Action = deployment.LastResult; }
                if (keyboard.hKey.wasPressedThisFrame && deployment.Inventory?.CanOperate == true)
                {
                    var item = deployment.Inventory.ReadInventorySnapshot().Backpack.FirstOrDefault(i => i.HasValue && i.Value.Category == QusapLootCategory.Consumable);
                    if (item.HasValue) playground.Action = "Consume: " + playground.Raid.Participants[0].TryConsume(item.Value.LootInstanceId);
                }
            }
            var repository = playground.Profile; var stash = repository.ReadStashSnapshot(repository.ProfileId);
            profile.text = "LOCAL PROFILE | Schema " + repository.SchemaVersion + " | Revision " + repository.Revision + "\nProfileId " + repository.ProfileId
                + "\nSTASH " + stash.Count + " | Escrituras " + repository.Storage.Writes + "\n" + string.Join("\n", stash.Select(Item))
                + "\nSELECCION arma: " + (playground.WeaponSelection ?? "Basic Sword / RaidLoaner")
                + "\nConsumibles (2 slots): " + string.Join(", ", playground.ConsumableSelection.Select(i => "#" + i.InstanceId.Split('/').Last() + " x" + i.Quantity));
            var world = deployment.Session?.World;
            raid.text = "DEPLOYMENT " + deployment.Status + "\nDeploymentId " + (deployment.DeploymentId ?? "None") + "\nRaidId " + (deployment.RaidId ?? "None")
                + "\nManifest " + (repository.ReadActiveDeploymentSnapshot()?.State.ToString() ?? "None") + " | Rechazos " + deployment.Rejections
                + " | Duplicados " + (world == null ? 0 : world.Instances.Count - world.Instances.Select(i => i.LootInstanceId).Distinct().Count())
                + "\nLoaners creados " + deployment.LoanersCreated + " / destruidos " + deployment.LoanersDestroyed
                + "\nMochila 6 | Bolsillo " + (deployment.Inventory?.ReadInventorySnapshot().SecurePocket.HasValue == true ? "ocupado" : "vacio")
                + "\n" + (world == null ? "" : string.Join("\n", world.Instances.Select(i => Item(new QusapLootSnapshot(i)))));
            action.text = playground.Action + "\nLoaner: combate nativo / valor permanente 0 / no stash / no pocket | Ruta aislada de diagnostico";
        }
    }
}
