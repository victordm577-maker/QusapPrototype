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

        internal bool HasActiveCandidate(QusapComboId comboId)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Definition.ComboId == comboId)
                {
                    return true;
                }
            }

            return false;
        }

        public QusapComboMatchResult ProcessPress(
            QusapCombatCommand command,
            ulong pressId,
            double timestamp)
        {
            Evaluation evaluation = EvaluatePress(command, pressId, timestamp);
            hasProcessedPress = evaluation.HasProcessedPress;
            lastProcessedPressId = evaluation.LastProcessedPressId;
            hasAcceptedTimestamp = evaluation.HasAcceptedTimestamp;
            lastAcceptedTimestamp = evaluation.LastAcceptedTimestamp;
            candidates.Clear();
            candidates.AddRange(evaluation.Candidates);
            return evaluation.Result;
        }

        public QusapComboMatchResult PreviewPress(
            QusapCombatCommand command,
            ulong pressId,
            double timestamp)
        {
            return EvaluatePress(command, pressId, timestamp).Result;
        }

        public void Reset()
        {
            candidates.Clear();
        }

        private Evaluation EvaluatePress(
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
                return Evaluation.Unchanged(
                    Result(QusapComboMatchFlags.DuplicateOrStalePressIgnored, candidates),
                    candidates,
                    hasProcessedPress,
                    lastProcessedPressId,
                    hasAcceptedTimestamp,
                    lastAcceptedTimestamp);
            }

            bool evaluatedHasProcessedPress = true;
            ulong evaluatedLastProcessedPressId = pressId;

            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp))
            {
                return Evaluation.Unchanged(
                    Result(QusapComboMatchFlags.InvalidTimestampIgnored, candidates),
                    candidates,
                    evaluatedHasProcessedPress,
                    evaluatedLastProcessedPressId,
                    hasAcceptedTimestamp,
                    lastAcceptedTimestamp);
            }

            if (hasAcceptedTimestamp && timestamp < lastAcceptedTimestamp)
            {
                return Evaluation.Unchanged(
                    Result(QusapComboMatchFlags.BackwardTimestampIgnored, candidates),
                    candidates,
                    evaluatedHasProcessedPress,
                    evaluatedLastProcessedPressId,
                    hasAcceptedTimestamp,
                    lastAcceptedTimestamp);
            }

            bool evaluatedHasAcceptedTimestamp = true;
            double evaluatedLastAcceptedTimestamp = timestamp;

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
                QusapComboMatchFlags completionFlags = QusapComboMatchFlags.Advanced
                    | QusapComboMatchFlags.Completed;
                if (discarded)
                {
                    completionFlags |= QusapComboMatchFlags.CandidatesDiscarded;
                }

                return new Evaluation(
                    new QusapComboMatchResult(
                        completionFlags, completedComboId.Value, Array.Empty<QusapComboId>()),
                    Array.Empty<Candidate>(),
                    evaluatedHasProcessedPress,
                    evaluatedLastProcessedPressId,
                    evaluatedHasAcceptedTimestamp,
                    evaluatedLastAcceptedTimestamp);
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
                    QusapComboMatchFlags singleStepFlags = QusapComboMatchFlags.Advanced
                        | QusapComboMatchFlags.Completed;
                    if (discarded)
                    {
                        singleStepFlags |= QusapComboMatchFlags.CandidatesDiscarded;
                    }

                    return new Evaluation(
                        new QusapComboMatchResult(
                            singleStepFlags, definition.ComboId, Array.Empty<QusapComboId>()),
                        Array.Empty<Candidate>(),
                        evaluatedHasProcessedPress,
                        evaluatedLastProcessedPressId,
                        evaluatedHasAcceptedTimestamp,
                        evaluatedLastAcceptedTimestamp);
                }

                survivors.Add(new Candidate(definition, 1, timestamp));
            }

            QusapComboMatchFlags flags = QusapComboMatchFlags.None;
            if (advanced)
            {
                flags |= QusapComboMatchFlags.Advanced;
            }

            if (discarded)
            {
                flags |= QusapComboMatchFlags.CandidatesDiscarded;
            }

            if (survivors.Count > 0)
            {
                flags |= QusapComboMatchFlags.CandidatesRemain;
            }

            if (hadCandidates && discarded && started)
            {
                flags |= QusapComboMatchFlags.SequenceRestarted;
            }

            return new Evaluation(
                Result(flags, survivors),
                survivors.ToArray(),
                evaluatedHasProcessedPress,
                evaluatedLastProcessedPressId,
                evaluatedHasAcceptedTimestamp,
                evaluatedLastAcceptedTimestamp);
        }

        private static QusapComboMatchResult Result(
            QusapComboMatchFlags flags,
            IReadOnlyList<Candidate> resultCandidates)
        {
            if (resultCandidates.Count > 0)
            {
                flags |= QusapComboMatchFlags.CandidatesRemain;
            }

            List<QusapComboId> activeIds = new();
            for (int i = 0; i < resultCandidates.Count; i++)
            {
                QusapComboId id = resultCandidates[i].Definition.ComboId;
                if (!activeIds.Contains(id))
                {
                    activeIds.Add(id);
                }
            }

            return new QusapComboMatchResult(flags, null, activeIds.ToArray());
        }

        private readonly struct Evaluation
        {
            public Evaluation(
                QusapComboMatchResult result,
                Candidate[] candidates,
                bool hasProcessedPress,
                ulong lastProcessedPressId,
                bool hasAcceptedTimestamp,
                double lastAcceptedTimestamp)
            {
                Result = result;
                Candidates = candidates;
                HasProcessedPress = hasProcessedPress;
                LastProcessedPressId = lastProcessedPressId;
                HasAcceptedTimestamp = hasAcceptedTimestamp;
                LastAcceptedTimestamp = lastAcceptedTimestamp;
            }

            public QusapComboMatchResult Result { get; }
            public Candidate[] Candidates { get; }
            public bool HasProcessedPress { get; }
            public ulong LastProcessedPressId { get; }
            public bool HasAcceptedTimestamp { get; }
            public double LastAcceptedTimestamp { get; }

            public static Evaluation Unchanged(
                QusapComboMatchResult result,
                IReadOnlyList<Candidate> candidates,
                bool hasProcessedPress,
                ulong lastProcessedPressId,
                bool hasAcceptedTimestamp,
                double lastAcceptedTimestamp)
            {
                Candidate[] copiedCandidates = new Candidate[candidates.Count];
                for (int i = 0; i < candidates.Count; i++)
                {
                    copiedCandidates[i] = candidates[i];
                }

                return new Evaluation(
                    result,
                    copiedCandidates,
                    hasProcessedPress,
                    lastProcessedPressId,
                    hasAcceptedTimestamp,
                    lastAcceptedTimestamp);
            }
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
