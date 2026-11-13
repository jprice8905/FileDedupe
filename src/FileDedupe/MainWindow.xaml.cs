using FileDedupe.Core;
using FileDedupe.Services;
using FileDedupe.ViewModels;
using Microsoft.UI.Xaml;

namespace FileDedupe;

/// <summary>
/// Thin shell. All UI lives in <see cref="MainPage"/> because a WinUI
/// <see cref="Window"/> is not a FrameworkElement, and compiled bindings inside a
/// DataTemplate need a FrameworkElement root to generate against.
/// </summary>
public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Composition root. The window supplies the HWND the file pickers need;
        // everything beyond this point depends only on interfaces.
        var dialogService = new FileDialogService(this);
        var viewModel = new MainViewModel(DedupeService.Create(), dialogService);

        Content = new MainPage(viewModel);
    }
}
