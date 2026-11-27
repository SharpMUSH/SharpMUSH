using System.Net.Sockets;
using System.Text.Json;
using System.Text;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Starts NATS in a local container through Docker's Engine API, for the socket server, which is published
/// ahead of time and so runs without reflection-based JSON (built or published) and cannot run Testcontainers.
/// The container is <see cref="NatsLocalContainer.Name"/>: one of this revision that is already there is used as it
/// is, whichever process started it; otherwise this one creates it, and stops it again on disposal.
/// </summary>
public sealed class NatsDockerStrategy : NatsStrategy
{
	private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

	// How long a container may stay created but not started before it counts as abandoned by its creator.
	private static readonly TimeSpan AbandonedAfter = TimeSpan.FromSeconds(15);

	private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

	private readonly DockerEngine _docker = DockerEngine.FromEnvironment();
	private string? _url;
	private string? _ownedContainer;

	public override async ValueTask<string> GetUrlAsync()
	{
		if (_url is not null) return _url;

		using var timeout = new CancellationTokenSource(ReadyTimeout);
		try
		{
			var url = await JoinOrCreateAsync(timeout.Token);
			await WaitUntilReadyAsync(url, timeout.Token);
			return _url = url;
		}
		catch (Exception ex) when (IsDockerFailure(ex))
		{
			throw new InvalidOperationException(
				$"NATS_URL is not set, and NATS could not be started in Docker at {_docker.Endpoint} ({ex.Message}). " +
				"Set NATS_URL to a running NATS server (nats://host:4222), or start Docker.", ex);
		}
	}

	/// <summary>
	/// The URL of the running container this strategy created in another process, or nothing when there is none
	/// or Docker cannot be reached. The server calls it before Testcontainers, which finds its own containers by
	/// their reuse hash but not one created here.
	/// </summary>
	internal static async Task<string?> FindRunningAsync(CancellationToken ct)
	{
		try
		{
			using var docker = DockerEngine.FromEnvironment();
			return await FindAsync(docker, ct) is { State: "running" } container
						 && IsThisRevision(container)
						 && container.Labels?.GetValueOrDefault(NatsLocalContainer.CreatorLabelKey) == NatsLocalContainer.CreatorLabelValue
						 && PublishedClientPort(container.Ports) is > 0 and var port
				? UrlFor(docker.Endpoint, port)
				: null;
		}
		catch (Exception ex) when (IsDockerFailure(ex))
		{
			return null;
		}
	}

	/// <summary>
	/// Waits for the container another process is creating under <see cref="NatsLocalContainer.Name"/> to run, for
	/// the server when Testcontainers finds the name taken. Nothing when there is no such container of this revision.
	/// </summary>
	internal static async Task<string?> JoinAsync(CancellationToken ct)
	{
		using var docker = DockerEngine.FromEnvironment();
		while (await FindAsync(docker, ct) is { } container && IsThisRevision(container))
		{
			switch (container.State)
			{
				case "running" when PublishedClientPort(container.Ports) is > 0 and var port:
					return UrlFor(docker.Endpoint, port);
				case "exited":
					await docker.StartAsync(container.Id, ct);
					continue;
			}

			await Task.Delay(PollInterval, ct);
		}

		return null;
	}

	private async Task<string> JoinOrCreateAsync(CancellationToken ct)
	{
		DateTimeOffset? createdSince = null;
		while (true)
		{
			switch (await FindAsync(_docker, ct))
			{
				case null:
					await CreateAsync(ct);
					continue;
				case var other when !IsThisRevision(other):
					if (other.State == "running")
						throw new InvalidOperationException(
							$"A container named {NatsLocalContainer.Name} from another revision is running; remove it with `docker rm -f {NatsLocalContainer.Name}`.");
					await _docker.RemoveAsync(other.Id, ct);
					continue;
				case { State: "running" } running when PublishedClientPort(running.Ports) is > 0 and var port:
					return UrlFor(_docker.Endpoint, port);
				case { State: "exited" } exited:
					await _docker.StartAsync(exited.Id, ct);
					continue;
				case { State: "created" } created:
					// Its creator is still writing the config in; start it only once that creator has given up.
					createdSince ??= DateTimeOffset.UtcNow;
					if (DateTimeOffset.UtcNow - createdSince > AbandonedAfter)
					{
						await _docker.RemoveAsync(created.Id, ct);
						createdSince = null;
						continue;
					}

					break;
			}

			await Task.Delay(PollInterval, ct);
		}
	}

	private static async Task<DockerContainerSummary?> FindAsync(DockerEngine docker, CancellationToken ct) =>
		// Docker matches the name with and without its leading slash, Podman only without.
		(await docker.ListAsync(new() { ["name"] = [$"^/?{NatsLocalContainer.Name}$"] }, all: true, ct)).FirstOrDefault();

	private static bool IsThisRevision(DockerContainerSummary container) =>
		container.Labels?.GetValueOrDefault(NatsLocalContainer.RevisionLabelKey) == NatsLocalContainer.Revision;

	/// <summary>The host port Docker publishes the container's NATS client port on, or 0 when it publishes none.</summary>
	internal static int PublishedClientPort(DockerPort[]? ports) =>
		ports?.FirstOrDefault(port => port.PrivatePort == NatsLocalContainer.ClientPort
																	&& port.PublicPort > 0
																	&& port.Type is null or "tcp")?.PublicPort ?? 0;

	internal static string UrlFor(DockerEndpoint endpoint, int port) => $"nats://{endpoint.PublishedHost}:{port}";

	/// <summary>Creates, configures and starts the container; another process creating it first is not a failure.</summary>
	private async Task CreateAsync(CancellationToken ct)
	{
		var clientPort = $"{NatsLocalContainer.ClientPort}/tcp";
		var body = new DockerContainerCreate(
			$"{NatsLocalContainer.Image}:{NatsLocalContainer.Tag}",
			NatsLocalContainer.Command,
			new()
			{
				[NatsLocalContainer.ReuseLabelKey] = NatsLocalContainer.ReuseLabelValue,
				[NatsLocalContainer.RevisionLabelKey] = NatsLocalContainer.Revision,
				[NatsLocalContainer.CreatorLabelKey] = NatsLocalContainer.CreatorLabelValue
			},
			new() { [clientPort] = new DockerEmpty() },
			// A random host port, as Testcontainers chooses, on loopback unless Docker runs on another machine;
			// removed by Docker once stopped.
			new DockerHostConfig(
				new() { [clientPort] = [new DockerPortBinding(_docker.Endpoint.Kind == DockerEndpointKind.Tcp ? "0.0.0.0" : "127.0.0.1", "")] },
				AutoRemove: true));

		var (outcome, id) = await _docker.CreateAsync(NatsLocalContainer.Name, body, ct);
		if (outcome == DockerCreateOutcome.NoImage)
		{
			await _docker.PullAsync(NatsLocalContainer.Image, NatsLocalContainer.Tag, ct);
			(outcome, id) = await _docker.CreateAsync(NatsLocalContainer.Name, body, ct);
		}

		if (outcome == DockerCreateOutcome.NameInUse) return;
		if (id is null) throw new InvalidOperationException("Docker created the NATS container without naming it.");
		_ownedContainer = id;
		await _docker.CopyFileAsync(id, NatsLocalContainer.ConfigPath, NatsLocalContainer.Config, ct);
		await _docker.StartAsync(id, ct);
	}

	private static bool IsDockerFailure(Exception ex) =>
		ex is HttpRequestException or SocketException or IOException or OperationCanceledException
			or InvalidOperationException or JsonException;

	/// <summary>Waits until NATS greets a client with its <c>INFO</c> line, which Docker's port proxy alone never sends.</summary>
	private static async Task WaitUntilReadyAsync(string url, CancellationToken ct)
	{
		var uri = new Uri(url);
		var buffer = new byte[4];
		while (true)
		{
			try
			{
				using var client = new TcpClient();
				await client.ConnectAsync(uri.Host, uri.Port, ct);
				await using var stream = client.GetStream();
				var read = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
				if (read == buffer.Length && Encoding.ASCII.GetString(buffer) == "INFO") return;
			}
			catch (Exception ex) when (ex is SocketException or IOException)
			{
				// Not listening yet.
			}

			await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
		}
	}

	public override async ValueTask DisposeAsync()
	{
		if (_ownedContainer is { } container)
		{
			_ownedContainer = null;
			try
			{
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
				await _docker.StopAsync(container, timeout.Token);
			}
			catch (Exception ex) when (IsDockerFailure(ex))
			{
				// Shutting down anyway; a container left running is used by the next start.
			}
		}

		_docker.Dispose();
	}
}
