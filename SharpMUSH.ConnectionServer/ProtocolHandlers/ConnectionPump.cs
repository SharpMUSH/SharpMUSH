using System.Text;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Owns the shared connection lifecycle: register the handle with its output delegate,
/// pump inbound frames (routing browser control frames vs commands), and disconnect on close.
/// Transport-agnostic — used by both WebSocket and WebTransport handlers, so QUIC connection
/// migration underneath a WebTransport session is invisible to this loop.
///
/// When <see cref="TerminalTransportOptions.SequencedOutput"/> is on, outbound frames are wrapped
/// with a per-handle sequence (for client ack) and buffered for replay, and an initial resume
/// frame from the client triggers replay of missed output after a fresh reconnect. With it off,
/// behavior is identical to the plain WebSocket path.
/// </summary>
public sealed class ConnectionPump(
	ILogger<ConnectionPump> logger,
	IConnectionServerService connectionService,
	IMessageBus publishEndpoint,
	IDescriptorGeneratorService descriptorGenerator,
	ITerminalReplayStore replayStore,
	IResumeTokenStore resumeTokens,
	TerminalTransportOptions options)
{
	/// <param name="resumeCapable">
	/// Whether this specific connection opted into sequenced output + replay (e.g. via a
	/// <c>?resume=1</c> query). Sequencing only activates when the server-wide flag AND the
	/// per-connection opt-in are both set, so connections that don't understand seq envelopes
	/// (the command terminal, legacy clients) keep receiving raw output.
	/// </param>
	public async Task RunAsync(IDuplexTransport transport, long handle, CancellationToken ct, bool resumeCapable = false)
	{
		var sequenced = options.SequencedOutput && resumeCapable;

		Func<byte[], ValueTask> output = sequenced
			? async data =>
			{
				var (_, wrapped) = await replayStore.AppendAsync(handle, data, ct);
				await transport.SendAsync(wrapped, ct);
			}
			: data => new ValueTask(transport.SendAsync(data, ct));

		await connectionService.RegisterAsync(
			handle,
			transport.RemoteIp,
			transport.Hostname,
			transport.Kind,
			output,
			output,
			() => Encoding.UTF8,
			() => _ = transport.CloseAsync());

		if (sequenced)
		{
			// Hand the client a resume token (raw control frame) so it can request replay on reconnect.
			var token = await resumeTokens.MintAsync(handle, ct);
			await transport.SendAsync(Encoding.UTF8.GetBytes($"{{\"resumeToken\":\"{token}\"}}"), ct);
		}

		try
		{
			var first = true;
			while (!ct.IsCancellationRequested)
			{
				var message = await transport.ReceiveTextAsync(ct);
				if (message is null) break; // peer closed
				if (message.Length == 0) continue;

				if (first && sequenced && SeqEnvelope.TryReadResume(message, out var resumeToken, out var lastSeq))
				{
					first = false;
					await HandleResumeAsync(transport, resumeToken, lastSeq, ct);
					continue; // resume frame is not a command
				}

				first = false;

				// Browser-sent JSON control frames are handled here and NOT forwarded as commands.
				// NAWS reuses the same NAWSUpdateMessage path telnet uses (Height=rows, Width=cols).
				if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
					await publishEndpoint.Publish(new NAWSUpdateMessage(handle, rows, cols), ct);
				else
					await publishEndpoint.Publish(new WebSocketInputMessage(handle, message), ct);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown / RequestAborted.
		}
		catch (Exception ex)
		{
			logger.LogError(ex, "Error pumping {Kind} connection {Handle}", transport.Kind, handle);
		}
		finally
		{
			await connectionService.DisconnectAsync(handle);
			descriptorGenerator.ReleaseWebSocketDescriptor(handle);
		}
	}

	private async Task HandleResumeAsync(IDuplexTransport transport, string resumeToken, long lastSeq, CancellationToken ct)
	{
		var (found, oldHandle) = await resumeTokens.TryResolveAsync(resumeToken, ct);
		if (!found)
		{
			logger.LogInformation("Resume token expired or unknown; continuing as fresh connection");
			return;
		}

		var missed = await replayStore.AfterAsync(oldHandle, lastSeq, ct);
		foreach (var frame in missed)
			await transport.SendAsync(frame, ct);

		await replayStore.DropAsync(oldHandle, ct);
		await resumeTokens.InvalidateAsync(resumeToken, ct);
		logger.LogInformation("Replayed {Count} frame(s) after resume of handle {OldHandle}", missed.Count, oldHandle);
	}
}
