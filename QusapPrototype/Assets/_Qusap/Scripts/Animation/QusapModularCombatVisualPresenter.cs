using UnityEngine;

namespace Qusap
{
    [DefaultExecutionOrder(120)]
    [DisallowMultipleComponent]
    public sealed class QusapModularCombatVisualPresenter : MonoBehaviour
    {
        private readonly struct NeutralTransform
        {
            public NeutralTransform(Transform target)
            {
                Target = target;
                LocalPosition = target != null ? target.localPosition : Vector3.zero;
                LocalRotation = target != null ? target.localRotation : Quaternion.identity;
                LocalScale = target != null ? target.localScale : Vector3.one;
            }

            public Transform Target { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }

            public void Restore()
            {
                if (Target == null)
                {
                    return;
                }

                Target.localPosition = LocalPosition;
                Target.localRotation = LocalRotation;
                Target.localScale = LocalScale;
            }
        }

        [Header("Authoritative sources")]
        [SerializeField] private QusapCombatController combatController;
        [SerializeField] private QusapModularVisualRig modularRig;
        [SerializeField] private QusapEquippedWeaponPresenter equippedWeaponPresenter;
        [SerializeField] private QusapProceduralCombatVisualProfile profile;

        [Header("Transform ownership")]
        [SerializeField, Tooltip("Child of ModularFacingPivot used only to freeze facing per execution.")]
        private Transform combatFacingCapturePivot;
        [SerializeField, Tooltip("Disabled while this presenter owns the equipped visual transform.")]
        private QusapWeaponAttackVisualPresenter legacyWeaponPresenter;

        private NeutralTransform bodyNeutral;
        private NeutralTransform leftFootNeutral;
        private NeutralTransform rightFootNeutral;
        private NeutralTransform combatFacingNeutral;
        private NeutralTransform weaponNeutral;
        private GameObject animatedWeapon;
        private QusapModularFacingPresenter facingPresenter;
        private QusapProceduralCombatVisualProfile runtimeFallbackProfile;
        private ulong activeExecutionId;
        private ulong lastTerminatedExecutionId;
        private bool neutralCaptured;

        public QusapCombatController CombatController => combatController;
        public QusapModularVisualRig ModularRig => modularRig;
        public QusapProceduralCombatVisualProfile Profile => profile != null
            ? profile
            : runtimeFallbackProfile;
        public Transform CombatFacingCapturePivot => combatFacingCapturePivot;
        public bool IsPresenting => activeExecutionId != 0;
        public ulong ActiveExecutionId => activeExecutionId;
        public ulong LastTerminatedExecutionId => lastTerminatedExecutionId;
        public QusapCombatVisualCancellationReason LastCancellationReason { get; private set; }
        public QusapProceduralCombatMotionId CurrentMotionId { get; private set; }

        private void Awake()
        {
            ResolveReferences();
            CaptureNeutralPose();
            DisableCompetingWeaponWriter();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CaptureNeutralPose();
            DisableCompetingWeaponWriter();
            if (combatController != null)
            {
                combatController.CombatVisualExecutionEnded += HandleExecutionEnded;
            }
        }

        private void LateUpdate()
        {
            if (combatController == null
                || modularRig == null
                || !combatController.TryGetCombatVisualContext(out QusapCombatVisualContext context))
            {
                CancelActiveExecution();
                return;
            }

            Present(context);
        }

        private void OnDisable()
        {
            if (combatController != null)
            {
                combatController.CombatVisualExecutionEnded -= HandleExecutionEnded;
            }

            CancelActiveExecution();
            RestoreNeutralPose();
        }

        private void OnDestroy()
        {
            RestoreNeutralPose();
            if (runtimeFallbackProfile != null)
            {
                Destroy(runtimeFallbackProfile);
                runtimeFallbackProfile = null;
            }
        }

        public void Configure(
            QusapCombatController controller,
            QusapModularVisualRig rig,
            QusapEquippedWeaponPresenter weaponPresenter,
            QusapProceduralCombatVisualProfile visualProfile,
            Transform facingCapturePivot = null,
            QusapWeaponAttackVisualPresenter competingWeaponPresenter = null)
        {
            if (isActiveAndEnabled && combatController != null)
            {
                combatController.CombatVisualExecutionEnded -= HandleExecutionEnded;
            }

            RestoreNeutralPose();
            combatController = controller;
            modularRig = rig;
            equippedWeaponPresenter = weaponPresenter;
            profile = visualProfile;
            combatFacingCapturePivot = facingCapturePivot;
            legacyWeaponPresenter = competingWeaponPresenter;
            facingPresenter = controller != null
                ? controller.GetComponent<QusapModularFacingPresenter>()
                : null;
            neutralCaptured = false;
            CaptureNeutralPose();
            DisableCompetingWeaponWriter();

            if (isActiveAndEnabled && combatController != null)
            {
                combatController.CombatVisualExecutionEnded += HandleExecutionEnded;
            }
        }

        public bool Present(QusapCombatVisualContext context)
        {
            if (!context.IsActive
                || context.AttackExecutionId <= lastTerminatedExecutionId
                || modularRig == null
                || !CaptureNeutralPose())
            {
                return false;
            }

            GameObject currentWeapon = equippedWeaponPresenter != null
                ? equippedWeaponPresenter.EquippedVisual
                : null;
            if (activeExecutionId != 0 && currentWeapon != animatedWeapon)
            {
                CancelActiveExecution(QusapCombatVisualCancellationReason.WeaponChanged);
                return false;
            }

            if (activeExecutionId != context.AttackExecutionId)
            {
                if (activeExecutionId != 0)
                {
                    TerminateActiveExecution();
                }

                activeExecutionId = context.AttackExecutionId;
                animatedWeapon = currentWeapon;
                weaponNeutral = new NeutralTransform(
                    animatedWeapon != null ? animatedWeapon.transform : null);
            }

            bool requiresWeapon = context.Command == QusapCombatCommand.WeaponLight
                || context.Command == QusapCombatCommand.WeaponStrong;
            if (requiresWeapon && animatedWeapon == null)
            {
                CancelActiveExecution(QusapCombatVisualCancellationReason.Disarmed);
                return false;
            }

            QusapProceduralCombatVisualProfile activeProfile = GetProfile();
            QusapProceduralCombatPoseValue pose = activeProfile.Evaluate(
                context, out QusapProceduralCombatMotionId motionId);
            CurrentMotionId = motionId;
            ApplyCapturedFacing(context.CapturedFacing);
            ApplyRigidPose(bodyNeutral, pose.BodyLocalPosition, pose.BodyLocalEulerAngles);
            ApplyRigidPose(
                leftFootNeutral,
                pose.LeftFootLocalPosition,
                pose.LeftFootLocalEulerAngles);
            ApplyRigidPose(
                rightFootNeutral,
                pose.RightFootLocalPosition,
                pose.RightFootLocalEulerAngles);
            ApplyWeaponPose(context.CapturedFacing, pose);
            return true;
        }

        public void CancelExecution(
            ulong executionId,
            QusapCombatVisualCancellationReason reason =
                QusapCombatVisualCancellationReason.Cancelled)
        {
            if (executionId == 0 || executionId < activeExecutionId)
            {
                return;
            }

            lastTerminatedExecutionId = System.Math.Max(
                lastTerminatedExecutionId, executionId);
            LastCancellationReason = reason;
            if (executionId == activeExecutionId)
            {
                RestoreNeutralPose();
                activeExecutionId = 0;
                animatedWeapon = null;
            }
        }

        public bool RestoreNeutralPose()
        {
            if (!neutralCaptured)
            {
                return false;
            }

            bodyNeutral.Restore();
            leftFootNeutral.Restore();
            rightFootNeutral.Restore();
            combatFacingNeutral.Restore();
            weaponNeutral.Restore();
            return true;
        }

        private void ResolveReferences()
        {
            combatController ??= GetComponent<QusapCombatController>();
            modularRig ??= GetComponentInChildren<QusapModularVisualRig>(true);
            equippedWeaponPresenter ??= GetComponent<QusapEquippedWeaponPresenter>();
            legacyWeaponPresenter ??= GetComponent<QusapWeaponAttackVisualPresenter>();
            facingPresenter ??= GetComponent<QusapModularFacingPresenter>();
            if (combatFacingCapturePivot == null)
            {
                Transform alignment = transform.Find("PlayerVisual_ModularAlignment");
                Transform facing = alignment != null ? alignment.Find("ModularFacingPivot") : null;
                combatFacingCapturePivot = facing != null
                    ? facing.Find("CombatFacingCapturePivot")
                    : null;
            }
        }

        private bool CaptureNeutralPose()
        {
            if (neutralCaptured)
            {
                return true;
            }

            if (modularRig == null || !modularRig.TryValidateReferences(out _))
            {
                return false;
            }

            bodyNeutral = new NeutralTransform(modularRig.BodyPivot);
            leftFootNeutral = new NeutralTransform(modularRig.FootPivotLeft);
            rightFootNeutral = new NeutralTransform(modularRig.FootPivotRight);
            combatFacingNeutral = new NeutralTransform(combatFacingCapturePivot);
            neutralCaptured = true;
            return true;
        }

        private void DisableCompetingWeaponWriter()
        {
            if (legacyWeaponPresenter != null)
            {
                legacyWeaponPresenter.ResetPresentation();
                legacyWeaponPresenter.enabled = false;
            }
        }

        private QusapProceduralCombatVisualProfile GetProfile()
        {
            if (profile != null)
            {
                return profile;
            }

            runtimeFallbackProfile ??= QusapProceduralCombatVisualProfile.CreateDefault();
            return runtimeFallbackProfile;
        }

        private void ApplyCapturedFacing(int capturedFacing)
        {
            if (combatFacingCapturePivot == null || facingPresenter == null)
            {
                return;
            }

            float desiredYaw = capturedFacing < 0
                ? facingPresenter.LeftFacingYaw
                : facingPresenter.RightFacingYaw;
            float currentYaw = combatController != null && combatController.FacingDirection < 0
                ? facingPresenter.LeftFacingYaw
                : facingPresenter.RightFacingYaw;
            combatFacingCapturePivot.localPosition = combatFacingNeutral.LocalPosition;
            combatFacingCapturePivot.localRotation = combatFacingNeutral.LocalRotation
                * Quaternion.Euler(0f, Mathf.DeltaAngle(currentYaw, desiredYaw), 0f);
            combatFacingCapturePivot.localScale = combatFacingNeutral.LocalScale;
        }

        private void ApplyRigidPose(
            NeutralTransform neutral,
            Vector3 visualSpacePosition,
            Vector3 visualSpaceEulerAngles)
        {
            Transform target = neutral.Target;
            if (target == null || target.parent == null || modularRig == null)
            {
                return;
            }

            Vector3 worldOffset = modularRig.transform.TransformVector(visualSpacePosition);
            Vector3 parentLocalOffset = target.parent.InverseTransformVector(worldOffset);
            Quaternion visualWorldRotation = modularRig.transform.rotation
                * Quaternion.Euler(visualSpaceEulerAngles)
                * Quaternion.Inverse(modularRig.transform.rotation);
            Quaternion parentLocalDelta = Quaternion.Inverse(target.parent.rotation)
                * visualWorldRotation
                * target.parent.rotation;
            target.localPosition = neutral.LocalPosition + parentLocalOffset;
            target.localRotation = parentLocalDelta * neutral.LocalRotation;
            target.localScale = neutral.LocalScale;
        }

        private void ApplyWeaponPose(
            int capturedFacing,
            QusapProceduralCombatPoseValue pose)
        {
            if (animatedWeapon == null || equippedWeaponPresenter == null)
            {
                return;
            }

            Transform socket = equippedWeaponPresenter.WeaponSocket;
            Transform weapon = animatedWeapon.transform;
            if (socket == null || weapon.parent != socket)
            {
                CancelActiveExecution(QusapCombatVisualCancellationReason.WeaponChanged);
                return;
            }

            int direction = capturedFacing < 0 ? -1 : 1;
            Vector3 socketOffset = equippedWeaponPresenter.SocketOffset;
            Vector3 desiredSocketPosition = new(
                Mathf.Abs(socketOffset.x) * direction,
                socketOffset.y,
                socketOffset.z);
            Vector3 socketEuler = equippedWeaponPresenter.SocketEulerAngles;
            Quaternion desiredSocketRotation = Quaternion.Euler(
                socketEuler.x,
                socketEuler.y,
                -Mathf.Abs(socketEuler.z) * direction);
            Vector3 desiredWeaponInRoot = desiredSocketPosition
                + desiredSocketRotation
                * (weaponNeutral.LocalPosition + pose.WeaponLocalPosition);
            Quaternion desiredWeaponRotationInRoot = desiredSocketRotation
                * weaponNeutral.LocalRotation
                * Quaternion.Euler(pose.WeaponLocalEulerAngles);
            Quaternion inverseActualSocketRotation = Quaternion.Inverse(socket.localRotation);
            weapon.localPosition = inverseActualSocketRotation
                * (desiredWeaponInRoot - socket.localPosition);
            weapon.localRotation = inverseActualSocketRotation * desiredWeaponRotationInRoot;
            weapon.localScale = weaponNeutral.LocalScale;
        }

        private void HandleExecutionEnded(QusapCombatVisualContext context)
        {
            CancelExecution(context.AttackExecutionId, context.CancellationReason);
        }

        private void CancelActiveExecution(
            QusapCombatVisualCancellationReason reason =
                QusapCombatVisualCancellationReason.Cancelled)
        {
            if (activeExecutionId == 0)
            {
                RestoreNeutralPose();
                return;
            }

            TerminateActiveExecution(reason);
        }

        private void TerminateActiveExecution(
            QusapCombatVisualCancellationReason reason =
                QusapCombatVisualCancellationReason.Cancelled)
        {
            lastTerminatedExecutionId = System.Math.Max(
                lastTerminatedExecutionId, activeExecutionId);
            LastCancellationReason = reason;
            RestoreNeutralPose();
            activeExecutionId = 0;
            animatedWeapon = null;
        }
    }
}
