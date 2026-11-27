using System.Net.Sockets;
using System.Text;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// Starts NATS in a local container through Docker's Engine API, for the socket server, which is published
/// ahead of time and so runs without reflection-based JSON (built or published) and cannot run Testcontainers.
/// A running container labelled <see cref="NatsLocalContainer.ReuseLabel"/> is used as it is, whichever process
/// started it; otherwise this one creates it, and stops it again on disposal.
/// </summary>
public sealed class NatsDockerStrategy : NatsStrategy
{
	/// <summary>The container's name, which makes two processes starting at once create only one.</summary>
	public const string ContainerName = "sharpmush-local-nats";

	private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

	private readonly DockerEngine _docker = DockerEngine.FromEnvironment();
	private string? _url;
	private string? _ownedContainer;

	public override async ValueTask<string> GetUrlAsync()
	{
		if (_url is not null) return _url;

		using var timeout = new CancellationTokenSource(ReadyTimeout);
		try
		{
			_url = await FindAsync(_docker, timeout.Token) ?? await CreateAsync(timeout.Token);
			await WaitUntilReadyAsync(_url, timeout.Token);
			return _url;
		}
		catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException
																 or OperationCanceledException or InvalidOperationException)
		{
			throw new InvalidOperationException(
				$"NATS_URL is not set, and NATS could not be started in Docker at {_docker.Endpoint} ({ex.Message}). " +
				"Set NATS_URL to a running NATS server (nats://host:4222), or start Docker.", ex);
		}
	}

	/// <summary>
	/// The URL of a running container labelled <see cref="NatsLocalContainer.ReuseLabel"/>, or nothing when there
	/// is none or Docker cannot be reached. The server calls it before Testcontainers, so that it joins a container a
	/// native process started.
	/// </summary>
	internal static async Task<string?> FindRunningAsync(CancellationToken ct)
	{
		try
		{
			using var docker = DockerEngine.FromEnvironment();
			return await FindAsync(docker, ct);
		}
		catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException
																 or OperationCanceledException or InvalidOperationException)
		{
			return null;
		}
	}

	private static async Task<string?> FindAsync(DockerEngine docker, CancellationToken ct)
	{
		var running = await docker.ListAsync(
			new() { ["label"] = [NatsLocalContainer.ReuseLabel], ["status"] = ["running"] }, all: false, ct);
		return running.Select(container => PublishedClientPort(container.Ports))
			.Where(port => port > 0)
			.Select(port => UrlFor(docker.Endpoint, port))
			.FirstOrDefault();
	}

	/// <summary>The host port Docker publishes the container's NATS client port on, or 0 when it publishes none.</summary>
	internal static int PublishedClientPort(DockerPort[]? ports) =>
		ports?.FirstOrDefault(port => port.PrivatePort == NatsLocalContainer.ClientPort
																	&& port.PublicPort > 0
																	&& port.Type is null or "tcp")?.PublicPort ?? 0;

	internal static string UrlFor(DockerEndpoint endpoint, int port) => $"nats://{endpoint.PublishedHost}:{port}";

	private async Task<string> CreateAsync(CancellationToken ct)
	{
		var clientPort = $"{NatsLocalContainer.ClientPort}/tcp";
		var body = new DockerContainerCreate(
			$"{NatsLocalContainer.Image}:{NatsLocalContainer.Tag}",
			NatsLocalContainer.Command,
			new() { [NatsLocalContainer.ReuseLabelKey] = NatsLocalContainer.ReuseLabelValue },
			new() { [clientPort] = new DockerEmpty() },
			// A random host port, as Testcontainers chooses, on loopback unless Docker runs on another machine;
			// removed by Docker once stopped.
			new DockerHostConfig(
				new() { [clientPort] = [new DockerPortBinding(_docker.Endpoint.Kind == DockerEndpointKind.Tcp ? "0.0.0.0" : "127.0.0.1", "")] },
				AutoRemove: true));

		var (outcome, id) = await _docker.CreateAsync(ContainerName, body, ct);
		if (outcome == DockerCreateOutcome.NoImage)
		{
			await _docker.PullAsync(NatsLocalContainer.Image, NatsLocalContainer.Tag, ct);
			(outcome, id) = await _docker.CreateAsync(ContainerName, body, ct);
		}

		if (outcome == DockerCreateOutcome.NameInUse)
		{
			// Another process created it a moment ago, or a stopped one was kept: start it and use it.
			await _docker.StartAsync(ContainerName, ct);
			return await FindByNameAsync(ct);
		}

		if (id is null) throw new InvalidOperationException("Docker created the NATS container without naming it.");
		await _docker.CopyFileAsync(id, NatsLocalContainer.ConfigPath, NatsLocalContainer.Config, ct);
		await _docker.StartAsync(id, ct);
		_ownedContainer = id;
		return await FindByNameAsync(ct);
	}

	private async Task<string> FindByNameAsync(CancellationToken ct)
	{
		var containers = await _docker.ListAsync(new() { ["name"] = [$"^/{ContainerName}$"] }, all: false, ct);
		var port = containers.Select(container => PublishedClientPort(container.Ports)).FirstOrDefault(p => p > 0);
		return port > 0
			? UrlFor(_docker.Endpoint, port)
			: throw new InvalidOperationException($"Container {ContainerName} is not running with NATS's port published.");
	}

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
			catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException
																	 or OperationCanceledException or InvalidOperationException)
			{
				// Shutting down anyway; a container left running is used by the next start.
			}
		}

		_docker.Dispose();
	}
}
