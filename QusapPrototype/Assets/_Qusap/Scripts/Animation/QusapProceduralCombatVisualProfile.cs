using System;
using UnityEngine;

namespace Qusap
{
    public readonly struct QusapProceduralCombatPoseValue
    {
        public QusapProceduralCombatPoseValue(QusapProceduralCombatPose pose)
        {
            pose ??= new QusapProceduralCombatPose();
            BodyLocalPosition = pose.BodyLocalPosition;
            BodyLocalEulerAngles = pose.BodyLocalEulerAngles;
            LeftFootLocalPosition = pose.LeftFootLocalPosition;
            LeftFootLocalEulerAngles = pose.LeftFootLocalEulerAngles;
            RightFootLocalPosition = pose.RightFootLocalPosition;
            RightFootLocalEulerAngles = pose.RightFootLocalEulerAngles;
            WeaponLocalPosition = pose.WeaponLocalPosition;
            WeaponLocalEulerAngles = pose.WeaponLocalEulerAngles;
        }

        private QusapProceduralCombatPoseValue(
            Vector3 bodyPosition, Vector3 bodyRotation,
            Vector3 leftPosition, Vector3 leftRotation,
            Vector3 rightPosition, Vector3 rightRotation,
            Vector3 weaponPosition, Vector3 weaponRotation)
        {
            BodyLocalPosition = bodyPosition;
            BodyLocalEulerAngles = bodyRotation;
            LeftFootLocalPosition = leftPosition;
            LeftFootLocalEulerAngles = leftRotation;
            RightFootLocalPosition = rightPosition;
            RightFootLocalEulerAngles = rightRotation;
            WeaponLocalPosition = weaponPosition;
            WeaponLocalEulerAngles = weaponRotation;
        }

        public Vector3 BodyLocalPosition { get; }
        public Vector3 BodyLocalEulerAngles { get; }
        public Vector3 LeftFootLocalPosition { get; }
        public Vector3 LeftFootLocalEulerAngles { get; }
        public Vector3 RightFootLocalPosition { get; }
        public Vector3 RightFootLocalEulerAngles { get; }
        public Vector3 WeaponLocalPosition { get; }
        public Vector3 WeaponLocalEulerAngles { get; }

        public static QusapProceduralCombatPoseValue Lerp(
            QusapProceduralCombatPose from,
            QusapProceduralCombatPose to,
            float t)
        {
            QusapProceduralCombatPoseValue a = new(from);
            QusapProceduralCombatPoseValue b = new(to);
            t = Mathf.Clamp01(float.IsFinite(t) ? t : 0f);
            return new QusapProceduralCombatPoseValue(
                Vector3.LerpUnclamped(a.BodyLocalPosition, b.BodyLocalPosition, t),
                Vector3.LerpUnclamped(a.BodyLocalEulerAngles, b.BodyLocalEulerAngles, t),
                Vector3.LerpUnclamped(a.LeftFootLocalPosition, b.LeftFootLocalPosition, t),
                Vector3.LerpUnclamped(a.LeftFootLocalEulerAngles, b.LeftFootLocalEulerAngles, t),
                Vector3.LerpUnclamped(a.RightFootLocalPosition, b.RightFootLocalPosition, t),
                Vector3.LerpUnclamped(a.RightFootLocalEulerAngles, b.RightFootLocalEulerAngles, t),
                Vector3.LerpUnclamped(a.WeaponLocalPosition, b.WeaponLocalPosition, t),
                Vector3.LerpUnclamped(a.WeaponLocalEulerAngles, b.WeaponLocalEulerAngles, t));
        }

        public QusapProceduralCombatPoseValue ScaledAndMirrored(float intensity, int facing)
        {
            int direction = facing < 0 ? -1 : 1;
            return new QusapProceduralCombatPoseValue(
                MirrorPosition(BodyLocalPosition, direction, intensity),
                MirrorRotation(BodyLocalEulerAngles, direction, intensity),
                MirrorPosition(LeftFootLocalPosition, direction, intensity),
                MirrorRotation(LeftFootLocalEulerAngles, direction, intensity),
                MirrorPosition(RightFootLocalPosition, direction, intensity),
                MirrorRotation(RightFootLocalEulerAngles, direction, intensity),
                MirrorPosition(WeaponLocalPosition, direction, intensity),
                MirrorRotation(WeaponLocalEulerAngles, direction, intensity));
        }

        private static Vector3 MirrorPosition(Vector3 value, int direction, float intensity)
        {
            value.x *= direction;
            return value * intensity;
        }

        private static Vector3 MirrorRotation(Vector3 value, int direction, float intensity)
        {
            value.y *= direction;
            value.z *= direction;
            return value * intensity;
        }
    }

    public enum QusapProceduralCombatMotionId
    {
        BodyAttack,
        WeaponLightFirst,
        WeaponLightSecond,
        WeaponStrong,
        Headbutt,
        DamageBodyAttack,
        DamageFinisher,
        DisarmFinisher,
        LaunchWeaponLight,
        LaunchFinisher
    }

    [Serializable]
    public sealed class QusapProceduralCombatPose
    {
        [Header("Body")]
        [SerializeField, Tooltip("Body offset in modular visual space.")]
        private Vector3 bodyLocalPosition;
        [SerializeField, Tooltip("Body rotation in modular visual space, in degrees.")]
        private Vector3 bodyLocalEulerAngles;

        [Header("Left foot")]
        [SerializeField, Tooltip("Left-foot offset in modular visual space.")]
        private Vector3 leftFootLocalPosition;
        [SerializeField, Tooltip("Left-foot rotation in modular visual space, in degrees.")]
        private Vector3 leftFootLocalEulerAngles;

        [Header("Right foot")]
        [SerializeField, Tooltip("Right-foot offset in modular visual space.")]
        private Vector3 rightFootLocalPosition;
        [SerializeField, Tooltip("Right-foot rotation in modular visual space, in degrees.")]
        private Vector3 rightFootLocalEulerAngles;

        [Header("Equipped sword")]
        [SerializeField, Tooltip("Equipped-visual offset relative to its neutral socket pose.")]
        private Vector3 weaponLocalPosition;
        [SerializeField, Tooltip("Equipped-visual rotation relative to neutral, in degrees.")]
        private Vector3 weaponLocalEulerAngles;

        public QusapProceduralCombatPose()
        {
        }

        public QusapProceduralCombatPose(
            Vector3 bodyLocalPosition,
            Vector3 bodyLocalEulerAngles,
            Vector3 leftFootLocalPosition,
            Vector3 leftFootLocalEulerAngles,
            Vector3 rightFootLocalPosition,
            Vector3 rightFootLocalEulerAngles,
            Vector3 weaponLocalPosition,
            Vector3 weaponLocalEulerAngles)
        {
            this.bodyLocalPosition = bodyLocalPosition;
            this.bodyLocalEulerAngles = bodyLocalEulerAngles;
            this.leftFootLocalPosition = leftFootLocalPosition;
            this.leftFootLocalEulerAngles = leftFootLocalEulerAngles;
            this.rightFootLocalPosition = rightFootLocalPosition;
            this.rightFootLocalEulerAngles = rightFootLocalEulerAngles;
            this.weaponLocalPosition = weaponLocalPosition;
            this.weaponLocalEulerAngles = weaponLocalEulerAngles;
        }

        public Vector3 BodyLocalPosition => bodyLocalPosition;
        public Vector3 BodyLocalEulerAngles => bodyLocalEulerAngles;
        public Vector3 LeftFootLocalPosition => leftFootLocalPosition;
        public Vector3 LeftFootLocalEulerAngles => leftFootLocalEulerAngles;
        public Vector3 RightFootLocalPosition => rightFootLocalPosition;
        public Vector3 RightFootLocalEulerAngles => rightFootLocalEulerAngles;
        public Vector3 WeaponLocalPosition => weaponLocalPosition;
        public Vector3 WeaponLocalEulerAngles => weaponLocalEulerAngles;

        internal void Validate(float translationLimit, float rotationLimit)
        {
            bodyLocalPosition = NormalizeAndClamp(bodyLocalPosition, translationLimit);
            leftFootLocalPosition = NormalizeAndClamp(leftFootLocalPosition, translationLimit);
            rightFootLocalPosition = NormalizeAndClamp(rightFootLocalPosition, translationLimit);
            weaponLocalPosition = NormalizeAndClamp(weaponLocalPosition, translationLimit);
            bodyLocalEulerAngles = NormalizeAndClamp(bodyLocalEulerAngles, rotationLimit);
            leftFootLocalEulerAngles = NormalizeAndClamp(leftFootLocalEulerAngles, rotationLimit);
            rightFootLocalEulerAngles = NormalizeAndClamp(rightFootLocalEulerAngles, rotationLimit);
            weaponLocalEulerAngles = NormalizeAndClamp(weaponLocalEulerAngles, rotationLimit);
        }

        internal static QusapProceduralCombatPose Lerp(
            QusapProceduralCombatPose from,
            QusapProceduralCombatPose to,
            float t)
        {
            from ??= new QusapProceduralCombatPose();
            to ??= new QusapProceduralCombatPose();
            t = Mathf.Clamp01(IsFinite(t) ? t : 0f);
            return new QusapProceduralCombatPose(
                Vector3.LerpUnclamped(from.bodyLocalPosition, to.bodyLocalPosition, t),
                Vector3.LerpUnclamped(from.bodyLocalEulerAngles, to.bodyLocalEulerAngles, t),
                Vector3.LerpUnclamped(from.leftFootLocalPosition, to.leftFootLocalPosition, t),
                Vector3.LerpUnclamped(from.leftFootLocalEulerAngles, to.leftFootLocalEulerAngles, t),
                Vector3.LerpUnclamped(from.rightFootLocalPosition, to.rightFootLocalPosition, t),
                Vector3.LerpUnclamped(from.rightFootLocalEulerAngles, to.rightFootLocalEulerAngles, t),
                Vector3.LerpUnclamped(from.weaponLocalPosition, to.weaponLocalPosition, t),
                Vector3.LerpUnclamped(from.weaponLocalEulerAngles, to.weaponLocalEulerAngles, t));
        }

        internal QusapProceduralCombatPose ScaledAndMirrored(float intensity, int facing)
        {
            int direction = facing < 0 ? -1 : 1;
            return new QusapProceduralCombatPose(
                MirrorPosition(bodyLocalPosition, direction) * intensity,
                MirrorRotation(bodyLocalEulerAngles, direction) * intensity,
                MirrorPosition(leftFootLocalPosition, direction) * intensity,
                MirrorRotation(leftFootLocalEulerAngles, direction) * intensity,
                MirrorPosition(rightFootLocalPosition, direction) * intensity,
                MirrorRotation(rightFootLocalEulerAngles, direction) * intensity,
                MirrorPosition(weaponLocalPosition, direction) * intensity,
                MirrorRotation(weaponLocalEulerAngles, direction) * intensity);
        }

        private static Vector3 MirrorPosition(Vector3 value, int direction)
        {
            value.x *= direction;
            return value;
        }

        private static Vector3 MirrorRotation(Vector3 value, int direction)
        {
            value.y *= direction;
            value.z *= direction;
            return value;
        }

        private static Vector3 NormalizeAndClamp(Vector3 value, float limit)
        {
            if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
            {
                return Vector3.zero;
            }

            return new Vector3(
                Mathf.Clamp(value.x, -limit, limit),
                Mathf.Clamp(value.y, -limit, limit),
                Mathf.Clamp(value.z, -limit, limit));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [Serializable]
    public sealed class QusapProceduralCombatMotion
    {
        [SerializeField] private QusapProceduralCombatPose neutral = new();
        [SerializeField] private QusapProceduralCombatPose preparation = new();
        [SerializeField] private QusapProceduralCombatPose impact = new();
        [SerializeField] private QusapProceduralCombatPose followThrough = new();
        [SerializeField] private QusapProceduralCombatPose returnToNeutral = new();
        [SerializeField] private AnimationCurve startupCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve activeCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve recoveryCurve =
            AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public QusapProceduralCombatMotion()
        {
        }

        public QusapProceduralCombatMotion(
            QusapProceduralCombatPose neutral,
            QusapProceduralCombatPose preparation,
            QusapProceduralCombatPose impact,
            QusapProceduralCombatPose followThrough,
            QusapProceduralCombatPose returnToNeutral,
            AnimationCurve startupCurve = null,
            AnimationCurve activeCurve = null,
            AnimationCurve recoveryCurve = null)
        {
            this.neutral = neutral;
            this.preparation = preparation;
            this.impact = impact;
            this.followThrough = followThrough;
            this.returnToNeutral = returnToNeutral;
            this.startupCurve = startupCurve;
            this.activeCurve = activeCurve;
            this.recoveryCurve = recoveryCurve;
        }

        public QusapProceduralCombatPose Neutral => neutral;
        public QusapProceduralCombatPose Preparation => preparation;
        public QusapProceduralCombatPose Impact => impact;
        public QusapProceduralCombatPose FollowThrough => followThrough;
        public QusapProceduralCombatPose ReturnToNeutral => returnToNeutral;
        public AnimationCurve StartupCurve => startupCurve;
        public AnimationCurve ActiveCurve => activeCurve;
        public AnimationCurve RecoveryCurve => recoveryCurve;

        public QusapProceduralCombatPoseValue Evaluate(QusapAttackPhase phase, float progress)
        {
            progress = Mathf.Clamp01(IsFinite(progress) ? progress : 0f);
            switch (phase)
            {
                case QusapAttackPhase.Startup:
                    return QusapProceduralCombatPoseValue.Lerp(
                        neutral, preparation, EvaluateCurve(startupCurve, progress));
                case QusapAttackPhase.Active:
                {
                    float shaped = EvaluateCurve(activeCurve, progress);
                    return shaped <= 0.68f
                        ? QusapProceduralCombatPoseValue.Lerp(preparation, impact, shaped / 0.68f)
                        : QusapProceduralCombatPoseValue.Lerp(
                            impact, followThrough, (shaped - 0.68f) / 0.32f);
                }
                case QusapAttackPhase.Recovery:
                {
                    float shaped = EvaluateCurve(recoveryCurve, progress);
                    return shaped <= 0.55f
                        ? QusapProceduralCombatPoseValue.Lerp(
                            followThrough, returnToNeutral, shaped / 0.55f)
                        : QusapProceduralCombatPoseValue.Lerp(
                            returnToNeutral, neutral, (shaped - 0.55f) / 0.45f);
                }
                default:
                    return new QusapProceduralCombatPoseValue(neutral);
            }
        }

        internal void Validate(float translationLimit, float rotationLimit)
        {
            neutral ??= new QusapProceduralCombatPose();
            preparation ??= new QusapProceduralCombatPose();
            impact ??= new QusapProceduralCombatPose();
            followThrough ??= new QusapProceduralCombatPose();
            returnToNeutral ??= new QusapProceduralCombatPose();
            neutral.Validate(translationLimit, rotationLimit);
            preparation.Validate(translationLimit, rotationLimit);
            impact.Validate(translationLimit, rotationLimit);
            followThrough.Validate(translationLimit, rotationLimit);
            returnToNeutral.Validate(translationLimit, rotationLimit);
            startupCurve = NormalizeCurve(startupCurve);
            activeCurve = NormalizeCurve(activeCurve);
            recoveryCurve = NormalizeCurve(recoveryCurve);
        }

        private static AnimationCurve NormalizeCurve(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            }

            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (!IsFinite(keys[i].time)
                    || !IsFinite(keys[i].value)
                    || !IsFinite(keys[i].inTangent)
                    || !IsFinite(keys[i].outTangent))
                {
                    return AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
                }
            }

            return curve;
        }

        private static float EvaluateCurve(AnimationCurve curve, float value)
        {
            float evaluated = curve != null ? curve.Evaluate(value) : value;
            return Mathf.Clamp01(IsFinite(evaluated) ? evaluated : value);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [CreateAssetMenu(
        fileName = "QusapProceduralCombatVisualProfile",
        menuName = "Qusap/Combat/Procedural Combat Visual Profile")]
    public sealed class QusapProceduralCombatVisualProfile : ScriptableObject
    {
        private const int CurrentContentVersion = 1;

        public const float DefaultTranslationLimit = 1.25f;
        public const float DefaultRotationLimit = 220f;
        public const float MaximumIntensity = 2f;

        [SerializeField, HideInInspector] private int contentVersion;

        [Header("Global safety")]
        [SerializeField, Min(0f), Tooltip("Multiplier applied to every combat pose.")]
        private float globalIntensity = 1f;
        [SerializeField, Min(0.01f), Tooltip("Maximum absolute offset per local axis.")]
        private float safeTranslationLimit = DefaultTranslationLimit;
        [SerializeField, Range(1f, DefaultRotationLimit), Tooltip("Maximum absolute angle per axis.")]
        private float safeRotationLimit = DefaultRotationLimit;

        [Header("Basic attacks")]
        [SerializeField] private QusapProceduralCombatMotion bodyAttack;
        [SerializeField] private QusapProceduralCombatMotion weaponLightFirst;
        [SerializeField] private QusapProceduralCombatMotion weaponLightSecond;
        [SerializeField] private QusapProceduralCombatMotion weaponStrong;
        [SerializeField] private QusapProceduralCombatMotion headbutt;

        [Header("Combo-specific attacks")]
        [SerializeField] private QusapProceduralCombatMotion damageBodyAttack;
        [SerializeField] private QusapProceduralCombatMotion damageFinisher;
        [SerializeField] private QusapProceduralCombatMotion disarmFinisher;
        [SerializeField] private QusapProceduralCombatMotion launchWeaponLight;
        [SerializeField] private QusapProceduralCombatMotion launchFinisher;

        public float GlobalIntensity => globalIntensity;
        public float SafeTranslationLimit => safeTranslationLimit;
        public float SafeRotationLimit => safeRotationLimit;

        private void OnEnable()
        {
            EnsureDefaults();
            ValidateProfile();
        }

        private void OnValidate()
        {
            EnsureDefaults();
            ValidateProfile();
        }

        public void ConfigureSafety(float intensity, float translationLimit, float rotationLimit)
        {
            globalIntensity = intensity;
            safeTranslationLimit = translationLimit;
            safeRotationLimit = rotationLimit;
            ValidateProfile();
        }

        public void ConfigureMotion(
            QusapProceduralCombatMotionId motionId,
            QusapProceduralCombatMotion motion)
        {
            EnsureDefaults();
            motion ??= CreateDefaultMotion(motionId);
            switch (motionId)
            {
                case QusapProceduralCombatMotionId.BodyAttack: bodyAttack = motion; break;
                case QusapProceduralCombatMotionId.WeaponLightFirst: weaponLightFirst = motion; break;
                case QusapProceduralCombatMotionId.WeaponLightSecond: weaponLightSecond = motion; break;
                case QusapProceduralCombatMotionId.WeaponStrong: weaponStrong = motion; break;
                case QusapProceduralCombatMotionId.Headbutt: headbutt = motion; break;
                case QusapProceduralCombatMotionId.DamageBodyAttack: damageBodyAttack = motion; break;
                case QusapProceduralCombatMotionId.DamageFinisher: damageFinisher = motion; break;
                case QusapProceduralCombatMotionId.DisarmFinisher: disarmFinisher = motion; break;
                case QusapProceduralCombatMotionId.LaunchWeaponLight: launchWeaponLight = motion; break;
                case QusapProceduralCombatMotionId.LaunchFinisher: launchFinisher = motion; break;
                default: throw new ArgumentOutOfRangeException(nameof(motionId), motionId, null);
            }
            motion.Validate(safeTranslationLimit, safeRotationLimit);
        }

        public QusapProceduralCombatMotion GetMotion(QusapProceduralCombatMotionId motionId)
        {
            EnsureDefaults();
            return motionId switch
            {
                QusapProceduralCombatMotionId.BodyAttack => bodyAttack,
                QusapProceduralCombatMotionId.WeaponLightFirst => weaponLightFirst,
                QusapProceduralCombatMotionId.WeaponLightSecond => weaponLightSecond,
                QusapProceduralCombatMotionId.WeaponStrong => weaponStrong,
                QusapProceduralCombatMotionId.Headbutt => headbutt,
                QusapProceduralCombatMotionId.DamageBodyAttack => damageBodyAttack,
                QusapProceduralCombatMotionId.DamageFinisher => damageFinisher,
                QusapProceduralCombatMotionId.DisarmFinisher => disarmFinisher,
                QusapProceduralCombatMotionId.LaunchWeaponLight => launchWeaponLight,
                QusapProceduralCombatMotionId.LaunchFinisher => launchFinisher,
                _ => throw new ArgumentOutOfRangeException(nameof(motionId), motionId, null)
            };
        }

        public QusapProceduralCombatPoseValue Evaluate(
            QusapCombatVisualContext context,
            out QusapProceduralCombatMotionId motionId)
        {
            motionId = SelectMotionId(context);
            QusapProceduralCombatPoseValue pose = GetMotion(motionId)
                .Evaluate(context.Phase, context.NormalizedProgress);
            return pose.ScaledAndMirrored(globalIntensity, context.CapturedFacing);
        }

        public static QusapProceduralCombatMotionId SelectMotionId(
            QusapCombatVisualContext context)
        {
            if (context.IsFinisher && context.ComboId.HasValue)
            {
                return context.ComboId.Value switch
                {
                    QusapComboId.Damage => QusapProceduralCombatMotionId.DamageFinisher,
                    QusapComboId.Disarm => QusapProceduralCombatMotionId.DisarmFinisher,
                    QusapComboId.Launch => QusapProceduralCombatMotionId.LaunchFinisher,
                    _ => QusapProceduralCombatMotionId.BodyAttack
                };
            }

            if (context.ComboId == QusapComboId.Damage
                && context.ComboStepIndex == 2
                && context.Command == QusapCombatCommand.BodyAttack)
            {
                return QusapProceduralCombatMotionId.DamageBodyAttack;
            }

            if (context.ComboId == QusapComboId.Launch
                && context.ComboStepIndex == 1
                && context.Command == QusapCombatCommand.WeaponLight)
            {
                return QusapProceduralCombatMotionId.LaunchWeaponLight;
            }

            return context.Command switch
            {
                QusapCombatCommand.BodyAttack => QusapProceduralCombatMotionId.BodyAttack,
                QusapCombatCommand.WeaponLight when context.ComboStepIndex == 1 =>
                    QusapProceduralCombatMotionId.WeaponLightSecond,
                QusapCombatCommand.WeaponLight => QusapProceduralCombatMotionId.WeaponLightFirst,
                QusapCombatCommand.WeaponStrong => QusapProceduralCombatMotionId.WeaponStrong,
                QusapCombatCommand.Headbutt => QusapProceduralCombatMotionId.Headbutt,
                _ => QusapProceduralCombatMotionId.BodyAttack
            };
        }

        public void ValidateProfile()
        {
            globalIntensity = IsFinite(globalIntensity)
                ? Mathf.Clamp(globalIntensity, 0f, MaximumIntensity)
                : 1f;
            safeTranslationLimit = IsFinite(safeTranslationLimit)
                ? Mathf.Clamp(safeTranslationLimit, 0.01f, DefaultTranslationLimit)
                : DefaultTranslationLimit;
            safeRotationLimit = IsFinite(safeRotationLimit)
                ? Mathf.Clamp(safeRotationLimit, 1f, DefaultRotationLimit)
                : DefaultRotationLimit;

            EnsureDefaults();
            foreach (QusapProceduralCombatMotionId id in
                Enum.GetValues(typeof(QusapProceduralCombatMotionId)))
            {
                GetMotion(id).Validate(safeTranslationLimit, safeRotationLimit);
            }
        }

        public static QusapProceduralCombatVisualProfile CreateDefault()
        {
            QusapProceduralCombatVisualProfile profile =
                CreateInstance<QusapProceduralCombatVisualProfile>();
            profile.name = "QusapProceduralCombatVisualProfile_Default";
            profile.EnsureDefaults();
            profile.ValidateProfile();
            return profile;
        }

        private void EnsureDefaults()
        {
            if (contentVersion < CurrentContentVersion)
            {
                bodyAttack = CreateDefaultMotion(QusapProceduralCombatMotionId.BodyAttack);
                weaponLightFirst = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.WeaponLightFirst);
                weaponLightSecond = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.WeaponLightSecond);
                weaponStrong = CreateDefaultMotion(QusapProceduralCombatMotionId.WeaponStrong);
                headbutt = CreateDefaultMotion(QusapProceduralCombatMotionId.Headbutt);
                damageBodyAttack = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.DamageBodyAttack);
                damageFinisher = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.DamageFinisher);
                disarmFinisher = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.DisarmFinisher);
                launchWeaponLight = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.LaunchWeaponLight);
                launchFinisher = CreateDefaultMotion(
                    QusapProceduralCombatMotionId.LaunchFinisher);
                contentVersion = CurrentContentVersion;
                return;
            }

            bodyAttack ??= CreateDefaultMotion(QusapProceduralCombatMotionId.BodyAttack);
            weaponLightFirst ??= CreateDefaultMotion(QusapProceduralCombatMotionId.WeaponLightFirst);
            weaponLightSecond ??= CreateDefaultMotion(QusapProceduralCombatMotionId.WeaponLightSecond);
            weaponStrong ??= CreateDefaultMotion(QusapProceduralCombatMotionId.WeaponStrong);
            headbutt ??= CreateDefaultMotion(QusapProceduralCombatMotionId.Headbutt);
            damageBodyAttack ??= CreateDefaultMotion(QusapProceduralCombatMotionId.DamageBodyAttack);
            damageFinisher ??= CreateDefaultMotion(QusapProceduralCombatMotionId.DamageFinisher);
            disarmFinisher ??= CreateDefaultMotion(QusapProceduralCombatMotionId.DisarmFinisher);
            launchWeaponLight ??= CreateDefaultMotion(QusapProceduralCombatMotionId.LaunchWeaponLight);
            launchFinisher ??= CreateDefaultMotion(QusapProceduralCombatMotionId.LaunchFinisher);
        }

        private static QusapProceduralCombatMotion CreateDefaultMotion(
            QusapProceduralCombatMotionId id)
        {
            QusapProceduralCombatPose P(
                Vector3 bodyP, Vector3 bodyR,
                Vector3 leftP, Vector3 leftR,
                Vector3 rightP, Vector3 rightR,
                Vector3 weaponP, Vector3 weaponR)
            {
                return new QusapProceduralCombatPose(
                    bodyP, bodyR, leftP, leftR, rightP, rightR, weaponP, weaponR);
            }

            QusapProceduralCombatPose zero = P(
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero);

            return id switch
            {
                QusapProceduralCombatMotionId.BodyAttack => new QusapProceduralCombatMotion(
                    zero,
                    P(new(-0.07f, 0.02f, 0f), new(0f, 0f, 7f), new(-0.11f, 0.06f, 0f), new(0f, 0f, -22f), new(0.04f, 0f, 0f), new(0f, 0f, 8f), Vector3.zero, Vector3.zero),
                    P(new(0.08f, 0.01f, 0f), new(0f, 0f, -9f), new(0.34f, 0.13f, 0f), new(0f, 0f, 38f), new(-0.05f, -0.01f, 0f), new(0f, 0f, -10f), Vector3.zero, Vector3.zero),
                    P(new(0.04f, 0f, 0f), new(0f, 0f, -5f), new(0.27f, 0.08f, 0f), new(0f, 0f, 28f), new(-0.03f, 0f, 0f), new(0f, 0f, -6f), Vector3.zero, Vector3.zero),
                    P(new(-0.015f, 0f, 0f), new(0f, 0f, 2f), new(0.04f, 0.01f, 0f), new(0f, 0f, 5f), Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero)),

                QusapProceduralCombatMotionId.WeaponLightFirst => new(
                    zero,
                    P(new(-0.21f, 0.035f, 0f), new(0f, 0f, 26f),
                        new(0.12f, 0.045f, 0f), new(0f, 0f, -20f),
                        new(-0.10f, 0f, 0f), Vector3.zero,
                        new(-0.18f, 0.18f, 0f), new(0f, 0f, 58f)),
                    P(new(0.20f, -0.045f, 0f), new(0f, 0f, -30f),
                        new(0.30f, 0.075f, 0f), new(0f, 0f, 30f),
                        new(-0.10f, 0f, 0f), Vector3.zero,
                        new(0.25f, -0.08f, 0f), new(0f, 0f, -58f)),
                    P(new(0.24f, -0.055f, 0f), new(0f, 0f, -35f),
                        new(0.33f, 0.025f, 0f), new(0f, 0f, 22f),
                        new(-0.10f, 0f, 0f), Vector3.zero,
                        new(0.32f, -0.16f, 0f), new(0f, 0f, -78f)),
                    P(new(0.04f, -0.01f, 0f), new(0f, 0f, -6f),
                        new(0.05f, 0f, 0f), Vector3.zero,
                        new(-0.02f, 0f, 0f), Vector3.zero,
                        new(0.08f, -0.03f, 0f), new(0f, 0f, -18f)),
                    LightFirstStartupCurve(), LinearActiveCurve(), LightRecoveryCurve()),

                QusapProceduralCombatMotionId.WeaponLightSecond => new(
                    zero,
                    P(new(0.17f, -0.045f, 0f), new(0f, 0f, -24f),
                        new(-0.10f, 0f, 0f), Vector3.zero,
                        new(0.12f, 0.045f, 0f), new(0f, 0f, -22f),
                        new(0.18f, -0.10f, 0f), new(0f, 0f, -62f)),
                    P(new(-0.13f, 0.085f, 0f), new(0f, 0f, 29f),
                        new(-0.10f, 0f, 0f), Vector3.zero,
                        new(0.31f, 0.10f, 0f), new(0f, 0f, 32f),
                        new(0.30f, 0.19f, 0f), new(0f, 0f, 64f)),
                    P(new(-0.17f, 0.11f, 0f), new(0f, 0f, 34f),
                        new(-0.10f, 0f, 0f), Vector3.zero,
                        new(0.34f, 0.06f, 0f), new(0f, 0f, 24f),
                        new(0.36f, 0.25f, 0f), new(0f, 0f, 82f)),
                    P(new(-0.03f, 0.015f, 0f), new(0f, 0f, 6f),
                        new(-0.02f, 0f, 0f), Vector3.zero,
                        new(0.05f, 0f, 0f), Vector3.zero,
                        new(0.09f, 0.06f, 0f), new(0f, 0f, 20f)),
                    LightSecondStartupCurve(), LinearActiveCurve(), LightRecoveryCurve()),

                QusapProceduralCombatMotionId.WeaponStrong => WeaponMotion(
                    -0.12f, 13f, -0.08f, 0.04f, 0.08f, -0.04f,
                    new(-0.14f, 0.09f, 0f), new(0f, 0f, 78f),
                    new(0.18f, -0.09f, 0f), new(0f, 0f, -82f),
                    new(0.10f, -0.05f, 0f), new(0f, 0f, -58f)),

                QusapProceduralCombatMotionId.Headbutt => new QusapProceduralCombatMotion(
                    zero,
                    P(new(-0.16f, 0.01f, 0f), new(0f, 0f, 12f), new(0.06f, 0f, 0f), new(0f, 0f, -8f), new(0.08f, 0f, 0f), new(0f, 0f, -9f), Vector3.zero, Vector3.zero),
                    P(new(0.29f, 0.02f, 0f), new(0f, 0f, -18f), new(-0.08f, 0f, 0f), new(0f, 0f, 13f), new(-0.06f, 0f, 0f), new(0f, 0f, 11f), Vector3.zero, Vector3.zero),
                    P(new(0.21f, 0.01f, 0f), new(0f, 0f, -13f), new(-0.05f, 0f, 0f), new(0f, 0f, 8f), new(-0.04f, 0f, 0f), new(0f, 0f, 7f), Vector3.zero, Vector3.zero),
                    P(new(0.03f, 0f, 0f), new(0f, 0f, -2f), Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero)),

                QusapProceduralCombatMotionId.DamageBodyAttack => new QusapProceduralCombatMotion(
                    zero,
                    P(new(-0.16f, -0.11f, 0f), new(0f, 0f, 18f),
                        new(-0.10f, 0.10f, 0f), new(0f, 0f, -24f),
                        Vector3.zero, Vector3.zero,
                        new(-0.18f, 0.16f, 0f), new(0f, 0f, 35f)),
                    P(new(-0.14f, 0.015f, 0f), new(0f, 0f, 28f),
                        new(0.58f, 0.26f, 0f), new(0f, 0f, 52f),
                        Vector3.zero, Vector3.zero,
                        new(-0.24f, 0.26f, 0f), new(0f, 0f, 52f)),
                    P(new(-0.17f, 0.025f, 0f), new(0f, 0f, 32f),
                        new(0.55f, 0.23f, 0f), new(0f, 0f, 46f),
                        Vector3.zero, Vector3.zero,
                        new(-0.22f, 0.28f, 0f), new(0f, 0f, 62f)),
                    P(new(-0.07f, -0.035f, 0f), new(0f, 0f, 10f),
                        new(0.10f, 0.04f, 0f), new(0f, 0f, 10f),
                        Vector3.zero, Vector3.zero,
                        new(-0.10f, 0.16f, 0f), new(0f, 0f, 75f)),
                    KickStartupCurve(), LinearActiveCurve(), KickRecoveryCurve()),

                QusapProceduralCombatMotionId.DamageFinisher => new(
                    zero,
                    P(new(-0.31f, -0.15f, 0f), new(0f, 0f, 36f),
                        new(-0.27f, 0f, 0f), new(0f, 0f, -18f),
                        new(0.29f, 0f, 0f), new(0f, 0f, 16f),
                        new(-0.28f, 0.28f, 0f), new(0f, 0f, 105f)),
                    P(new(0.31f, -0.04f, 0f), new(0f, 0f, -39f),
                        new(0.34f, 0f, 0f), new(0f, 0f, 12f),
                        new(-0.25f, 0.08f, 0f), new(0f, 0f, -32f),
                        new(0.36f, -0.20f, 0f), new(0f, 0f, -105f)),
                    P(new(0.36f, -0.055f, 0f), new(0f, 0f, -45f),
                        new(0.38f, 0f, 0f), new(0f, 0f, 8f),
                        new(-0.29f, 0.045f, 0f), new(0f, 0f, -38f),
                        new(0.43f, -0.24f, 0f), new(0f, 0f, -128f)),
                    P(new(0.08f, -0.025f, 0f), new(0f, 0f, -9f),
                        new(0.09f, 0f, 0f), new(0f, 0f, 7f),
                        new(-0.07f, 0f, 0f), new(0f, 0f, -5f),
                        new(0.10f, -0.06f, 0f), new(0f, 0f, -42f)),
                    FinisherStartupCurve(), LinearActiveCurve(), FinisherRecoveryCurve()),

                QusapProceduralCombatMotionId.DisarmFinisher => new QusapProceduralCombatMotion(
                    zero,
                    P(new(-0.22f, -0.02f, 0f), new(0f, 0f, 17f), new(0.09f, 0f, 0f), new(0f, 0f, -12f), new(0.11f, 0f, 0f), new(0f, 0f, -14f), new(-0.05f, 0.04f, 0f), new(0f, 0f, 22f)),
                    P(new(0.36f, 0.04f, 0f), new(0f, 0f, -24f), new(-0.11f, 0f, 0f), new(0f, 0f, 16f), new(-0.09f, 0f, 0f), new(0f, 0f, 14f), new(-0.08f, 0.02f, 0f), new(0f, 0f, 30f)),
                    P(new(0.25f, 0.02f, 0f), new(0f, 0f, -16f), new(-0.06f, 0f, 0f), new(0f, 0f, 9f), new(-0.05f, 0f, 0f), new(0f, 0f, 8f), new(-0.05f, 0.01f, 0f), new(0f, 0f, 20f)),
                    P(new(0.04f, 0f, 0f), new(0f, 0f, -3f), Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero)),

                QusapProceduralCombatMotionId.LaunchWeaponLight => WeaponMotion(
                    -0.07f, 8f, -0.05f, 0.035f, 0.06f, -0.03f,
                    new(-0.05f, -0.07f, 0f), new(0f, 0f, -46f),
                    new(0.12f, 0.14f, 0f), new(0f, 0f, 58f),
                    new(0.07f, 0.09f, 0f), new(0f, 0f, 42f)),

                QusapProceduralCombatMotionId.LaunchFinisher => new QusapProceduralCombatMotion(
                    zero,
                    P(new(-0.13f, -0.06f, 0f), new(0f, 0f, 13f), new(-0.10f, -0.02f, 0f), new(0f, 0f, -20f), new(0.08f, -0.03f, 0f), new(0f, 0f, 12f), Vector3.zero, Vector3.zero),
                    P(new(0.12f, 0.14f, 0f), new(0f, 0f, -15f), new(0.31f, 0.36f, 0f), new(0f, 0f, 44f), new(-0.07f, -0.02f, 0f), new(0f, 0f, -13f), Vector3.zero, Vector3.zero),
                    P(new(0.08f, 0.09f, 0f), new(0f, 0f, -10f), new(0.23f, 0.28f, 0f), new(0f, 0f, 34f), new(-0.05f, 0f, 0f), new(0f, 0f, -9f), Vector3.zero, Vector3.zero),
                    P(new(-0.02f, 0.02f, 0f), new(0f, 0f, 3f), new(0.04f, 0.05f, 0f), new(0f, 0f, 6f), Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero)),

                _ => new QusapProceduralCombatMotion()
            };

            QusapProceduralCombatMotion WeaponMotion(
                float bodyPreparationX, float bodyPreparationZ,
                float leftStepX, float leftStepY,
                float rightStepX, float rightStepY,
                Vector3 weaponPreparationP, Vector3 weaponPreparationR,
                Vector3 weaponImpactP, Vector3 weaponImpactR,
                Vector3 weaponFollowP, Vector3 weaponFollowR)
            {
                return new QusapProceduralCombatMotion(
                    zero,
                    P(new(bodyPreparationX, 0f, 0f), new(0f, 0f, bodyPreparationZ),
                        new(leftStepX, leftStepY, 0f), new(0f, 0f, -bodyPreparationZ * 0.45f),
                        new(rightStepX, rightStepY, 0f), new(0f, 0f, bodyPreparationZ * 0.35f),
                        weaponPreparationP, weaponPreparationR),
                    P(new(-bodyPreparationX * 0.8f, 0.02f, 0f), new(0f, 0f, -bodyPreparationZ * 0.9f),
                        new(-leftStepX * 0.5f, 0f, 0f), new(0f, 0f, bodyPreparationZ * 0.4f),
                        new(-rightStepX * 0.5f, 0f, 0f), new(0f, 0f, -bodyPreparationZ * 0.3f),
                        weaponImpactP, weaponImpactR),
                    P(new(-bodyPreparationX * 0.45f, 0.01f, 0f), new(0f, 0f, -bodyPreparationZ * 0.55f),
                        new(-leftStepX * 0.3f, 0f, 0f), Vector3.zero,
                        new(-rightStepX * 0.3f, 0f, 0f), Vector3.zero,
                        weaponFollowP, weaponFollowR),
                    P(new(bodyPreparationX * 0.12f, 0f, 0f), new(0f, 0f, bodyPreparationZ * 0.12f),
                        Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                        weaponFollowP * 0.18f, weaponFollowR * 0.18f));
            }

            AnimationCurve LightFirstStartupCurve() => new(
                new Keyframe(0f, 0f, 0f, 0.18f),
                new Keyframe(0.72f, 0.48f, 1.35f, 1.35f),
                new Keyframe(1f, 1f, 2.1f, 0f));

            AnimationCurve LightSecondStartupCurve() => new(
                new Keyframe(0f, 0f, 0f, 0.3f),
                new Keyframe(0.58f, 0.42f, 1.25f, 1.25f),
                new Keyframe(1f, 1f, 1.75f, 0f));

            AnimationCurve KickStartupCurve() => new(
                new Keyframe(0f, 0f, 0f, 0.12f),
                new Keyframe(0.7f, 0.38f, 1.4f, 1.4f),
                new Keyframe(1f, 1f, 2.3f, 0f));

            AnimationCurve FinisherStartupCurve() => new(
                new Keyframe(0f, 0f, 0f, 0.06f),
                new Keyframe(0.78f, 0.32f, 1.45f, 1.45f),
                new Keyframe(1f, 1f, 3f, 0f));

            AnimationCurve LinearActiveCurve() => AnimationCurve.Linear(0f, 0f, 1f, 1f);

            AnimationCurve LightRecoveryCurve() => new(
                new Keyframe(0f, 0f, 0f, 1.35f),
                new Keyframe(0.62f, 0.82f, 0.8f, 0.8f),
                new Keyframe(1f, 1f, 0.25f, 0f));

            AnimationCurve KickRecoveryCurve() => new(
                new Keyframe(0f, 0f, 0f, 1.05f),
                new Keyframe(0.58f, 0.68f, 1.15f, 1.15f),
                new Keyframe(1f, 1f, 0.45f, 0f));

            // Retain the visual overshoot through the first quarter of recovery.
            AnimationCurve FinisherRecoveryCurve() => new(
                new Keyframe(0f, 0f, 0f, 0.05f),
                new Keyframe(0.25f, 0.035f, 0.2f, 0.2f),
                new Keyframe(0.65f, 0.55f, 1.6f, 1.6f),
                new Keyframe(1f, 1f, 0f, 0f));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
