using Microsoft.Extensions.DependencyInjection;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.NATS;

namespace SharpMUSH.Tests.Services;

/// <summary>
/// Covers what <see cref="NatsConsumerConfigurator.AddConsumer{TConsumer,TMessage}"/> puts in the registry:
/// the message type, the derived subject and durable name, the DI registration, and a dispatch delegate
/// that actually reaches the consumer. Registration is pure bookkeeping, so none of this needs a NATS server.
/// </summary>
public class NatsConsumerConfiguratorTests
{
	private sealed record ProbeOutputMessage(string Payload);

	/// <summary>Shared observation point — the configurator registers the consumer type, not an instance.</summary>
	private sealed class ProbeSink
	{
		public ProbeOutputMessage? Received { get; set; }
		public CancellationToken Token { get; set; }
	}

	private sealed class ProbeOutputConsumer(ProbeSink sink) : IMessageConsumer<ProbeOutputMessage>
	{
		public Task HandleAsync(ProbeOutputMessage message, CancellationToken cancellationToken = default)
		{
			sink.Received = message;
			sink.Token = cancellationToken;
			return Task.CompletedTask;
		}
	}

	private static (NatsConsumerRegistry Registry, ServiceCollection Services, NatsConsumerConfigurator Configurator)
		Build(string groupPrefix = "test-group")
	{
		var registry = new NatsConsumerRegistry();
		var services = new ServiceCollection();
		var options = new NatsOptions { SubjectPrefix = "probe.prefix" };

		return (registry, services, new NatsConsumerConfigurator(registry, services, options, groupPrefix));
	}

	/// <summary>
	/// The subject drops the "Message" suffix and kebab-cases the rest, under the consuming prefix.
	/// </summary>
	[Test]
	public async Task AddConsumer_DerivesTheSubjectFromTheMessageTypeName()
	{
		var (registry, _, configurator) = Build();

		configurator.AddConsumer<ProbeOutputConsumer, ProbeOutputMessage>();

		var registration = registry.Registrations.Single();

		await Assert.That(registration.MessageType).IsEqualTo(typeof(ProbeOutputMessage));
		await Assert.That(registration.Subject).IsEqualTo("probe.prefix.probe-output");
	}

	/// <summary>
	/// The durable name carries the group prefix so two applications consuming the same subject get
	/// separate JetStream cursors. An empty group falls back to "consumer".
	/// </summary>
	[Test]
	[Arguments("test-group", "test-group-probe-output")]
	[Arguments("", "consumer-probe-output")]
	public async Task AddConsumer_PrefixesTheDurableNameWithTheGroup(string groupPrefix, string expected)
	{
		var (registry, _, configurator) = Build(groupPrefix);

		configurator.AddConsumer<ProbeOutputConsumer, ProbeOutputMessage>();

		await Assert.That(registry.Registrations.Single().DurableName).IsEqualTo(expected);
	}

	/// <summary>
	/// The consumer is registered against its <see cref="IMessageConsumer{T}"/> interface, which is how
	/// the dispatch delegate resolves it.
	/// </summary>
	[Test]
	public async Task AddConsumer_RegistersTheConsumerAgainstItsMessageInterface()
	{
		var (_, services, configurator) = Build();

		configurator.AddConsumer<ProbeOutputConsumer, ProbeOutputMessage>();

		var descriptor = services.Single(d => d.ServiceType == typeof(IMessageConsumer<ProbeOutputMessage>));

		await Assert.That(descriptor.ImplementationType).IsEqualTo(typeof(ProbeOutputConsumer));
		await Assert.That(descriptor.Lifetime).IsEqualTo(ServiceLifetime.Transient);
	}

	/// <summary>
	/// The registered delegate resolves the consumer from the provider and hands it the message and the
	/// cancellation token it was called with — the behaviour a closed generic method used to supply.
	/// </summary>
	[Test]
	public async Task AddConsumer_DispatchesToTheConsumerWithTheMessageAndToken()
	{
		var (registry, services, configurator) = Build();
		var sink = new ProbeSink();
		services.AddSingleton(sink);

		configurator.AddConsumer<ProbeOutputConsumer, ProbeOutputMessage>();

		await using var provider = services.BuildServiceProvider();
		using var cts = new CancellationTokenSource();
		var message = new ProbeOutputMessage("payload");

		await registry.Registrations.Single().Handler(provider, message, cts.Token);

		await Assert.That(sink.Received).IsEqualTo(message);
		await Assert.That(sink.Token).IsEqualTo(cts.Token);
	}
}
