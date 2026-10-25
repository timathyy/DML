using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Versions;

namespace Dml.Launcher.App;

public partial class VersionSelectionDialog : Window, IDisposable
{
    private readonly IMinecraftVersionService versionService;
    private readonly CancellationTokenSource cancellation = new();
    private readonly List<MinecraftVersionInfo> allVersions = [];
    private readonly ObservableCollection<MinecraftVersionInfo> visibleVersions = [];
    private bool isLoading;
    private bool disposed;

    public VersionSelectionDialog(IMinecraftVersionService versionService)
    {
        this.versionService = versionService;
        InitializeComponent();
        VersionsList.ItemsSource = visibleVersions;
    }

    public MinecraftVersionInfo? SelectedVersion { get; private set; }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadVersionsAsync(forceRefresh: false);

    private void Window_Closed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        cancellation.Cancel();
        cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadVersionsAsync(forceRefresh: true);

    private async Task LoadVersionsAsync(bool forceRefresh)
    {
        if (isLoading)
        {
            return;
        }

        isLoading = true;
        StatusText.Text = forceRefresh ? "正在刷新 Mojang 官方版本清单…" : "正在加载 Mojang 官方版本清单…";
        try
        {
            MinecraftVersionManifest manifest = await versionService.GetManifestAsync(forceRefresh, cancellation.Token);
            allVersions.Clear();
            allVersions.AddRange(manifest.Versions.Where(version =>
                version.Type is MinecraftVersionType.Release or MinecraftVersionType.Snapshot));
            ApplyFilter();
            StatusText.Text = manifest.IsFromCache
                ? $"已从本地缓存加载 {visibleVersions.Count} 个版本。"
                : $"已加载 {visibleVersions.Count} 个版本。";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            StatusText.Text = $"无法加载版本清单：{exception.Message}";
        }
        finally
        {
            isLoading = false;
        }
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (VersionsList is null || ShowSnapshotsBox is null)
        {
            return;
        }

        string? selectedId = (VersionsList.SelectedItem as MinecraftVersionInfo)?.Id;
        visibleVersions.Clear();
        foreach (MinecraftVersionInfo version in allVersions.Where(version =>
                     version.Type == MinecraftVersionType.Release ||
                     (ShowSnapshotsBox.IsChecked == true && version.Type == MinecraftVersionType.Snapshot)))
        {
            visibleVersions.Add(version);
        }

        VersionsList.SelectedItem = visibleVersions.FirstOrDefault(version => version.Id == selectedId) ?? visibleVersions.FirstOrDefault();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (VersionsList.SelectedItem is not MinecraftVersionInfo version)
        {
            MessageBox.Show("请选择一个 Minecraft 版本。", "DML", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        SelectedVersion = version;
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
