using System.Windows;
using System.Windows.Input;
using Dml.Launcher.App.ViewModels;

namespace Dml.Launcher.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        bool maximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = maximized ? "\uE923" : "\uE922";
        TitleBarRow.Height = new GridLength(maximized ? 44 : 38);
        TitleBar.Padding = maximized ? new Thickness(0, 6, 6, 0) : new Thickness(0);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
