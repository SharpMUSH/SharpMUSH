using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using System.Net;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.Library.Utilities;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Messaging.Abstractions;
using System.Text;
using TelnetNegotiationCore.Builders;
using MarkupString.Mxp;
using TelnetNegotiationCore.Connections;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Protocols;
using TelnetNegotiationCore.WebSockets;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

/// <summary>
/// Handles Telnet protocol connections and publishes messages to the message queue: TCP from Kestrel's telnet
/// listeners, and telnet carried in a WebSocket (<see cref="HandleWebSocketAsync"/>). Both run the same
/// <see cref="TelnetSession"/> on a TelnetNegotiationCore <see cref="TelnetConnection"/>; only the transport and
/// where the address comes from differ.
/// </summary>
public partial class TelnetServer : ConnectionHandler
{
	private readonly ILogger _logger;
	private readonly IConnectionServerService _connectionService;
	private readonly IMessageBus _publishEndpoint;
	private readonly IDescriptorGeneratorService _descriptorGenerator;
	private readonly ITelnetInterpreterFactory _telnetFactory;
	private readonly ConnectionServerOptions _options;
	private readonly MsspReportHolder _mssp;
	private readonly TerminalProbes _probes;
	private readonly OutputSettingsHolder? _settings;
	private readonly ConnectionTrafficCounters _traffic;
	private readonly KeepAliveOptions? _keepAlive;

	/// <summary>
	/// How much a client may type before its connection is registered. Registration normally takes
	/// milliseconds, so this is only reached by a client flooding a connection the engine is slow to
	/// register; past it, input is dropped rather than buffered.
	/// </summary>
	internal const int MaxInputHeldBeforeRegistration = 16 * 1024;

	public TelnetServer(
		ILogger<TelnetServer> logger,
		IConnectionServerService connectionService,
		IMessageBus publishEndpoint,
		IDescriptorGeneratorService descriptorGenerator,
		ITelnetInterpreterFactory telnetFactory,
		ConnectionServerOptions options,
		MsspReportHolder mssp,
		TerminalProbes probes,
		OutputSettingsHolder? settings = null,
		ConnectionTrafficCounters? traffic = null,
		KeepAliveOptions? keepAlive = null)
	{
		Console.OutputEncoding = Encoding.UTF8;
		_logger = logger;
		_connectionService = connectionService;
		_publishEndpoint = publishEndpoint;
		_descriptorGenerator = descriptorGenerator;
		_telnetFactory = telnetFactory;
		_options = options;
		_mssp = mssp;
		_probes = probes;
		_settings = settings;
		_traffic = traffic ?? new ConnectionTrafficCounters();
		_keepAlive = keepAlive;
	}

	public override Task OnConnectedAsync(ConnectionContext connection) =>
		ServeAsync(new TelnetEndpoint(
			connection.ConnectionId,
			// Ending the connection from this side drops the socket at once, as PennMUSH does.
			TelnetTransport.Pipe(connection.Transport, () =>
			{
				connection.Abort();
				return ValueTask.CompletedTask;
			}),
			connection.Abort,
			// The address alone, as PennMUSH's ipaddr() and a WebSocket connection give it: sitelock rules, @ban and
			// softcode read it as an address.
			connection.RemoteEndPoint is IPEndPoint remote ? WebSocketServer.ClientAddress(remote.Address) : "unknown",
			// PennMUSH's CONN_SSL. Kestrel attaches ITlsHandshakeFeature only on an endpoint that actually
			// terminated TLS, so this is the handshake that happened rather than anything the client says
			// about itself — and it is false, correctly, on the plaintext listener.
			connection.Features.Get<ITlsHandshakeFeature>() is not null,
			connection.ConnectionClosed));

	/// <summary>
	/// The subprotocols a MUD client may ask for on <c>/telnet</c>, in the order this server prefers them: the
	/// mudstandards.org names for the telnet stream. A client that names none is taken as telnet too, as most
	/// wss-to-telnet bridges and older clients send none.
	/// </summary>
	internal static readonly string[] WebSocketSubprotocols =
	[
		MudWebSocketSubprotocols.Telnet,
		MudWebSocketSubprotocols.TelnetAlias,
		MudWebSocketSubprotocols.TelnetBase64
	];

	/// <summary>
	/// Telnet in a WebSocket, for MUD clients that connect over <c>wss://</c> (the mudstandards.org proposal). The
	/// connection is a telnet connection in every way the game sees: the same negotiation, registration and
	/// output as one on the telnet port. The portal's terminal is not this; it speaks its own protocol on
	/// <c>/ws</c>.
	/// </summary>
	public async Task HandleWebSocketAsync(HttpContext context)
	{
		var offered = context.WebSockets.WebSocketRequestedProtocols;
		var chosen = WebSocketSubprotocols.FirstOrDefault(name =>
			offered.Any(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase)));
		if (!context.WebSockets.IsWebSocketRequest || (chosen is null && offered.Count > 0))
		{
			// Not a WebSocket, or one that only speaks something else (the portal's protocol belongs on /ws).
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		// No permessage-deflate: telnet has MCCP for that, and compressing a stream that carries a typed password
		// next to text another player chose is what CRIME and BREACH exploit. KeepAliveTimeout aborts a socket whose
		// pongs stop, so a client that vanished is noticed as on /ws.
		using var socket = await context.WebSockets.AcceptWebSocketAsync(new WebSocketAcceptContext
		{
			SubProtocol = chosen,
			KeepAliveInterval = _keepAlive?.WsInterval ?? TimeSpan.FromSeconds(15),
			KeepAliveTimeout = _keepAlive?.WsTimeout ?? TimeSpan.FromSeconds(20)
		});

		await ServeAsync(new TelnetEndpoint(
			context.TraceIdentifier,
			TelnetTransport.WebSocket(socket),
			context.Abort,
			// Behind a proxy listed in ForwardedHeaders:KnownProxies, UseForwardedHeaders has already put the
			// X-Forwarded-For client here (TrustedProxies), as for /ws.
			WebSocketServer.ClientAddress(context.Connection.RemoteIpAddress),
			// wss:// reports SSL; behind a TLS-terminating proxy only once its X-Forwarded-Proto is honoured.
			context.Request.IsHttps,
			context.RequestAborted));
	}

	private async Task ServeAsync(TelnetEndpoint endpoint)
	{
		var ct = endpoint.Closed;
		try
		{
			var nextPort = await _descriptorGenerator.GetNextTelnetDescriptorAsync(ct);
			try
			{
				await RunConnectionAsync(endpoint, nextPort, ct);
			}
			finally
			{
				using var teardown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
				try { await _connectionService.DisconnectAsync(nextPort, teardown.Token).WaitAsync(teardown.Token); }
				catch (OperationCanceledException) { _logger.LogDebug("Connection {Handle} teardown timed out", nextPort); }
				finally { _descriptorGenerator.ReleaseTelnetDescriptor(nextPort); }
			}
		}
		catch (OperationCanceledException) when (ct.IsCancellationRequested && !endpoint.ClosedByServer)
		{
			// The client left before its connection was registered: a port probe that connects and
			// closes at once ends here. Kestrel would log anything thrown as an unhandled exception.
			_logger.LogDebug("Connection {ConnectionId} closed before it was registered", endpoint.Id);
		}
	}

	/// <summary>
	/// Where one telnet connection runs: a TCP connection from Kestrel or a WebSocket, and what the transport says
	/// about the client.
	/// </summary>
	/// <param name="Id">The transport's id for the connection, for logs.</param>
	/// <param name="Closed">Cancelled when the client leaves (or the server closes the connection).</param>
	/// <param name="Abort">Drops the connection at once, with no goodbye to the client.</param>
	private sealed record TelnetEndpoint(
		string Id, ITelnetTransport Transport, Action Abort, string RemoteIp, bool IsSecure, CancellationToken Closed)
	{
		/// <summary>
		/// How long a close from this side may take before the connection is dropped. A WebSocket close waits behind
		/// a send to a client that has stopped reading, and TelnetNegotiationCore's own close timeout starts only
		/// once the close frame is out.
		/// </summary>
		internal static readonly TimeSpan CloseDeadline = TimeSpan.FromSeconds(10);

		/// <summary>The running connection, once it has started.</summary>
		public TelnetConnection? Connection { get; set; }

		/// <summary>
		/// True once the server closed a connection whose client was still there. Closing cancels
		/// <see cref="Closed"/> just as a client leaving does, so the token alone cannot say whose close it was.
		/// </summary>
		public bool ClosedByServer => Connection?.ClosedByServer == true;

		/// <summary>Ends the connection from this side, as the main process asks (a @boot, QUIT, a failed registration).</summary>
		public void Close()
		{
			if (Connection is not { } connection) return;
			_ = connection.CloseAsync().AsTask().ContinueWith(
				static (t, _) => _ = t.Exception, null, TaskContinuationOptions.OnlyOnFaulted);
			_ = AbortUnlessEndedAsync(connection.Completion);
		}

		private async Task AbortUnlessEndedAsync(Task completion)
		{
			if (await Task.WhenAny(completion, Task.Delay(CloseDeadline)) != completion)
				Abort();
		}
	}

	private async Task RunConnectionAsync(TelnetEndpoint endpoint, long nextPort, CancellationToken ct)
	{
		using var held = new RegistrationHold(nextPort, _logger, ct);
		var traffic = _traffic.Start(nextPort);
		var session = new TelnetSession(this, endpoint, nextPort, held, traffic, ct);

		TelnetConnection? connection = null;
		try
		{
			connection = await session.StartAsync();
			var telnet = connection.Interpreter;
			if (telnet.PluginManager?.GetPlugin<TerminalQueryProtocol>() is { } terminalQueries)
			{
				_probes.Register(nextPort, () => terminalQueries.ProbeAsync());
			}

			var readTask = connection.Completion;
			// The read loop is already running by now, so a fast client could have negotiated in the gap
			// above and found nothing to sample. Re-sampling here closes it without needing a lock.
			await session.AnnounceTelnetIfNegotiatedAsync();
			var negotiationEnd = await session.WaitForNegotiationAsync(TimeSpan.FromMilliseconds(_settings?.NegotiationWaitMilliseconds ?? 400), readTask);
			if (negotiationEnd is InitialNegotiationEnd.Cancelled or InitialNegotiationEnd.ConnectionClosed)
				return;
			await session.RegisterAsync();

			// The handle exists in the main process from here on, so everything negotiation produced before
			// now can go out and land on a connection that is there to receive it.
			await held.ReleaseAsync();

			// Await the read task, which completes when the connection closes or the cancellation token
			// is triggered.
			await ReportReadEndAsync(readTask, nextPort);
		}
		finally
		{
			_probes.Unregister(nextPort);
			_traffic.End(nextPort, traffic);
			if (connection is not null)
				await DisposeTelnetAsync(connection, endpoint.Id);
		}
	}

	/// <summary>Stops the read loop and disposes the interpreter. A failure there is logged, never thrown over the connection's own.</summary>
	private async Task DisposeTelnetAsync(TelnetConnection connection, string connectionId)
	{
		try
		{
			await connection.DisposeAsync();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Connection {ConnectionId} telnet disposal failed", connectionId);
		}
	}

	// Says once, at Information, how the read side ended. Without it a socket the kernel timed out,
	// a reset from the peer and a client that simply quit all leave the same "Disconnecting handle" line.
	private async Task ReportReadEndAsync(Task readTask, long handle)
	{
		try
		{
			await readTask;
			_logger.LogInformation("Connection {Handle} closed: the read side ended without an error", handle);
		}
		catch (OperationCanceledException)
		{
			_logger.LogInformation("Connection {Handle} closed: the read was cancelled", handle);
		}
		catch (Exception ex)
		{
			_logger.LogInformation("Connection {Handle} closed: {ErrorType}: {Reason}", handle, ex.GetType().Name, ex.Message);
		}
	}

	/// <summary>
	/// Records what the client's terminal types say it can display, so the renderer stops writing colour
	/// below what it can in fact show.
	/// <para>
	/// Unlike the format updates, this does not wait for registration: TTYPE is answered in the
	/// opening burst, usually before the main process has registered the connection, and the types
	/// are reported again for every entry in the client's list — so the next one lands after
	/// registration and carries the same conclusion. Missing the first is harmless; blocking the
	/// negotiation read loop on a retry loop would not be.
	/// </para>
	/// </summary>
	private void TryApplyTerminalCapabilities(long handle, IReadOnlyList<string> terminalTypes)
	{
		var connection = _connectionService.Get(handle);
		if (connection is null)
		{
			return;
		}

		var reported = TerminalCapabilityReader.Read(terminalTypes);
		var updated = _connectionService.UpdateCapabilities(handle, current => WithTerminalTypes(current, terminalTypes));

		if (updated)
		{
			_logger.LogDebug(
				"Terminal capabilities for handle {Handle}: ansi={Ansi}, xterm256={Xterm256}, truecolor={Truecolor}, utf8={Utf8}, screenreader={ScreenReader}",
				handle, reported.Ansi, reported.Xterm256, reported.Truecolor, reported.Utf8, reported.ScreenReader);
		}
	}

	/// <summary>
	/// <paramref name="current"/> with what <paramref name="terminalTypes"/> say. Only the fields this report
	/// speaks to: the record also carries the negotiated output format and any SOCKSET colorstyle pin, which have
	/// their own writers and must survive this one. The colour claims are kept as reported: a screen reader is
	/// sent plain text by the colour style, which a SCREENREADER pin can turn back off.
	/// </summary>
	private static ProtocolCapabilities WithTerminalTypes(ProtocolCapabilities current, IReadOnlyList<string> terminalTypes)
	{
		var reported = TerminalCapabilityReader.Read(terminalTypes);
		return current with
		{
			SupportsAnsi = reported.Ansi,
			SupportsXterm256 = reported.Xterm256,
			SupportsTruecolor = reported.Truecolor,
			SupportsUtf8 = reported.Utf8,
			ScreenReader = reported.ScreenReader,
			TerminalTypes = terminalTypes
		};
	}

	/// <summary>
	/// Writes what the client has said it renders onto the connection: the elements it named, of those
	/// it was asked about. Updated whenever a reply arrives, including after registration.
	/// </summary>
	private void RecordMxpSupport(long handle, TelnetInterpreter? interpreter)
	{
		if (interpreter?.PluginManager?.GetPlugin<MXPProtocol>() is not { } mxp) return;

		var supported = string.Join(' ', MxpRegistration.Elements.Where(mxp.Support.Supports));
		_connectionService.UpdateCapabilities(handle, current => current with { MxpSupported = supported });
		_logger.LogDebug("Handle {Handle} renders MXP: {Supported}",
			handle, supported.Length == 0 ? "(nothing it was asked about)" : supported);
	}

	private async ValueTask<bool> TryUpdateFormatAsync(long handle, OutputFormat format, CancellationToken cancellationToken)
	{
		for (var attempt = 0; attempt < ConnectionRetryPolicy.MaxAttempts; attempt++)
		{
			if (_connectionService.Get(handle) is not null)
			{
				// True only when the format actually changed; a client that negotiates the same format
				// twice is not a failure, so a no-op counts as success here.
				// An MXP connection starts out rendering none of the elements it is about to be asked about,
				// in the same update that makes it MXP: output that races the question — the welcome, a
				// login — must not carry a tag the client may be unable to read. An answer that already
				// landed is kept.
				_connectionService.UpdateCapabilities(handle, current => current with
				{
					Format = current.Format.Negotiate(format),
					MxpSupported = format == OutputFormat.Mxp ? current.MxpSupported ?? string.Empty : current.MxpSupported
				});
				return true;
			}

			if (cancellationToken.IsCancellationRequested)
			{
				return false;
			}

			try
			{
				await Task.Delay(ConnectionRetryPolicy.Delay, cancellationToken);
			}
			catch (OperationCanceledException)
			{
				return false;
			}
		}

		return false;
	}
}
