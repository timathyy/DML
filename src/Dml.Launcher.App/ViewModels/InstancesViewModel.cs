using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dml.Launcher.Application.Instances;
using Dml.Launcher.Application.Settings;
using Dml.Launcher.Domain.Instances;
using Microsoft.Win32;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class InstancesViewModel : ObservableObject
{
    private readonly IInstanceService instances;
    private readonly ISettingsService settings;

    [ObservableProperty] private GameInstance? selectedInstance;
    [ObservableProperty] private string statusMessage = "正在加载实例…";
    public ObservableCollection<GameInstance> Items { get; } = [];

    public InstancesViewModel(IInstanceService instances, ISettingsService settings)
    {
        this.instances = instances;
        this.settings = settings;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        try
        {
            IReadOnlyList<GameInstance> loaded = await instances.GetAllAsync();
            Items.Clear();
            foreach (GameInstance instance in loaded.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)) Items.Add(instance);
            SelectedInstance = Items.FirstOrDefault();
            string countMessage = Items.Count == 0 ? "还没有实例。创建一个独立实例，或导入已有的 .minecraft 目录。" : $"共 {Items.Count} 个实例";
            StatusMessage = $"{countMessage} {GetDiskSpaceMessage()}";
        }
        catch (Exception exception) { ShowError(exception); }
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!TryPrompt("创建实例", "实例名称", "新建实例", out string name)) return;
        try
        {
            GameInstance created = await instances.CreateAsync(name, "未安装");
            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == created.Id);
            StatusMessage = "实例已创建。可在后续版本安装游戏版本。";
        }
        catch (Exception exception) { ShowError(exception); }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task CopyAsync()
    {
        GameInstance source = SelectedInstance!;
        if (!TryPrompt("复制实例", "新实例名称", source.Name + " - 副本", out string name)) return;
        try { GameInstance copy = await instances.CopyAsync(source.Id, name); await RefreshAsync(); SelectedInstance = Items.FirstOrDefault(item => item.Id == copy.Id); }
        catch (Exception exception) { ShowError(exception); }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        GameInstance source = SelectedInstance!;
        if (!TryPrompt("重命名实例", "实例名称", source.Name, out string name)) return;
        try { await instances.RenameAsync(source.Id, name); await RefreshAsync(); }
        catch (Exception exception) { ShowError(exception); }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        GameInstance source = SelectedInstance!;
        MessageBoxResult confirmation = MessageBox.Show($"要将“{source.Name}”及其游戏文件移入 Windows 回收站吗？\n\n此操作不会影响官方启动器或其他 DML 实例。", "删除实例", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes) return;
        try { await instances.DeleteAsync(source.Id); await RefreshAsync(); StatusMessage = "实例已移入回收站。"; }
        catch (Exception exception) { ShowError(exception); }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        OpenFolderDialog dialog = new() { Title = "选择要导入的 .minecraft 目录" };
        if (dialog.ShowDialog() != true) return;
        string suggestedName = Path.GetFileName(dialog.FolderName.TrimEnd(Path.DirectorySeparatorChar));
        if (!TryPrompt("导入实例", "实例名称", string.IsNullOrWhiteSpace(suggestedName) ? "已导入实例" : suggestedName, out string name)) return;
        try
        {
            GameInstance imported = await instances.ImportAsync(dialog.FolderName, name);
            await RefreshAsync();
            SelectedInstance = Items.FirstOrDefault(item => item.Id == imported.Id);
            StatusMessage = "目录已复制为独立实例；原目录未被修改。";
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private bool HasSelection() => SelectedInstance is not null;
    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        CopyCommand.NotifyCanExecuteChanged(); RenameCommand.NotifyCanExecuteChanged(); DeleteCommand.NotifyCanExecuteChanged();
    }

    private static bool TryPrompt(string title, string label, string initialValue, out string value)
    {
        InstanceNameDialog dialog = new(title, label, initialValue);
        bool accepted = dialog.ShowDialog() == true;
        value = dialog.Value;
        return accepted;
    }

    private void ShowError(Exception exception)
    {
        StatusMessage = "操作未完成。";
        MessageBox.Show(exception.Message, "DML", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private string GetDiskSpaceMessage()
    {
        try
        {
            string gameRoot = Path.GetFullPath(settings.Current.GameRootDirectory);
            DriveInfo drive = new(Path.GetPathRoot(gameRoot)!);
            long availableGigabytes = drive.AvailableFreeSpace / 1024 / 1024 / 1024;
            return availableGigabytes < 5 ? $"警告：游戏磁盘仅剩 {availableGigabytes} GB 可用空间。" : $"游戏磁盘可用空间：{availableGigabytes} GB。";
        }
        catch (Exception)
        {
            return "无法读取游戏目录所在磁盘的可用空间。";
        }
    }
}
