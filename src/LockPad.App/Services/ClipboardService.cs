using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using LockPad.Core;

namespace LockPad.App.Services
{
    /// <summary>
    /// Thin seam over the OLE clipboard so failure paths can be tested. Each WPF call behind it
    /// (OleSetClipboard + OleFlushClipboard, OleGetClipboard, Clear) already retries internally,
    /// sleeping on the calling thread, so callers must not add an outer retry.
    /// </summary>
    internal interface IClipboardBackend
    {
        void SetDataObject(DataObject data);
        IDataObject GetDataObject();
        void Clear();
    }

    internal sealed class WpfClipboardBackend : IClipboardBackend
    {
        public void SetDataObject(DataObject data) { Clipboard.SetDataObject(data, true); }
        public IDataObject GetDataObject() { return Clipboard.GetDataObject(); }
        public void Clear() { Clipboard.Clear(); }
    }

    /// <summary>
    /// Copies secrets to the clipboard with the Windows history/cloud/monitor exclusion formats
    /// and clears them after a delay. Only a keyed fingerprint (HMAC-SHA256 under a per-instance
    /// random key) of the copied text is kept, never the text, so a memory dump does not allow
    /// brute-forcing short secrets such as PINs.
    /// <para>
    /// Must be created and used on a single STA UI thread: it owns a <see cref="DispatcherTimer"/>
    /// bound to that thread's dispatcher.
    /// </para>
    /// <para>
    /// The copy uses <c>Clipboard.SetDataObject(obj, true)</c>, so the text stays on the clipboard
    /// after this process exits. Callers MUST call <see cref="ClearIfOwned"/> on lock and on
    /// application exit.
    /// </para>
    /// </summary>
    public sealed class ClipboardService : IClipboardService
    {
        // WPF retries OleSetClipboard/OleFlushClipboard/OleGetClipboard/Clear itself (about
        // 10 x 100 ms each, on the calling thread), so those are called exactly once here.
        // Only reading data from an already-obtained IDataObject is retried by this class.
        private const int ReadAttempts = 3;
        private const int ReadRetryDelayMs = 50;
        private const int MinClearSeconds = 5;
        private const int MaxClearSeconds = 120;
        private static readonly int[] RescheduleBackoffSeconds = { 1, 2, 4, 8, 16 };

        private readonly Func<int> _clearSecondsProvider;
        private readonly IClipboardBackend _backend;
        private readonly byte[] _key = new byte[32];
        private readonly DispatcherTimer _timer;
        private byte[] _fingerprint;
        // Set only after a failed copy whose readback was inconclusive: the oldest fingerprint that
        // may still be on the clipboard (normally the last one known to have landed). Later
        // inconclusive failures replace the primary, never this slot. Never more than these two.
        private byte[] _secondary;
        private int _reschedules;

        public ClipboardService(Func<int> clearSecondsProvider)
            : this(clearSecondsProvider, new WpfClipboardBackend())
        {
        }

        internal ClipboardService(Func<int> clearSecondsProvider, IClipboardBackend backend)
        {
            if (clearSecondsProvider == null) throw new ArgumentNullException(nameof(clearSecondsProvider));
            if (backend == null) throw new ArgumentNullException(nameof(backend));
            _clearSecondsProvider = clearSecondsProvider;
            _backend = backend;
            using (var rng = new RNGCryptoServiceProvider()) rng.GetBytes(_key);
            _timer = new DispatcherTimer(DispatcherPriority.Normal);
            _timer.Tick += (sender, e) => OnClearTimerElapsed();
        }

        /// <inheritdoc />
        public void CopyProtected(string text)
        {
            // Windows reads clipboard text only up to the first U+0000 (possible in a note edited by another KeePass
            // client), so text containing one would never match its fingerprint and never be cleared (full-check red
            // team 2026-10-22, L2). The NULs are left out of the copy.
            if (text != null) text = text.Replace("\0", "");
            if (string.IsNullOrEmpty(text)) return;

            int seconds = Math.Min(MaxClearSeconds, Math.Max(MinClearSeconds, _clearSecondsProvider()));
            byte[] fingerprint = Fingerprint(text);
            bool adopted = false;
            try
            {
                DataObject obj = BuildDataObject(text);
                try
                {
                    _backend.SetDataObject(obj);
                }
                catch (Exception)
                {
                    // The call can fail after the data reached the clipboard (e.g. the flush step).
                    // Track the new text unless a readback proves it is not there: clearing is
                    // gated by the fingerprint check, so a wrong guess costs only a wasted check.
                    // If the readback itself fails, the old secret may also still be there, so the
                    // previous fingerprint is kept as the secondary one.
                    bool? landed;
                    try { landed = ClipboardMatches(fingerprint, null); }
                    catch (Exception) { landed = null; }
                    if (landed != false)
                    {
                        Adopt(fingerprint, seconds, keepPrevious: landed == null);
                        adopted = true;
                    }
                    throw;
                }
                Adopt(fingerprint, seconds, keepPrevious: false);
                adopted = true;
            }
            finally
            {
                if (!adopted) Array.Clear(fingerprint, 0, fingerprint.Length);
            }
        }

        /// <summary>
        /// Clears the clipboard only if it still holds text LockPad placed on the clipboard (the last
        /// copy, or the previous one after an inconclusive failed copy). Never throws. Callers MUST call this on lock and on application exit, because
        /// <c>SetDataObject(obj, true)</c> keeps the data on the clipboard after the process ends.
        /// If the clipboard cannot be read or cleared, the fingerprint and any pending timer are kept.
        /// </summary>
        public void ClearIfOwned()
        {
            try { TryClearIfOwned(); }
            catch (Exception) { }
        }

        /// <inheritdoc />
        public bool OwnsClipboardContent
        {
            get { return _fingerprint != null || _secondary != null; }
        }

        internal void OnClearTimerElapsed()
        {
            _timer.Stop();
            bool done;
            try { done = TryClearIfOwned(); }
            catch (Exception) { done = false; }
            if (done) return;

            // Give up silently after the backoff schedule; the fingerprint stays so lock/exit can
            // still clear.
            if (_reschedules >= RescheduleBackoffSeconds.Length) return;
            _timer.Interval = TimeSpan.FromSeconds(RescheduleBackoffSeconds[_reschedules]);
            _reschedules++;
            _timer.Start();
        }

        internal byte[] FingerprintForTest
        {
            get { return _fingerprint == null ? null : (byte[])_fingerprint.Clone(); }
        }

        internal byte[] SecondaryFingerprintForTest
        {
            get { return _secondary == null ? null : (byte[])_secondary.Clone(); }
        }

        internal bool IsClearPendingForTest
        {
            get { return _timer.IsEnabled; }
        }

        internal TimeSpan ClearIntervalForTest
        {
            get { return _timer.Interval; }
        }

        /// <summary>
        /// Starts tracking <paramref name="fingerprint"/> and restarts the clear timer.
        /// <paramref name="keepPrevious"/> is true after an inconclusive failed copy: then the
        /// secondary slot holds the oldest still-relevant fingerprint (under a sustained clipboard
        /// lock, failed copies never land, so the clipboard keeps the oldest landed text), and
        /// intermediate failed attempts are the ones dropped.
        /// </summary>
        private void Adopt(byte[] fingerprint, int seconds, bool keepPrevious)
        {
            if (!keepPrevious)
            {
                Forget();
                _fingerprint = fingerprint;
            }
            else
            {
                bool matchesPrimary = FixedTimeEquals(fingerprint, _fingerprint);
                bool matchesSecondary = FixedTimeEquals(fingerprint, _secondary);
                if (matchesPrimary | matchesSecondary)
                {
                    // Already tracked (e.g. the user retried the same copy): never fill both
                    // slots with one value.
                    Array.Clear(fingerprint, 0, fingerprint.Length);
                }
                else
                {
                    if (_secondary == null) _secondary = _fingerprint; // old primary moves down
                    else Zero(ref _fingerprint);                       // drop the intermediate attempt
                    _fingerprint = fingerprint;
                }
            }
            _reschedules = 0;
            _timer.Stop();
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            _timer.Start();
        }

        private void Forget()
        {
            Zero(ref _fingerprint);
            Zero(ref _secondary);
        }

        private static void Zero(ref byte[] fingerprint)
        {
            if (fingerprint != null) Array.Clear(fingerprint, 0, fingerprint.Length);
            fingerprint = null;
        }

        /// <summary>
        /// True when the work is finished: the clipboard was cleared, held foreign content, or
        /// nothing was owned. False when the clipboard could not be read or cleared.
        /// </summary>
        private bool TryClearIfOwned()
        {
            if (_fingerprint == null && _secondary == null)
            {
                _timer.Stop();
                return true;
            }

            bool owned;
            try { owned = ClipboardMatches(_fingerprint, _secondary); }
            catch (ExternalException) { return false; }

            if (owned)
            {
                try { _backend.Clear(); }
                catch (ExternalException) { return false; }
            }

            _timer.Stop();
            Forget();
            return true;
        }

        /// <summary>True if the clipboard text matches <paramref name="a"/> or <paramref name="b"/> (either may be null).</summary>
        private bool ClipboardMatches(byte[] a, byte[] b)
        {
            IDataObject data = _backend.GetDataObject();
            if (data == null) return false;
            string current = ReadUnicodeText(data);
            if (string.IsNullOrEmpty(current)) return false;
            byte[] candidate = Fingerprint(current);
            try
            {
                bool matchA = FixedTimeEquals(candidate, a);
                bool matchB = FixedTimeEquals(candidate, b);
                return matchA | matchB;
            }
            finally { Array.Clear(candidate, 0, candidate.Length); }
        }

        // WPF does not retry reads from an obtained IDataObject: up to 3 attempts, 50 ms apart.
        private static string ReadUnicodeText(IDataObject data)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (!data.GetDataPresent(DataFormats.UnicodeText, false)) return null;
                    return data.GetData(DataFormats.UnicodeText, false) as string;
                }
                catch (ExternalException) when (attempt < ReadAttempts)
                {
                    Thread.Sleep(ReadRetryDelayMs);
                }
            }
        }

        private byte[] Fingerprint(string text)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(text); // UTF-16LE, no BOM
            try
            {
                using (var hmac = new HMACSHA256(_key)) return hmac.ComputeHash(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }

        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static DataObject BuildDataObject(string text)
        {
            var obj = new DataObject();
            // autoConvert: false keeps the secret in UnicodeText only (no extra String/Text copies);
            // Windows itself synthesizes CF_TEXT for legacy apps.
            obj.SetData(DataFormats.UnicodeText, text, false);
            obj.SetData(ClipboardFormats.ExcludeFromMonitor, new MemoryStream(ClipboardFormats.DwordZero()), false);
            obj.SetData(ClipboardFormats.CanIncludeInHistory, new MemoryStream(ClipboardFormats.DwordZero()), false);
            obj.SetData(ClipboardFormats.CanUploadToCloud, new MemoryStream(ClipboardFormats.DwordZero()), false);
            return obj;
        }
    }
}
