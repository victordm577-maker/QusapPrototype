using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapParryCuePresenter : MonoBehaviour
    {
        internal const string VisualObjectName = "QusapParryCueVisual";

        private QusapCombatController combatController;
        private QusapParryCueVisualSettings settings;
        private GameObject visualObject;
        private SpriteRenderer cueRenderer;
        private Sprite runtimeSprite;
        private double successFlashEndsAt;
        private bool successFlashActive;
        private bool suppressUntilIncomingFinishersClear;

        public SpriteRenderer CueRenderer => cueRenderer;
        public bool IsShowingWindow { get; private set; }
        public bool IsShowingSuccessFlash => successFlashActive && cueRenderer != null && cueRenderer.enabled;
        public int SuccessFlashCount { get; private set; }
        public QusapParryCueInfo CurrentCue { get; private set; }

        private void Awake()
        {
            combatController = GetComponent<QusapCombatController>();
            EnsureVisualObject();
            Hide();
        }

        private void OnEnable()
        {
            if (combatController == null)
            {
                combatController = GetComponent<QusapCombatController>();
            }

            if (combatController != null)
            {
                combatController.ParrySucceeded -= HandleParrySucceeded;
                combatController.ParrySucceeded += HandleParrySucceeded;
            }

            EnsureVisualObject();
            Hide();
        }

        private void OnDisable()
        {
            if (combatController != null)
            {
                combatController.ParrySucceeded -= HandleParrySucceeded;
            }

            ResetPresentation();
        }

        private void OnDestroy()
        {
            if (combatController != null)
            {
                combatController.ParrySucceeded -= HandleParrySucceeded;
            }

            DestroyRuntimeObject(runtimeSprite);
            runtimeSprite = null;
            if (visualObject != null)
            {
                DestroyRuntimeObject(visualObject);
                visualObject = null;
                cueRenderer = null;
            }
        }

        private void Update()
        {
            Refresh(InputState.currentTime, Time.unscaledTimeAsDouble);
        }

        internal void Initialize(
            QusapCombatController owner,
            QusapParryCueVisualSettings visualSettings)
        {
            if (combatController != null && combatController != owner)
            {
                combatController.ParrySucceeded -= HandleParrySucceeded;
            }

            combatController = owner;
            Configure(visualSettings);

            if (isActiveAndEnabled && combatController != null)
            {
                combatController.ParrySucceeded -= HandleParrySucceeded;
                combatController.ParrySucceeded += HandleParrySucceeded;
            }
        }

        internal void Configure(QusapParryCueVisualSettings visualSettings)
        {
            settings = visualSettings ?? QusapParryCueVisualSettings.CreateDefault();
            settings.ValidateSerializedValues();
            EnsureVisualObject();
            ApplyStaticSettings();
            if (!settings.IndicatorEnabled)
            {
                ResetPresentation();
            }
        }

        internal void ResetForRespawn()
        {
            suppressUntilIncomingFinishersClear = combatController != null
                && combatController.IncomingFinisherCount > 0;
            ResetPresentation(keepRespawnSuppression: true);
        }

        internal void Refresh(double inputTimestamp, double visualTimestamp)
        {
            EnsureVisualObject();
            if (cueRenderer == null
                || settings == null
                || !settings.IndicatorEnabled
                || combatController == null
                || !combatController.isActiveAndEnabled
                || !combatController.gameObject.activeInHierarchy)
            {
                ResetPresentation();
                return;
            }

            if (suppressUntilIncomingFinishersClear)
            {
                if (combatController.IncomingFinisherCount > 0)
                {
                    ResetPresentation(keepRespawnSuppression: true);
                    return;
                }

                suppressUntilIncomingFinishersClear = false;
            }

            if (successFlashActive)
            {
                if (visualTimestamp < successFlashEndsAt)
                {
                    ShowSuccessFlash();
                    return;
                }

                successFlashActive = false;
            }

            if (!combatController.TryGetCurrentParryCue(inputTimestamp, out QusapParryCueInfo cueInfo))
            {
                ResetPresentation();
                return;
            }

            CurrentCue = cueInfo;
            IsShowingWindow = true;
            cueRenderer.enabled = true;

            double elapsed = cueInfo.SampledAt - cueInfo.WindowOpensAt;
            float pulse = 0.5f + 0.5f * Mathf.Sin(
                (float)(elapsed * settings.PulseFrequency * Mathf.PI * 2f));
            float size = Mathf.Lerp(settings.MinimumSize, settings.MaximumSize, pulse);
            cueRenderer.transform.localScale = new Vector3(size, size, 1f);
            Color color = settings.WindowColor;
            color.a *= Mathf.Lerp(0.65f, 1f, pulse);
            cueRenderer.color = color;
        }

        private void HandleParrySucceeded(QusapCombatController attacker, QusapComboId comboId)
        {
            if (settings == null || !settings.IndicatorEnabled || !isActiveAndEnabled)
            {
                return;
            }

            successFlashActive = true;
            successFlashEndsAt = Time.unscaledTimeAsDouble + settings.SuccessFlashDuration;
            SuccessFlashCount++;
            ShowSuccessFlash();
        }

        private void ShowSuccessFlash()
        {
            EnsureVisualObject();
            if (cueRenderer == null)
            {
                return;
            }

            CurrentCue = default;
            IsShowingWindow = false;
            cueRenderer.enabled = true;
            cueRenderer.color = settings.SuccessColor;
            float size = settings.MaximumSize;
            cueRenderer.transform.localScale = new Vector3(size, size, 1f);
        }

        internal void ResetPresentation(bool keepRespawnSuppression = false)
        {
            if (!keepRespawnSuppression)
            {
                suppressUntilIncomingFinishersClear = false;
            }

            successFlashActive = false;
            successFlashEndsAt = 0d;
            Hide();
        }

        private void Hide()
        {
            CurrentCue = default;
            IsShowingWindow = false;
            if (cueRenderer != null)
            {
                cueRenderer.enabled = false;
            }
        }

        private void EnsureVisualObject()
        {
            if (cueRenderer != null)
            {
                return;
            }

            visualObject = new GameObject(VisualObjectName);
            visualObject.transform.SetParent(transform, false);
            visualObject.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            cueRenderer = visualObject.AddComponent<SpriteRenderer>();

            Texture2D texture = Texture2D.whiteTexture;
            runtimeSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                Mathf.Max(texture.width, texture.height));
            runtimeSprite.name = "QusapParryCueRuntimeSprite";
            cueRenderer.sprite = runtimeSprite;
            ApplyStaticSettings();
        }

        private void ApplyStaticSettings()
        {
            if (cueRenderer == null || settings == null)
            {
                return;
            }

            cueRenderer.transform.localPosition = settings.LocalOffset;
            cueRenderer.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            cueRenderer.sortingOrder = settings.SortingOrder;
        }

        private static void DestroyRuntimeObject(Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
