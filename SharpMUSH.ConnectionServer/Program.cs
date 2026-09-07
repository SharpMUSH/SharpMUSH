using Microsoft.AspNetCore.Server.Kestrel.Core;
using System.Net.Sockets;
using SharpMUSH.ConnectionServer.Services;

namespace SharpMUSH.RenderingWorker;

public static class Program
{
	public static async Task Main(string[] args)
	{
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
