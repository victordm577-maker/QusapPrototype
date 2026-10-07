using UnityEngine;

namespace Qusap
{
    // A bridge to the approved presentation; never an authority for combat.
    public interface IQusapImpactVisualSource
    {
        Transform ImpactVisualRoot { get; }
        Transform ImpactSword { get; }
        bool IsImpactSwordWindowActive { get; }
    }

    public readonly struct QusapCombatImpactProfile
    {
        public readonly float Hold, FlashDuration, FlashIntensity, ShakeAmplitude, ShakeDuration;
        public readonly Color Color;

        private QusapCombatImpactProfile(float hold, float flash, float intensity,
            float shake, float shakeDuration, Color color)
        {
            Hold = hold; FlashDuration = flash; FlashIntensity = intensity;
            ShakeAmplitude = shake; ShakeDuration = shakeDuration; Color = color;
        }

        public static QusapCombatImpactProfile For(QusapCombatFeedbackType kind) => kind switch
        {
            QusapCombatFeedbackType.Light => new(.035f, .08f, .55f, .025f, .09f, new(1f, .72f, .22f)),
            QusapCombatFeedbackType.Heavy => new(.055f, .12f, .80f, .055f, .12f, new(1f, .34f, .08f)),
            QusapCombatFeedbackType.Damage => new(.075f, .14f, .90f, .095f, .16f, new(1f, .10f, .06f)),
            QusapCombatFeedbackType.Disarm => new(.075f, .14f, .90f, .095f, .16f, new(.72f, .20f, 1f)),
            QusapCombatFeedbackType.Launch => new(.075f, .14f, .90f, .095f, .16f, new(.05f, .90f, 1f)),
            QusapCombatFeedbackType.ParrySucceeded => new(.075f, .14f, .85f, .11f, .18f, new(.15f, 1f, .65f)),
            _ => default
        };
    }
}
