using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;
using SharpMUSH.Library.Webhooks;

namespace SharpMUSH.Server.Controllers;

/// <summary>
/// A webhook subscription's event stream: the same events its endpoint is posted, written to a WebSocket,
/// for a tool that has no public address to be posted to. The tool connects to
/// <c>api/webhooks/{name}/stream</c> with <c>Authorization: Bearer &lt;secret&gt;</c>; each text frame is
/// one event's body with its delivery id added as <c>id</c>. Nothing is kept for a stream that is not
/// connected, and a reader slower than the game loses its oldest events past
/// <see cref="WebhookOptions.StreamBuffer"/>.
/// </summary>
[ApiController]
[Route("api/webhooks")]
[AllowAnonymous]
public sealed class WebhookStreamController(IWebhookService webhooks, IOptions<WebhookOptions> options) : ControllerBase
{
	private static readonly TimeSpan CloseHandshakeWait = TimeSpan.FromSeconds(5);

	[HttpGet("{name}/stream")]
	public async Task Stream(string name, CancellationToken cancellationToken)
	{
		if (!HttpContext.WebSockets.IsWebSocketRequest)
		{
			Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		// One answer for an unknown name and a wrong secret, so the names cannot be found by asking.
		if (await webhooks.GetAsync(name) is not WebhookSubscription subscription || !Presents(subscription.Secret))
		{
			Response.StatusCode = StatusCodes.Status401Unauthorized;
			return;
		}

		var events = Channel.CreateBounded<WebhookMessage>(new BoundedChannelOptions(Math.Max(1, options.Value.StreamBuffer))
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true,
		});

		using var socket = await HttpContext.WebSockets.AcceptWebSocketAsync();
		using var lease = webhooks.OpenStream(subscription.Name, events.Writer);
		// A rotation or delete between the check above and OpenStream closed no writer of this stream's.
		if (await webhooks.GetAsync(name) is not WebhookSubscription current || current.Secret != subscription.Secret)
		{
			events.Writer.TryComplete();
		}

		using var closed = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var watching = WatchForCloseAsync(socket, closed);

		try
		{
			await foreach (var message in events.Reader.ReadAllAsync(closed.Token))
			{
				await socket.SendAsync(Frame(message), WebSocketMessageType.Text, true, closed.Token);
			}

			// The subscription was deleted or its secret rotated. Wait a moment for the tool's own close frame, so
			// it sees a clean close with this reason rather than a dropped connection.
			await socket.CloseOutputAsync(WebSocketCloseStatus.PolicyViolation, "webhook deleted or secret rotated", CancellationToken.None);
			await Task.WhenAny(watching, Task.Delay(CloseHandshakeWait, cancellationToken));
		}
		catch (OperationCanceledException)
		{
			// The tool closed the socket or the request was aborted: nothing is left to send.
		}
		catch (WebSocketException)
		{
			// The connection dropped mid-send: nothing is left to send, and nobody to tell.
		}

		await closed.CancelAsync();
		await watching;
		if (socket.State == WebSocketState.CloseReceived)
		{
			try
			{
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
			}
			catch (WebSocketException)
			{
				// The tool went away before the close handshake finished; the socket is gone either way.
			}
		}
	}

	private bool Presents(string secret)
		=> Request.Headers.Authorization.ToString() is var header
			&& header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
			&& WebhookSignature.SecretEquals(secret, header["Bearer ".Length..].Trim());

	/// <summary>The body with the delivery id first: <c>{"id":"msg_…","type":…}</c>.</summary>
	private static ReadOnlyMemory<byte> Frame(WebhookMessage message)
		=> Encoding.UTF8.GetBytes($"{{\"id\":\"{message.Id}\",{message.Body[1..]}");

	/// <summary>Reads (and ignores) what the tool sends, until it closes the socket.</summary>
	private static async Task WatchForCloseAsync(WebSocket socket, CancellationTokenSource closed)
	{
		var buffer = new byte[1024];
		try
		{
			while (!closed.IsCancellationRequested)
			{
				var result = await socket.ReceiveAsync(buffer, closed.Token);
				if (result.MessageType == WebSocketMessageType.Close) break;
			}
		}
		catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
		{
			// The stream ended or the connection dropped: either way the tool is no longer listening.
		}

		await closed.CancelAsync();
	}
}
