using System;
using System.Text;

namespace LockPad.Core
{
    /// <summary>
    /// Normalizes note strings so that the value held in memory is exactly the value KeePassLib stores and reads
    /// back. Otherwise reopen-verification would fail on every save.
    /// </summary>
    internal static class XmlSafeText
    {
        /// <summary>
        /// Title rule (titles are single-line). Guarantee: for any input s, writing Sanitize(s) as an unprotected
        /// KDBX string and reading it back with KeePassLib yields Sanitize(s) exactly (ordinal), and
        /// Sanitize(Sanitize(s)) == Sanitize(s). Both are tested against real KeePassLib.
        /// Steps:
        /// 1. StripXmlInvalid (= StrUtil.SafeXmlString, applied by KdbxFile on write).
        /// 2. CRLF, CR, LF and TAB each become one space. The XML writer and reader normalize line ends
        ///    (CR and CRLF read back as LF).
        /// 3. A result made only of spaces becomes "". The reader uses IgnoreWhitespace, so whitespace-only
        ///    values read back empty. Leading and trailing spaces on other titles round-trip and are kept.
        /// Null becomes "".
        /// </summary>
        internal static string Sanitize(string text)
        {
            string s = StripXmlInvalid(text);
            if (s.Length == 0) return s;

            if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0 || s.IndexOf('\t') >= 0)
                s = s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

            for (int i = 0; i < s.Length; i++)
                if (s[i] != ' ') return s;
            return "";
        }

        /// <summary>
        /// Identical rule to KeePassLib.Utility.StrUtil.SafeXmlString: keeps U+0009, U+000A, U+000D,
        /// U+0020..U+D7FF, U+E000..U+FFFD and valid surrogate pairs, and drops everything else. Reimplemented
        /// because StrUtil.SafeXmlString Debug.Asserts on lone surrogates (KeePassLib is compiled with DEBUG in
        /// Debug builds). Parity is tested. Null becomes "".
        /// </summary>
        internal static string StripXmlInvalid(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (IsXmlSafe(text)) return text;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (IsXmlChar(ch))
                {
                    sb.Append(ch);
                }
                else if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(ch).Append(text[i + 1]);
                    i++;
                }
                // else: dropped (control char, U+FFFE/U+FFFF, lone surrogate)
            }
            return sb.ToString();
        }

        /// <summary>
        /// For protected strings (note text), which KeePassLib stores as UTF-8 (base64 in the file). Control
        /// characters and line ends survive unchanged, but each lone surrogate becomes U+FFFD, matching
        /// StrUtil.Utf8's replacement fallback. Returns the same instance when nothing changes, so no extra copy
        /// of the secret is made. Null becomes "".
        /// </summary>
        internal static string NormalizeText(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            if (!HasLoneSurrogate(text)) return text;

            var chars = text.ToCharArray();
            try
            {
                for (int i = 0; i < chars.Length; i++)
                {
                    if (char.IsHighSurrogate(chars[i]) && i + 1 < chars.Length && char.IsLowSurrogate(chars[i + 1])) { i++; continue; }
                    if (char.IsSurrogate(chars[i])) chars[i] = '\uFFFD';
                }
                return new string(chars);
            }
            finally
            {
                Array.Clear(chars, 0, chars.Length);
            }
        }

        private static bool IsXmlChar(char ch)
        {
            return (ch >= '\u0020' && ch <= '\uD7FF') || ch == '\u0009' || ch == '\u000A' || ch == '\u000D'
                || (ch >= '\uE000' && ch <= '\uFFFD');
        }

        private static bool IsXmlSafe(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (IsXmlChar(ch)) continue;
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                return false;
            }
            return true;
        }

        private static bool HasLoneSurrogate(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) { i++; continue; }
                if (char.IsSurrogate(ch)) return true;
            }
            return false;
        }
    }
}