using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(QusapHitReceiver))]
    public sealed class QusapHitReactionVisual : MonoBehaviour
    {
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");

        [Header("References")]
        [SerializeField] private Transform playerVisual;
        [SerializeField] private Renderer[] renderers;

        [Header("Provisional Reaction")]
        [SerializeField, Min(0.01f)] private float reactionDuration = 0.16f;
        [SerializeField, Range(0f, 20f)] private float tiltDegrees = 7f;
        [SerializeField, Range(0f, 0.2f)] private float squashAmount = 0.06f;
        [SerializeField, Range(0f, 0.25f)] private float verticalOffset = 0.04f;
        [SerializeField] private Color flashColor = new(1f, 0.35f, 0.2f, 1f);
        [SerializeField, Range(0f, 1f)] private float flashIntensity = 0.65f;

        private QusapHitReceiver hitReceiver;
        private MaterialPropertyBlock[] originalPropertyBlocks;
        private MaterialPropertyBlock reactionPropertyBlock;
        private Vector3 originalLocalPosition;
        private Vector3 originalLocalScale;
        private Vector3 originalLocalEulerAngles;
        private float reactionTimeRemaining;
        private int reactionDirection = 1;
        private bool visualIsReacting;
        private bool missingVisualWarningIssued;

        private void Awake()
        {
            hitReceiver = GetComponent<QusapHitReceiver>();
            ResolveVisualReferences();

            if (playerVisual == null)
            {
                WarnAboutMissingVisual();
                enabled = false;
                return;
            }

            if (renderers == null || renderers.Length == 0)
            {
                renderers = playerVisual.GetComponentsInChildren<Renderer>(true);
            }

            originalLocalPosition = playerVisual.localPosition;
            originalLocalScale = playerVisual.localScale;
            originalLocalEulerAngles = playerVisual.localEulerAngles;
            CaptureOriginalPropertyBlocks();
        }

        public void Configure(Transform visual, Renderer[] visualRenderers = null)
        {
            playerVisual = visual;
            renderers = visualRenderers != null && visualRenderers.Length > 0
                ? visualRenderers
                : visual != null
                    ? visual.GetComponentsInChildren<Renderer>(true)
                    : new Renderer[0];
        }

        private void OnEnable()
        {
            if (hitReceiver != null)
            {
                hitReceiver.HitReceived += HandleHitReceived;
            }
        }

        private void OnDisable()
        {
            if (hitReceiver != null)
            {
                hitReceiver.HitReceived -= HandleHitReceived;
            }

            RestoreVisual();
            reactionTimeRemaining = 0f;
            visualIsReacting = false;
        }

        private void Update()
        {
            if (!visualIsReacting)
            {
                return;
            }

            reactionTimeRemaining = Mathf.Max(reactionTimeRemaining - Time.deltaTime, 0f);
        }

        private void LateUpdate()
        {
            if (playerVisual == null)
            {
                return;
            }

            if (!visualIsReacting)
            {
                return;
            }

            if (reactionTimeRemaining <= 0f)
            {
                RestoreVisual();
                visualIsReacting = false;
                return;
            }

            float normalizedTime = 1f - reactionTimeRemaining / reactionDuration;
            float weight = Mathf.Sin(normalizedTime * Mathf.PI);
            float currentFacingYaw = playerVisual.localEulerAngles.y;
            float tilt = -reactionDirection * tiltDegrees * weight;
            float squash = squashAmount * weight;

            playerVisual.localPosition = originalLocalPosition + Vector3.up * (verticalOffset * weight);
            playerVisual.localScale = new Vector3(
                originalLocalScale.x * (1f + squash),
                originalLocalScale.y * (1f - squash),
                originalLocalScale.z * (1f + squash));
            playerVisual.localRotation = Quaternion.Euler(
                originalLocalEulerAngles.x,
                currentFacingYaw,
                originalLocalEulerAngles.z + tilt);

            ApplyFlash(weight * flashIntensity);
        }

        private void OnValidate()
        {
            reactionDuration = Mathf.Max(reactionDuration, 0.01f);
            tiltDegrees = Mathf.Max(tiltDegrees, 0f);
            squashAmount = Mathf.Clamp(squashAmount, 0f, 0.2f);
            verticalOffset = Mathf.Max(verticalOffset, 0f);
            flashIntensity = Mathf.Clamp01(flashIntensity);
        }

        private void HandleHitReceived(QusapHitInfo hitInfo)
        {
            reactionDirection = hitInfo.HorizontalDirection < 0 ? -1 : 1;
            reactionTimeRemaining = reactionDuration;
            visualIsReacting = true;
        }

        private void ResolveVisualReferences()
        {
            if (playerVisual == null)
            {
                playerVisual = transform.Find("PlayerVisual");
            }

            if (playerVisual == null)
            {
                foreach (Transform candidate in GetComponentsInChildren<Transform>(true))
                {
                    if (candidate != transform && candidate.name == "PlayerVisual")
                    {
                        playerVisual = candidate;
                        break;
                    }
                }
            }

            if (playerVisual != null && (renderers == null || renderers.Length == 0))
            {
                renderers = playerVisual.GetComponentsInChildren<Renderer>(true);
            }
        }

        private void WarnAboutMissingVisual()
        {
            if (missingVisualWarningIssued)
            {
                return;
            }

            missingVisualWarningIssued = true;
            Debug.LogWarning(
                $"{nameof(QusapHitReactionVisual)} could not resolve PlayerVisual on '{name}'. "
                + "Only the provisional visual hit reaction has been disabled; gameplay remains active.",
                this);
        }

        private void CaptureOriginalPropertyBlocks()
        {
            originalPropertyBlocks = new MaterialPropertyBlock[renderers.Length];
            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer targetRenderer = renderers[index];
                if (targetRenderer == null)
                {
                    continue;
                }

                MaterialPropertyBlock originalBlock = new();
                targetRenderer.GetPropertyBlock(originalBlock);
                originalPropertyBlocks[index] = originalBlock;
            }

            reactionPropertyBlock = new MaterialPropertyBlock();
        }

        private void ApplyFlash(float weight)
        {
            Color reactionColor = Color.Lerp(Color.white, flashColor, weight);
            foreach (Renderer targetRenderer in renderers)
            {
                if (targetRenderer == null)
                {
                    continue;
                }

                targetRenderer.GetPropertyBlock(reactionPropertyBlock);
                reactionPropertyBlock.SetColor(BaseColorProperty, reactionColor);
                reactionPropertyBlock.SetColor(ColorProperty, reactionColor);
                targetRenderer.SetPropertyBlock(reactionPropertyBlock);
                reactionPropertyBlock.Clear();
            }
        }

        private void RestoreVisual()
        {
            if (playerVisual == null)
            {
                return;
            }

            float currentFacingYaw = playerVisual.localEulerAngles.y;
            playerVisual.localPosition = originalLocalPosition;
            playerVisual.localScale = originalLocalScale;
            playerVisual.localRotation = Quaternion.Euler(
                originalLocalEulerAngles.x,
                currentFacingYaw,
                originalLocalEulerAngles.z);

            if (renderers == null || originalPropertyBlocks == null)
            {
                return;
            }

            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index] != null)
                {
                    renderers[index].SetPropertyBlock(originalPropertyBlocks[index]);
                }
            }
        }
    }
}
