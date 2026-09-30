namespace SharpMUSH.Messaging.NATS;

/// <summary>
/// Holds the metadata and dispatch delegate for a single NATS JetStream consumer.
/// </summary>
public record NatsConsumerRegistration(
	Type MessageType,
	string Subject,
	string DurableName,
	Func<IServiceProvider, object, CancellationToken, Task> Handler);

/// <summary>
/// Registry that accumulates <see cref="NatsConsumerRegistration"/> entries built by
/// <see cref="NatsConsumerConfigurator"/> during DI setup.
/// </summary>
public sealed class NatsConsumerRegistry
{
	public List<NatsConsumerRegistration> Registrations { get; } = [];
	private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly HashSet<string> _active = [];

	/// <summary>Completes the first time every registered consumer is active. One-shot: it stays
	/// complete through a later reconnect; <see cref="AllActive"/> is the current state.</summary>
	public Task WaitUntilReadyAsync(CancellationToken ct) =>
		Registrations.Count == 0 ? Task.CompletedTask : _ready.Task.WaitAsync(ct);

	private Func<bool>? _connectionOpen;

	/// <summary>Whether every registered consumer is consuming right now: each has subscribed, and the
	/// connection they share is open. False while the broker is unreachable — the client reconnects on its
	/// own without the consumers ever failing, so the subscriptions alone would not show it — and while the
	/// consumer group is being rebuilt.</summary>
	public bool AllActive
	{
		get
		{
			lock (_active)
			{
				return (Registrations.Count == 0 || _connectionOpen?.Invoke() == true)
					&& Registrations.All(registration => _active.Contains(registration.DurableName));
			}
		}
	}

	/// <summary>The consumers' connection state, read live by <see cref="AllActive"/>.</summary>
	internal void Attach(Func<bool> connectionOpen)
	{
		lock (_active)
		{
			_connectionOpen = connectionOpen;
		}
	}

	internal void MarkActive(string durableName)
	{
		lock (_active)
		{
			_active.Add(durableName);
			if (Registrations.All(registration => _active.Contains(registration.DurableName))) _ready.TrySetResult();
		}
	}

	/// <summary>The consumer group stopped; each consumer marks itself active again as it resubscribes.</summary>
	internal void MarkAllInactive()
	{
		lock (_active)
		{
			_active.Clear();
			_connectionOpen = null;
		}
	}
}
