using System;

namespace Traysky.Services;

/// <summary>
/// Marshals work onto the UI thread without the caller knowing about
/// <c>Microsoft.UI.Dispatching</c>; see <see cref="DispatcherQueueUiDispatcher"/>.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>True when the calling thread is the UI thread.</summary>
    bool HasThreadAccess { get; }

    /// <summary>Queues <paramref name="action"/> on the UI thread; false when the dispatcher is shutting down.</summary>
    bool TryEnqueue(Action action);
}
