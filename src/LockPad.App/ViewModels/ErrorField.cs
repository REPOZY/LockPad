namespace LockPad.App.ViewModels
{
    /// <summary>The input box an unlock or create page error belongs to: the view focuses it (and outlines a path box).</summary>
    public enum ErrorField
    {
        None,
        NotebookPath,
        KeyFilePath,
        Password
    }
}
