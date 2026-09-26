using UnityEngine;

public sealed class QusapWeaponGripPosePlayer : MonoBehaviour
{
    [SerializeField] private AnimationClip gripClip;

    public AnimationClip GripClip
    {
        get => gripClip;
        set => gripClip = value;
    }

    private void LateUpdate()
    {
        if (gripClip) gripClip.SampleAnimation(gameObject, 0f);
    }
}
