using LockPad.App.Infrastructure;

namespace LockPad.App.ViewModels
{
    /// <summary>One row of the notes list. Holds only the id and title, never the note text.</summary>
    public sealed class NoteItemViewModel : ObservableObject
    {
        internal const string UntitledText = "(untitled)";

        private string _displayTitle;

        internal NoteItemViewModel(string id, string title)
        {
            Id = id;
            _displayTitle = Display(title);
        }

        public string Id { get; }

        /// <summary>The note title, or "(untitled)" when it is empty or whitespace.</summary>
        public string DisplayTitle
        {
            get { return _displayTitle; }
            private set { SetProperty(ref _displayTitle, value); }
        }

        internal void SetTitle(string title)
        {
            DisplayTitle = Display(title);
        }

        private static string Display(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? UntitledText : title;
        }
    }
}
