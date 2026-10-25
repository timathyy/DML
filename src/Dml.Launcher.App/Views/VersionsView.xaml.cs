using System.Windows;
using System.Windows.Controls;
using Dml.Launcher.App.ViewModels;

namespace Dml.Launcher.App.Views;

public partial class VersionsView : UserControl
{
    public VersionsView() => InitializeComponent();

    private void View_Loaded(object sender, RoutedEventArgs e) =>
        ((VersionsViewModel)DataContext).LoadCommand.Execute(null);
}
