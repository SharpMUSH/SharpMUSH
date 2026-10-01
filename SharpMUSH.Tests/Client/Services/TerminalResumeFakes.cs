using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace SharpMUSH.Tests.Client.Services;

/// <summary>
/// Stands in for <c>js/terminal-resume.js</c> (whose own behaviour the node tests cover): a stored map
/// that <c>write</c> fills at once, a staged map that <c>stage</c> fills and <see cref="Flush"/> moves
/// across, and whether this page counts as reloaded.
/// </summary>
internal sealed class FakeResumeJs : IJSRuntime
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, string> _stored = new();
	private readonly Dictionary<string, string> _staged = new();

	public bool Reloaded { get; set; } = true;
	public int Writes { get; private set; }

	public void Seed(string key, string value)
	{
		lock (_gate) _stored[key] = value;
	}

	public string? StoredValue(string key)
	{
		lock (_gate) return _stored.GetValueOrDefault(key);
	}

	public string? StagedValue(string key)
	{
		lock (_gate) return _staged.GetValueOrDefault(key);
	}

	public void Flush()
	{
		lock (_gate)
		{
			foreach (var (key, value) in _staged)
			{
				_stored[key] = value;
				Writes++;
			}
			_staged.Clear();
		}
	}

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
	{
		lock (_gate)
		{
			var key = (string)args![0]!;
			object? result = null;
			switch (identifier)
			{
				case "SharpMUSH.Resume.resumable":
					result = Reloaded ? _stored.GetValueOrDefault(key) : null;
					break;
				case "SharpMUSH.Resume.write":
					_staged.Remove(key);
					_stored[key] = (string)args[1]!;
					Writes++;
					result = true;
					break;
				case "SharpMUSH.Resume.stage":
					_staged[key] = (string)args[1]!;
					break;
				case "SharpMUSH.Resume.remove":
					_staged.Remove(key);
					_stored.Remove(key);
					break;
				case "SharpMUSH.Resume.removeAll":
					foreach (var k in _stored.Keys.Where(k => k.StartsWith(key, StringComparison.Ordinal)).ToList()) _stored.Remove(k);
					foreach (var k in _staged.Keys.Where(k => k.StartsWith(key, StringComparison.Ordinal)).ToList()) _staged.Remove(k);
					break;
				default:
					throw new InvalidOperationException($"Unexpected JS call {identifier}");
			}
			return new ValueTask<TValue>((TValue)result!);
		}
	}

	public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
		=> InvokeAsync<TValue>(identifier, args);
}

/// <summary>
/// A websocket endpoint that answers a terminal's first frame with a scripted reply, then records
/// everything else the client sends until it closes. Stands in for the connection server's
/// <c>ConnectionPump</c>, whose own answers to hello and resume the pump tests cover.
/// </summary>
internal sealed class ScriptedTerminalServer : IAsyncDisposable
{
	private readonly WebApplication _app;

	private ScriptedTerminalServer(WebApplication app, string uri)
	{
		_app = app;
		Uri = uri;
	}

	public string Uri { get; }
	public ConcurrentQueue<string> FirstFrames { get; } = new();
	public ConcurrentQueue<string> LaterFrames { get; } = new();

	/// <summary>
	/// The first frame the client sent. A hello connect returns before the server has read it, so wait.
	/// </summary>
	public async Task<string> FirstFrameAsync()
	{
		await Eventually.TrueAsync(() => !FirstFrames.IsEmpty);
		return FirstFrames.TryPeek(out var first)
			? first
			: throw new TimeoutException("The client sent no first frame");
	}

	public static async Task<ScriptedTerminalServer> StartAsync(Func<string, IReadOnlyList<string>> reply)
	{
		var builder = WebApplication.CreateSlimBuilder();
		builder.Logging.ClearProviders();
		builder.WebHost.UseUrls("http://127.0.0.1:0");
		var app = builder.Build();
		app.UseWebSockets();

		ScriptedTerminalServer? server = null;
		app.Map("/ws", async context =>
		{
			if (!context.WebSockets.IsWebSocketRequest)
			{
				context.Response.StatusCode = StatusCodes.Status400BadRequest;
				return;
			}

			using var socket = await context.WebSockets.AcceptWebSocketAsync();
			var first = await ReceiveAsync(socket, context.RequestAborted);
			if (first is null) return;
			server!.FirstFrames.Enqueue(first);
			foreach (var frame in reply(first))
				await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, context.RequestAborted);

			while (await ReceiveAsync(socket, context.RequestAborted) is { } later)
				server.LaterFrames.Enqueue(later);
			if (socket.State == WebSocketState.CloseReceived)
				await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
		});

		await app.StartAsync();
		var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
			?? throw new InvalidOperationException("Kestrel reported no addresses");
		var address = addresses.Addresses.Single();
		server = new ScriptedTerminalServer(app, address.Replace("http://", "ws://") + "/ws");
		return server;
	}

	private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken ct)
	{
		var buffer = new byte[4096];
		using var message = new MemoryStream();
		try
		{
			WebSocketReceiveResult result;
			do
			{
				result = await socket.ReceiveAsync(buffer, ct);
				if (result.MessageType == WebSocketMessageType.Close) return null;
				message.Write(buffer, 0, result.Count);
			}
			while (!result.EndOfMessage);
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
		{
			return null;
		}
		return Encoding.UTF8.GetString(message.ToArray());
	}

	public async ValueTask DisposeAsync()
	{
		// A test that failed before disposing its client leaves a socket open; do not wait out
		// Kestrel's 30 s graceful shutdown for it.
		using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(1));
		await _app.StopAsync(deadline.Token);
		await _app.DisposeAsync();
	}
}

internal static class Eventually
{
	public static async Task<bool> TrueAsync(Func<bool> condition, int timeoutMs = 5000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (DateTime.UtcNow < deadline)
		{
			if (condition()) return true;
			await Task.Delay(10);
		}
		return condition();
	}
}
