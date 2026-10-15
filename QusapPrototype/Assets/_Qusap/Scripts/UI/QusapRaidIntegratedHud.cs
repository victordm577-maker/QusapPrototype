using System.Linq;
using UnityEngine;

namespace Qusap
{
    [RequireComponent(typeof(QusapRaidIntegratedPlaytest))]
    public sealed class QusapRaidIntegratedHud : MonoBehaviour
    {
        private QusapRaidIntegratedPlaytest scene;
        private GUIStyle text, title, small, selected;
        private readonly Vector2[] scroll = new Vector2[2];
        private Vector2 inventoryScroll;
        private void Awake() => scene = GetComponent<QusapRaidIntegratedPlaytest>();
        private void Styles()
        {
            if (text != null) return;
            text = new(GUI.skin.label) { fontSize = 17, wordWrap = true };
            title = new(text) { fontSize = 21, fontStyle = FontStyle.Bold };
            small = new(text) { fontSize = 14 };
            selected = new(text); selected.normal.textColor = Color.yellow;
        }
        private void OnGUI()
        {
            if (scene.Profiles == null) return;
            Styles(); var previous = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new(Screen.width / 1280f, Screen.height / 720f, 1));
            if (scene.Preparing) Preparation(); else Gameplay();
            GUI.matrix = previous;
        }
        private void Preparation()
        {
            GUI.Box(new(100, 45, 1080, 620), "");
            GUI.Label(new(120, 55, 1040, 32), "INCURSIÓN INTEGRADA · PREPARACIÓN LOCAL", title);
            GUI.Label(new(120, 90, 1040, 42), "Perfiles independientes en memoria · equipo inicial diagnóstico entregado una sola vez · no se repone tras morir", small);
            if (GUI.Button(new(120, 135, 470, 32), scene.ReducedPreset ? "REDUCIDO 120/20/5 · R / Select para alternar" : "NORMAL 480/180/5 · R / Select para alternar"))
                scene.SetPreset(!scene.ReducedPreset);
            GUI.Label(new(610, 138, 520, 30), "P1: ↑/↓ elegir · Enter alternar · P listo | P2: D-pad · A · Start", small);
            for (int i = 0; i < 2; i++)
            {
                float x = 120 + i * 530;
                GUI.Label(new(x, 180, 490, 35), "P" + (i + 1) + " · " + (scene.IsReady(i) ? "LISTO" : "elige equipo") + " · Cuerno " + (scene.Profiles[i].HasTriumphHorn ? "sí" : "no"), title);
                GUI.Label(new(x, 217, 490, 24), "Perfil " + scene.Profiles[i].ProfileId.Substring(0, 11) + " · revisión " + scene.Profiles[i].Revision, small);
                var choices = scene.LoadoutChoices(i);
                GUI.enabled = !scene.IsReady(i);
                if (GUI.Button(new(x, 248, 490, 30), (scene.WeaponSelection(i) == null ? "✓ " : "") + "Préstamo de incursión · no persiste")) scene.ToggleLoadout(i, null);
                scroll[i] = GUI.BeginScrollView(new(x, 285, 490, 235), scroll[i], new(0, 0, 465, Mathf.Max(230, choices.Length * 38)));
                for (int j = 0; j < choices.Length; j++)
                {
                    var item = choices[j]; bool chosen = scene.WeaponSelection(i) == item.LootInstanceId || scene.ConsumableSelected(i, item.LootInstanceId);
                    string prefix = scene.PreparationSelection(i) == j + 1 ? "> " : "";
                    if (GUI.Button(new(0, j * 38, 460, 34), prefix + (chosen ? "✓ " : "") + item.DisplayName + " x" + item.Quantity)) scene.ToggleLoadout(i, item.LootInstanceId);
                }
                GUI.EndScrollView(); GUI.enabled = true;
                GUI.Label(new(x, 525, 490, 26), "Stash completo: " + scene.Stash(i).Length + " objetos / stacks", small);
                if (GUI.Button(new(x, 565, 490, 38), scene.IsReady(i) ? "Cambiar preparación" : "Confirmar P" + (i + 1))) scene.SetReady(i, !scene.IsReady(i));
            }
            GUI.Label(new(120, 620, 1030, 32), "Ambos confirman para desplegar · mochila 6 / bolsillo 1 · 100 HP · conecta el gamepad antes de Play.", small);
        }
        private void Gameplay()
        {
            var session = scene.Context.Session; var clock = session.Timing; var hazard = scene.Hazard;
            GUI.Box(new(12, 10, 1256, 94), "");
            GUI.Label(new(24, 15, 1215, 26), (scene.ReducedPreset ? "REDUCIDO" : "NORMAL") + " · " + clock.ElapsedSeconds.ToString("F1") + " / " + clock.Settings.DurationSeconds + " s"
                + " · salidas " + (session.CanExtract ? "ABIERTAS" : "cerradas hasta " + clock.Settings.ExtractionOpeningSeconds + " s")
                + " · peligro " + (hazard.WarningActive ? "¡AVISO! " : "") + (hazard.StageIndex + 1)
                + (hazard.NextStage == null ? " · área final" : " · cierre en " + Mathf.Max(0, (float)(hazard.NextStage.StartsAt - clock.ElapsedSeconds)).ToString("F1") + " s"), text);
            for (int i = 0; i < 2; i++)
            {
                var p = scene.Raid.Participants[i]; var inv = p.ReadInventorySnapshot(); var extraction = p.GetComponent<QusapRaidExtraction>();
                string state = inv.Status == QusapRaidInventoryStatus.Winner ? "No aplica · Winner" : extraction.Status + " " + extraction.Countdown.Elapsed.ToString("F1") + "/5 s";
                GUI.Label(new(24 + i * 620, 48, 610, 46), p.ParticipantId + " · HP " + p.Receiver.CurrentHealth.ToString("F0") + "/" + p.Receiver.MaxHealth
                    + " · mochila " + inv.Backpack.Count(item => item.HasValue) + "/6 · bolsillo " + (inv.SecurePocket.HasValue ? "1/1" : "0/1")
                    + "\n" + inv.Status + " · " + state + (hazard.IsExposed(p) ? " · EXPUESTO" : ""), text);
            }
            GUI.Box(new(12, 608, 1256, 100), "");
            GUI.Label(new(24, 614, 1210, 25), scene.Message, small);
            for (int i = 0; i < scene.Interactors.Length; i++)
            {
                var interactor = scene.Interactors[i]; var chosen = interactor.Observed?.Items.FirstOrDefault(item => item.LootInstanceId == interactor.SelectedInstanceId);
                string loot = chosen.HasValue && chosen.Value.LootInstanceId != null ? chosen.Value.DisplayName + " x" + chosen.Value.Quantity : interactor.Sensor.Target?.SpawnPointId ?? "acércate a loot";
                GUI.Label(new(24 + 620 * i, 642, 610, 27), "P" + (i + 1) + " · " + loot + " · " + interactor.Progress.Elapsed.ToString("F1") + "/0.45 · " + (interactor.LastReply?.Result.ToString() ?? "Sin solicitud"), small);
            }
            GUI.Label(new(24, 673, 1150, 25), "P1: F abrir · T tomar · B bolsillo · H curar · Tab detalles | P2: ↑ abrir · R3 tomar · L3 bolsillo · Select curar · Start detalles", small);
            if (GUI.Button(new(1150, 112, 115, 28), "Detalles")) scene.DetailsVisible = !scene.DetailsVisible;
            if (GUI.Button(new(1008, 112, 135, 28), "LT diagnóstico")) scene.ParryDiagnostic.Visible = !scene.ParryDiagnostic.Visible;
            if (scene.DetailsVisible) Details();
        }
        private void Details()
        {
            GUI.Box(new(870, 145, 398, 455), "");
            if (GUI.Button(new(882, 155, 125, 28), "Inventario P1")) scene.DetailPlayer = 0;
            if (GUI.Button(new(1012, 155, 125, 28), "Inventario P2")) scene.DetailPlayer = 1;
            if (GUI.Button(new(1142, 155, 110, 28), "Cerrar")) scene.DetailsVisible = false;
            int player = scene.DetailPlayer; var inventory = scene.Raid.Participants[player].ReadInventorySnapshot();
            float y = 193;
            if (scene.Running)
            {
                GUI.Label(new(882, y, 365, 27), "Arma: " + (inventory.EquippedWeapon?.DisplayName ?? "vacío"), small); y += 27;
                if (GUI.Button(new(882, y, 365, 25), "Guardar espada equipada en mochila")) scene.InventoryAction(player, "store"); y += 30;
                inventoryScroll = GUI.BeginScrollView(new(882, 250, 365, 291), inventoryScroll, new(0, 0, 345, 345));
                y = 0;
                for (int index = 0; index < inventory.Backpack.Count; index++)
                {
                    var slot = inventory.Backpack[index];
                    if (!slot.HasValue)
                    { GUI.Label(new(0, y, 335, 20), (scene.InventorySelection(player) == index ? "> " : "") + "Slot " + (index + 1) + " · vacío", small); y += 20; continue; }
                    var item = slot.Value;
                    GUI.Label(new(0, y, 335, 22), (scene.InventorySelection(player) == index ? "> " : "") + item.DisplayName + " x" + item.Quantity + (item.RaidLoaner ? " · préstamo" : ""), small); y += 22;
                    if (GUI.Button(new(0, y, 160, 24), "Proteger en bolsillo")) scene.InventoryAction(player, "pocket", item.LootInstanceId);
                    if (item.Category == QusapLootCategory.Weapon)
                    { if (GUI.Button(new(168, y, 170, 24), "Equipar espada")) scene.InventoryAction(player, "equip", item.LootInstanceId); }
                    else if (item.Category == QusapLootCategory.Consumable)
                    { if (GUI.Button(new(168, y, 170, 24), "Usar consumible")) scene.InventoryAction(player, "consume", item.LootInstanceId); }
                    y += 28;
                }
                if (inventory.SecurePocket.HasValue && GUI.Button(new(0, y, 335, 26), "Sacar " + inventory.SecurePocket.Value.DisplayName + " del bolsillo"))
                    scene.InventoryAction(player, "backpack", inventory.SecurePocket.Value.LootInstanceId);
                GUI.EndScrollView();
                GUI.Label(new(882, 553, 365, 40), "P1: ↑↓ / Enter · Z guardar · X sacar bolsillo\nP2: stick derecho ←→ / R3 · ↑ guardar · ↓ sacar", small);
            }
            else
            {
                GUI.Label(new(882, y, 365, 140), scene.LastResults, small); y += 145;
                GUI.Label(new(882, y, 365, 24), "STASH P" + (player + 1) + " · mismos perfiles", text); y += 27;
                foreach (var item in scene.Stash(player))
                { GUI.Label(new(882, y, 365, 22), item.DisplayName + " x" + item.Quantity, small); y += 23; }
                GUI.enabled = scene.CanPrepareNext;
                if (GUI.Button(new(882, 553, 365, 35), "Nueva preparación · N / gamepad A")) scene.PrepareNext();
                GUI.enabled = true;
            }
        }
    }
}
