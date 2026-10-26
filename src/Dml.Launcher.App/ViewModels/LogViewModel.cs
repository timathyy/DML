using Dml.Launcher.Infrastructure.Logging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dml.Launcher.App.ViewModels;

public sealed partial class LogViewModel(InMemoryLogStore logStore) : ObservableObject
{
    [ObservableProperty] private bool isAutoWrapEnabled;

    public System.Collections.ObjectModel.ObservableCollection<string> Entries => logStore.Entries;
}
