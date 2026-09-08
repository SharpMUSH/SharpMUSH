using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

public class NatsConsumerRecoveryTests
{
	[ClassDataSource<NatsTestServer>(Shared = SharedType.PerTestSession)]
	public required NatsTestServer NatsTestServer { get; init; }

	[Test]
	public async Task FailedSetupRetriesAndBeginsConsumingWithoutRestartingHost()
	{
		var id = Guid.NewGuid().ToString("N");
		var options = new NatsOptions
		{
			Url = $"nats://localhost:{NatsTestServer.Instance.GetMappedPublicPort(4222)}",
			StreamName = "invalid stream name",
			SubjectPrefix = "recovery" + id
		};
		var registry = new NatsConsumerRegistry();
		var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		registry.Registrations.Add(new(typeof(RecoveryMessage), $"{options.SubjectPrefix}.recovery", "test" + id,
			(_, _, _) => { received.TrySetResult(); return Task.CompletedTask; }));
		var logger = new FailureLogger();
		using var provider = new ServiceCollection().BuildServiceProvider();
		using var service = new NatsJetStreamConsumerService(registry, options, provider, logger);
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
		await service.StartAsync(timeout.Token);
		try
		{
			await logger.Failed.Task.WaitAsync(timeout.Token);
			options.StreamName = "RECOVERY" + id;
			await registry.WaitUntilReadyAsync(timeout.Token);
			await using var bus = await NatsJetStreamMessageBus.CreateAsync(options,
				NullLogger<NatsJetStreamMessageBus>.Instance, timeout.Token);
			await bus.Publish(new RecoveryMessage("test"), timeout.Token);
			await received.Task.WaitAsync(timeout.Token);
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}
	}

	public record RecoveryMessage(string Value);

	private sealed class FailureLogger : ILogger<NatsJetStreamConsumerService>
	{
		public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
		public bool IsEnabled(LogLevel logLevel) => true;
		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			if (logLevel == LogLevel.Error) Failed.TrySetResult();
		}
	}
}
