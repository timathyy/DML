using Dml.Launcher.Infrastructure.Logging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Dml.Launcher.App.ViewModels;

public sealed class LogViewModel(InMemoryLogStore logStore) : ObservableObject
{
    public System.Collections.ObjectModel.ObservableCollection<string> Entries => logStore.Entries;
}
