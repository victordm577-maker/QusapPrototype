using System;
using UnityEngine;

namespace Qusap
{
    public enum QusapWeaponVisualInterpolation
    {
        Linear,
        SmoothStep
    }

    public readonly struct QusapWeaponVisualPose
    {
        public QusapWeaponVisualPose(Vector3 localPosition, Vector3 localEulerAngles)
        {
            LocalPosition = localPosition;
            LocalEulerAngles = localEulerAngles;
        }

        public Vector3 LocalPosition { get; }
        public Vector3 LocalEulerAngles { get; }
    }

    [Serializable]
    public sealed class QusapWeaponAttackVisualProfile
    {
        [SerializeField, Tooltip("Offset local al terminar la preparacion.")]
        private Vector3 windupLocalPosition;
        [SerializeField, Tooltip("Rotacion local al terminar la preparacion.")]
        private Vector3 windupLocalEulerAngles = new(0f, 0f, 28f);
        [SerializeField, Tooltip("Offset local en el extremo del golpe.")]
        private Vector3 strikeLocalPosition = new(0.08f, 0f, 0f);
        [SerializeField, Tooltip("Rotacion local en el extremo del golpe.")]
        private Vector3 strikeLocalEulerAngles = new(0f, 0f, -42f);
        [SerializeField, Tooltip("Offset local de la pose intermedia de recuperacion.")]
        private Vector3 recoveryLocalPosition;
        [SerializeField, Tooltip("Rotacion local de la pose intermedia de recuperacion.")]
        private Vector3 recoveryLocalEulerAngles = new(0f, 0f, -10f);
        [SerializeField, Tooltip("Funcion determinista usada para interpolar cada fase.")]
        private QusapWeaponVisualInterpolation interpolation = QusapWeaponVisualInterpolation.SmoothStep;
        [SerializeField, Min(0f), Tooltip("Amplitud angular adicional del arco durante Active.")]
        private float arcAmplitude = 8f;

        public QusapWeaponAttackVisualProfile(
            Vector3 windupLocalPosition,
            Vector3 windupLocalEulerAngles,
            Vector3 strikeLocalPosition,
            Vector3 strikeLocalEulerAngles,
            Vector3 recoveryLocalPosition,
            Vector3 recoveryLocalEulerAngles,
            QusapWeaponVisualInterpolation interpolation,
            float arcAmplitude)
        {
            this.windupLocalPosition = windupLocalPosition;
            this.windupLocalEulerAngles = windupLocalEulerAngles;
            this.strikeLocalPosition = strikeLocalPosition;
            this.strikeLocalEulerAngles = strikeLocalEulerAngles;
            this.recoveryLocalPosition = recoveryLocalPosition;
            this.recoveryLocalEulerAngles = recoveryLocalEulerAngles;
            this.interpolation = interpolation;
            this.arcAmplitude = arcAmplitude;
            Validate();
        }

        public float ArcAmplitude => arcAmplitude;

        public static QusapWeaponAttackVisualProfile CreateLight()
        {
            return new QusapWeaponAttackVisualProfile(
                Vector3.zero, new Vector3(0f, 0f, 24f),
                new Vector3(0.06f, 0.02f, 0f), new Vector3(0f, 0f, -38f),
                Vector3.zero, new Vector3(0f, 0f, -8f),
                QusapWeaponVisualInterpolation.SmoothStep, 7f);
        }

        public static QusapWeaponAttackVisualProfile CreateStrong()
        {
            return new QusapWeaponAttackVisualProfile(
                new Vector3(-0.08f, 0.04f, 0f), new Vector3(0f, 0f, 58f),
                new Vector3(0.14f, -0.04f, 0f), new Vector3(0f, 0f, -92f),
                new Vector3(0.04f, -0.02f, 0f), new Vector3(0f, 0f, -20f),
                QusapWeaponVisualInterpolation.SmoothStep, 18f);
        }

        public QusapWeaponVisualPose Evaluate(
            QusapAttackPhase phase,
            float normalizedProgress,
            int capturedFacing)
        {
            float t = Shape(Mathf.Clamp01(IsFinite(normalizedProgress) ? normalizedProgress : 0f));
            Vector3 position;
            Vector3 euler;
            switch (phase)
            {
                case QusapAttackPhase.Startup:
                    position = Vector3.LerpUnclamped(Vector3.zero, windupLocalPosition, t);
                    euler = Vector3.LerpUnclamped(Vector3.zero, windupLocalEulerAngles, t);
                    break;
                case QusapAttackPhase.Active:
                    position = Vector3.LerpUnclamped(windupLocalPosition, strikeLocalPosition, t);
                    euler = Vector3.LerpUnclamped(windupLocalEulerAngles, strikeLocalEulerAngles, t);
                    euler.z -= Mathf.Sin(t * Mathf.PI) * arcAmplitude;
                    break;
                case QusapAttackPhase.Recovery:
                    if (t < 0.4f)
                    {
                        float first = Shape(t / 0.4f);
                        position = Vector3.LerpUnclamped(strikeLocalPosition, recoveryLocalPosition, first);
                        euler = Vector3.LerpUnclamped(strikeLocalEulerAngles, recoveryLocalEulerAngles, first);
                    }
                    else
                    {
                        float second = Shape((t - 0.4f) / 0.6f);
                        position = Vector3.LerpUnclamped(recoveryLocalPosition, Vector3.zero, second);
                        euler = Vector3.LerpUnclamped(recoveryLocalEulerAngles, Vector3.zero, second);
                    }
                    break;
                default:
                    position = Vector3.zero;
                    euler = Vector3.zero;
                    break;
            }

            int direction = capturedFacing < 0 ? -1 : 1;
            position.x *= direction;
            euler.z *= direction;
            return new QusapWeaponVisualPose(position, euler);
        }

        public void Validate()
        {
            windupLocalPosition = Normalize(windupLocalPosition);
            windupLocalEulerAngles = Normalize(windupLocalEulerAngles);
            strikeLocalPosition = Normalize(strikeLocalPosition);
            strikeLocalEulerAngles = Normalize(strikeLocalEulerAngles);
            recoveryLocalPosition = Normalize(recoveryLocalPosition);
            recoveryLocalEulerAngles = Normalize(recoveryLocalEulerAngles);
            if (!Enum.IsDefined(typeof(QusapWeaponVisualInterpolation), interpolation))
            {
                interpolation = QusapWeaponVisualInterpolation.SmoothStep;
            }
            arcAmplitude = IsFinite(arcAmplitude) ? Mathf.Max(arcAmplitude, 0f) : 0f;
        }

        private float Shape(float value)
        {
            return interpolation == QusapWeaponVisualInterpolation.Linear
                ? value
                : value * value * (3f - 2f * value);
        }

        private static Vector3 Normalize(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z)
                ? value
                : Vector3.zero;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class QusapWeaponAttackVisualPresenter : MonoBehaviour
    {
        [SerializeField] private QusapCombatController combatController;
        [SerializeField] private QusapEquippedWeaponPresenter equippedPresenter;
        [SerializeField] private QusapWeaponAttackVisualProfile lightProfile =
            QusapWeaponAttackVisualProfile.CreateLight();
        [SerializeField] private QusapWeaponAttackVisualProfile strongProfile =
            QusapWeaponAttackVisualProfile.CreateStrong();

        private GameObject animatedVisual;
        private Vector3 restLocalPosition;
        private Quaternion restLocalRotation;
        private Vector3 restLocalScale;

        public QusapWeaponAttackVisualProfile LightProfile => lightProfile;
        public QusapWeaponAttackVisualProfile StrongProfile => strongProfile;
        public bool IsAnimating { get; private set; }
        public int CapturedFacingDirection { get; private set; } = 1;

        private void Awake()
        {
            ResolveReferences();
            ValidateProfiles();
        }

        private void OnEnable()
        {
            ResolveReferences();
        }

        private void LateUpdate()
        {
            if (combatController == null || equippedPresenter == null)
            {
                ResetPresentation();
                return;
            }

            if (!combatController.TryGetWeaponAttackVisualState(
                    out QusapWeaponAttackKind kind,
                    out QusapAttackPhase phase,
                    out float progress,
                    out int facing))
            {
                ResetPresentation();
                return;
            }

            GameObject visual = equippedPresenter.EquippedVisual;
            if (visual == null)
            {
                ResetPresentation();
                return;
            }

            if (!IsAnimating || animatedVisual != visual)
            {
                ResetPresentation();
                animatedVisual = visual;
                restLocalPosition = visual.transform.localPosition;
                restLocalRotation = visual.transform.localRotation;
                restLocalScale = visual.transform.localScale;
                CapturedFacingDirection = facing < 0 ? -1 : 1;
                IsAnimating = true;
            }

            QusapWeaponAttackVisualProfile profile = kind == QusapWeaponAttackKind.Strong
                ? strongProfile
                : lightProfile;
            QusapWeaponVisualPose pose = profile.Evaluate(phase, progress, CapturedFacingDirection);
            visual.transform.localPosition = restLocalPosition + pose.LocalPosition;
            visual.transform.localRotation = restLocalRotation * Quaternion.Euler(pose.LocalEulerAngles);
            visual.transform.localScale = restLocalScale;
        }

        private void OnDisable()
        {
            ResetPresentation();
        }

        private void OnDestroy()
        {
            ResetPresentation();
        }

        public void Configure(
            QusapCombatController controller,
            QusapEquippedWeaponPresenter presenter,
            QusapWeaponAttackVisualProfile light,
            QusapWeaponAttackVisualProfile strong)
        {
            ResetPresentation();
            combatController = controller;
            equippedPresenter = presenter;
            lightProfile = light ?? QusapWeaponAttackVisualProfile.CreateLight();
            strongProfile = strong ?? QusapWeaponAttackVisualProfile.CreateStrong();
            ValidateProfiles();
        }

        public void ResetPresentation()
        {
            if (animatedVisual != null)
            {
                animatedVisual.transform.localPosition = restLocalPosition;
                animatedVisual.transform.localRotation = restLocalRotation;
                animatedVisual.transform.localScale = restLocalScale;
            }

            animatedVisual = null;
            IsAnimating = false;
        }

        private void ResolveReferences()
        {
            combatController ??= GetComponent<QusapCombatController>();
            equippedPresenter ??= GetComponent<QusapEquippedWeaponPresenter>();
        }

        private void ValidateProfiles()
        {
            lightProfile ??= QusapWeaponAttackVisualProfile.CreateLight();
            strongProfile ??= QusapWeaponAttackVisualProfile.CreateStrong();
            lightProfile.Validate();
            strongProfile.Validate();
        }
    }
}
