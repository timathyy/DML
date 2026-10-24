using System.Windows;

namespace Dml.Launcher.App;

public partial class InstanceNameDialog : Window
{
    public InstanceNameDialog(string title, string label, string initialValue)
    {
        InitializeComponent();
        TitleText.Text = title;
        LabelText.Text = label;
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
}
