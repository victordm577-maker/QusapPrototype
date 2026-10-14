using System;

namespace Qusap
{
    public sealed class QusapRaidTimingSettings
    {
        public const double NormalDurationSeconds = 480;
        public const double NormalExtractionOpeningSeconds = 180;
        public QusapRaidTimingSettings(double durationSeconds = NormalDurationSeconds,
            double extractionOpeningSeconds = NormalExtractionOpeningSeconds)
        {
            if (!double.IsFinite(durationSeconds) || durationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (!double.IsFinite(extractionOpeningSeconds) || extractionOpeningSeconds < 0
                || extractionOpeningSeconds >= durationSeconds)
                throw new ArgumentOutOfRangeException(nameof(extractionOpeningSeconds));
            DurationSeconds = durationSeconds; ExtractionOpeningSeconds = extractionOpeningSeconds;
        }
        public double DurationSeconds { get; }
        public double ExtractionOpeningSeconds { get; }
    }

    // One gameplay clock per session. Only the session advances it; no wall-clock or HUD authority.
    public sealed class QusapRaidTiming
    {
        internal QusapRaidTiming(QusapRaidTimingSettings settings) { Settings = settings; }
        public QusapRaidTimingSettings Settings { get; }
        public bool HasStarted { get; private set; }
        public bool IsRunning { get; private set; }
        public double ElapsedSeconds { get; private set; }
        public double RemainingSeconds => Math.Max(0, Settings.DurationSeconds - ElapsedSeconds);
        public double SecondsUntilExtraction => Math.Max(0, Settings.ExtractionOpeningSeconds - ElapsedSeconds);
        public bool HasExpired => HasStarted && ElapsedSeconds >= Settings.DurationSeconds;
        public bool ExtractionsOpen => IsRunning && SecondsUntilExtraction == 0 && !HasExpired;
        public double ExtractionStepSeconds { get; private set; }
        internal void Start() { if (HasStarted) throw new InvalidOperationException("Clock already started"); HasStarted = IsRunning = true; }
        internal void Stop() { IsRunning = false; ExtractionStepSeconds = 0; }
        internal void Advance(double seconds)
        {
            double previous = ElapsedSeconds;
            ElapsedSeconds += Math.Min(seconds, RemainingSeconds);
            ExtractionStepSeconds = HasExpired ? 0 : Math.Max(0, ElapsedSeconds - Math.Max(previous, Settings.ExtractionOpeningSeconds));
        }
    }
}
