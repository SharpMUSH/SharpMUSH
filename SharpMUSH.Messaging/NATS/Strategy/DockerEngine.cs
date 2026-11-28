using System.Formats.Tar;
using System.IO.Pipes;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharpMUSH.Messaging.NATS.Strategy;

/// <summary>
/// The few calls of Docker's Engine API that <see cref="NatsDockerStrategy"/> makes, with source-generated JSON
/// so that a native binary can make them. It reaches the engine the way the Docker CLI does: <c>DOCKER_HOST</c>
/// (<c>unix://</c>, <c>npipe://</c> or plain <c>tcp://</c>), else the platform's default socket.
/// </summary>
internal sealed class DockerEngine : IDisposable
{
	private readonly HttpClient _http;

	private DockerEngine(DockerEndpoint endpoint)
	{
		Endpoint = endpoint;
		var handler = new SocketsHttpHandler();
		var baseAddress = new Uri("http://localhost/");
		switch (endpoint.Kind)
		{
			case DockerEndpointKind.Unix:
				handler.ConnectCallback = async (_, ct) =>
				{
					var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
					try
					{
						await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.Address), ct);
						return new NetworkStream(socket, ownsSocket: true);
					}
					catch
					{
						socket.Dispose();
						throw;
					}
				};
				break;
			case DockerEndpointKind.NamedPipe:
				handler.ConnectCallback = async (_, ct) =>
				{
					var pipe = new NamedPipeClientStream(".", endpoint.Address, PipeDirection.InOut, PipeOptions.Asynchronous);
					try
					{
						await pipe.ConnectAsync(ct);
						return pipe;
					}
					catch
					{
						await pipe.DisposeAsync();
						throw;
					}
				};
				break;
			case DockerEndpointKind.Tcp:
				baseAddress = new Uri($"http://{endpoint.Address}/");
				break;
		}

		_http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = Timeout.InfiniteTimeSpan };
	}

	public DockerEndpoint Endpoint { get; }

	public static DockerEngine FromEnvironment() => new(DockerEndpoint.FromEnvironment());

	/// <summary>Containers matching Docker's <c>filters</c> query, running ones only unless <paramref name="all"/>.</summary>
	public async Task<IReadOnlyList<DockerContainerSummary>> ListAsync(
		Dictionary<string, string[]> filters, bool all, CancellationToken ct)
	{
		var query = Uri.EscapeDataString(JsonSerializer.Serialize(filters, DockerJsonContext.Default.DictionaryStringStringArray));
		using var response = await _http.GetAsync($"containers/json?all={(all ? "true" : "false")}&filters={query}", ct);
		await EnsureSuccessAsync(response, ct);
		return await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.DockerContainerSummaryArray, ct) ?? [];
	}

	/// <summary>Creates a container; <see cref="DockerCreateOutcome.NoImage"/> or <see cref="DockerCreateOutcome.NameInUse"/> without one.</summary>
	public async Task<(DockerCreateOutcome Outcome, string? Id)> CreateAsync(
		string name, DockerContainerCreate body, CancellationToken ct)
	{
		using var content = JsonContent.Create(body, DockerJsonContext.Default.DockerContainerCreate);
		using var response = await _http.PostAsync($"containers/create?name={Uri.EscapeDataString(name)}", content, ct);
		switch (response.StatusCode)
		{
			case HttpStatusCode.NotFound:
				return (DockerCreateOutcome.NoImage, null);
			case HttpStatusCode.Conflict:
				return (DockerCreateOutcome.NameInUse, null);
		}

		await EnsureSuccessAsync(response, ct);
		var created = await response.Content.ReadFromJsonAsync(DockerJsonContext.Default.DockerCreated, ct);
		return (DockerCreateOutcome.Created, created?.Id);
	}

	/// <summary>Pulls an image, reading the progress stream to its end and failing on an error line in it.</summary>
	public async Task PullAsync(string image, string tag, CancellationToken ct)
	{
		using var response = await _http.PostAsync(
			$"images/create?fromImage={Uri.EscapeDataString(image)}&tag={Uri.EscapeDataString(tag)}", null, ct);
		await EnsureSuccessAsync(response, ct);
		using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
		while (await reader.ReadLineAsync(ct) is { } line)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;
			var progress = JsonSerializer.Deserialize(line, DockerJsonContext.Default.DockerProgress);
			if (!string.IsNullOrEmpty(progress?.Error))
				throw new InvalidOperationException($"Docker could not pull {image}:{tag}: {progress.Error}");
		}
	}

	/// <summary>Writes one file into a container that is not yet started.</summary>
	public async Task CopyFileAsync(string container, string path, byte[] contents, CancellationToken ct)
	{
		using var tar = new MemoryStream();
		await using (var writer = new TarWriter(tar, TarEntryFormat.Pax, leaveOpen: true))
		{
			var entry = new PaxTarEntry(TarEntryType.RegularFile, Path.GetFileName(path))
			{
				DataStream = new MemoryStream(contents),
				Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead
			};
			await writer.WriteEntryAsync(entry, ct);
		}

		tar.Position = 0;
		var directory = Path.GetDirectoryName(path)!.Replace('\\', '/');
		using var content = new StreamContent(tar);
		content.Headers.ContentType = new("application/x-tar");
		using var response = await _http.PutAsync(
			$"containers/{Uri.EscapeDataString(container)}/archive?path={Uri.EscapeDataString(directory)}", content, ct);
		await EnsureSuccessAsync(response, ct);
	}

	/// <summary>Starts a container; one already running, or gone meanwhile, is left as it is.</summary>
	public async Task StartAsync(string container, CancellationToken ct)
	{
		using var response = await _http.PostAsync($"containers/{Uri.EscapeDataString(container)}/start", null, ct);
		if (response.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound) return;
		await EnsureSuccessAsync(response, ct);
	}

	/// <summary>Removes a container that is not running; one already gone is left as it is.</summary>
	public async Task RemoveAsync(string container, CancellationToken ct)
	{
		using var response = await _http.DeleteAsync($"containers/{Uri.EscapeDataString(container)}", ct);
		if (response.StatusCode == HttpStatusCode.NotFound) return;
		await EnsureSuccessAsync(response, ct);
	}

	/// <summary>Stops a container; one already stopped or gone is left as it is.</summary>
	public async Task StopAsync(string container, CancellationToken ct)
	{
		using var response = await _http.PostAsync($"containers/{Uri.EscapeDataString(container)}/stop", null, ct);
		if (response.StatusCode is HttpStatusCode.NotModified or HttpStatusCode.NotFound) return;
		await EnsureSuccessAsync(response, ct);
	}

	private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
	{
		if (response.IsSuccessStatusCode) return;
		var body = await response.Content.ReadAsStringAsync(ct);
		throw new InvalidOperationException(
			$"Docker answered {(int)response.StatusCode} to {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: {body.Trim()}");
	}

	public void Dispose() => _http.Dispose();
}

internal enum DockerCreateOutcome
{
	Created,
	NoImage,
	NameInUse
}

internal enum DockerEndpointKind
{
	Unix,
	NamedPipe,
	Tcp
}

/// <summary>Where Docker's engine listens, and the host name that reaches the ports its containers publish.</summary>
internal sealed record DockerEndpoint(DockerEndpointKind Kind, string Address, string PublishedHost)
{
	/// <summary>
	/// Reads <c>DOCKER_HOST</c>; <c>TESTCONTAINERS_HOST_OVERRIDE</c>, which Testcontainers honours too, names the host
	/// that reaches published ports when it is not the engine's.
	/// </summary>
	public static DockerEndpoint FromEnvironment()
	{
		var endpoint = Parse(Environment.GetEnvironmentVariable("DOCKER_HOST"),
			Environment.GetEnvironmentVariable("DOCKER_TLS_VERIFY"), OperatingSystem.IsWindows(), DefaultUnixSockets(), File.Exists);
		var hostOverride = Environment.GetEnvironmentVariable("TESTCONTAINERS_HOST_OVERRIDE");
		return string.IsNullOrWhiteSpace(hostOverride) ? endpoint : endpoint with { PublishedHost = hostOverride };
	}

	/// <summary>Reads <c>DOCKER_HOST</c>; unset, the first of <paramref name="unixSockets"/> that exists (Windows: Docker Desktop's pipe).</summary>
	public static DockerEndpoint Parse(string? dockerHost, string? tlsVerify, bool windows,
		IReadOnlyList<string> unixSockets, Func<string, bool> exists)
	{
		if (string.IsNullOrWhiteSpace(dockerHost))
		{
			if (windows) return new(DockerEndpointKind.NamedPipe, "docker_engine", "localhost");
			var socket = unixSockets.FirstOrDefault(exists) ?? unixSockets[0];
			return new(DockerEndpointKind.Unix, socket, "localhost");
		}

		if (!Uri.TryCreate(dockerHost, UriKind.Absolute, out var uri))
			throw new InvalidOperationException($"DOCKER_HOST '{dockerHost}' is not a URL.");

		return uri.Scheme switch
		{
			"unix" => new(DockerEndpointKind.Unix, uri.AbsolutePath, "localhost"),
			"npipe" => new(DockerEndpointKind.NamedPipe, PipeName(uri.AbsolutePath), "localhost"),
			"tcp" when !string.IsNullOrEmpty(tlsVerify) => throw new InvalidOperationException(
				$"DOCKER_HOST '{dockerHost}' uses TLS, which this binary cannot speak; set NATS_URL to a running NATS server."),
			"tcp" => new(DockerEndpointKind.Tcp, uri.Authority, uri.Host),
			_ => throw new InvalidOperationException($"DOCKER_HOST '{dockerHost}' has a scheme this binary cannot use.")
		};
	}

	// npipe:////./pipe/docker_engine → docker_engine
	private static string PipeName(string path)
	{
		var marker = path.IndexOf("/pipe/", StringComparison.OrdinalIgnoreCase);
		return marker < 0 ? path.TrimStart('/') : path[(marker + "/pipe/".Length)..];
	}

	private static string[] DefaultUnixSockets()
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
		string?[] candidates =
		[
			"/var/run/docker.sock",
			string.IsNullOrEmpty(home) ? null : Path.Join(home, ".docker", "run", "docker.sock"),
			string.IsNullOrEmpty(runtime) ? null : Path.Join(runtime, "docker.sock")
		];
		return candidates.OfType<string>().ToArray();
	}

	public override string ToString() => Kind switch
	{
		DockerEndpointKind.Unix => $"unix://{Address}",
		DockerEndpointKind.NamedPipe => $"npipe:////./pipe/{Address}",
		_ => $"tcp://{Address}"
	};
}

internal sealed record DockerContainerSummary(string Id, string? State, DockerPort[]? Ports, Dictionary<string, string>? Labels);

internal sealed record DockerPort(string? IP, int PrivatePort, int PublicPort, string? Type);

internal sealed record DockerContainerCreate(
	string Image,
	string[] Cmd,
	Dictionary<string, string> Labels,
	Dictionary<string, DockerEmpty> ExposedPorts,
	DockerHostConfig HostConfig);

internal sealed record DockerHostConfig(Dictionary<string, DockerPortBinding[]> PortBindings, bool AutoRemove);

internal sealed record DockerPortBinding(string HostIp, string HostPort);

internal sealed record DockerEmpty;

internal sealed record DockerCreated(string Id);

internal sealed record DockerProgress(string? Error);

[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(DockerContainerSummary[]))]
[JsonSerializable(typeof(DockerContainerCreate))]
[JsonSerializable(typeof(DockerCreated))]
[JsonSerializable(typeof(DockerProgress))]
internal sealed partial class DockerJsonContext : JsonSerializerContext;
