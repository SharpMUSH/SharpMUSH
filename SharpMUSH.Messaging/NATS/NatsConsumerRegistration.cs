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

	public Task WaitUntilReadyAsync(CancellationToken ct) =>
		Registrations.Count == 0 ? Task.CompletedTask : _ready.Task.WaitAsync(ct);

	internal void MarkActive(string durableName)
	{
		lock (_active)
		{
			_active.Add(durableName);
			if (Registrations.All(registration => _active.Contains(registration.DurableName))) _ready.TrySetResult();
		}
	}
}
