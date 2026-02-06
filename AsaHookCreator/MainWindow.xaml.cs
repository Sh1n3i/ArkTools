using System.Windows;
using AsaHookCreator.ViewModels;
using Wpf.Ui.Controls;

namespace AsaHookCreator;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.LoadDefaultHeadersCommand.ExecuteAsync(null);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // Dispose the ViewModel to stop file watcher
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }
}