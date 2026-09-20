using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net.Sockets;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.RenderingWorker;

public static class Program
{
	public static async Task Main(string[] args)
	{
		if (args.Contains("--healthcheck"))
		{
			var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
			Environment.ExitCode = await IsHealthyAsync(configuration["Rendering:SocketPath"] ?? "/run/sharpmush/render.sock") ? 0 : 1;
			return;
		}
		await using var app = CreateApplication(args);
		await app.RunAsync();
	}

	public static WebApplication CreateApplication(string[] args, string? socketPath = null)
	{
		if (!MarkupRegistry.IsConfigured)
			MarkupRegistry.Default = MarkupRegistry.Empty.WithAnsi().WithHtml();

		var builder = WebApplication.CreateBuilder(args);
		builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
		socketPath ??= builder.Configuration["Rendering:SocketPath"] ?? "/run/sharpmush/render.sock";
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(socketPath))!);
		RemoveStaleSocket(socketPath);
		builder.WebHost.ConfigureKestrel(options =>
		{
			options.Limits.MaxRequestBodySize = 16 * 1024 * 1024;
			options.ListenUnixSocket(socketPath, listen => listen.Protocols = HttpProtocols.Http2);
		});
		builder.Services.AddSingleton<MarkupOutputRenderer>();
		builder.Services.AddSingleton<OutputTransformService>();
		var app = builder.Build();
		app.MapGet("/health", () => Results.Ok());
		app.MapPost("/render", (RenderRequest request, MarkupOutputRenderer renderer, OutputTransformService transform) =>
		{
			if (request.Context is null || request.Context.Capabilities is null ||
				(request.Markup is null) == (request.Data is null))
				return Results.BadRequest();
			var rendered = request.Markup is not null
				? renderer.Render(request.Markup, request.Context)
				: new RenderedOutput(request.Data!, true);
			var bytes = rendered.ApplyOutputTransform
				? transform.Transform(rendered.Data, request.Context.Capabilities, request.Context.Preferences)
				: rendered.Data;
			return Results.Bytes(bytes, "application/octet-stream");
		});
		return app;
	}

	public static async Task<bool> IsHealthyAsync(string socketPath)
	{
		using var handler = new SocketsHttpHandler
		{
			ConnectCallback = async (_, ct) =>
			{
				var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
				try
				{
					await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), ct);
					return new NetworkStream(socket, ownsSocket: true);
				}
				catch { socket.Dispose(); throw; }
			}
		};
		using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
		using var request = new HttpRequestMessage(HttpMethod.Get, "http://renderer/health")
		{
			Version = System.Net.HttpVersion.Version20,
			VersionPolicy = HttpVersionPolicy.RequestVersionExact
		};
		try
		{
			using var response = await client.SendAsync(request);
			return response.IsSuccessStatusCode;
		}
		catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
		{
			return false;
		}
	}

	private static void RemoveStaleSocket(string path)
	{
		if (!File.Exists(path)) return;
		using var probe = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
		try
		{
			probe.Connect(new UnixDomainSocketEndPoint(path));
		}
		catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionRefused or SocketError.AddressNotAvailable)
		{
			// SIGKILL leaves the filesystem entry behind, although no process owns the listener.
			File.Delete(path);
			return;
		}
		throw new IOException($"A rendering worker already owns {path}.");
	}
}
