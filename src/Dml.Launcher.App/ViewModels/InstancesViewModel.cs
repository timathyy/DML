using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dml.Launcher.App.Services;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Launching;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Application.Versions;
using Dml.Launcher.Domain.Instances;
using Dml.Launcher.Domain.Versions;
using Microsoft.Win32;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class InstancesViewModel : ObservableObject
{
    private readonly IInstanceService instances;
    private readonly ISettingsService settings;
    private readonly IMinecraftVersionService versionService;
    private readonly DownloadManager downloadManager;
    private readonly IGameLauncher gameLauncher;
    private readonly IJavaRuntimeService javaRuntimeService;
    private CancellationTokenSource? activeLaunchCancellation;
    private Guid? activeInstanceId;
    private bool isRefreshing;

    [ObservableProperty] private InstanceListItemViewModel? selectedInstance;
    [ObservableProperty] private string statusMessage = "正在加载实例…";

    public InstancesViewModel(
        IInstanceService instances,
        ISettingsService settings,
        IMinecraftVersionService versionService,
        DownloadManager downloadManager,
        IGameLauncher gameLauncher,
        IJavaRuntimeService javaRuntimeService)
    {
        this.instances = instances;
        this.settings = settings;
        this.versionService = versionService;
        this.downloadManager = downloadManager;
        this.gameLauncher = gameLauncher;
        this.javaRuntimeService = javaRuntimeService;
        downloadManager.TasksChanged += DownloadManager_TasksChanged;
    }

    public ObservableCollection<InstanceListItemViewModel> Items { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (isRefreshing)
        {
            return;
        }

        isRefreshing = true;
        try
        {
            Guid? selectedId = SelectedInstance?.Id;
            IReadOnlyList<GameInstance> loaded = await instances.GetAllAsync();
            Items.Clear();
            foreach (GameInstance instance in loaded.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                Items.Add(new InstanceListItemViewModel(instance, activeInstanceId == instance.Id));
            }

            SelectedInstance = Items.FirstOrDefault(item => item.Id == selectedId) ?? Items.FirstOrDefault();
            UpdateSummaryMessage();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
        finally
        {
            isRefreshing = false;
        }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!TryPrompt("新建实例", "新建实例", out string name))
        {
            return;
        }

        VersionSelectionDialog versionDialog = new(versionService)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (versionDialog.ShowDialog() != true || versionDialog.SelectedVersion is not MinecraftVersionInfo version)
        {
            return;
        }

        try
        {
            GameInstance created = await instances.CreateAsync(name, version.Id);
            downloadManager.Enqueue(created, version);
            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = $"实例已创建，正在下载 Minecraft {version.Id}。";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenFolder()
    {
        try
        {
            string directory = SelectedInstance!.GameDirectory;
            if (!Directory.Exists(directory))
            {
                throw new DirectoryNotFoundException("实例文件夹不存在，可能已被移动或删除。");
            }

            Process.Start(new ProcessStartInfo { FileName = directory, UseShellExecute = true });
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task CopyAsync()
    {
        GameInstance source = SelectedInstance!.Instance;
        if (!TryPrompt("复制实例", source.Name + " - 副本", out string name))
        {
            return;
        }

        try
        {
            GameInstance copy = await instances.CopyAsync(source.Id, name);
            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == copy.Id);
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task RenameAsync()
    {
        GameInstance source = SelectedInstance!.Instance;
        if (!TryPrompt("重命名实例", source.Name, out string name))
        {
            return;
        }

        try
        {
            await instances.RenameAsync(source.Id, name);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task DeleteAsync()
    {
        GameInstance source = SelectedInstance!.Instance;
        ConfirmationDialog dialog = new(
            "删除实例",
            $"确定要删除“{source.Name}”吗？\n\n该实例及其游戏文件将移入 Windows 回收站，不会影响官方启动器或其他 DML 实例。")
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await instances.DeleteAsync(source.Id);
            await RefreshAsync();
            StatusMessage = "实例已移入回收站。";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanModifySelection))]
    private async Task ConfigureLaunchAsync()
    {
        GameInstance source = SelectedInstance!.Instance;
        InstanceLaunchSettingsDialog dialog = new(source, javaRuntimeService)
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await instances.UpdateLaunchSettingsAsync(
                source.Id,
                dialog.JavaPath,
                dialog.MemoryMegabytes,
                dialog.WindowWidth,
                dialog.WindowHeight,
                dialog.Fullscreen,
                dialog.JvmArguments);
            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == source.Id);
            StatusMessage = "启动设置已保存。";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    [RelayCommand(CanExecute = nameof(CanLaunchSelection))]
    private void Launch()
    {
        GameInstance source = SelectedInstance!.Instance;
        UpdateSummaryMessage();
        if (activeInstanceId == source.Id && activeLaunchCancellation is not null)
        {
            ConfirmationDialog dialog = new(
                "强制结束实例",
                $"确定要强制结束“{source.Name}”吗？\n\n未保存的游戏进度可能丢失，请优先在游戏内正常退出。")
            {
                Owner = System.Windows.Application.Current.MainWindow,
            };
            if (dialog.ShowDialog() == true)
            {
                activeLaunchCancellation.Cancel();
            }

            return;
        }

        CancellationTokenSource cancellation = new();
        activeInstanceId = source.Id;
        activeLaunchCancellation = cancellation;
        SelectedInstance.IsRunning = true;
        NotifySelectionCommands();
        _ = RunInstanceAsync(source, cancellation);
    }

    private async Task RunInstanceAsync(GameInstance source, CancellationTokenSource cancellation)
    {
        try
        {
            await gameLauncher.LaunchAsync(source, cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "DML", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            activeInstanceId = null;
            activeLaunchCancellation = null;
            cancellation.Dispose();

            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == source.Id);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        OpenFolderDialog dialog = new() { Title = "选择要导入的 .minecraft 目录" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        string suggestedName = Path.GetFileName(dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar));
        if (!TryPrompt("导入实例", string.IsNullOrWhiteSpace(suggestedName) ? "已导入实例" : suggestedName, out string name))
        {
            return;
        }

        try
        {
            GameInstance imported = await instances.ImportAsync(dialog.FolderName, name);
            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == imported.Id);
            StatusMessage = "目录已复制为独立实例；原目录未被修改。";
        }
        catch (Exception exception)
        {
            ShowError(exception);
        }
    }

    private bool HasSelection() => SelectedInstance is not null;

    private bool CanModifySelection() => SelectedInstance?.IsReady == true && SelectedInstance.IsRunning == false;

    private bool CanLaunchSelection() =>
        SelectedInstance?.IsReady == true && (activeInstanceId is null || activeInstanceId == SelectedInstance.Id);

    partial void OnSelectedInstanceChanged(InstanceListItemViewModel? value)
    {
        NotifySelectionCommands();
    }

    private void NotifySelectionCommands()
    {
        OpenFolderCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        ConfigureLaunchCommand.NotifyCanExecuteChanged();
        LaunchCommand.NotifyCanExecuteChanged();
    }

    private async void DownloadManager_TasksChanged(object? sender, EventArgs e) => await RefreshAsync();

    private static bool TryPrompt(string title, string initialValue, out string value)
    {
        InstanceNameDialog dialog = new(title, initialValue) { Owner = System.Windows.Application.Current.MainWindow };
        bool accepted = dialog.ShowDialog() == true;
        value = dialog.Value;
        return accepted;
    }

    private void ShowError(Exception exception)
    {
        StatusMessage = "操作未完成。";
        MessageBox.Show(exception.Message, "DML", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void UpdateSummaryMessage()
    {
        string countMessage = Items.Count == 0
            ? "还没有实例。创建实例时选择一个 Minecraft 版本即可开始下载。"
            : $"共 {Items.Count} 个实例";
        StatusMessage = $"{countMessage} {GetDiskSpaceMessage()}";
    }

    private string GetDiskSpaceMessage()
    {
        try
        {
            string gameRoot = Path.GetFullPath(settings.Current.GameRootDirectory);
            DriveInfo drive = new(Path.GetPathRoot(gameRoot)!);
            long availableGigabytes = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
            return availableGigabytes < 5
                ? $"警告：游戏磁盘仅剩 {availableGigabytes} GB 可用空间。"
                : $"游戏磁盘可用空间：{availableGigabytes} GB。";
        }
        catch (Exception)
        {
            return "无法读取游戏目录所在磁盘的可用空间。";
        }
    }
}
