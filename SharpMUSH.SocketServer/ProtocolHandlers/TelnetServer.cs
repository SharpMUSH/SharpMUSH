using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using System.IO.Pipelines;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.Library.Utilities;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.Messaging.Abstractions;
using System.Net;
using System.Text;
using TelnetNegotiationCore.Builders;
using MarkupString.Mxp;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Protocols;
using TelnetNegotiationCore.Models;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

/// <summary>
/// Handles Telnet protocol connections and publishes messages to the message queue
/// </summary>
public class TelnetServer : ConnectionHandler
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

		// Assigned once BuildAsync returns; the callbacks below only run after that, since the
		// interpreter has to exist before it can hand any of them anything.
		TelnetInterpreter? telnetInterpreter = null;
		var telnetAnnounced = 0;

		// Anything that writes connection metadata in the main process, and any line of input, has to arrive after the handle
		// is registered there, because every one of those consumers gives up on an unregistered handle
		// after ConnectionRetryPolicy's five 50ms attempts. The read loop starts before RegisterAsync
		// below, so a client that answers TTYPE within one round trip
		// can outrun its own registration and have its terminal type silently dropped. Widening the
		// consumers' retry window would only make that less likely; holding the messages here makes the
		// ordering a fact. Null once registration has happened, after which publishing is direct.
		var pendingLock = new object();
		using var publishGate = new SemaphoreSlim(1, 1);
		List<Func<Task>>? pendingPublishes = [];
		// Input is held too (see OnSubmit), but a client decides how much of it there is, so what it may
		// send before registration is capped rather than buffered for as long as registration takes.
		var heldInputChars = 0;
		var heldInputDropped = false;

		async ValueTask PublishAfterRegistrationAsync(Func<Task> publish, int inputChars = 0)
		{
			lock (pendingLock)
			{
				if (pendingPublishes is not null)
				{
					if (inputChars > 0 && (heldInputChars += inputChars) > MaxInputHeldBeforeRegistration)
					{
						if (!heldInputDropped)
						{
							heldInputDropped = true;
							_logger.LogWarning("Dropping input on handle {Handle}: over {Limit} characters sent before it was registered",
								nextPort, MaxInputHeldBeforeRegistration);
						}

						return;
					}

					pendingPublishes.Add(publish);
					return;
				}
			}

			await publishGate.WaitAsync(ct);
			try
			{
				await publish();
			}
			finally
			{
				publishGate.Release();
			}
		}

		async ValueTask FlushPendingPublishesAsync()
		{
			await publishGate.WaitAsync(ct);
			try
			{
				Func<Task>[] queued;
				lock (pendingLock)
				{
					queued = [.. pendingPublishes ?? []];
					pendingPublishes = null;
				}

				// In the order they were produced: a client's terminal-type list is reported once per entry,
				// and the last report is the complete one.
				// Each on its own: a negotiation report that fails must not keep the input queued behind it
				// from going out.
				foreach (var publish in queued)
				{
					try
					{
						await publish();
					}
					catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
					{
						_logger.LogWarning(ex, "A message held for registration could not be published for handle {Handle}", nextPort);
					}
				}
			}
			finally
			{
				publishGate.Release();
			}
		}

		// Reports the client as speaking telnet the first time any option has genuinely negotiated.
		// TelnetNegotiationCore tracks that per plugin as ITelnetProtocolPlugin.IsNegotiated — set at
		// the state where a WILL/DO exchange resolves, so it is an answer from the client rather than
		// an option we merely offered. Sampling beats subscribing here: the library raises no event for
		// "some option settled", and the flag is monotonic, so one publish per connection is enough.
		async ValueTask AnnounceTelnetIfNegotiatedAsync()
		{
			if (Volatile.Read(ref telnetAnnounced) != 0
					// The terminal-query plugin counts itself negotiated as soon as it is registered — it is not a
					// telnet option and nothing is exchanged — so it says nothing about whether the client speaks telnet.
					|| telnetInterpreter?.PluginManager?.GetAllPlugins()
						.Any(plugin => plugin.IsNegotiated && plugin is not TerminalQueryProtocol) != true
					|| Interlocked.Exchange(ref telnetAnnounced, 1) != 0)
			{
				return;
			}

			_logger.LogDebug("Telnet negotiation confirmed on handle {Handle}", nextPort);
			await PublishAfterRegistrationAsync(
				() => _publishEndpoint.Publish(new TelnetNegotiatedMessage(nextPort), ct));
		}

		IReadOnlyList<string> publishedTerminalTypes = [];
		var terminalTypeProtocol = new TerminalTypeProtocol().OnTerminalTypes(
			async terminalTypes =>
			{
				if (publishedTerminalTypes.SequenceEqual(terminalTypes))
				{
					return;
				}

				var snapshot = terminalTypes.ToArray();
				publishedTerminalTypes = snapshot;

				// MTTS is the only thing that ever tells us a client can render more than 16 colours.
				// Until it was read, ProtocolCapabilities.SupportsXterm256 sat at its default of
				// false for every telnet connection, and every xterm256 colour the game produced was
				// sent as one of the sixteen — to clients that had said, in the one place there is to
				// say it, that they could display them.
				await PublishAfterRegistrationAsync(async () =>
				{
					TryApplyTerminalCapabilities(nextPort, snapshot);
					await _publishEndpoint.Publish(
						new TerminalTypeNegotiatedMessage(nextPort, snapshot), ct);
				});
			});

		TelnetInterpreterBuilder builder = _telnetFactory.CreateBuilder()
			.OnSubmit(async (byteArray, encoding, _) =>
			{
				var input = encoding.GetString(byteArray);

				// By the time a client has sent a line, whatever it was going to negotiate has settled.
				await AnnounceTelnetIfNegotiatedAsync();

				// Held like the negotiation messages: a client that types as soon as it connects can
				// deliver a line before RegisterAsync, and published then it carries no SessionId, so the
				// engine skips its registration wait and parses it for a handle it does not know yet — the
				// line is lost. The SessionId is read when the message goes out, after registration.
				await PublishAfterRegistrationAsync(() => ConnectionInputPublisher.PublishAsync(_publishEndpoint,
					_connectionService, _logger, nextPort,
					new TelnetInputMessage(nextPort, input, _connectionService.Get(nextPort)?.SessionId), ct),
					// A blank line is still a queued message, so it counts too.
					Math.Max(input.Length, 1));
			})
			// Each of these callbacks is also a sampling point for AnnounceTelnetIfNegotiatedAsync,
			// which asks every plugin rather than just the one that fired: reaching any of them means
			// some option settled, and a client that answers only NAWS still speaks telnet.
			.AddPlugin<GMCPProtocol>().OnGMCPMessage(async data =>
			{
				await AnnounceTelnetIfNegotiatedAsync();
				await PublishAfterRegistrationAsync(
					() => _publishEndpoint.Publish(new GMCPSignalMessage(nextPort, data.Package, data.Info), ct));
			})
			.AddPlugin<MSSPProtocol>().WithMSSPConfig(() => _mssp.Current)
			// A client reporting MSSP to a server means nothing here; the callback is only a sampling point.
			.OnMSSP(async _ => await AnnounceTelnetIfNegotiatedAsync())
			.AddPlugin<NAWSProtocol>().OnNAWS(async (newHeight, newWidth) =>
			{
				await AnnounceTelnetIfNegotiatedAsync();
				await PublishAfterRegistrationAsync(() =>
				{
					// Kept here as well as sent to the engine: the renderer lays automatic-width boxes out at it.
					_connectionService.UpdateCapabilities(nextPort, current => current with { Width = newWidth });
					return _publishEndpoint.Publish(new NAWSUpdateMessage(nextPort, newHeight, newWidth), ct);
				});
			})
			.AddPlugin<MSDPProtocol>().OnMSDPMessage(MSDPCallback(connection))
			.AddPlugin<CharsetProtocol>().WithCharsetOrder(Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1"))
			// What the client agreed to read: output is written in it, each character it lacks replaced.
			.OnCharsetChange(async encoding =>
			{
				await AnnounceTelnetIfNegotiatedAsync();
				await PublishAfterRegistrationAsync(() =>
				{
					_connectionService.UpdateCapabilities(nextPort, current => current with
					{
						Charset = TerminalCharsets.Parse(encoding.WebName) ?? encoding.WebName
					});
					return Task.CompletedTask;
				});
			})
			.AddPlugin<MCCPProtocol>()
			// RFC 1091 terminal type: the only way a client names itself over plain telnet, and what
			// terminfo() reports as the client. Without it every connection is "unknown".
			.AddPlugin(terminalTypeProtocol)
			// Asks the terminal what it can draw, only when the player asks for it (SOCKSET graphics=detect):
			// a client in line mode echoes the answers, so asking unprompted would print them at everyone.
			.AddPlugin<TerminalQueryProtocol>().OnTerminalReport(async report =>
			{
				var answered = new TerminalProbeResult(report.KittyGraphics, report.Sixel == true, report.Version,
					report.CellWidth ?? 0, report.CellHeight ?? 0);
				await PublishAfterRegistrationAsync(async () =>
				{
					_connectionService.UpdateCapabilities(nextPort, current => current with
					{
						Probe = answered,
						CellWidth = answered.CellWidth,
						CellHeight = answered.CellHeight
					});
					await _publishEndpoint.Publish(new TerminalReportMessage(nextPort, answered), ct);
				});
			});

		// The handshake itself — the hello, consuming PUEBLOCLIENT, and the start sequence that moves the
		// client into HTML mode — is TelnetNegotiationCore's. What is left here is the part that is this
		// server's: the render format for the connection, and telling the main process.
		if (_options.PuebloEnabled)
		{
			builder = builder.AddPlugin<PuebloProtocol>().OnPuebloEnabled(async client =>
			{
				_logger.LogDebug("Pueblo negotiated on handle {Handle}", nextPort);

				if (await TryUpdateFormatAsync(nextPort, OutputFormat.Pueblo, ct))
				{
					_logger.LogDebug("Updated Pueblo capabilities for handle {Handle}", nextPort);
				}

				await PublishAfterRegistrationAsync(() => _publishEndpoint.Publish(
					new PuebloNegotiatedMessage(nextPort, PuebloProtocol.ClientCommand + client.Version), ct));
			});
		}

		if (_options.MxpEnabled)
		{
			builder = builder.AddPlugin<MXPProtocol>()
				// Registered with the plugin rather than when the question goes out: a client can answer
				// the moment MXP mode starts, and the reply must never arrive before anything is listening.
				.OnMxpSupports(_ =>
				{
					RecordMxpSupport(nextPort, telnetInterpreter);
					return ValueTask.CompletedTask;
				})
				.OnMXPEnabled(() =>
			{
				_logger.LogInformation("MXP negotiated on handle {Handle}", nextPort);

				async ValueTask DoMxpSetup()
				{
					await AnnounceTelnetIfNegotiatedAsync();

					if (await TryUpdateFormatAsync(nextPort, OutputFormat.Mxp, ct))
					{
						_logger.LogDebug("Updated MXP capabilities for handle {Handle}", nextPort);
						StartAskingWhatMxpClientRenders(nextPort, telnetInterpreter, ct);
					}
					else if (!ct.IsCancellationRequested)
					{
						_logger.LogWarning("MXP negotiated but connection {Handle} not yet registered", nextPort);
					}

					await PublishAfterRegistrationAsync(
						() => _publishEndpoint.Publish(new MxpNegotiatedMessage(nextPort), ct));
				}

				return DoMxpSetup();
			});
		}

		builder.UsePipe(connection.Transport);
		var telnet = await builder.BuildAsync();

		// Before the read loop starts, not after: the loop can deliver a line — a Pueblo handshake buffered
		// with the client's first packet — before an assignment below it would have run, and every callback
		// that reaches for the interpreter would find null.
		telnetInterpreter = telnet;
		if (telnet.PluginManager?.GetPlugin<TerminalQueryProtocol>() is { } terminalQueries)
		{
			_probes.Register(nextPort, () => terminalQueries.ProbeAsync());
		}

		var readTask = ReadAndObserveNegotiationAsync(
			telnet, connection.Transport.Input, AnnounceTelnetIfNegotiatedAsync, ct);
		try
		{
			// The read loop is already running by now, so a fast client could have negotiated in the gap
			// above and found nothing to sample. Re-sampling here closes it without needing a lock.
			await AnnounceTelnetIfNegotiatedAsync();

			var remoteIp = connection.RemoteEndPoint is not IPEndPoint remoteEndpoint
				? "unknown"
				: $"{remoteEndpoint.Address}:{remoteEndpoint.Port}";

			// PennMUSH's CONN_SSL. Kestrel attaches ITlsHandshakeFeature only on an endpoint that actually
			// terminated TLS, so this is the handshake that happened rather than anything the client says
			// about itself — and it is false, correctly, on the plaintext listener.
			var isSecure = connection.Features.Get<ITlsHandshakeFeature>() is not null;

			await _connectionService.RegisterAsync(
				nextPort,
				remoteIp,
				connection.RemoteEndPoint?.ToString() ?? remoteIp,
				"telnet",
			async (data) =>
			{
				// Write output to the network transport using SendAsync which handles
				// IAC (0xFF) escaping and CRLF line termination internally.
				// Write serialization is handled by the library's internal write lock.
				_logger.LogTrace("OutputFunction called with {ByteCount} bytes for handle {Handle}", data.Length, nextPort);
				try
				{
					await telnet.SendAsync(data);
					_logger.LogTrace("Successfully sent {ByteCount} bytes to transport for handle {Handle}",
						data.Length, nextPort);
				}
				catch (ObjectDisposedException ode)
				{
					_logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
				}
			},
			async (data) =>
			{
				// Write prompt output using SendPromptAsync which handles IAC (0xFF) escaping
				// and adds the appropriate prompt terminator (EOR, GA, or CRLF) based on
				// what the client has negotiated.
				// Write serialization is handled by the library's internal write lock.
				try
				{
					await telnet.SendPromptAsync(data);
				}
				catch (ObjectDisposedException ode)
				{
					_logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
				}
			},
			() => telnet.CurrentEncoding,
			connection.Abort,
			async (module, message) =>
			{
				await telnet.SendGMCPCommand(module, message);
			},
			isSecure: isSecure, cancellationToken: ct);

			// The handle exists in the main process from here on, so everything negotiation produced before
			// now can go out and land on a connection that is there to receive it.
			await FlushPendingPublishesAsync();

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

	private Func<TelnetInterpreter, string, ValueTask> MSDPCallback(ConnectionContext connection)
	{
		return async (ti, str) =>
		{
			try
			{
				// Write MSDP response using the library's thread-safe WriteToNetworkAsync
				await ti.WriteToNetworkAsync(ti.CurrentEncoding.GetBytes(str));
			}
			catch (ObjectDisposedException ode)
			{
				_logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
			}
		};
	}
}
