namespace SharpMUSH.Database.Lightning.Semantics;

/// <param name="Active">Queries running at once across the game.</param>
/// <param name="ActivePerPrincipal">Queries one principal may have running at once.</param>
/// <param name="QueuedPerPrincipal">Queries one principal may have waiting.</param>
/// <param name="Queued">Queries waiting across the game.</param>
internal sealed record SemanticQueryAdmissionLimits(int Active, int ActivePerPrincipal, int QueuedPerPrincipal, int Queued)
{
	/// <summary>The spec's initial budget (§10): 2 running, 1 running and 2 waiting per principal, 16 waiting.</summary>
	internal static readonly SemanticQueryAdmissionLimits Default = new(2, 1, 2, 16);
}

/// <summary>What the queue holds now, how many it has refused, and the longest any admitted query waited.</summary>
internal sealed record SemanticQueryAdmissionStats(int Active, int Queued, long Refused, TimeSpan LongestWait);

/// <summary>
/// The dedicated wiki-query admission queue (spec §9). A query is admitted before it reads anything: it runs at
/// once when a game slot and its principal's slot are free and none of its principal's queries is already
/// waiting, otherwise it waits in order, and past either queue limit it is refused. Every principal counts,
/// administrators included. A freed slot goes to the oldest waiter whose principal has nothing running.
/// </summary>
internal sealed class SemanticQueryAdmission(SemanticQueryAdmissionLimits limits, TimeProvider clock)
{
	internal abstract record Entry
	{
		private Entry() { }
		internal sealed record Admitted(Ticket Ticket) : Entry;
		/// <summary>Refused by <paramref name="Limit"/>: <c>principal</c> or <c>queue</c>.</summary>
		internal sealed record Refused(string Limit) : Entry;
	}

	/// <summary>A running query's slot, released when disposed; disposing twice releases once.</summary>
	internal sealed class Ticket(SemanticQueryAdmission owner, string principal, TimeSpan waited) : IDisposable
	{
		private int _released;
		internal TimeSpan Waited { get; } = waited;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) owner.Release(principal);
		}
	}

	private sealed class Waiter(string principal, long since)
	{
		internal string Principal { get; } = principal;
		internal long Since { get; } = since;
		internal TaskCompletionSource<Entry> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		internal CancellationTokenRegistration Cancellation { get; set; }
	}

	private readonly Lock _gate = new();
	private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _waiting = new(StringComparer.Ordinal);
	private readonly LinkedList<Waiter> _queue = new();
	private int _active;
	private long _refused;
	private TimeSpan _longestWait;
	private int _aggregate;

	internal ValueTask<Entry> EnterAsync(string principal, CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(principal);
		if (cancellationToken.IsCancellationRequested) return ValueTask.FromCanceled<Entry>(cancellationToken);
		LinkedListNode<Waiter> node;
		lock (_gate)
		{
			if (CanStart(principal) && Count(_waiting, principal) == 0)
				return ValueTask.FromResult<Entry>(new Entry.Admitted(Start(principal, TimeSpan.Zero)));
			if (Count(_waiting, principal) >= limits.QueuedPerPrincipal) return Refuse("principal");
			if (_queue.Count >= limits.Queued) return Refuse("queue");
			node = _queue.AddLast(new Waiter(principal, clock.GetTimestamp()));
			_waiting[principal] = Count(_waiting, principal) + 1;
		}
		node.Value.Cancellation = cancellationToken.Register(() => Cancel(node, cancellationToken));
		return new ValueTask<Entry>(node.Value.Started.Task);
	}

	/// <summary>The one aggregate slot (spec §10: one active aggregate), or null while another aggregate holds it.</summary>
	internal IDisposable? TryEnterAggregate()
		=> Interlocked.CompareExchange(ref _aggregate, 1, 0) == 0 ? new AggregateSlot(this) : null;

	private sealed class AggregateSlot(SemanticQueryAdmission owner) : IDisposable
	{
		private int _released;

		public void Dispose()
		{
			if (Interlocked.Exchange(ref _released, 1) == 0) Volatile.Write(ref owner._aggregate, 0);
		}
	}

	internal SemanticQueryAdmissionStats Snapshot()
	{
		lock (_gate) return new(_active, _queue.Count, _refused, _longestWait);
	}

	private ValueTask<Entry> Refuse(string limit)
	{
		_refused++;
		return ValueTask.FromResult<Entry>(new Entry.Refused(limit));
	}

	private bool CanStart(string principal) => _active < limits.Active && Count(_running, principal) < limits.ActivePerPrincipal;

	private Ticket Start(string principal, TimeSpan waited)
	{
		_active++;
		_running[principal] = Count(_running, principal) + 1;
		if (waited > _longestWait) _longestWait = waited;
		return new Ticket(this, principal, waited);
	}

	private void Release(string principal)
	{
		var started = new List<(Waiter Waiter, Ticket Ticket)>();
		lock (_gate)
		{
			_active--;
			Decrement(_running, principal);
			var now = clock.GetTimestamp();
			for (var node = _queue.First; node is not null && _active < limits.Active;)
			{
				var next = node.Next;
				if (CanStart(node.Value.Principal))
				{
					_queue.Remove(node);
					Decrement(_waiting, node.Value.Principal);
					started.Add((node.Value, Start(node.Value.Principal, clock.GetElapsedTime(node.Value.Since, now))));
				}
				node = next;
			}
		}
		foreach (var (waiter, ticket) in started)
		{
			waiter.Cancellation.Dispose();
			waiter.Started.TrySetResult(new Entry.Admitted(ticket));
		}
	}

	private void Cancel(LinkedListNode<Waiter> node, CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			// Already started: the query owns its ticket and sees the cancellation when it reads.
			if (node.List is null) return;
			_queue.Remove(node);
			Decrement(_waiting, node.Value.Principal);
		}
		node.Value.Started.TrySetCanceled(cancellationToken);
	}

	private static int Count(Dictionary<string, int> counts, string principal) => counts.GetValueOrDefault(principal);

	private static void Decrement(Dictionary<string, int> counts, string principal)
	{
		if (counts[principal] == 1) counts.Remove(principal);
		else counts[principal]--;
	}
}
