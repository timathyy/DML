using System.Collections.ObjectModel;

namespace Dml.Launcher.Infrastructure.Logging;

public sealed class InMemoryLogStore
{
    private const int MaximumEntries = 500;
    private readonly SynchronizationContext? synchronizationContext = SynchronizationContext.Current;

    public ObservableCollection<string> Entries { get; } = [];

    public void Add(string entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);

        if (synchronizationContext is not null && SynchronizationContext.Current != synchronizationContext)
        {
            synchronizationContext.Post(static state =>
            {
                (InMemoryLogStore store, string value) = ((InMemoryLogStore, string))state!;
                store.AddCore(value);
            }, (this, entry));
            return;
        }

        AddCore(entry);
    }

    private void AddCore(string entry)
    {
        if (Entries.Count == MaximumEntries)
        {
            Entries.RemoveAt(0);
        }

        Entries.Add(entry);
    }
}
