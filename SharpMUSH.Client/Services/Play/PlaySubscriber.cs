using SharpMUSH.Library.Models.Portal.Play;

namespace SharpMUSH.Client.Services.Play;

/// <summary>
/// One subscription: its filter, and a queue that hands its handler one event at a time, in order, each inside
/// its own error boundary.
/// </summary>
internal sealed class PlaySubscriber(PackagePatterns packages, PackagePatterns? scope, bool acceptSelfSent,
	Func<PlayEvent, Task> handler, Func<Func<Task>, Task>? dispatch, string owner, ILogger logger)
{
	private readonly Queue<PlayEvent> _queue = new();
	private readonly Lock _lock = new();
	private bool _draining;
	private bool _stopped;

	/// <summary>Whether this subscription receives <paramref name="package"/>.</summary>
	public bool Accepts(PlayPackage package) =>
		packages.Matches(package.Package)
		&& (scope is null || scope.Matches(package.Package))
		&& (acceptSelfSent || package.Sender != PlaySender.Self);

	/// <summary>Queues <paramref name="item"/>, and starts handing the queue over when it is not already.</summary>
	public void Post(PlayEvent item)
	{
		lock (_lock)
		{
			if (_stopped) return;
			_queue.Enqueue(item);
			if (_draining) return;
			_draining = true;
		}

		_ = DrainAsync();
	}

	/// <summary>Delivers nothing more, including what is queued.</summary>
	public void Stop()
	{
		lock (_lock)
		{
			_stopped = true;
			_queue.Clear();
		}
	}

	private async Task DrainAsync()
	{
		while (true)
		{
			PlayEvent next;
			lock (_lock)
			{
				if (_stopped || !_queue.TryDequeue(out next!))
				{
					_draining = false;
					return;
				}
			}

			try
			{
				if (dispatch is null) await handler(next);
				else await dispatch(() => handler(next));
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "A play event handler of {Owner} threw on {Event}", owner, next.GetType().Name);
			}
		}
	}
}
