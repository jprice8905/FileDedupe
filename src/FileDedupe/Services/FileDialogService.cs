using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace FileDedupe.Services;

/// <summary>
/// WinUI implementation of <see cref="IFileDialogService"/>.
///
/// In an unpackaged app the pickers have no window identity of their own, so each one
/// must be associated with the parent HWND via InitializeWithWindow. Without that the
/// picker throws at display time rather than silently misbehaving.
/// </summary>
public sealed class FileDialogService : IFileDialogService
{
    private readonly Window _window;

    public FileDialogService(Window window) => _window = window;

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));

        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedFileName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName)
        };
        picker.FileTypeChoices.Add("Text file", [".txt"]);

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(_window));

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new ScrollViewer
            {
                Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
                MaxHeight = 400
            },
            CloseButtonText = "OK",
            XamlRoot = _window.Content.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
