using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    public enum QusapDamageVolumeMode { OncePerEntry, Periodic }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(Collider))]
    public sealed class QusapDamageVolume : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float damage = 10f;
        [SerializeField] private QusapDamageVolumeMode mode = QusapDamageVolumeMode.Periodic;
        [SerializeField, Min(0.02f)] private float interval = 1f;
        [SerializeField] private Vector3 knockback = Vector3.zero;

        private sealed class Contact
        {
            public readonly HashSet<Collider> Colliders = new();
            public double NextDamageAt;
        }
        private readonly Dictionary<QusapHitReceiver, Contact> contacts = new();
        public float Damage => damage;
        public float Interval => interval;
        public QusapDamageVolumeMode Mode => mode;

        private void OnValidate()
        {
            damage = float.IsNaN(damage) || float.IsInfinity(damage) ? 0f : Mathf.Max(0f, damage);
            interval = float.IsNaN(interval) || float.IsInfinity(interval) ? 1f : Mathf.Max(0.02f, interval);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isActiveAndEnabled) return;
            var receiver = other.GetComponentInParent<QusapHitReceiver>();
            if (receiver == null || receiver.IsHealthDepleted) return;
            if (!contacts.TryGetValue(receiver, out var contact))
            {
                contact = new Contact { NextDamageAt = Time.timeAsDouble + interval };
                contacts.Add(receiver, contact);
                contact.Colliders.Add(other);
                receiver.TryReceiveEnvironmentDamage(damage, this, knockback);
            }
            else contact.Colliders.Add(other);
        }

        private void OnTriggerExit(Collider other)
        {
            var receiver = other.GetComponentInParent<QusapHitReceiver>();
            if (receiver == null || !contacts.TryGetValue(receiver, out var contact)) return;
            contact.Colliders.Remove(other);
            if (contact.Colliders.Count == 0) contacts.Remove(receiver);
        }

        private void FixedUpdate()
        {
            if (mode != QusapDamageVolumeMode.Periodic) return;
            double now = Time.timeAsDouble;
            foreach (var entry in contacts)
            {
                if (entry.Key == null || entry.Key.IsHealthDepleted) continue;
                entry.Value.Colliders.RemoveWhere(c => c == null || !c.enabled || !c.gameObject.activeInHierarchy);
                if (entry.Value.Colliders.Count == 0 || now < entry.Value.NextDamageAt) continue;
                entry.Value.NextDamageAt = now + Mathf.Max(0.02f, interval);
                entry.Key.TryReceiveEnvironmentDamage(damage, this, knockback);
            }
        }

        private void OnDisable() => contacts.Clear();
    }
}
