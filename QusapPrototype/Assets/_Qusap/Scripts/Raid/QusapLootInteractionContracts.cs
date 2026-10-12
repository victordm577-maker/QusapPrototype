using System;

namespace Qusap
{
    public enum QusapLootInteractionRejection
    { None, OutOfRange, NoLineOfSight, Incapacitated, SessionNotRunning, InvalidTarget, Released, TargetChanged, Depleted, Closed }

    public readonly struct QusapLootInteractionCommand
    {
        public QusapLootInteractionCommand(bool held, int selectionDelta = 0, bool takeOne = false, bool takeStack = false, bool close = false)
        { Held = held; SelectionDelta = selectionDelta; TakeOne = takeOne; TakeStack = takeStack; Close = close; }
        public bool Held { get; }
        public int SelectionDelta { get; }
        public bool TakeOne { get; }
        public bool TakeStack { get; }
        public bool Close { get; }
    }

    public interface IQusapLootInteractionInput
    {
        string BindingLabel { get; }
        QusapLootInteractionCommand ReadCommand();
    }

    // Only the opening clock lives here. Combat clocks remain in their authoritative components.
    public sealed class QusapLootInteractionProgress
    {
        private bool armed = true;
        public QusapLootInteractionProgress(float duration = .45f)
        { if (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0) throw new ArgumentOutOfRangeException(nameof(duration)); Duration = duration; }
        public float Duration { get; }
        public float Elapsed { get; private set; }
        public bool IsHolding { get; private set; }
        public string TargetId { get; private set; }
        public float Normalized => Math.Min(1, Elapsed / Duration);
        public QusapLootInteractionRejection LastCancellation { get; private set; }

        public void Cancel(QusapLootInteractionRejection reason)
        {
            if (IsHolding) { LastCancellation = reason; armed = false; }
            IsHolding = false; Elapsed = 0; TargetId = null;
        }

        // One attempt per press, including a cancelled press: release explicitly rearms it.
        public bool Advance(bool held, string targetId, bool unopened, QusapLootInteractionRejection rejection, float delta)
        {
            if (!held) { Cancel(QusapLootInteractionRejection.Released); armed = true; return false; }
            if (rejection != QusapLootInteractionRejection.None) { Cancel(rejection); armed = false; return false; }
            if (IsHolding && !string.Equals(TargetId, targetId, StringComparison.Ordinal))
            { Cancel(QusapLootInteractionRejection.TargetChanged); return false; }
            if (!IsHolding)
            {
                if (!armed || !unopened || string.IsNullOrEmpty(targetId)) return false;
                TargetId = targetId; IsHolding = true; Elapsed = 0; LastCancellation = QusapLootInteractionRejection.None;
            }
            Elapsed += Math.Max(0, delta);
            if (Elapsed + .000001f < Duration) return false;
            IsHolding = false; armed = false; TargetId = null; Elapsed = Duration; return true;
        }
    }
}
