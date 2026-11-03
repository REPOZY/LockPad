using System;
using KeePassLib.Security;

namespace LockPad.Core
{
    public sealed class Note
    {
        private readonly ProtectedString _text;

        public string Id { get; }
        public string Title { get; }
        public DateTime LastModifiedUtc { get; }

        public string Text
        {
            get { return _text.ReadString(); }
        }

        private Note(string id, string title, string text, DateTime lastModifiedUtc)
        {
            Id = id;
            Title = title ?? "";
            _text = new ProtectedString(true, text ?? "");
            LastModifiedUtc = lastModifiedUtc;
        }

        internal static Note Create(string id, string title, string text, DateTime lastModifiedUtc)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            return new Note(id, title, text, lastModifiedUtc);
        }
    }
}
