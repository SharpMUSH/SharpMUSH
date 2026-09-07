using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using SharpMUSH.ConnectionServer.Models;

namespace SharpMUSH.ConnectionServer.Services;

/// <summary>Retries pure rendering across worker restarts; socket writes remain in this process.</summary>
public sealed class RemoteOutputRenderer : IMarkupOutputRenderer, IOutputTransformService, IDisposable
{
	private readonly HttpClient _client;
	private readonly SemaphoreSlim _concurrency = new(16);
	private readonly CancellationToken _stopping;
	private readonly ILogger<RemoteOutputRenderer> _logger;

	public RemoteOutputRenderer(IConfiguration configuration, IHostApplicationLifetime lifetime,
		ILogger<RemoteOutputRenderer> logger)
	{
		_logger = logger;
		_stopping = lifetime.ApplicationStopping;
		var socketPath = configuration["Rendering:SocketPath"] ?? "/run/sharpmush/render.sock";
		_client = new HttpClient(new SocketsHttpHandler
		{
			ConnectCallback = async (_, ct) =>
			{
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				try
				{
					await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
					return new NetworkStream(socket, ownsSocket: true);
				}
				catch
				{
					socket.Dispose();
					throw;
				}
			},
			EnableMultipleHttp2Connections = false
		})
		{
			BaseAddress = new Uri("http://renderer"),
			DefaultRequestVersion = HttpVersion.Version20,
			DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact,
			Timeout = Timeout.InfiniteTimeSpan
		};
	}

	public RenderedOutput Render(string markup, ConnectionServerService.ConnectionData connection) =>
		RenderAsync(markup, connection).AsTask().GetAwaiter().GetResult();

	public async ValueTask<RenderedOutput> RenderAsync(string markup,
		ConnectionServerService.ConnectionData connection, CancellationToken ct = default) =>
		new(await SendAsync(new RenderRequest(markup, null,
			new RenderContext(connection.ConnectionType, connection.Capabilities, connection.Preferences)), ct), false);

	public ValueTask<byte[]> TransformAsync(byte[] rawOutput, ProtocolCapabilities capabilities,
		PlayerOutputPreferences? preferences) =>
		SendAsync(new RenderRequest(null, rawOutput, new RenderContext("telnet", capabilities, preferences)), default);

	public byte[] Transform(byte[] rawOutput, ProtocolCapabilities capabilities, PlayerOutputPreferences? preferences) =>
		TransformAsync(rawOutput, capabilities, preferences).AsTask().GetAwaiter().GetResult();

	private async ValueTask<byte[]> SendAsync(RenderRequest payload, CancellationToken ct)
	{
		using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, _stopping);
		await _concurrency.WaitAsync(lifetime.Token);
		try
		{
			var recovering = false;
			while (true)
			{
				using var attempt = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
				attempt.CancelAfter(TimeSpan.FromSeconds(10));
				try
				{
					using var response = await _client.PostAsJsonAsync("/render", payload, attempt.Token);
					response.EnsureSuccessStatusCode();
					var bytes = await response.Content.ReadAsByteArrayAsync(attempt.Token);
					if (recovering) _logger.LogInformation("Rendering worker recovered; resuming pending output");
					return bytes;
				}
				catch (HttpRequestException ex) when (ex.StatusCode is null or HttpStatusCode.ServiceUnavailable or HttpStatusCode.BadGateway)
				{
					if (!recovering) _logger.LogWarning(ex, "Rendering worker unavailable; retaining sockets and pending output");
				}
				catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
				{
					if (!recovering) _logger.LogWarning("Rendering worker timed out; retaining sockets and pending output");
				}
				recovering = true;
				await Task.Delay(TimeSpan.FromMilliseconds(250), lifetime.Token);
			}
		}
		finally
		{
			_concurrency.Release();
		}
	}

	public void Dispose()
	{
		_client.Dispose();
		_concurrency.Dispose();
	}
}
