using System;
using System.Threading.Tasks;
using LockPad.Core;

namespace LockPad.App.ViewModels
{
    /// <summary>
    /// The wrong-current-password back-off of File > Change master password (fix round 1, F1). The check there compares
    /// raw keys (no key derivation), so a guess costs microseconds; as on the held-notebook unlock, an escalating wait
    /// restores a work factor: the same schedule (<see cref="UnlockViewModel.HeldBackOff"/>: 1 s, 2 s, 4 s, then 8 s for
    /// every further consecutive wrong attempt). One instance per editor, i.e. per notebook session: closing and
    /// reopening the dialog keeps the wait and the count; a successful change resets it, and a lock discards the editor
    /// and with it this state. Measured on a monotonic clock, so changing the Windows clock does not shorten it.
    /// </summary>
    internal sealed class PasswordBackOff
    {
        private readonly IClock _clock;
        private DateTime _blockedUntil = DateTime.MinValue;

        public PasswordBackOff(IClock clock)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            _clock = clock;
        }

        /// <summary>Consecutive wrong attempts since the last reset.</summary>
        public int Failures { get; private set; }

        /// <summary>Time left before the next attempt may be checked; zero when not waiting.</summary>
        public TimeSpan Remaining
        {
            get
            {
                TimeSpan left = _blockedUntil - _clock.UtcNow;
                return left > TimeSpan.Zero ? left : TimeSpan.Zero;
            }
        }

        /// <summary>Waits for the given time; the dialog re-enables its button afterwards. Test seam.</summary>
        internal Func<TimeSpan, Task> DelayAsync { get; set; } = Task.Delay;

        /// <summary>Counts a wrong attempt and starts its wait; returns the wait.</summary>
        public TimeSpan RecordFailure()
        {
            Failures++;
            TimeSpan wait = UnlockViewModel.HeldBackOff(Failures);
            _blockedUntil = _clock.UtcNow + wait;
            return wait;
        }

        public void Reset()
        {
            Failures = 0;
            _blockedUntil = DateTime.MinValue;
        }
    }
}
