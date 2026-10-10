using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Qusap
{
    // Diagnostic display only. It observes persistence and the approved extraction/session authorities.
    public sealed class QusapLocalProfileDebugHud : MonoBehaviour
    {
        [SerializeField] private QusapLocalProfilePersistence profile;
        [SerializeField] private QusapRaidSessionObserver observer;
        private Text metadata, objects, action;
        public Canvas Canvas { get; private set; }
        public string DiagnosticAction { get; set; } = "ENTER: iniciar | A/D: P1 | R: reliquia al bolsillo | zona verde: extraer";
        public void Configure(QusapLocalProfilePersistence persistence, QusapRaidSessionObserver session)
        { profile = persistence; observer = session; }

        private void Start()
        {
            var root = new GameObject("LocalProfile_UGUI", typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false); Canvas = root.GetComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            metadata = Label(root.transform, "Profile", new Vector2(0, 1), new Vector2(12, -12), new Vector2(660, 320), 16);
            objects = Label(root.transform, "Stash", new Vector2(1, 1), new Vector2(-12, -12), new Vector2(590, 320), 15);
            action = Label(root.transform, "Evidence", new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1256, 95), 20);
        }
        private static Text Label(Transform parent, string name, Vector2 anchor, Vector2 offset, Vector2 size, int fontSize)
        {
            var box = new GameObject(name, typeof(RectTransform), typeof(Image)); box.transform.SetParent(parent, false);
            var rect = box.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = offset; rect.sizeDelta = size; box.GetComponent<Image>().color = new Color(0.02f, 0.03f, 0.05f, 0.95f);
            var child = new GameObject("Text", typeof(RectTransform), typeof(Text)); child.transform.SetParent(box.transform, false);
            var tr = child.GetComponent<RectTransform>(); tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(10, 8); tr.offsetMax = new Vector2(-10, -8);
            var text = child.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize; text.color = Color.white; return text;
        }
        private void Update()
        {
            var repo = profile?.Repository;
            if (repo == null || metadata == null) return;
            var player = observer.Raid.Participants.First(p => p.ProfileId == repo.ProfileId);
            if (Keyboard.current != null)
            {
                if (Keyboard.current.enterKey.wasPressedThisFrame) observer.StartSession();
                if (Keyboard.current.rKey.wasPressedThisFrame && player.CanOperate)
                {
                    var relic = player.ReadInventorySnapshot().Backpack.FirstOrDefault(i => i.HasValue && i.Value.Category == QusapLootCategory.Relic);
                    if (relic.HasValue) player.TryMoveToSecurePocket(relic.Value.LootInstanceId);
                }
            }
            var store = repo.Storage;
            metadata.text = $"LOCAL PROFILE / STASH | {store.Source}\nProfileId: {repo.ProfileId}\n"
                + $"SchemaVersion: {repo.SchemaVersion} | Revision: {repo.Revision} | Escrituras: {store.Writes}\n"
                + $"NextItem: {repo.NextItemInstanceId} | NextNativeWeapon: {repo.NextNativeWeaponInstanceId}\n"
                + $"SHA-256: {repo.Checksum.Substring(0, 32)}\n{repo.Checksum.Substring(32)}\n"
                + $"{store.MainPath}\nMain: {store.MainExists} | tmp: {store.TemporaryExists} | bak: {store.BackupExists}\n"
                + $"Resultado: {repo.LastResult}";
            var stash = repo.ReadStashSnapshot(repo.ProfileId);
            objects.text = $"STASH: {stash.Count} | Duplicados: {stash.Count - stash.Select(i => i.LootInstanceId).Distinct().Count()}\n"
                + string.Join("\n", stash.Select(i => i.LootInstanceId + "\n" + i.DefinitionId
                    + (i.Weapon != null ? " | WeaponId " + i.Weapon.InstanceId : "")));
            var extraction = player.GetComponent<QusapRaidExtraction>();
            action.text = DiagnosticAction + $"\nP1: {extraction.Status} | Restante: {extraction.Countdown.Remaining:0.00}s"
                + $" | Mochila: {player.ReadInventorySnapshot().Backpack.Count(i => i.HasValue)} | Bolsillo: {(player.ReadInventorySnapshot().SecurePocket.HasValue ? 1 : 0)}";
        }
    }
}
