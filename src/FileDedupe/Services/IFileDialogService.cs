namespace FileDedupe.Services;

/// <summary>
/// Abstracts the Windows file dialogs so the view model can ask for a folder or a save
/// location without referencing any WinUI type. That keeps the view model testable and
/// is the main seam between UI and logic.
/// </summary>
public interface IFileDialogService
{
    /// <summary>Returns the chosen folder, or null if the user cancelled.</summary>
    Task<string?> PickFolderAsync();

    /// <summary>Returns the chosen save path, or null if the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string suggestedFileName);

    /// <summary>Shows a modal message.</summary>
    Task ShowMessageAsync(string title, string message);
}
