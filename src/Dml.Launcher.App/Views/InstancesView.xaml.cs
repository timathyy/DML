using System.Windows;
using System.Windows.Controls;
using Dml.Launcher.App.ViewModels;

namespace Dml.Launcher.App.Views;

public partial class InstancesView : UserControl
{
    public InstancesView() => InitializeComponent();
    private void View_Loaded(object sender, RoutedEventArgs e) => ((InstancesViewModel)DataContext).RefreshCommand.Execute(null);
}
