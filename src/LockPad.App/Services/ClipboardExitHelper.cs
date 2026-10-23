using System;
using System.Diagnostics;

namespace LockPad.App.Services
{
    /// <summary>
    /// Clears LockPad's clipboard content on exit with retries. A single <see cref="IClipboardService.ClearIfOwned"/>
    /// only gets WPF's internal retries (about 1 s), while another process (e.g. the Windows clipboard history
    /// service) has been seen holding the clipboard for about 15 s.
    /// </summary>
    internal static class ClipboardExitHelper
    {
        /// <summary>
        /// Calls ClearIfOwned until <see cref="IClipboardService.OwnsClipboardContent"/> is false, waiting
        /// <paramref name="interval"/> between attempts through <paramref name="pumpingWait"/> (which must keep the
        /// dispatcher running, never Thread.Sleep: a blocked UI thread cannot answer the clipboard's window messages).
        /// Gives up after <paramref name="budget"/>: by the number of waits, and by real elapsed time. Does nothing
        /// when nothing is owned. Returns true when nothing is owned any more.
        /// </summary>
        public static bool ClearWithRetry(IClipboardService clipboard, TimeSpan budget, TimeSpan interval,
            Action<TimeSpan> pumpingWait)
        {
            if (clipboard == null) throw new ArgumentNullException(nameof(clipboard));
            if (pumpingWait == null) throw new ArgumentNullException(nameof(pumpingWait));
            if (interval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(interval));

            if (!clipboard.OwnsClipboardContent) return true;

            long maxWaits = budget <= TimeSpan.Zero ? 0 : (long)Math.Ceiling(budget.Ticks / (double)interval.Ticks);
            Stopwatch elapsed = Stopwatch.StartNew();
            for (long waits = 0; ; waits++)
            {
                clipboard.ClearIfOwned();
                if (!clipboard.OwnsClipboardContent) return true;
                if (waits >= maxWaits || elapsed.Elapsed >= budget) return false;
                pumpingWait(interval);
            }
        }
    }
}
