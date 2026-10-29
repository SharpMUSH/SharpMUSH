namespace SharpMUSH.Library.Services;

/// <summary>
/// Lets one message to many connections go out to all of them at once. Inside a batch a publication
/// still takes its place in its handle's order (<see cref="HandlePublicationLane"/>) and starts at once,
/// but <see cref="NotifyService"/> does not wait for the broker to acknowledge it before going on to the
/// next recipient; the batch waits for every one of them when it ends, and rethrows a failure there. A
/// batch begun inside another joins the outer one. Every queue entry runs inside one (<see cref="TaskScheduler"/>).
/// </summary>
public sealed class OutputBatch : IAsyncDisposable
{
	private static readonly AsyncLocal<OutputBatch?> Current = new();
	private static readonly OutputBatch Joined = new();
	private readonly List<Task> _pending = [];
	private bool _closed;

	private OutputBatch() { }

	/// <summary>Begins a batch for the rest of the calling method, or joins the one already open.</summary>
	public static OutputBatch Begin()
	{
		if (Current.Value is not null) return Joined;
		var batch = new OutputBatch();
		Current.Value = batch;
		return batch;
	}

	/// <summary><paramref name="publication"/>, or nothing to wait for when an open batch waits for it instead.</summary>
	internal static ValueTask Defer(Task publication)
	{
		if (publication.IsCompleted || Current.Value is not { } batch) return new(publication);
		lock (batch._pending)
		{
			// Work started inside the batch that outlives it still sees it; it waits for its own publications.
			if (batch._closed) return new(publication);
			batch._pending.Add(publication);
		}
		return ValueTask.CompletedTask;
	}

	// Not async: the batch must stop being current in the caller's own context, which an async method's
	// changes to an AsyncLocal never reach.
	public ValueTask DisposeAsync()
	{
		if (ReferenceEquals(this, Joined)) return ValueTask.CompletedTask;
		Current.Value = null;
		Task[] pending;
		lock (_pending)
		{
			_closed = true;
			pending = [.. _pending];
		}
		return pending.Length == 0 ? ValueTask.CompletedTask : new(Task.WhenAll(pending));
	}
}
