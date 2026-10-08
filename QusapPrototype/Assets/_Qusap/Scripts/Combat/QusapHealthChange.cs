using UnityEngine;

namespace Qusap
{
    public enum QusapHealthChangeSource { Combat, Environment, Healing, NewSession }

    public readonly struct QusapHealthChange
    {
        public QusapHealthChange(QusapHitReceiver receiver, float previousDamage,
            QusapHealthChangeSource source, Object sourceObject)
        {
            Receiver = receiver;
            PreviousDamage = previousDamage;
            TotalDamage = receiver.TotalDamageReceived;
            MaxHealth = receiver.MaxHealth;
            CurrentHealth = receiver.CurrentHealth;
            Source = source;
            SourceObject = sourceObject;
        }

        public QusapHitReceiver Receiver { get; }
        public float PreviousDamage { get; }
        public float TotalDamage { get; }
        public float MaxHealth { get; }
        public float CurrentHealth { get; }
        public float AppliedAmount => Mathf.Abs(TotalDamage - PreviousDamage);
        public QusapHealthChangeSource Source { get; }
        public Object SourceObject { get; }
    }
}
