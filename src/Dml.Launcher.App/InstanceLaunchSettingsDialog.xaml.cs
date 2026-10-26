using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Dml.Launcher.Application.Launching;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Launching;
using Microsoft.Win32;

namespace Dml.Launcher.App;

public partial class InstanceLaunchSettingsDialog : Window
{
    private readonly IJavaRuntimeService javaRuntimeService;
    private readonly ObservableCollection<JavaInstallation> detectedJava = [];

    public InstanceLaunchSettingsDialog(GameInstance instance, IJavaRuntimeService javaRuntimeService)
    {
        this.javaRuntimeService = javaRuntimeService;
        InitializeComponent();
        DetectedJavaBox.ItemsSource = detectedJava;
        JavaPathBox.Text = instance.JavaPath ?? string.Empty;
        MemoryBox.Text = instance.MemoryMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
        WidthBox.Text = instance.Width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        HeightBox.Text = instance.Height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        FullscreenBox.IsChecked = instance.Fullscreen;
        JvmArgumentsBox.Text = instance.JvmArguments ?? string.Empty;
    }

    public string? JavaPath { get; private set; }

    public int MemoryMegabytes { get; private set; }

    public int WindowWidth { get; private set; }

    public int WindowHeight { get; private set; }

    public bool Fullscreen { get; private set; }

    public string? JvmArguments { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        JavaStatusText.Text = "正在检测系统 Java…";
        try
        {
            IReadOnlyList<JavaInstallation> installations = await javaRuntimeService.DiscoverAsync();
            foreach (JavaInstallation installation in installations)
            {
                detectedJava.Add(installation);
            }

            JavaStatusText.Text = detectedJava.Count == 0
                ? "未自动检测到 Java，请点击“浏览”选择 java.exe。"
                : $"检测到 {detectedJava.Count} 个 Java；可从列表选择，留空则启动时自动匹配版本。";
            DetectedJavaBox.SelectedItem = detectedJava.FirstOrDefault(item =>
                string.Equals(item.ExecutablePath, JavaPathBox.Text, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            JavaStatusText.Text = $"Java 检测未完成：{exception.Message}";
        }
    }

    private void DetectedJavaBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetectedJavaBox.SelectedItem is JavaInstallation installation)
        {
            JavaPathBox.Text = installation.ExecutablePath;
        }
    }

    private async void BrowseJava_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "选择 Java 可执行文件",
            Filter = "Java 可执行文件 (java.exe;javaw.exe)|java.exe;javaw.exe|可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            JavaInstallation installation = await javaRuntimeService.InspectAsync(dialog.FileName);
            JavaPathBox.Text = installation.ExecutablePath;
            JavaStatusText.Text = $"已验证：{installation.DisplayName}";
            if (!detectedJava.Any(item => string.Equals(item.ExecutablePath, installation.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                detectedJava.Add(installation);
            }

            DetectedJavaBox.SelectedItem = detectedJava.First(item =>
                string.Equals(item.ExecutablePath, installation.ExecutablePath, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "DML", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(MemoryBox.Text, out int memory) || memory is < 512 or > 131072 ||
            !int.TryParse(WidthBox.Text, out int width) || width is < 320 or > 16384 ||
            !int.TryParse(HeightBox.Text, out int height) || height is < 240 or > 16384)
        {
            MessageBox.Show("请输入有效设置：内存 512–131072 MB，宽度 320–16384，高度 240–16384。", "DML", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        JavaPath = string.IsNullOrWhiteSpace(JavaPathBox.Text) ? null : JavaPathBox.Text.Trim();
        MemoryMegabytes = memory;
        WindowWidth = width;
        WindowHeight = height;
        Fullscreen = FullscreenBox.IsChecked == true;
        JvmArguments = string.IsNullOrWhiteSpace(JvmArgumentsBox.Text) ? null : JvmArgumentsBox.Text.Trim();
        DialogResult = true;
    }

    private void TitleArea_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
