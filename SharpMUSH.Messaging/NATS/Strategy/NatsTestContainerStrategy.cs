using System.Diagnostics.CodeAnalysis;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using Microsoft.Extensions.Logging;
using Testcontainers.Nats;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Strategy that starts a NATS server in a Testcontainer.
/// Used for local development when <c>NATS_URL</c> is not set —
/// no external NATS installation is required. A running container a native process started
/// (<see cref="NatsDockerStrategy"/>) is joined instead of starting a second one.
/// </summary>
[RequiresUnreferencedCode("Testcontainers reads Docker's API with reflection-based JSON.")]
[RequiresDynamicCode("Testcontainers reads Docker's API with reflection-based JSON.")]
public sealed class NatsTestContainerStrategy : NatsStrategy
{
	private NatsContainer? _container;
	private string? _joinedUrl;
	private readonly ILogger? _logger;

	public NatsTestContainerStrategy() { }

	public NatsTestContainerStrategy(ILogger logger) => _logger = logger;

	public override async ValueTask<string> GetUrlAsync()
	{
		if (_joinedUrl is not null) return _joinedUrl;
		if (_container is null)
		{
			using var lookup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			_joinedUrl = await NatsDockerStrategy.FindRunningAsync(lookup.Token);
			if (_joinedUrl is not null) return _joinedUrl;

			var builder = new NatsBuilder($"{NatsLocalContainer.Image}:{NatsLocalContainer.Tag}")
				.WithResourceMapping(NatsLocalContainer.Config, NatsLocalContainer.ConfigPath)
				.WithCommand(new OverwriteEnumerable<string>(NatsLocalContainer.Command))
				.WithLabel(NatsLocalContainer.ReuseLabelKey, NatsLocalContainer.ReuseLabelValue)
				.WithReuse(true)   // shared across Server and ConnectionServer processes
													 // Port-based readiness instead of the image default's log-message wait. A reused
													 // container's startup log has already scrolled past, so replaying a log wait hangs
													 // forever (notably under rootless podman); checking the client port is open succeeds
													 // immediately whether the container is fresh or reused.
				.WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(NatsLocalContainer.ClientPort));
			if (_logger is not null) builder = builder.WithLogger(_logger);
			_container = builder.Build();

			await _container.StartAsync();
		}

		return _container.GetConnectionString();
	}

	public override async ValueTask DisposeAsync()
	{
		if (_container is not null)
		{
			await _container.StopAsync();
			await _container.DisposeAsync();
			_container = null;
		}
	}
}
