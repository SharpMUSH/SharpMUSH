using System.Text;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;

namespace SharpMUSH.ConnectionServer.ProtocolHandlers;

/// <summary>
/// Owns the shared connection lifecycle. Output is sequence-wrapped and buffered for replay, routed
/// through a per-handle <see cref="SessionSink"/> so it can be rebound to a new socket on reconnect.
/// On drop the session is DETACHED (held for a grace window) instead of disconnected, so a quick
/// reconnect rebinds to the same handle and the engine never logs the character out.
/// </summary>
public sealed class ConnectionPump(
	ILogger<ConnectionPump> logger,
	IConnectionServerService connectionService,
	IMessageBus publishEndpoint,
	IDescriptorGeneratorService descriptorGenerator,
	ITerminalReplayStore replayStore,
	IResumeTokenStore resumeTokens,
	SessionSinkRegistry sinkRegistry,
	DetachedSessionTracker detachedTracker,
	TimeSpan grace)
{
	public async Task RunAsync(IDuplexTransport transport, long candidateHandle, CancellationToken ct)
	{
		long handle;

		var firstFrame = await transport.ReceiveTextAsync(ct);
		if (firstFrame is null)
		{
			descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			return; // peer closed before saying anything
		}

		if (SeqEnvelope.TryReadResume(firstFrame, out var token, out var lastSeq)
			&& await TryRebindAsync(transport, token, lastSeq, ct) is { } rebound)
		{
			descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			handle = rebound;
		}
		else
		{
			handle = candidateHandle;
			await RegisterFreshAsync(transport, handle, ct);

			if (SeqEnvelope.TryReadResume(firstFrame, out var deadToken, out var deadLastSeq))
			{
				// resume-to-dead: still replay the old session's durable buffer, then continue fresh. Keyed
				// by the token's session id, so it never picks up a later occupant of the same (reused) handle.
				var (found, _, oldSession) = await resumeTokens.TryResolveAsync(deadToken, ct);
				if (found)
					foreach (var f in await replayStore.AfterAsync(oldSession, deadLastSeq, ct))
						await transport.SendAsync(f, ct);
			}
			else if (!IsHello(firstFrame))
			{
				// Not hello and not resume — a real command arrived first; don't drop it.
				await PublishInputAsync(handle, firstFrame, ct);
			}
		}

		try
		{
			while (!ct.IsCancellationRequested)
			{
				var message = await transport.ReceiveTextAsync(ct);
				if (message is null) break;
				if (message.Length == 0) continue;
				await PublishInputAsync(handle, message, ct);
			}
		}
		catch (OperationCanceledException)
		{
			// Expected on shutdown / RequestAborted.
		}
		catch (Exception ex)
		{
			// Intentional per-connection boundary: an unexpected error on one connection is logged and
			// the session detached (finally), never allowed to escape and disrupt the server.
			logger.LogError(ex, "Error pumping connection {Handle}", handle);
		}
		finally
		{
			// Detach (hold the session) instead of disconnecting; the grace timer does the real
			// disconnect if the client does not come back.
			sinkRegistry.Get(handle)?.Detach();
			detachedTracker.Detach(handle, async () =>
			{
				await connectionService.DisconnectAsync(handle);
				descriptorGenerator.ReleaseWebSocketDescriptor(handle);
				sinkRegistry.Remove(handle);
			}, grace);
		}
	}

	/// <summary>Rebind to a live handle; returns the handle, or null to fall back to the fresh path.</summary>
	private async Task<long?> TryRebindAsync(IDuplexTransport transport, string token, long lastSeq, CancellationToken ct)
	{
		var (found, oldHandle, oldSession) = await resumeTokens.TryResolveAsync(token, ct);
		if (!found) return null;

		var sink = sinkRegistry.Get(oldHandle);
		if (sink is null || connectionService.Get(oldHandle) is null)
			return null; // session no longer alive → fresh path (will still durably replay)

		detachedTracker.Reattach(oldHandle);           // cancel any grace timer
		var previous = sink.Current;
		sink.Attach(transport);
		if (previous is not null)
			await previous.CloseAsync();               // connection-steal: last write wins

		await transport.SendAsync(SeqEnvelope.Reattached(), ct);
		foreach (var f in await replayStore.AfterAsync(oldSession, lastSeq, ct))
			await transport.SendAsync(f, ct);

		// Rotate the resume token: the one just used is now spent (single-use / forward-secure), and the
		// client needs a fresh one so a *subsequent* drop can resume too. Same incarnation → same session id.
		await resumeTokens.InvalidateAsync(token, ct);
		var newToken = await resumeTokens.MintAsync(oldHandle, oldSession, ct);
		await transport.SendAsync(SeqEnvelope.ResumeToken(newToken), ct);

		logger.LogInformation("Reattached to session {Handle}", oldHandle);
		return oldHandle;
	}

	private async Task RegisterFreshAsync(IDuplexTransport transport, long handle, CancellationToken ct)
	{
		// A fresh incarnation gets a unique session id. Replay is keyed by it (never by the reusable
		// handle), so a recycled handle's new occupant can never read this session's buffer, and vice
		// versa — closing the cross-session replay leak at its root rather than relying on purge timing.
		var session = Guid.NewGuid().ToString("N");

		var sink = sinkRegistry.GetOrCreate(handle);
		sink.Attach(transport);

		// The output path outlives this socket: after a drop the session is DETACHED and engine output
		// must keep buffering to the replay store (and flow to a reattached transport) during the grace
		// window. So it must NOT capture the per-connection token — that is RequestAborted, which cancels
		// on drop and would abort buffering exactly when replay matters most.
		Func<byte[], ValueTask> output = async data =>
		{
			var (_, wrapped) = await replayStore.AppendAsync(session, data, CancellationToken.None);
			var current = sink.Current;
			if (current is not null)
				await current.SendAsync(wrapped, CancellationToken.None);
		};

		await connectionService.RegisterAsync(
			handle, transport.RemoteIp, transport.Hostname, transport.Kind,
			output, output, () => Encoding.UTF8, () => _ = sink.Current?.CloseAsync());

		var token = await resumeTokens.MintAsync(handle, session, ct);
		await transport.SendAsync(SeqEnvelope.ResumeToken(token), ct);
	}

	private async Task PublishInputAsync(long handle, string message, CancellationToken ct)
	{
		if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
			await publishEndpoint.Publish(new NAWSUpdateMessage(handle, rows, cols), ct);
		else
			await publishEndpoint.Publish(new WebSocketInputMessage(handle, message), ct);
	}

	private static bool IsHello(string frame) => SeqEnvelope.IsHello(frame);
}
