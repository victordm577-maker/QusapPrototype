using System;
using System.Collections.Generic;

namespace Qusap
{
    public enum QusapCombatCommand
    {
        BodyAttack,
        WeaponLight,
        WeaponStrong,
        Headbutt,
        Parry
    }

    public enum QusapComboId
    {
        Damage,
        Disarm,
        Launch
    }

    [Flags]
    public enum QusapComboMatchFlags
    {
        None = 0,
        Advanced = 1 << 0,
        CandidatesRemain = 1 << 1,
        CandidatesDiscarded = 1 << 2,
        SequenceRestarted = 1 << 3,
        Completed = 1 << 4,
        DuplicateOrStalePressIgnored = 1 << 5,
        BackwardTimestampIgnored = 1 << 6,
        InvalidTimestampIgnored = 1 << 7
    }

    public readonly struct QusapComboMatchResult
    {
        private static readonly IReadOnlyList<QusapComboId> NoCandidates =
            Array.AsReadOnly(Array.Empty<QusapComboId>());
        private readonly IReadOnlyList<QusapComboId> activeComboIds;

        internal QusapComboMatchResult(
            QusapComboMatchFlags flags,
            QusapComboId? completedComboId,
            QusapComboId[] activeComboIds)
        {
            Flags = flags;
            CompletedComboId = completedComboId;
            this.activeComboIds = activeComboIds == null
                ? NoCandidates
                : Array.AsReadOnly(activeComboIds);
        }

        public QusapComboMatchFlags Flags { get; }
        public QusapComboId? CompletedComboId { get; }
        public IReadOnlyList<QusapComboId> ActiveComboIds => activeComboIds ?? NoCandidates;
        public bool NoChange => Flags == QusapComboMatchFlags.None;
        public bool Advanced => HasFlag(QusapComboMatchFlags.Advanced);
        public bool CandidatesRemain => HasFlag(QusapComboMatchFlags.CandidatesRemain);
        public bool CandidatesWereDiscarded => HasFlag(QusapComboMatchFlags.CandidatesDiscarded);
        public bool SequenceWasRestarted => HasFlag(QusapComboMatchFlags.SequenceRestarted);
        public bool Completed => HasFlag(QusapComboMatchFlags.Completed);

        public bool HasActiveCandidate(QusapComboId comboId)
        {
            IReadOnlyList<QusapComboId> ids = ActiveComboIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == comboId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasFlag(QusapComboMatchFlags flag)
        {
            return (Flags & flag) != 0;
        }
    }
}
