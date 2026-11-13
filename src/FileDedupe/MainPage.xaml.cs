using FileDedupe.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FileDedupe;

public sealed partial class MainPage : Page
{
    public MainPage(MainViewModel viewModel)
    {
        // Assigned before InitializeComponent so compiled bindings resolve on first pass.
        ViewModel = viewModel;
        InitializeComponent();
        RootGrid.DataContext = viewModel;
    }

    public MainViewModel ViewModel { get; }

    /// <summary>
    /// A DataTemplate has its own namescope, so an ElementName binding back to the page
    /// cannot resolve. Routing the click through code-behind keeps the removal logic
    /// itself in the view model.
    /// </summary>
    private void OnRemoveDirectoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ScanDirectory directory })
        {
            ViewModel.RemoveDirectoryCommand.Execute(directory);
        }
    }
}
