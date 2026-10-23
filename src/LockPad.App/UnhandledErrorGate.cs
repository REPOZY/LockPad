using System;
using System.Collections.Generic;

namespace LockPad.App
{
    /// <summary>
    /// Decides whether an unhandled-exception dialog may be shown. A modal MessageBox keeps pumping
    /// dispatcher work, so a repeating fault would otherwise nest dialogs without limit. Blocks
    /// re-entrant dialogs, and after more than 5 errors within 10 seconds blocks all dialogs until
    /// 10 seconds pass with no error. UI thread only.
    /// </summary>
    internal sealed class UnhandledErrorGate
    {
        private const int MaxErrors = 5;
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

        private readonly Queue<DateTime> _times = new Queue<DateTime>();
        private DateTime? _last;
        private bool _suppressed;
        private bool _showing;

        /// <summary>Registers an error at <paramref name="utcNow"/> and returns whether to show a dialog.</summary>
        public bool ShouldShow(DateTime utcNow)
        {
            if (_last.HasValue && utcNow - _last.Value >= Window)
            {
                _suppressed = false;
                _times.Clear();
            }
            _last = utcNow;
            if (_suppressed) return false;

            while (_times.Count > 0 && utcNow - _times.Peek() >= Window) _times.Dequeue();
            _times.Enqueue(utcNow);
            if (_times.Count > MaxErrors)
            {
                _suppressed = true;
                return false;
            }
            return !_showing;
        }

        public void Enter() { _showing = true; }

        public void Exit() { _showing = false; }
    }
}
