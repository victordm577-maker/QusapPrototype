using System;
using System.Collections.Generic;

namespace Qusap
{
    public sealed class QusapComboSequenceMatcher
    {
        private readonly QusapComboDefinition[] definitions;
        private readonly List<Candidate> candidates = new();
        private bool hasProcessedPress;
        private ulong lastProcessedPressId;
        private bool hasAcceptedTimestamp;
        private double lastAcceptedTimestamp;

        public QusapComboSequenceMatcher(IEnumerable<QusapComboDefinition> definitions)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            List<QusapComboDefinition> copiedDefinitions = new();
            foreach (QusapComboDefinition definition in definitions)
            {
                if (definition == null)
                {
                    throw new ArgumentException("The definition collection cannot contain null.", nameof(definitions));
                }

                ValidateNoDuplicate(definition, copiedDefinitions, nameof(definitions));
                copiedDefinitions.Add(definition);
            }

            if (copiedDefinitions.Count == 0)
            {
                throw new ArgumentException("At least one combo definition is required.", nameof(definitions));
            }

            this.definitions = copiedDefinitions.ToArray();
        }

        public int ActiveCandidateCount => candidates.Count;
        public bool HasActiveCandidates => candidates.Count > 0;

        public QusapComboMatchResult ProcessPress(
            QusapCombatCommand command,
            ulong pressId,
            double timestamp)
        {
            if (!Enum.IsDefined(typeof(QusapCombatCommand), command))
            {
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown combat command.");
            }

            if (hasProcessedPress && pressId <= lastProcessedPressId)
            {
                return Result(QusapComboMatchFlags.DuplicateOrStalePressIgnored);
            }

            hasProcessedPress = true;
            lastProcessedPressId = pressId;

            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp))
            {
                return Result(QusapComboMatchFlags.InvalidTimestampIgnored);
            }

            if (hasAcceptedTimestamp && timestamp < lastAcceptedTimestamp)
            {
                return Result(QusapComboMatchFlags.BackwardTimestampIgnored);
            }

            hasAcceptedTimestamp = true;
            lastAcceptedTimestamp = timestamp;

            bool hadCandidates = candidates.Count > 0;
            bool advanced = false;
            bool discarded = false;
            QusapComboId? completedComboId = null;
            List<Candidate> survivors = new(candidates.Count + definitions.Length);

            for (int i = 0; i < candidates.Count; i++)
            {
                Candidate candidate = candidates[i];
                QusapComboStep expectedStep = candidate.Definition.GetStep(candidate.NextStepIndex);

                if (expectedStep.Command != command)
                {
                    discarded = true;
                    continue;
                }

                double elapsed = timestamp - candidate.PreviousStepTimestamp;
                if (elapsed < expectedStep.MinimumDelay)
                {
                    survivors.Add(candidate);
                    continue;
                }

                if (elapsed > expectedStep.MaximumDelay)
                {
                    discarded = true;
                    continue;
                }

                advanced = true;
                int nextStepIndex = candidate.NextStepIndex + 1;
                if (nextStepIndex == candidate.Definition.StepCount)
                {
                    completedComboId = candidate.Definition.ComboId;
                    break;
                }

                survivors.Add(new Candidate(candidate.Definition, nextStepIndex, timestamp));
            }

            if (completedComboId.HasValue)
            {
                candidates.Clear();
                QusapComboMatchFlags completionFlags = QusapComboMatchFlags.Advanced
                    | QusapComboMatchFlags.Completed;
                if (discarded)
                {
                    completionFlags |= QusapComboMatchFlags.CandidatesDiscarded;
                }

                return new QusapComboMatchResult(
                    completionFlags, completedComboId.Value, Array.Empty<QusapComboId>());
            }

            bool started = false;
            bool canStartNewSequence = !hadCandidates || discarded;
            for (int i = 0; canStartNewSequence && i < definitions.Length; i++)
            {
                QusapComboDefinition definition = definitions[i];
                if (definition.GetStep(0).Command != command)
                {
                    continue;
                }

                started = true;
                advanced = true;
                if (definition.StepCount == 1)
                {
                    candidates.Clear();
                    QusapComboMatchFlags singleStepFlags = QusapComboMatchFlags.Advanced
                        | QusapComboMatchFlags.Completed;
                    if (discarded)
                    {
                        singleStepFlags |= QusapComboMatchFlags.CandidatesDiscarded;
                    }

                    return new QusapComboMatchResult(
                        singleStepFlags, definition.ComboId, Array.Empty<QusapComboId>());
                }

                survivors.Add(new Candidate(definition, 1, timestamp));
            }

            candidates.Clear();
            candidates.AddRange(survivors);

            QusapComboMatchFlags flags = QusapComboMatchFlags.None;
            if (advanced)
            {
                flags |= QusapComboMatchFlags.Advanced;
            }

            if (discarded)
            {
                flags |= QusapComboMatchFlags.CandidatesDiscarded;
            }

            if (candidates.Count > 0)
            {
                flags |= QusapComboMatchFlags.CandidatesRemain;
            }

            if (hadCandidates && discarded && started)
            {
                flags |= QusapComboMatchFlags.SequenceRestarted;
            }

            return Result(flags);
        }

        public void Reset()
        {
            candidates.Clear();
        }

        private QusapComboMatchResult Result(QusapComboMatchFlags flags)
        {
            if (candidates.Count > 0)
            {
                flags |= QusapComboMatchFlags.CandidatesRemain;
            }

            List<QusapComboId> activeIds = new();
            for (int i = 0; i < candidates.Count; i++)
            {
                QusapComboId id = candidates[i].Definition.ComboId;
                if (!activeIds.Contains(id))
                {
                    activeIds.Add(id);
                }
            }

            return new QusapComboMatchResult(flags, null, activeIds.ToArray());
        }

        private static void ValidateNoDuplicate(
            QusapComboDefinition definition,
            List<QusapComboDefinition> existingDefinitions,
            string parameterName)
        {
            for (int i = 0; i < existingDefinitions.Count; i++)
            {
                QusapComboDefinition existing = existingDefinitions[i];
                if (existing.ComboId == definition.ComboId)
                {
                    throw new ArgumentException(
                        $"Combo id {definition.ComboId} is defined more than once.", parameterName);
                }

                if (HaveSameSequence(existing, definition))
                {
                    throw new ArgumentException(
                        $"Combos {existing.ComboId} and {definition.ComboId} have duplicate sequences.",
                        parameterName);
                }
            }
        }

        private static bool HaveSameSequence(QusapComboDefinition left, QusapComboDefinition right)
        {
            if (left.StepCount != right.StepCount)
            {
                return false;
            }

            for (int i = 0; i < left.StepCount; i++)
            {
                if (left.GetStep(i).Command != right.GetStep(i).Command)
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct Candidate
        {
            public Candidate(
                QusapComboDefinition definition,
                int nextStepIndex,
                double previousStepTimestamp)
            {
                Definition = definition;
                NextStepIndex = nextStepIndex;
                PreviousStepTimestamp = previousStepTimestamp;
            }

            public QusapComboDefinition Definition { get; }
            public int NextStepIndex { get; }
            public double PreviousStepTimestamp { get; }
        }
    }
}
