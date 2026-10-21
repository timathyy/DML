using System.Collections.ObjectModel;

namespace Dml.Launcher.Infrastructure.Logging;

public sealed class InMemoryLogStore
{
    private const int MaximumEntries = 500;

    public ObservableCollection<string> Entries { get; } = [];

    public void Add(string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        if (Entries.Count == MaximumEntries)
        {
            Entries.RemoveAt(0);
        }

        Entries.Add(entry);
    }
}
