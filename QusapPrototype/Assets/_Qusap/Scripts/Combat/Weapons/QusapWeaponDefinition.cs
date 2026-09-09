using System;
using UnityEngine;

namespace Qusap
{
    [Serializable]
    public sealed class QusapWeaponDefinition
    {
        public const int MaximumIdLength = 64;

        [SerializeField] private string id;
        [SerializeField] private string displayName;

        public QusapWeaponDefinition(string id, string displayName)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A weapon definition requires an identifier.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new ArgumentException("A weapon definition requires a display name.", nameof(displayName));
            }

            string normalizedId = id.Trim();
            if (normalizedId.Length > MaximumIdLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    $"A weapon definition identifier cannot exceed {MaximumIdLength} characters.");
            }

            this.id = normalizedId;
            this.displayName = displayName.Trim();
        }

        public string Id => id;
        public string DisplayName => displayName;
    }
}
