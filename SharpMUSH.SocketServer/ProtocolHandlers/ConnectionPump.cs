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
	TimeSpan grace,
	SharpMUSH.Library.Services.Interfaces.IConnectionStateStore? stateStore = null,
	ISessionResumeAuthorizationService? authorization = null)
{
	public async Task RunAsync(IDuplexTransport transport, long candidateHandle, CancellationToken ct)
	{
		try { await RunCoreAsync(transport, candidateHandle, ct); }
		catch (Exception ex)
		{
			logger.LogWarning(ex, "Connection handshake or teardown failed for {Handle}", candidateHandle);
			var sink = sinkRegistry.Get(candidateHandle);
			if (sink is not null && sink.Detach(transport))
			{
				ScheduleExpiry(candidateHandle, sink.SessionId, grace);
			}
			else if (connectionService.Get(candidateHandle) is null)
				descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			await CloseTransportAsync(transport);
		}
	}

	private async Task RunCoreAsync(IDuplexTransport transport, long candidateHandle, CancellationToken ct)
	{
		long handle;
		string session;

		var firstFrame = await transport.ReceiveTextAsync(ct);
		if (firstFrame is null)
		{
			descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			return; // peer closed before saying anything
		}

		var resume = SeqEnvelope.TryReadResume(firstFrame, out var token, out var lastSeq);
		var claim = resume ? await resumeTokens.TryResolveAsync(token, ct) : (Found: false, Handle: 0L, Session: "");
		if (claim.Found && await TryRebindAsync(transport, claim.Handle, claim.Session, token, lastSeq, ct) is { } rebound)
		{
			descriptorGenerator.ReleaseWebSocketDescriptor(candidateHandle);
			(handle, session) = rebound;
		}
		else
		{
			if (claim.Found) await resumeTokens.InvalidateAsync(token, ct);
			handle = candidateHandle;
			session = await RegisterFreshAsync(transport, handle, ReadPresenceClass(firstFrame), ct);
			// A failed authorization must not reveal old output. Fresh registration has its own token.
			if (!resume && !SeqEnvelope.IsHello(firstFrame))
				await PublishInputAsync(handle, firstFrame, ct);
		}

		try
		{
			while (!ct.IsCancellationRequested)
			{
				var message = await transport.ReceiveTextAsync(ct);
				if (message is null) break;
				if (message.Length == 0) continue;
				if (!ReferenceEquals(sinkRegistry.Get(handle)?.Current, transport)) break;
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
			// Only tear down if THIS pump's transport is still the sink's active one. If a newer pump
			// rebound this handle while we were exiting (connection-steal), it has already Attached its own
			// transport and cancelled the grace timer — so our exit must not detach its transport or
			// schedule a disconnect for the live session it now owns. Ownership is "I am sink.Current".
			var sink = sinkRegistry.Get(handle);
			if (sink is not null && ReferenceEquals(sink.Current, transport))
			{
				// Detach (hold the session) instead of disconnecting; the grace timer does the real
				// disconnect if the client does not come back.
				using var teardown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				var acquired = await sink.OutputGate.WaitAsync(TimeSpan.FromSeconds(5));
				if (!acquired)
				{
					if (sink.Detach(transport)) ScheduleExpiry(handle, session, grace);
				}
				else try
					{
						if (ReferenceEquals(sink.Current, transport))
						{
							sink.Detach();
							try { await SetExpiryAsync(handle, DateTimeOffset.UtcNow.Add(grace), teardown.Token); }
							finally { ScheduleExpiry(handle, session, grace); }
						}
					}
					finally { sink.OutputGate.Release(); }
			}
		}
	}

	/// <summary>Rebind to a live handle; returns the (handle, session), or null to fall back to the fresh path.</summary>
	private async Task<(long Handle, string Session)?> TryRebindAsync(IDuplexTransport transport,
		long oldHandle, string oldSession, string token, long lastSeq, CancellationToken ct)
	{
		var sink = sinkRegistry.Get(oldHandle);
		if (sink is null || sink.SessionId != oldSession || connectionService.Get(oldHandle) is null) return null;
		await sink.ResumeGate.WaitAsync(ct);
		try
		{
			if (sinkRegistry.Get(oldHandle) != sink || connectionService.Get(oldHandle) is null) return null;
			if (authorization is not null && !await authorization.AuthorizeAsync(oldHandle, oldSession, transport, ct))
				return null;
			SharpMUSH.Library.Services.Interfaces.ConnectionStateData? persisted = null;
			if (stateStore is not null)
			{
				persisted = await stateStore.GetConnectionAsync(oldHandle, ct);
				if (persisted?.Metadata.GetValueOrDefault("SessionId") != oldSession) return null;
			}
			using var attachDeadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
			attachDeadline.CancelAfter(TimeSpan.FromSeconds(5));
			await sink.OutputGate.WaitAsync(attachDeadline.Token);
			try
			{
				if (sink.Ended || connectionService.Get(oldHandle) is null) return null;
				var history = await replayStore.ReadAsync(oldSession, lastSeq, ct);
				if (!history.Complete) return null;
				var frames = history.Frames;
				var consumed = await resumeTokens.TryConsumeAsync(token, ct);
				if (!consumed.Found || consumed.Handle != oldHandle || consumed.Session != oldSession) return null;
				// Logout and this transition CAS the same persisted incarnation. Perform the fence
				// after token consumption and before sending any remembered output.
				if (stateStore is not null)
				{
					if (persisted is null || !await stateStore.TryUpdateTransportAsync(oldHandle, oldSession,
						persisted.PlayerObjid, persisted.State, transport.RemoteIp, transport.Hostname, transport.IsSecure, ct))
						return null;
				}
				if (sink.Ended || connectionService.Get(oldHandle) is null) return null;
				detachedTracker.Reattach(oldHandle);
				var previous = sink.Current;
				sink.Detach();
				if (previous is not null) await CloseTransportAsync(previous, ct);
				await SendTransportAsync(transport, SeqEnvelope.Reattached(), ct);
				foreach (var frame in frames)
					await SendTransportAsync(transport, frame, ct);
				var newToken = await resumeTokens.MintAsync(oldHandle, oldSession, ct);
				await SendTransportAsync(transport, SeqEnvelope.ResumeToken(newToken), ct);
				sink.TokenIssuedAt = DateTimeOffset.UtcNow;
				await SetExpiryAsync(oldHandle, null, ct);
				if (!sink.TryAttach(transport)) return null;
				return (oldHandle, oldSession);
			}
			catch
			{
				ScheduleExpiry(oldHandle, oldSession, grace);
				throw;
			}
			finally { sink.OutputGate.Release(); }
		}
		finally { sink.ResumeGate.Release(); }
	}

	private async Task SetExpiryAsync(long handle, DateTimeOffset? expiry, CancellationToken ct)
	{
		if (stateStore is not null)
			await stateStore.UpdateMetadataAsync(handle, "ResumeExpiresAt", expiry?.ToUnixTimeMilliseconds().ToString() ?? "0", ct);
	}

	private void ScheduleExpiry(long handle, string session, TimeSpan remaining) =>
		detachedTracker.Detach(handle, async () =>
		{
			var sink = sinkRegistry.Get(handle);
			if (sink is null || sink.SessionId != session) return;
			await sink.ResumeGate.WaitAsync();
			try
			{
				if (sink.Current is not null) return;
				try { await connectionService.DisconnectAsync(handle); }
				finally
				{
					descriptorGenerator.ReleaseWebSocketDescriptor(handle);
					sinkRegistry.Remove(handle);
				}
			}
			finally { sink.ResumeGate.Release(); }
		}, remaining);

	public async Task RestoreDormantAsync(SharpMUSH.Library.Services.Interfaces.ConnectionStateData data,
		DateTimeOffset expiry, CancellationToken ct)
	{
		var session = data.Metadata["SessionId"];
		await SetExpiryAsync(data.Handle, expiry, ct);
		descriptorGenerator.ReserveWebSocketDescriptor(data.Handle);
		var sink = sinkRegistry.GetOrCreate(data.Handle);
		sink.SessionId = session;
		connectionService.RestoreDormant(data, CreateOutput(data.Handle, session, sink), CreateDisconnect(data.Handle, session, sink));
		ScheduleExpiry(data.Handle, session, expiry - DateTimeOffset.UtcNow);
	}

	private static async Task SendTransportAsync(IDuplexTransport transport, ReadOnlyMemory<byte> data, CancellationToken ct = default)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(TimeSpan.FromSeconds(5));
		await transport.SendAsync(data, deadline.Token).WaitAsync(deadline.Token);
	}

	private async Task CloseTransportAsync(IDuplexTransport transport, CancellationToken ct = default)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
		deadline.CancelAfter(TimeSpan.FromSeconds(5));
		try { await transport.CloseAsync(deadline.Token).WaitAsync(deadline.Token); }
		catch (Exception ex) { logger.LogDebug(ex, "Transport close did not complete cleanly"); }
	}

	private Func<byte[], ValueTask> CreateOutput(long handle, string session, SessionSink sink) => async data =>
	{
		await sink.OutputGate.WaitAsync();
		try
		{
			if (sink.Ended) return;
			var (_, wrapped) = await replayStore.AppendAsync(session, data, CancellationToken.None);
			if (sink.Ended)
			{
				using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
				await replayStore.DropAsync(session, cleanup.Token);
				return;
			}
			if (sink.Current is not { } current) return;
			try { await SendTransportAsync(current, wrapped); }
			catch (Exception ex)
			{
				logger.LogWarning(ex, "Socket output failed for {Handle}; preserving replay and detaching transport", handle);
				if (sink.Detach(current))
				{
					using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
					try { await SetExpiryAsync(handle, DateTimeOffset.UtcNow.Add(grace), deadline.Token); }
					catch (Exception expiryError) { logger.LogWarning(expiryError, "Could not persist detached expiry for {Handle}", handle); }
					finally { ScheduleExpiry(handle, session, grace); }
					await CloseTransportAsync(current);
				}
			}
		}
		finally { sink.OutputGate.Release(); }
	};

	private Action CreateDisconnect(long handle, string session, SessionSink sink) => () =>
	{
		var current = sink.End();
		sinkRegistry.Remove(handle);
		descriptorGenerator.ReleaseWebSocketDescriptor(handle);
		detachedTracker.Reattach(handle);
		TimerGraceScheduler.Fire(async () =>
		{
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
			try
			{
				await sink.OutputGate.WaitAsync(cleanup.Token);
				try { await replayStore.DropAsync(session, cleanup.Token); }
				finally { sink.OutputGate.Release(); }
			}
			catch (Exception ex) { logger.LogWarning(ex, "Could not purge replay for ended session {Handle}", handle); }
			try
			{
				if (current is not null)
				{
					try { await SendTransportAsync(current, SeqEnvelope.Bye()); }
					finally { await CloseTransportAsync(current); }
				}
			}
			finally
			{
				for (var attempt = 0; attempt < 5; attempt++)
				{
					using var revokeDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
					try { await resumeTokens.RevokeSessionAsync(session, revokeDeadline.Token).AsTask().WaitAsync(revokeDeadline.Token); break; }
					catch (Exception ex)
					{
						logger.LogWarning(ex, "Session revocation failed for {Handle}; attempt {Attempt}", handle, attempt + 1);
						await Task.Delay(TimeSpan.FromSeconds(1));
					}
				}
			}
		}, ex => logger.LogError(ex, "Error closing transport for handle {Handle}", handle));
	};

	/// <summary>
	/// The presence class carried on the first frame — "play" for a real interactive session, "portal" for
	/// a background query connection. Read from either a hello or a resume-to-dead first frame; defaults to
	/// "play" so telnet and older clients are unaffected.
	/// </summary>
	private static string ReadPresenceClass(string firstFrame)
	{
		if (SeqEnvelope.TryReadHello(firstFrame, out var helloClass))
			return helloClass;
		if (SeqEnvelope.TryReadResume(firstFrame, out _, out _, out var resumeClass))
			return resumeClass;
		return "play";
	}

	/// <summary>Registers a fresh session and returns its per-incarnation session id.</summary>
	private async Task<string> RegisterFreshAsync(IDuplexTransport transport, long handle, string presenceClass, CancellationToken ct)
	{
		// A fresh incarnation gets a unique session id. Replay is keyed by it (never by the reusable
		// handle), so a recycled handle's new occupant can never read this session's buffer, and vice
		// versa — closing the cross-session replay leak at its root rather than relying on purge timing.
		var session = Guid.NewGuid().ToString("N");

		var sink = sinkRegistry.GetOrCreate(handle);
		sink.Attach(transport);

		sink.SessionId = session;
		var output = CreateOutput(handle, session, sink);
		await connectionService.RegisterAsync(handle, transport.RemoteIp, transport.Hostname, transport.Kind,
			output, output, () => Encoding.UTF8, CreateDisconnect(handle, session, sink),
			presenceClass: presenceClass, isSecure: transport.IsSecure, sessionId: session, cancellationToken: ct);

		var token = await resumeTokens.MintAsync(handle, session, ct);
		await SendTransportAsync(transport, SeqEnvelope.ResumeToken(token), ct);
		sink.TokenIssuedAt = DateTimeOffset.UtcNow;
		return session;
	}

	private async Task PublishInputAsync(long handle, string message, CancellationToken ct)
	{
		if (WebSocketControlFrame.TryParseNaws(message, out var cols, out var rows))
			await ConnectionInputPublisher.PublishAsync(publishEndpoint, connectionService, logger,
				handle, new NAWSUpdateMessage(handle, rows, cols), ct);
		else
			await ConnectionInputPublisher.PublishAsync(publishEndpoint, connectionService, logger,
				handle, new WebSocketInputMessage(handle, message, connectionService.Get(handle)?.SessionId), ct);
	}
}
