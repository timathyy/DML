using System.Windows;
using System.Windows.Input;

namespace Dml.Launcher.App;

public partial class InstanceNameDialog : Window
{
    public InstanceNameDialog(string title, string initialValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        NameBox.Text = initialValue;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public string Value => NameBox.Text;

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Value))
        {
            MessageBox.Show("请输入实例名称。", "DML", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void TitleText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
