using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [DisallowMultipleComponent]
    public sealed class QusapModularVisualRig : MonoBehaviour
    {
        [Serializable]
        private struct NeutralTransform
        {
            public Transform Transform;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;

            public static NeutralTransform Capture(Transform target)
            {
                return new NeutralTransform
                {
                    Transform = target,
                    LocalPosition = target.localPosition,
                    LocalRotation = target.localRotation,
                    LocalScale = target.localScale
                };
            }

            public void Restore()
            {
                if (Transform == null)
                    return;

                Transform.localPosition = LocalPosition;
                Transform.localRotation = LocalRotation;
                Transform.localScale = LocalScale;
            }
        }

        [Header("Imported modular hierarchy")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform bodyPivot;
        [SerializeField] private Transform body;
        [SerializeField] private Transform footPivotLeft;
        [SerializeField] private Transform footLeft;
        [SerializeField] private Transform footPivotRight;
        [SerializeField] private Transform footRight;

        [Header("Captured neutral pose")]
        [SerializeField, HideInInspector] private bool neutralPoseCaptured;
        [SerializeField, HideInInspector] private NeutralTransform visualRootNeutral;
        [SerializeField, HideInInspector] private NeutralTransform bodyPivotNeutral;
        [SerializeField, HideInInspector] private NeutralTransform bodyNeutral;
        [SerializeField, HideInInspector] private NeutralTransform footPivotLeftNeutral;
        [SerializeField, HideInInspector] private NeutralTransform footLeftNeutral;
        [SerializeField, HideInInspector] private NeutralTransform footPivotRightNeutral;
        [SerializeField, HideInInspector] private NeutralTransform footRightNeutral;

        public Transform VisualRoot => visualRoot;
        public Transform BodyPivot => bodyPivot;
        public Transform Body => body;
        public Transform FootPivotLeft => footPivotLeft;
        public Transform FootLeft => footLeft;
        public Transform FootPivotRight => footPivotRight;
        public Transform FootRight => footRight;
        public bool NeutralPoseCaptured => neutralPoseCaptured;

        private void OnEnable()
        {
            if (neutralPoseCaptured)
                ResetNeutralPose();
            else
                CaptureNeutralPose();
        }

        private void OnDisable()
        {
            if (neutralPoseCaptured)
                ResetNeutralPose();
        }

        public void Configure(
            Transform configuredVisualRoot,
            Transform configuredBodyPivot,
            Transform configuredBody,
            Transform configuredFootPivotLeft,
            Transform configuredFootLeft,
            Transform configuredFootPivotRight,
            Transform configuredFootRight)
        {
            visualRoot = configuredVisualRoot;
            bodyPivot = configuredBodyPivot;
            body = configuredBody;
            footPivotLeft = configuredFootPivotLeft;
            footLeft = configuredFootLeft;
            footPivotRight = configuredFootPivotRight;
            footRight = configuredFootRight;
            CaptureNeutralPose();
        }

        public bool TryValidateReferences(out string error)
        {
            Transform[] references =
            {
                visualRoot,
                bodyPivot,
                body,
                footPivotLeft,
                footLeft,
                footPivotRight,
                footRight
            };

            string[] labels =
            {
                nameof(VisualRoot),
                nameof(BodyPivot),
                nameof(Body),
                nameof(FootPivotLeft),
                nameof(FootLeft),
                nameof(FootPivotRight),
                nameof(FootRight)
            };

            var unique = new HashSet<Transform>();
            for (int i = 0; i < references.Length; i++)
            {
                if (references[i] == null)
                {
                    error = $"{labels[i]} is missing.";
                    return false;
                }

                if (!unique.Add(references[i]))
                {
                    error = $"{labels[i]} duplicates another modular visual reference.";
                    return false;
                }
            }

            if (visualRoot.name != "QusapVisualRoot"
                || bodyPivot.name != "BodyPivot"
                || body.name != "Body"
                || footPivotLeft.name != "FootPivot_L"
                || footLeft.name != "FloatingFoot_L"
                || footPivotRight.name != "FootPivot_R"
                || footRight.name != "FloatingFoot_R")
            {
                error = "The assigned transforms do not use the expected modular hierarchy names.";
                return false;
            }

            if (bodyPivot.parent != visualRoot
                || footPivotLeft.parent != visualRoot
                || footPivotRight.parent != visualRoot
                || body.parent != bodyPivot
                || footLeft.parent != footPivotLeft
                || footRight.parent != footPivotRight)
            {
                error = "The assigned transforms do not match the expected modular parent-child hierarchy.";
                return false;
            }

            if (!HasRigidMesh(body) || !HasRigidMesh(footLeft) || !HasRigidMesh(footRight))
            {
                error = "Body and both feet must each contain a MeshFilter and MeshRenderer.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        public bool CaptureNeutralPose()
        {
            if (!TryValidateReferences(out _))
            {
                neutralPoseCaptured = false;
                return false;
            }

            visualRootNeutral = NeutralTransform.Capture(visualRoot);
            bodyPivotNeutral = NeutralTransform.Capture(bodyPivot);
            bodyNeutral = NeutralTransform.Capture(body);
            footPivotLeftNeutral = NeutralTransform.Capture(footPivotLeft);
            footLeftNeutral = NeutralTransform.Capture(footLeft);
            footPivotRightNeutral = NeutralTransform.Capture(footPivotRight);
            footRightNeutral = NeutralTransform.Capture(footRight);
            neutralPoseCaptured = true;
            return true;
        }

        public bool ResetNeutralPose()
        {
            if (!neutralPoseCaptured || !TryValidateReferences(out _))
                return false;

            visualRootNeutral.Restore();
            bodyPivotNeutral.Restore();
            bodyNeutral.Restore();
            footPivotLeftNeutral.Restore();
            footLeftNeutral.Restore();
            footPivotRightNeutral.Restore();
            footRightNeutral.Restore();
            return true;
        }

        private static bool HasRigidMesh(Transform target)
        {
            return target.GetComponent<MeshFilter>() != null
                && target.GetComponent<MeshRenderer>() != null
                && target.GetComponent<SkinnedMeshRenderer>() == null;
        }
    }
}
