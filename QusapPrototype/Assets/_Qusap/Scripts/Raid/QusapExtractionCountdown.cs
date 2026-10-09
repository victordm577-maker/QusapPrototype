using System;

namespace Qusap
{
    public enum QusapExtractionStatus { Active, Extracting, Extracted, Eliminated }
    public enum QusapExtractionCancellation { None, LeftVolume, Damage, Eliminated, TechnicalRecovery, ZoneDisabled, PlayerDisabled, SettlementRejected }

    // Gameplay seconds supplied by the C# owner; independent of animation and presentation.
    public sealed class QusapExtractionCountdown
    {
        private bool resolving;
        public QusapExtractionStatus Status { get; private set; }
        public QusapExtractionCancellation Cancellation { get; private set; }
        public float Duration { get; private set; } = 5f;
        public float Elapsed { get; private set; }
        public float Remaining => Math.Max(0f, Duration - Elapsed);
        public float Progress => Elapsed / Duration;
        public int TransfersResolved { get; private set; }
        public bool Begin(float seconds, bool eligible)
        {
            if (resolving || !eligible || Status != QusapExtractionStatus.Active || !float.IsFinite(seconds) || seconds <= 0f) return false;
            Duration = seconds; Elapsed = 0f; Status = QusapExtractionStatus.Extracting;
            return true;
        }
        public void Cancel(QusapExtractionCancellation reason)
        {
            if (Status != QusapExtractionStatus.Extracting) return;
            Elapsed = 0f; Cancellation = reason; Status = QusapExtractionStatus.Active;
        }
        public void Eliminate()
        {
            if (Status == QusapExtractionStatus.Extracted) return;
            Cancel(QusapExtractionCancellation.Eliminated); Status = QusapExtractionStatus.Eliminated;
        }
        public bool Advance(float seconds, Func<QusapLootResult> resolve)
        {
            if (resolving || Status != QusapExtractionStatus.Extracting || !float.IsFinite(seconds) || seconds <= 0f) return false;
            Elapsed = Math.Min(Duration, Elapsed + seconds);
            if (Elapsed < Duration) return false;
            // Close the timer before external callbacks: reentry cannot resolve it twice.
            Status = QusapExtractionStatus.Active;
            resolving = true;
            QusapLootResult result;
            try { result = resolve(); }
            finally { resolving = false; }
            if (result != QusapLootResult.Success)
            { Elapsed = 0f; Cancellation = QusapExtractionCancellation.SettlementRejected; return false; }
            Complete();
            return true;
        }
        internal void Complete()
        {
            if (Status == QusapExtractionStatus.Extracted) return;
            Elapsed = Duration; Status = QusapExtractionStatus.Extracted; TransfersResolved++;
        }
    }
}
