using Avalonia.Controls;
using Lantern.Desktop.ViewModels;

namespace Lantern.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.BeginInitialLoad();
            }
        };
    }
}
