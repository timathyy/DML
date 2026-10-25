using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dml.Launcher.App.Services;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly DownloadManager downloadManager;

    public DownloadsViewModel(DownloadManager downloadManager) => this.downloadManager = downloadManager;

    public ObservableCollection<DownloadTaskViewModel> Tasks => downloadManager.Tasks;

    [RelayCommand]
    private Task TogglePauseAsync(DownloadTaskViewModel? task) =>
        task is null ? Task.CompletedTask : downloadManager.TogglePauseAsync(task);

    [RelayCommand]
    private async Task CancelAsync(DownloadTaskViewModel? task)
    {
        if (task is null)
        {
            return;
        }

        ConfirmationDialog dialog = new(
            "取消下载",
            $"确定要取消“{task.InstanceName}”的下载吗？\n\n该未完成实例及其文件将移入 Windows 回收站。")
        {
            Owner = System.Windows.Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() == true)
        {
            try
            {
                await downloadManager.CancelAndDeleteAsync(task);
            }
            catch (Exception exception)
            {
                MessageBox.Show(exception.Message, "DML", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
