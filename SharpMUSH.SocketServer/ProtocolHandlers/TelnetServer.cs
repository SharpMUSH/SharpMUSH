using Microsoft.AspNetCore.Connections;
using System.IO.Pipelines;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.Library.Utilities;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Messaging.Abstractions;
using System.Text;
using TelnetNegotiationCore.Builders;
using MarkupString.Mxp;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Protocols;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

/// <summary>
/// Handles Telnet protocol connections and publishes messages to the message queue
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
		TerminalProbes probes)
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
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var ct = connection.ConnectionClosed;
		var nextPort = await _descriptorGenerator.GetNextTelnetDescriptorAsync(ct);
		try
		{
			await RunConnectionAsync(connection, nextPort, ct);
		}
		finally
		{
			using var teardown = new CancellationTokenSource(TimeSpan.FromSeconds(2));
			try { await _connectionService.DisconnectAsync(nextPort, teardown.Token).WaitAsync(teardown.Token); }
			catch (OperationCanceledException) { _logger.LogDebug("Connection {Handle} teardown timed out", nextPort); }
			finally { _descriptorGenerator.ReleaseTelnetDescriptor(nextPort); }
		}
	}

	private async Task RunConnectionAsync(ConnectionContext connection, long nextPort, CancellationToken ct)
	{
		using var readLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
		ct = readLifetime.Token;
		using var held = new RegistrationHold(nextPort, _logger, ct);
		var session = new TelnetSession(this, connection, nextPort, held, ct);

		var telnet = await session.BuildInterpreterAsync();
		if (telnet.PluginManager?.GetPlugin<TerminalQueryProtocol>() is { } terminalQueries)
		{
			_probes.Register(nextPort, () => terminalQueries.ProbeAsync());
		}

		var readTask = ReadAndObserveNegotiationAsync(
			telnet, connection.Transport.Input, session.AnnounceTelnetIfNegotiatedAsync, ct);
		try
		{
			// The read loop is already running by now, so a fast client could have negotiated in the gap
			// above and found nothing to sample. Re-sampling here closes it without needing a lock.
			await session.AnnounceTelnetIfNegotiatedAsync();
			await session.RegisterAsync();

			// The handle exists in the main process from here on, so everything negotiation produced before
			// now can go out and land on a connection that is there to receive it.
			await held.ReleaseAsync();

			// Await the read task, which completes when the connection closes or the cancellation token
			// is triggered.
			await AwaitReadTaskAsync(readTask, connection.ConnectionId);
		}
		finally
		{
			_probes.Unregister(nextPort);
			await StopTelnetAsync(readLifetime, readTask, telnet, connection.ConnectionId);
		}
	}

	private async Task StopTelnetAsync(
		CancellationTokenSource readLifetime,
		Task readTask,
		TelnetInterpreter telnet,
		string connectionId)
	{
		try
		{
			await readLifetime.CancelAsync();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Connection {ConnectionId} read cancellation failed", connectionId);
		}

		var readStopped = AwaitReadTaskAsync(readTask, connectionId);
		var interpreterStopped = DisposeTelnetAsync(telnet, connectionId);
		await Task.WhenAll(readStopped, interpreterStopped);
	}

	private async Task DisposeTelnetAsync(TelnetInterpreter telnet, string connectionId)
	{
		try
		{
			await telnet.DisposeAsync();
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Connection {ConnectionId} telnet disposal failed", connectionId);
		}
	}

	private async Task AwaitReadTaskAsync(Task readTask, string connectionId)
	{
		try
		{
			await readTask;
		}
		catch (Exception ex) when (ex is ConnectionResetException or OperationCanceledException)
		{
			// Normal disconnect or read-loop shutdown.
		}
		catch (Exception ex)
		{
			_logger.LogDebug(ex, "Connection {ConnectionId} disconnected unexpectedly.", connectionId);
		}
	}

	private static async Task ReadAndObserveNegotiationAsync(
		TelnetInterpreter interpreter,
		PipeReader reader,
		Func<ValueTask> onProcessed,
		CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			var result = await reader.ReadAtLeastAsync(1, cancellationToken);

			foreach (var segment in result.Buffer)
			{
				await interpreter.InterpretByteArrayAsync(segment);
				await interpreter.WaitForProcessingAsync(additionalDelayMs: 0);
				await onProcessed();
			}

			reader.AdvanceTo(result.Buffer.End);

			if (result.IsCompleted)
			{
				break;
			}
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

		// Only the fields this report speaks to: the record also carries the negotiated output format
		// and any SOCKSET colorstyle pin, which have their own writers and must survive this one. The colour
		// claims are kept as reported: a screen reader is sent plain text by the colour style, which a
		// SCREENREADER pin can turn back off.
		var updated = _connectionService.UpdateCapabilities(handle, current => current with
		{
			SupportsAnsi = reported.Ansi,
			SupportsXterm256 = reported.Xterm256,
			SupportsTruecolor = reported.Truecolor,
			SupportsUtf8 = reported.Utf8,
			ScreenReader = reported.ScreenReader,
			TerminalTypes = terminalTypes
		});

		if (updated)
		{
			_logger.LogDebug(
				"Terminal capabilities for handle {Handle}: ansi={Ansi}, xterm256={Xterm256}, truecolor={Truecolor}, utf8={Utf8}, screenreader={ScreenReader}",
				handle, reported.Ansi, reported.Xterm256, reported.Truecolor, reported.Utf8, reported.ScreenReader);
		}
	}

	/// <summary>
	/// Asks the client which of the elements this server writes it can render, and records the answer on
	/// the connection so the renderer sends it nothing else.
	/// </summary>
	/// <remarks>
	/// <para>MXP has this exchange for a reason: a client that cannot open a frame is better off with
	/// the text that would have gone in it than with a tag it shows to the player. The answer belongs to
	/// one connection, so it is kept with that connection's capabilities.</para>
	/// <para><b>Not awaited from the callback that starts it.</b> That callback runs inside the
	/// interpreter's processing of the bytes that began MXP mode, and the read loop is waiting for that
	/// processing to finish — so waiting here for a reply that can only arrive through the next read
	/// would stall the loop until the deadline and then record silence from a client that had answered
	/// at once. The question is asked on a task of its own, leaving the loop free to deliver the
	/// answer.</para>
	/// <para>A client need not answer. What is waited for is the answer to this question, and what is
	/// recorded is what came back — an element nobody answered about is one the client does not get,
	/// which is the safe way round. An answer after the deadline is recorded when it lands, through the
	/// handler registered with the plugin, and governs everything sent after it.</para>
	/// </remarks>
	private void StartAskingWhatMxpClientRenders(long handle, TelnetInterpreter? interpreter, CancellationToken cancellationToken)
	{
		if (interpreter?.PluginManager?.GetPlugin<MXPProtocol>() is not { } mxp) return;

		_ = Task.Run(async () =>
		{
			try
			{
				await mxp.RequestSupportAsync(
					TimeSpan.FromMilliseconds(Math.Max(0, _options.MxpSupportTimeoutMilliseconds)),
					MxpRegistration.Elements.ToArray());

				if (!cancellationToken.IsCancellationRequested) RecordMxpSupport(handle, interpreter);
			}
			catch (OperationCanceledException)
			{
				// The connection went away while the question was outstanding.
			}
			catch (Exception ex)
			{
				// Broad on purpose: nothing awaits this task, so whatever the plugin or a closing pipe
				// throws would otherwise go unobserved, and none of it should reach the connection.
				_logger.LogDebug(ex, "Asking handle {Handle} what MXP it renders failed", handle);
			}
		}, cancellationToken);
	}

	/// <summary>
	/// Writes what the client has said it renders onto the connection: the elements it named, of those
	/// it was asked about. Called both when a reply lands and when the wait for one runs out, and either
	/// may be first.
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
