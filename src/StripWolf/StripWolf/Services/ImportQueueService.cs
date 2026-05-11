using System.Collections.ObjectModel;
using Avalonia.Threading;
using StripWolf.Models;

namespace StripWolf.Services;

public class ImportQueueService
{
    public ObservableCollection<PendingImport> PendingImports { get; } = [];

    public Task EnqueueAsync(PendingImport pendingImport)
    {
        ArgumentNullException.ThrowIfNull(pendingImport);

        if (Dispatcher.UIThread.CheckAccess())
        {
            PendingImports.Add(pendingImport);
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => PendingImports.Add(pendingImport)).GetTask();
    }

    public Task RemoveAsync(PendingImport pendingImport)
    {
        ArgumentNullException.ThrowIfNull(pendingImport);

        if (Dispatcher.UIThread.CheckAccess())
        {
            PendingImports.Remove(pendingImport);
            return Task.CompletedTask;
        }

        return Dispatcher.UIThread.InvokeAsync(() => PendingImports.Remove(pendingImport)).GetTask();
    }
}
