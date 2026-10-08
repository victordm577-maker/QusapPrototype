using UnityEngine;
using UnityEngine.UI;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapVitalityDebugHud : MonoBehaviour
    {
        [SerializeField] private QusapCombatArenaController arena;
        private readonly QusapHitReceiver[] players = new QusapHitReceiver[2];
        private readonly Text[] labels = new Text[2];
        private readonly RectTransform[] fills = new RectTransform[2];
        private Canvas canvas;

        public void Configure(QusapCombatArenaController configuredArena) => arena = configuredArena;
        public string PlayerLabel(int index) => labels[index] != null ? labels[index].text : string.Empty;

        private void Awake()
        {
            if (arena == null) arena = FindAnyObjectByType<QusapCombatArenaController>();
            if (arena == null) return;
            players[0] = arena.PlayerOne.GetComponent<QusapHitReceiver>();
            players[1] = arena.PlayerTwo.GetComponent<QusapHitReceiver>();
            var root = new GameObject("VitalityCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int i = 0; i < 2; i++)
            {
                var panel = Rect("P" + (i + 1), root.transform);
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(i, 1);
                panel.anchoredPosition = new Vector2(i == 0 ? 24 : -24, -20);
                panel.sizeDelta = new Vector2(340, 86);
                panel.gameObject.AddComponent<Image>().color = new Color(0.04f, 0.06f, 0.09f, 0.94f);
                var label = Rect("Health", panel);
                label.anchorMin = new Vector2(0, 0.4f); label.anchorMax = Vector2.one;
                label.offsetMin = new Vector2(12, 0); label.offsetMax = new Vector2(-12, -4);
                labels[i] = label.gameObject.AddComponent<Text>();
                labels[i].font = font; labels[i].fontSize = 25;
                labels[i].alignment = TextAnchor.MiddleLeft;
                labels[i].color = i == 0 ? new Color(0.5f, 0.82f, 1) : new Color(0.9f, 0.64f, 1);
                var bar = Rect("HealthBar", panel);
                bar.anchorMin = new Vector2(0, 0); bar.anchorMax = new Vector2(1, 0.4f);
                bar.offsetMin = new Vector2(12, 12); bar.offsetMax = new Vector2(-12, -5);
                bar.gameObject.AddComponent<Image>().color = new Color(0.18f, 0.2f, 0.24f);
                fills[i] = Rect("RemainingHealth", bar);
                fills[i].anchorMin = Vector2.zero; fills[i].anchorMax = Vector2.one;
                fills[i].offsetMin = fills[i].offsetMax = Vector2.zero;
                fills[i].gameObject.AddComponent<Image>().color = new Color(0.25f, 0.8f, 0.4f);
            }
            Refresh();
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private void OnEnable()
        {
            if (canvas != null) canvas.enabled = true;
            foreach (var player in players)
            {
                if (player == null) continue;
                player.HealthChanged += Changed;
                player.Eliminated += Eliminated;
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (canvas != null) canvas.enabled = false;
            foreach (var player in players)
            {
                if (player == null) continue;
                player.HealthChanged -= Changed;
                player.Eliminated -= Eliminated;
            }
        }

        private void Changed(QusapHealthChange _) => Refresh();
        private void Eliminated(QusapHitReceiver _) => Refresh();
        private void Refresh()
        {
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (player == null || labels[i] == null) continue;
                labels[i].text = player.IsEliminated ? $"P{i + 1}  ELIMINADO"
                    : $"P{i + 1}  VIDA {player.CurrentHealth:0.##} / {player.MaxHealth:0.##}";
                fills[i].anchorMax = new Vector2(player.CurrentHealth / player.MaxHealth, 1);
            }
        }
    }
}
