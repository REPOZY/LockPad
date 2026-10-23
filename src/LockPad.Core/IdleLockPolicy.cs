using System;

namespace LockPad.Core
{
    public sealed class IdleLockPolicy
    {
        private readonly IClock _clock;
        private TimeSpan _timeout;
        private DateTime _lastActivity;

        public IdleLockPolicy(IClock clock, TimeSpan timeout)
        {
            if (clock == null) throw new ArgumentNullException("clock");
            RequirePositive(timeout);
            _clock = clock;
            _timeout = timeout;
            _lastActivity = clock.UtcNow;
        }

        /// <summary>Changing the timeout keeps the last-activity time (no implicit reset).</summary>
        public TimeSpan Timeout
        {
            get { return _timeout; }
            set
            {
                RequirePositive(value);
                _timeout = value;
            }
        }

        public void RecordActivity()
        {
            _lastActivity = _clock.UtcNow;
        }

        public bool IsExpired
        {
            get { return Elapsed() >= _timeout; }
        }

        /// <summary>Time until expiry, in [0, Timeout].</summary>
        public TimeSpan Remaining
        {
            get
            {
                TimeSpan left = _timeout - Elapsed();
                return left < TimeSpan.Zero ? TimeSpan.Zero : left;
            }
        }

        // A clock reading earlier than the last activity counts as zero elapsed.
        private TimeSpan Elapsed()
        {
            TimeSpan e = _clock.UtcNow - _lastActivity;
            return e < TimeSpan.Zero ? TimeSpan.Zero : e;
        }

        private static void RequirePositive(TimeSpan t)
        {
            if (t <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("timeout", "Timeout must be positive.");
        }
    }
}
