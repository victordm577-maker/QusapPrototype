using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapCombatFeedbackPresenter : MonoBehaviour
    {
        internal const string VisualObjectPrefix = "QusapCombatFeedback_";

        private readonly List<FeedbackSlot> pool = new();
        private readonly HashSet<ulong> processedParryPresses = new();
        private readonly HashSet<FailureKey> markedFinisherFailures = new();
        private QusapCombatController combatController;
        private QusapCombatFeedbackSettings settings;
        private Sprite runtimeSprite;
        private ulong activationSequence;
        private bool subscribed;
        private bool hasVisualTimestamp;
        private double lastVisualTimestamp;

        public int PoolCapacity => pool.Count;
        public int ActiveEffectCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < pool.Count; i++)
                {
                    if (pool[i].Active)
                    {
                        count++;
                    }
                }

                return count;
            }
        }
        public int TotalFeedbackCount { get; private set; }
        public int FailureMarkCount { get; private set; }
        public int LastActivatedSlotIndex { get; private set; } = -1;
        public QusapCombatFeedbackEvent? LastFeedback { get; private set; }

        private void Awake()
        {
            combatController = GetComponent<QusapCombatController>();
        }

        private void OnEnable()
        {
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ResetPresentation();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                ResetPresentation();
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            for (int i = 0; i < pool.Count; i++)
            {
                DestroyRuntimeObject(pool[i].Root);
            }

            pool.Clear();
            DestroyRuntimeObject(runtimeSprite);
            runtimeSprite = null;
        }

        private void Update()
        {
            Refresh(Time.unscaledTimeAsDouble);
        }

        internal void Initialize(
            QusapCombatController owner,
            QusapCombatFeedbackSettings feedbackSettings)
        {
            if (combatController != owner)
            {
                Unsubscribe();
                combatController = owner;
            }

            Configure(feedbackSettings);
            Subscribe();
        }

        internal void Configure(QusapCombatFeedbackSettings feedbackSettings)
        {
            settings = feedbackSettings ?? QusapCombatFeedbackSettings.CreateDefault();
            settings.ValidateSerializedValues();
            EnsureRuntimeSprite();
            EnsurePool(settings.PoolCapacity);
            ApplySortingOrder();
            if (!settings.FeedbackEnabled)
            {
                ResetPresentation();
            }
        }

        internal void Refresh(double visualTimestamp)
        {
            if (!IsFinite(visualTimestamp)
                || (hasVisualTimestamp && visualTimestamp < lastVisualTimestamp))
            {
                ResetPresentation();
                return;
            }

            hasVisualTimestamp = true;
            lastVisualTimestamp = visualTimestamp;
            bool anyActive = false;
            for (int i = 0; i < pool.Count; i++)
            {
                FeedbackSlot slot = pool[i];
                if (!slot.Active)
                {
                    continue;
                }

                double elapsed = Math.Max(0d, visualTimestamp - slot.StartedAt);
                if (slot.Duration <= 0f || elapsed >= slot.Duration)
                {
                    HideSlot(slot);
                    continue;
                }

                float progress = Mathf.Clamp01((float)(elapsed / slot.Duration));
                float expansion = 1f - Mathf.Pow(1f - progress, settings.ExpansionSpeed);
                float fade = 1f - progress;
                slot.Root.transform.position = slot.WorldPosition;
                slot.Primary.transform.localScale = Vector3.Lerp(
                    slot.PrimaryStartScale, slot.PrimaryEndScale, expansion);
                Color primaryColor = slot.PrimaryBaseColor;
                primaryColor.a *= fade;
                slot.Primary.color = primaryColor;

                if (slot.Secondary.enabled)
                {
                    slot.Secondary.transform.localScale = Vector3.Lerp(
                        slot.SecondaryStartScale, slot.SecondaryEndScale, expansion);
                    Color secondaryColor = slot.SecondaryBaseColor;
                    secondaryColor.a *= fade;
                    slot.Secondary.color = secondaryColor;
                }

                anyActive = true;
            }

            if (!anyActive)
            {
                LastFeedback = null;
            }
        }

        internal void ResetForRespawn()
        {
            ResetPresentation();
        }

        internal void ResetPresentation()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                HideSlot(pool[i]);
            }

            processedParryPresses.Clear();
            markedFinisherFailures.Clear();
            hasVisualTimestamp = false;
            lastVisualTimestamp = 0d;
            LastFeedback = null;
            LastActivatedSlotIndex = -1;
        }

        internal GameObject GetFeedbackObject(int index)
        {
            return pool[index].Root;
        }

        internal SpriteRenderer GetPrimaryRenderer(int index)
        {
            return pool[index].Primary;
        }

        internal SpriteRenderer GetSecondaryRenderer(int index)
        {
            return pool[index].Secondary;
        }

        private void HandleFinisherResolved(QusapFinisherResolution resolution)
        {
            if (!CanShowFeedback())
            {
                return;
            }

            QusapCombatFeedbackType feedbackType;
            if (resolution.Outcome == QusapFinisherResolutionOutcome.Whiffed)
            {
                feedbackType = QusapCombatFeedbackType.Whiff;
            }
            else if (resolution.Outcome == QusapFinisherResolutionOutcome.Rejected)
            {
                feedbackType = QusapCombatFeedbackType.Rejected;
            }
            else
            {
                feedbackType = resolution.ComboId switch
                {
                    QusapComboId.Damage => QusapCombatFeedbackType.Damage,
                    QusapComboId.Launch => QusapCombatFeedbackType.Launch,
                    QusapComboId.Disarm => QusapCombatFeedbackType.Disarm,
                    _ => QusapCombatFeedbackType.Rejected
                };
            }

            bool onAttacker = feedbackType == QusapCombatFeedbackType.Whiff
                || resolution.ExpectedTarget == null;
            Vector3 worldPosition = onAttacker
                ? combatController.transform.position + settings.AttackerOffset
                : resolution.ExpectedTarget.transform.position + settings.TargetOffset;
            double timestamp = Time.unscaledTimeAsDouble;
            QusapCombatFeedbackEvent feedbackEvent = new(
                feedbackType,
                combatController,
                resolution.ExpectedTarget,
                resolution.ComboId,
                worldPosition,
                timestamp,
                resolution.Outcome,
                null);
            Show(feedbackEvent);
        }

        private void HandleParryAttemptFinished(QusapParryAttemptFeedback attempt)
        {
            if (!processedParryPresses.Add(attempt.PressId)
                || !CanShowFeedback())
            {
                return;
            }

            if (attempt.Succeeded)
            {
                // QusapParryCuePresenter remains the sole owner of the green success flash.
                return;
            }

            if (!attempt.HasIncomingFinisher
                || (attempt.Outcome != QusapParryAttemptOutcome.TooEarly
                    && attempt.Outcome != QusapParryAttemptOutcome.TooLate
                    && attempt.Outcome != QusapParryAttemptOutcome.AlreadyAttempted))
            {
                return;
            }

            FailureKey key = new(
                EntityId.ToULong(attempt.Attacker.GetEntityId()),
                attempt.FinisherSequenceId);
            if (!markedFinisherFailures.Add(key))
            {
                return;
            }

            Vector3 worldPosition = transform.position + settings.TargetOffset;
            double timestamp = Time.unscaledTimeAsDouble;
            QusapCombatFeedbackEvent feedbackEvent = new(
                QusapCombatFeedbackType.ParryFailed,
                attempt.Attacker,
                combatController.HitReceiver,
                attempt.ComboId,
                worldPosition,
                timestamp,
                null,
                attempt.Outcome);
            Show(feedbackEvent);
            FailureMarkCount++;
        }

        private void Show(QusapCombatFeedbackEvent feedbackEvent)
        {
            FeedbackSlot slot = AcquireSlot();
            ConfigureSlot(slot, feedbackEvent);
            LastFeedback = feedbackEvent;
            TotalFeedbackCount++;
        }

        private FeedbackSlot AcquireSlot()
        {
            int selectedIndex = -1;
            ulong oldestSequence = ulong.MaxValue;
            for (int i = 0; i < pool.Count; i++)
            {
                FeedbackSlot candidate = pool[i];
                if (!candidate.Active)
                {
                    selectedIndex = i;
                    break;
                }

                if (candidate.ActivationSequence < oldestSequence)
                {
                    oldestSequence = candidate.ActivationSequence;
                    selectedIndex = i;
                }
            }

            LastActivatedSlotIndex = selectedIndex;
            return pool[selectedIndex];
        }

        private void ConfigureSlot(
            FeedbackSlot slot,
            QusapCombatFeedbackEvent feedbackEvent)
        {
            float duration;
            Vector3 maximumScale;
            Color primaryColor;
            Color secondaryColor = Color.clear;
            bool showSecondary = false;
            bool failureMark = false;

            switch (feedbackEvent.FeedbackType)
            {
                case QusapCombatFeedbackType.Damage:
                    duration = settings.DamageDuration;
                    maximumScale = Vector3.one * settings.DamageMaximumSize;
                    primaryColor = settings.DamageColor;
                    secondaryColor = settings.DamageCenterColor;
                    showSecondary = true;
                    break;
                case QusapCombatFeedbackType.Launch:
                    duration = settings.LaunchDuration;
                    maximumScale = new Vector3(
                        settings.LaunchMaximumHeight * 0.38f,
                        settings.LaunchMaximumHeight,
                        1f);
                    primaryColor = settings.LaunchColor;
                    break;
                case QusapCombatFeedbackType.Disarm:
                    duration = settings.DisarmDuration;
                    maximumScale = Vector3.one * settings.DisarmMaximumSize;
                    primaryColor = settings.DisarmColor;
                    showSecondary = feedbackEvent.FinisherOutcome
                        == QusapFinisherResolutionOutcome.DisarmSucceeded;
                    secondaryColor = settings.DisarmSuccessCenterColor;
                    break;
                case QusapCombatFeedbackType.Whiff:
                    duration = settings.WhiffDuration;
                    maximumScale = Vector3.one * settings.WhiffMaximumSize;
                    primaryColor = settings.WhiffColor;
                    break;
                case QusapCombatFeedbackType.ParryFailed:
                    duration = settings.FailedParryDuration;
                    maximumScale = new Vector3(
                        settings.FailedParryMaximumSize,
                        settings.FailedParryMaximumSize * 0.16f,
                        1f);
                    primaryColor = settings.FailedParryColor;
                    secondaryColor = settings.FailedParryColor;
                    showSecondary = true;
                    failureMark = true;
                    break;
                default:
                    duration = settings.RejectedDuration;
                    maximumScale = Vector3.one * settings.RejectedMaximumSize;
                    primaryColor = settings.RejectedColor;
                    break;
            }

            slot.Active = true;
            slot.ActivationSequence = ++activationSequence;
            slot.StartedAt = feedbackEvent.Timestamp;
            slot.Duration = duration;
            slot.WorldPosition = feedbackEvent.WorldPosition;
            slot.PrimaryBaseColor = primaryColor;
            slot.SecondaryBaseColor = secondaryColor;
            slot.PrimaryEndScale = maximumScale;
            slot.SecondaryEndScale = failureMark
                ? maximumScale
                : maximumScale * 0.36f;
            slot.PrimaryStartScale = slot.PrimaryEndScale * 0.20f;
            slot.SecondaryStartScale = slot.SecondaryEndScale * 0.20f;
            slot.Root.transform.position = slot.WorldPosition;
            slot.Primary.transform.localRotation = failureMark
                ? Quaternion.Euler(0f, 0f, 45f)
                : Quaternion.identity;
            slot.Secondary.transform.localRotation = failureMark
                ? Quaternion.Euler(0f, 0f, -45f)
                : Quaternion.identity;
            slot.Primary.transform.localScale = slot.PrimaryStartScale;
            slot.Secondary.transform.localScale = slot.SecondaryStartScale;
            slot.Primary.color = primaryColor;
            slot.Secondary.color = secondaryColor;
            slot.Primary.enabled = true;
            slot.Secondary.enabled = showSecondary;
            slot.Root.SetActive(true);
        }

        private bool CanShowFeedback()
        {
            return settings != null
                && settings.FeedbackEnabled
                && combatController != null
                && combatController.isActiveAndEnabled
                && combatController.gameObject.activeInHierarchy
                && pool.Count > 0;
        }

        private void Subscribe()
        {
            if (subscribed || combatController == null)
            {
                return;
            }

            combatController.FinisherResolved += HandleFinisherResolved;
            combatController.ParryAttemptFinished += HandleParryAttemptFinished;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed || combatController == null)
            {
                return;
            }

            combatController.FinisherResolved -= HandleFinisherResolved;
            combatController.ParryAttemptFinished -= HandleParryAttemptFinished;
            subscribed = false;
        }

        private void EnsureRuntimeSprite()
        {
            if (runtimeSprite != null)
            {
                return;
            }

            Texture2D texture = Texture2D.whiteTexture;
            runtimeSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                Mathf.Max(texture.width, texture.height));
            runtimeSprite.name = "QusapCombatFeedbackRuntimeSprite";
        }

        private void EnsurePool(int capacity)
        {
            while (pool.Count < capacity)
            {
                int index = pool.Count;
                GameObject root = new($"{VisualObjectPrefix}{index}");
                root.transform.SetParent(transform, false);
                GameObject primaryObject = new("Primary");
                primaryObject.transform.SetParent(root.transform, false);
                SpriteRenderer primary = primaryObject.AddComponent<SpriteRenderer>();
                GameObject secondaryObject = new("Secondary");
                secondaryObject.transform.SetParent(root.transform, false);
                SpriteRenderer secondary = secondaryObject.AddComponent<SpriteRenderer>();
                primary.sprite = runtimeSprite;
                secondary.sprite = runtimeSprite;
                FeedbackSlot slot = new(root, primary, secondary);
                pool.Add(slot);
                HideSlot(slot);
            }

            while (pool.Count > capacity)
            {
                int last = pool.Count - 1;
                DestroyRuntimeObject(pool[last].Root);
                pool.RemoveAt(last);
            }
        }

        private void ApplySortingOrder()
        {
            for (int i = 0; i < pool.Count; i++)
            {
                pool[i].Primary.sortingOrder = settings.SortingOrder;
                pool[i].Secondary.sortingOrder = Mathf.Min(
                    settings.SortingOrder + 1,
                    QusapCombatFeedbackSettings.MaximumSortingOrder);
            }
        }

        private static void HideSlot(FeedbackSlot slot)
        {
            slot.Active = false;
            slot.Primary.enabled = false;
            slot.Secondary.enabled = false;
            slot.Root.SetActive(false);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
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

        private sealed class FeedbackSlot
        {
            public FeedbackSlot(
                GameObject root,
                SpriteRenderer primary,
                SpriteRenderer secondary)
            {
                Root = root;
                Primary = primary;
                Secondary = secondary;
            }

            public GameObject Root { get; }
            public SpriteRenderer Primary { get; }
            public SpriteRenderer Secondary { get; }
            public bool Active { get; set; }
            public ulong ActivationSequence { get; set; }
            public double StartedAt { get; set; }
            public float Duration { get; set; }
            public Vector3 WorldPosition { get; set; }
            public Color PrimaryBaseColor { get; set; }
            public Color SecondaryBaseColor { get; set; }
            public Vector3 PrimaryStartScale { get; set; }
            public Vector3 PrimaryEndScale { get; set; }
            public Vector3 SecondaryStartScale { get; set; }
            public Vector3 SecondaryEndScale { get; set; }
        }

        private readonly struct FailureKey : IEquatable<FailureKey>
        {
            public FailureKey(ulong attackerId, ulong finisherSequenceId)
            {
                AttackerId = attackerId;
                FinisherSequenceId = finisherSequenceId;
            }

            private ulong AttackerId { get; }
            private ulong FinisherSequenceId { get; }

            public bool Equals(FailureKey other)
            {
                return AttackerId == other.AttackerId
                    && FinisherSequenceId == other.FinisherSequenceId;
            }

            public override bool Equals(object obj)
            {
                return obj is FailureKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (AttackerId.GetHashCode() * 397)
                        ^ FinisherSequenceId.GetHashCode();
                }
            }
        }
    }
}
