using System;
using System.Collections.Generic;
using UnityEngine;

namespace Qusap
{
    [Serializable]
    public sealed class QusapComboStep
    {
        [SerializeField] private QusapCombatCommand command;
        [SerializeField] private double minimumDelay;
        [SerializeField] private double maximumDelay;

        public QusapComboStep(QusapCombatCommand command, double minimumDelay, double maximumDelay)
        {
            if (!Enum.IsDefined(typeof(QusapCombatCommand), command))
            {
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown combat command.");
            }

            ValidateTiming(minimumDelay, maximumDelay);
            this.command = command;
            this.minimumDelay = minimumDelay;
            this.maximumDelay = maximumDelay;
        }

        public QusapCombatCommand Command => command;
        public double MinimumDelay => minimumDelay;
        public double MaximumDelay => maximumDelay;

        internal QusapComboStep AsFirstStep()
        {
            return minimumDelay == 0d && maximumDelay == 0d
                ? this
                : new QusapComboStep(command, 0d, 0d);
        }

        private static void ValidateTiming(double minimumDelay, double maximumDelay)
        {
            if (double.IsNaN(minimumDelay) || double.IsInfinity(minimumDelay) || minimumDelay < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(minimumDelay), minimumDelay, "Minimum delay must be finite and non-negative.");
            }

            if (double.IsNaN(maximumDelay) || double.IsInfinity(maximumDelay) || maximumDelay < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumDelay), maximumDelay, "Maximum delay must be finite and non-negative.");
            }

            if (maximumDelay < minimumDelay)
            {
                throw new ArgumentException("Maximum delay cannot be less than minimum delay.");
            }
        }
    }

    [Serializable]
    public sealed class QusapComboDefinition
    {
        public const int MaximumStepCount = 32;

        [SerializeField] private QusapComboId comboId;
        [SerializeField] private string displayName;
        [SerializeField] private QusapComboStep[] steps = Array.Empty<QusapComboStep>();

        public QusapComboDefinition(
            QusapComboId comboId,
            string displayName,
            IEnumerable<QusapComboStep> steps)
        {
            if (!Enum.IsDefined(typeof(QusapComboId), comboId))
            {
                throw new ArgumentOutOfRangeException(nameof(comboId), comboId, "Unknown combo identifier.");
            }

            if (steps == null)
            {
                throw new ArgumentNullException(nameof(steps));
            }

            List<QusapComboStep> copiedSteps = new();
            foreach (QusapComboStep step in steps)
            {
                if (step == null)
                {
                    throw new ArgumentException("A combo cannot contain a null step.", nameof(steps));
                }

                if (copiedSteps.Count >= MaximumStepCount)
                {
                    throw new ArgumentException(
                        $"A combo cannot contain more than {MaximumStepCount} steps.", nameof(steps));
                }

                if (step.Command == QusapCombatCommand.Parry)
                {
                    throw new ArgumentException("Offensive combos cannot contain Parry.", nameof(steps));
                }

                copiedSteps.Add(step);
            }

            if (copiedSteps.Count == 0)
            {
                throw new ArgumentException("A combo must contain at least one step.", nameof(steps));
            }

            copiedSteps[0] = copiedSteps[0].AsFirstStep();
            this.comboId = comboId;
            this.displayName = string.IsNullOrWhiteSpace(displayName) ? comboId.ToString() : displayName.Trim();
            this.steps = copiedSteps.ToArray();
        }

        public QusapComboId ComboId => comboId;
        public string DisplayName => displayName;
        public IReadOnlyList<QusapComboStep> Steps => Array.AsReadOnly(steps);
        public int StepCount => steps.Length;

        public QusapComboStep GetStep(int index)
        {
            return steps[index];
        }

        public static IReadOnlyList<QusapComboDefinition> CreateDefaultDefinitions(
            double minimumDelay = 0.05d,
            double maximumDelay = 0.5d)
        {
            QusapComboStep Step(QusapCombatCommand command)
            {
                return new QusapComboStep(command, minimumDelay, maximumDelay);
            }

            return Array.AsReadOnly(new[]
            {
                new QusapComboDefinition(
                    QusapComboId.Damage,
                    "Damage",
                    new[]
                    {
                        Step(QusapCombatCommand.WeaponLight),
                        Step(QusapCombatCommand.WeaponLight),
                        Step(QusapCombatCommand.BodyAttack),
                        Step(QusapCombatCommand.WeaponStrong)
                    }),
                new QusapComboDefinition(
                    QusapComboId.Disarm,
                    "Disarm",
                    new[]
                    {
                        Step(QusapCombatCommand.WeaponLight),
                        Step(QusapCombatCommand.WeaponLight),
                        Step(QusapCombatCommand.Headbutt)
                    }),
                new QusapComboDefinition(
                    QusapComboId.Launch,
                    "Launch",
                    new[]
                    {
                        Step(QusapCombatCommand.BodyAttack),
                        Step(QusapCombatCommand.WeaponLight),
                        Step(QusapCombatCommand.BodyAttack)
                    })
            });
        }
    }
}
