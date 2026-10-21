using System.Windows;
using Dml.Launcher.App.ViewModels;

namespace Dml.Launcher.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
