using System.Text.Json;
using SharpMUSH.Messaging.NATS.Strategy;

namespace SharpMUSH.Tests.Messaging;

/// <summary>
/// Without <c>NATS_URL</c>, the socket server starts NATS through Docker's API (<see cref="NatsDockerStrategy"/>):
/// where it finds the engine, and which port it reads off a container.
/// </summary>
public class NatsDockerStrategyTests
{
	private static readonly string[] Sockets = ["/var/run/docker.sock", "/home/a/.docker/run/docker.sock"];

	[Test]
	public async Task UnsetUsesTheFirstSocketThatExists()
	{
		var endpoint = DockerEndpoint.Parse(null, null, windows: false, Sockets, path => path == Sockets[1]);

		await Assert.That(endpoint).IsEqualTo(new DockerEndpoint(DockerEndpointKind.Unix, Sockets[1], "localhost"));
	}

	[Test]
	public async Task UnsetWithNoSocketUsesTheDefault()
	{
		var endpoint = DockerEndpoint.Parse(" ", null, windows: false, Sockets, _ => false);

		await Assert.That(endpoint).IsEqualTo(new DockerEndpoint(DockerEndpointKind.Unix, Sockets[0], "localhost"));
	}

	[Test]
	public async Task UnsetOnWindowsUsesDockerDesktopsPipe()
	{
		var endpoint = DockerEndpoint.Parse(null, null, windows: true, Sockets, _ => true);

		await Assert.That(endpoint).IsEqualTo(new DockerEndpoint(DockerEndpointKind.NamedPipe, "docker_engine", "localhost"));
	}

	[Test]
	[Arguments("unix:///run/user/1000/podman/podman.sock", "Unix", "/run/user/1000/podman/podman.sock", "localhost")]
	[Arguments("npipe:////./pipe/docker_engine", "NamedPipe", "docker_engine", "localhost")]
	[Arguments("tcp://docker.internal:2375", "Tcp", "docker.internal:2375", "docker.internal")]
	public async Task DockerHostNamesTheEngine(string dockerHost, string kind, string address, string publishedHost)
	{
		var endpoint = DockerEndpoint.Parse(dockerHost, null, windows: false, Sockets, _ => false);

		await Assert.That(endpoint).IsEqualTo(new DockerEndpoint(Enum.Parse<DockerEndpointKind>(kind), address, publishedHost));
	}

	[Test]
	[Arguments("tcp://docker.internal:2376", "1")]
	[Arguments("ssh://user@docker.internal", null)]
	[Arguments("not a url", null)]
	public async Task AnEngineItCannotReachIsRefused(string dockerHost, string? tlsVerify) =>
		await Assert.That(() => DockerEndpoint.Parse(dockerHost, tlsVerify, windows: false, Sockets, _ => false))
			.Throws<InvalidOperationException>();

	[Test]
	public async Task ReadsThePublishedClientPortOffDockersListing()
	{
		const string listing = """
			[{"Id":"abc","State":"running","Labels":{"sharpmush.nats-revision":"0123456789ab"},"Ports":[
				{"IP":"0.0.0.0","PrivatePort":6222,"PublicPort":32769,"Type":"tcp"},
				{"PrivatePort":8222,"Type":"tcp"},
				{"IP":"0.0.0.0","PrivatePort":4222,"PublicPort":32768,"Type":"tcp"},
				{"IP":"::","PrivatePort":4222,"PublicPort":32768,"Type":"tcp"}]}]
			""";
		var containers = JsonSerializer.Deserialize(listing, DockerJsonContext.Default.DockerContainerSummaryArray)!;

		await Assert.That(NatsDockerStrategy.PublishedClientPort(containers[0].Ports)).IsEqualTo(32768);
		await Assert.That(containers[0].Labels!["sharpmush.nats-revision"]).IsEqualTo("0123456789ab");
	}

	[Test]
	public async Task AnUnpublishedClientPortIsNone() =>
		await Assert.That(NatsDockerStrategy.PublishedClientPort([new DockerPort(null, 4222, 0, "tcp")])).IsEqualTo(0);

	[Test]
	public async Task TheUrlUsesTheHostThatReachesPublishedPorts() =>
		await Assert.That(NatsDockerStrategy.UrlFor(new DockerEndpoint(DockerEndpointKind.Tcp, "docker.internal:2375", "docker.internal"), 32768))
			.IsEqualTo("nats://docker.internal:32768");

	[Test]
	public async Task TheCreateRequestIsWhatDockerReads()
	{
		var body = new DockerContainerCreate("nats:2.14-alpine", ["-c", "/etc/nats/nats.conf"],
			new() { ["reuse-id"] = "SharpMUSH-NATS" }, new() { ["4222/tcp"] = new DockerEmpty() },
			new DockerHostConfig(new() { ["4222/tcp"] = [new DockerPortBinding("127.0.0.1", "")] }, AutoRemove: true));

		await Assert.That(JsonSerializer.Serialize(body, DockerJsonContext.Default.DockerContainerCreate)).IsEqualTo(
			"""{"Image":"nats:2.14-alpine","Cmd":["-c","/etc/nats/nats.conf"],"Labels":{"reuse-id":"SharpMUSH-NATS"},"ExposedPorts":{"4222/tcp":{}},"HostConfig":{"PortBindings":{"4222/tcp":[{"HostIp":"127.0.0.1","HostPort":""}]},"AutoRemove":true}}""");
	}
}
