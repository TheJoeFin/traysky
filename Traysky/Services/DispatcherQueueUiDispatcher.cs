using Microsoft.UI.Dispatching;
using System;

namespace Traysky.Services;

/// <summary><see cref="IUiDispatcher"/> over a WinUI <see cref="DispatcherQueue"/>.</summary>
public sealed class DispatcherQueueUiDispatcher : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueUiDispatcher(DispatcherQueue queue) => _queue = queue;

    /// <summary>Wraps the current thread's queue; throws when there is none (not on a UI thread).</summary>
    public static DispatcherQueueUiDispatcher ForCurrentThread() =>
        new(DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Must be created on the UI thread."));

    public bool HasThreadAccess => _queue.HasThreadAccess;

    public bool TryEnqueue(Action action) => _queue.TryEnqueue(() => action());
}
