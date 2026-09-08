using System;
using System.Collections.Generic;

namespace Qusap
{
    public sealed class QusapCombatCommandBuffer
    {
        private readonly Queue<QusapCombatCommandPress> pendingPresses = new();
        private ulong lastPressId;

        public QusapCombatCommandBuffer(int capacity = 32)
        {
            Capacity = Math.Max(1, capacity);
        }

        public int Capacity { get; }
        public int PendingCount => pendingPresses.Count;

        public QusapCombatCommandPress Enqueue(QusapCombatCommand command, double timestamp)
        {
            if (!Enum.IsDefined(typeof(QusapCombatCommand), command))
            {
                throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown combat command.");
            }

            if (double.IsNaN(timestamp) || double.IsInfinity(timestamp))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timestamp), timestamp, "Timestamp must be finite.");
            }

            if (lastPressId == ulong.MaxValue)
            {
                throw new OverflowException(
                    "The combat command buffer exhausted its press identifiers and cannot safely enqueue more events.");
            }

            ulong pressId = ++lastPressId;
            QusapCombatCommandPress press = new(command, pressId, timestamp);
            if (pendingPresses.Count == Capacity)
            {
                pendingPresses.Dequeue();
            }

            pendingPresses.Enqueue(press);
            return press;
        }

        public bool TryDequeue(out QusapCombatCommandPress press)
        {
            if (pendingPresses.Count == 0)
            {
                press = default;
                return false;
            }

            press = pendingPresses.Dequeue();
            return true;
        }

        public void Clear()
        {
            pendingPresses.Clear();
        }
    }
}
