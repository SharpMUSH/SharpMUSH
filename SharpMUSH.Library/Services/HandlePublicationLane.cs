namespace SharpMUSH.Library.Services;

/// <summary>
/// Orders every markup publication to one connection handle. A publication reserves its place
/// synchronously, so a caller can take that place while it holds its own state lock and publish after
/// releasing it; each publication starts only once every earlier reservation for the handle has
/// published, been abandoned or been cancelled.
/// </summary>
/// <remarks>
/// With the prompt and ordinary output on one ordered subject, reservation order is the order a
/// connection displays them in. A place reserved while a guided-input transition is committed
/// therefore reaches the socket before anything reserved after that transition.
/// </remarks>
public sealed class HandlePublicationLane
{
	private readonly Lock _gate = new();
	private readonly Dictionary<long, Task> _tails = [];
	private readonly AsyncLocal<Slot?> _bound = new();

	/// <summary>Takes the next place for <paramref name="handle"/>.</summary>
	public Slot Reserve(long handle)
	{
		var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		Task previous;
		lock (_gate)
		{
			previous = _tails.GetValueOrDefault(handle, Task.CompletedTask);
			_tails[handle] = done.Task;
		}
		return new Slot(this, handle, previous, done);
	}

	/// <summary>
	/// Makes <paramref name="slot"/> the place the next publication to its handle in this execution
	/// context uses, instead of reserving a new one. Disposing the scope releases the slot if nothing
	/// published through it.
	/// </summary>
	public IDisposable Bind(Slot slot)
	{
		var prior = _bound.Value;
		_bound.Value = slot;
		return new Binding(this, slot, prior);
	}

	/// <summary>Publishes in <paramref name="handle"/>'s order, through a bound place when there is one.</summary>
	public Task PublishAsync(long handle, Func<CancellationToken, Task> publish, CancellationToken cancellationToken)
	{
		var slot = _bound.Value is { } bound && bound.Handle == handle && bound.TryClaim() ? bound : Reserve(handle);
		return slot.PublishAsync(publish, cancellationToken);
	}

	private void Complete(Slot slot)
	{
		lock (_gate)
		{
			if (_tails.TryGetValue(slot.Handle, out var tail) && tail == slot.Done.Task) _tails.Remove(slot.Handle);
		}
		slot.Done.TrySetResult();
	}

	/// <summary>One reserved place in a handle's publication order.</summary>
	public sealed class Slot : IDisposable
	{
		private readonly HandlePublicationLane _lane;
		private readonly Task _previous;
		private int _claimed;

		internal Slot(HandlePublicationLane lane, long handle, Task previous, TaskCompletionSource done)
		{
			_lane = lane;
			Handle = handle;
			_previous = previous;
			Done = done;
		}

		public long Handle { get; }

		internal TaskCompletionSource Done { get; }

		internal bool TryClaim() => Interlocked.Exchange(ref _claimed, 1) == 0;

		internal async Task PublishAsync(Func<CancellationToken, Task> publish, CancellationToken cancellationToken)
		{
			_claimed = 1;
			try
			{
				// Earlier places always complete, so this wait ends unless the caller gives up.
				await _previous.WaitAsync(cancellationToken);
				await publish(cancellationToken);
			}
			finally
			{
				// A caller that gave up while waiting still holds its place until the earlier ones finish.
				if (_previous.IsCompleted) _lane.Complete(this);
				else _ = ReleaseAfterAsync();
			}
		}

		/// <summary>Releases the place if nothing was published through it.</summary>
		public void Dispose()
		{
			if (TryClaim()) _ = ReleaseAfterAsync();
		}

		// An abandoned place still holds back later ones until the earlier places finish.
		private async Task ReleaseAfterAsync()
		{
			try { await _previous; }
			finally { _lane.Complete(this); }
		}
	}

	private sealed class Binding(HandlePublicationLane lane, Slot slot, Slot? prior) : IDisposable
	{
		public void Dispose()
		{
			lane._bound.Value = prior;
			slot.Dispose();
		}
	}
}

/// <summary>A notifier that publishes through a <see cref="HandlePublicationLane"/>.</summary>
public interface IOrderedHandlePublisher
{
	HandlePublicationLane Lane { get; }
}
