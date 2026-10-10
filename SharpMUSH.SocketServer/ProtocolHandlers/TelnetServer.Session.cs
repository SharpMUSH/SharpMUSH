using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Connections.Features;
using SharpMUSH.Library.Utilities;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;
using SharpMUSH.Messaging.Messages;
using System.Net;
using System.Text;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Handlers;
using TelnetNegotiationCore.Interpreters;
using TelnetNegotiationCore.Protocols;

namespace SharpMUSH.SocketServer.ProtocolHandlers;

public partial class TelnetServer
{
	/// <summary>
	/// One telnet connection: the interpreter's negotiation callbacks, the state they share, and the
	/// connection's registration with the main process.
	/// </summary>
	private sealed class TelnetSession(
		TelnetServer server,
		ConnectionContext connection,
		long handle,
		RegistrationHold held,
		CancellationToken ct)
	{
		// Assigned once BuildAsync returns; the callbacks below only run after that, since the
		// interpreter has to exist before it can hand any of them anything.
		private TelnetInterpreter? _telnet;
		private int _telnetAnnounced;
		private IReadOnlyList<string> _publishedTerminalTypes = [];

		public async ValueTask<TelnetInterpreter> BuildInterpreterAsync()
		{
			var terminalTypeProtocol = new TerminalTypeProtocol().OnTerminalTypes(OnTerminalTypesAsync);
			var builder = AddMxp(AddPueblo(AddNegotiationPlugins(server._telnetFactory.CreateBuilder(), terminalTypeProtocol)));

			builder.UsePipe(connection.Transport);
			var telnet = await builder.BuildAsync();

			// Before the read loop starts, not after: the loop can deliver a line — a Pueblo handshake buffered
			// with the client's first packet — before an assignment below it would have run, and every callback
			// that reaches for the interpreter would find null.
			_telnet = telnet;
			return telnet;
		}

		private TelnetInterpreterBuilder AddNegotiationPlugins(
			TelnetInterpreterBuilder builder, TerminalTypeProtocol terminalTypeProtocol) =>
			builder
				.OnSubmit((byteArray, encoding, _) => OnSubmitAsync(encoding.GetString(byteArray)))
				// Each of these callbacks is also a sampling point for AnnounceTelnetIfNegotiatedAsync,
				// which asks every plugin rather than just the one that fired: reaching any of them means
				// some option settled, and a client that answers only NAWS still speaks telnet.
				// The Core session answers Core.Ping and keeps Core.Supports; every message, Core.* included,
				// still reaches the callback below.
				.AddPlugin<GMCPProtocol>().UseGmcpServerSession(out _)
				.OnGMCPMessage(async data =>
				{
					await AnnounceTelnetIfNegotiatedAsync();
					await held.PublishAsync(
						() => server._publishEndpoint.Publish(new GMCPSignalMessage(handle, data.Package, data.Info), ct));
				})
				.OnGMCPNegotiated(OnGmcpNegotiatedAsync)
				.AddPlugin<MSSPProtocol>().WithMSSPConfig(() => server._mssp.Current)
				// A client reporting MSSP to a server means nothing here; the callback is only a sampling point.
				.OnMSSP(async _ => await AnnounceTelnetIfNegotiatedAsync())
				.AddPlugin<NAWSProtocol>().OnNAWS(OnNawsAsync)
				.AddPlugin<MSDPProtocol>().OnMSDPMessage(CreateMsdpHandler().HandleAsync)
				// Prompts end in IAC EOR for a client that agrees to it, the marker Mudlet, TinTin++ and
				// MUSHclient look for; otherwise in IAC GA as before. A client's own EOR means nothing to a
				// server, so the callback is only a sampling point.
				.AddPlugin<EORProtocol>().OnPrompt(async () => await AnnounceTelnetIfNegotiatedAsync())
				// MNES: the client's name and version, for clients that keep them out of TTYPE. IPADDRESS is
				// not read: any client can send one, and believing it would let a client choose the address
				// sitelock and bans see.
				.AddPlugin<NewEnvironProtocol>().OnEnvironmentVariables(OnEnvironmentVariablesAsync)
				.AddPlugin<CharsetProtocol>().WithCharsetOrder(Encoding.GetEncoding("utf-8"), Encoding.GetEncoding("iso-8859-1"))
				// What the client agreed to read: output is written in it, each character it lacks replaced.
				.OnCharsetChange(OnCharsetChangeAsync)
				.AddPlugin<MCCPProtocol>()
				// RFC 1091 terminal type: the only way a client names itself over plain telnet, and what
				// terminfo() reports as the client. Without it every connection is "unknown".
				.AddPlugin(terminalTypeProtocol)
				// Asks the terminal what it can draw, only when the player asks for it (SOCKSET graphics=detect):
				// a client in line mode echoes the answers, so asking unprompted would print them at everyone.
				.AddPlugin<TerminalQueryProtocol>().OnTerminalReport(report =>
					OnTerminalReportAsync(new TerminalProbeResult(report.KittyGraphics, report.Sixel == true,
						report.Version, report.CellWidth ?? 0, report.CellHeight ?? 0)));

		// The handshake itself — the hello, consuming PUEBLOCLIENT, and the start sequence that moves the
		// client into HTML mode — is TelnetNegotiationCore's. What is left here is the part that is this
		// server's: the render format for the connection, and telling the main process.
		private TelnetInterpreterBuilder AddPueblo(TelnetInterpreterBuilder builder) =>
			!server._options.PuebloEnabled
				? builder
				: builder.AddPlugin<PuebloProtocol>().OnPuebloEnabled(client => OnPuebloEnabledAsync(client.Version));

		private TelnetInterpreterBuilder AddMxp(TelnetInterpreterBuilder builder) =>
			!server._options.MxpEnabled
				? builder
				: builder.AddPlugin<MXPProtocol>()
					// Registered with the plugin rather than when the question goes out: a client can answer
					// the moment MXP mode starts, and the reply must never arrive before anything is listening.
					.OnMxpSupports(_ =>
					{
						server.RecordMxpSupport(handle, _telnet);
						return ValueTask.CompletedTask;
					})
					.OnMXPEnabled(() =>
					{
						server._logger.LogInformation("MXP negotiated on handle {Handle}", handle);
						return OnMxpEnabledAsync();
					});

		// Reports the client as speaking telnet the first time any option has genuinely negotiated.
		// TelnetNegotiationCore tracks that per plugin as ITelnetProtocolPlugin.IsNegotiated — set at
		// the state where a WILL/DO exchange resolves, so it is an answer from the client rather than
		// an option we merely offered. Sampling beats subscribing here: the library raises no event for
		// "some option settled", and the flag is monotonic, so one publish per connection is enough.
		public async ValueTask AnnounceTelnetIfNegotiatedAsync()
		{
			if (Volatile.Read(ref _telnetAnnounced) != 0
					|| !AnyOptionNegotiated()
					|| Interlocked.Exchange(ref _telnetAnnounced, 1) != 0)
			{
				return;
			}

			server._logger.LogDebug("Telnet negotiation confirmed on handle {Handle}", handle);
			await held.PublishAsync(
				() => server._publishEndpoint.Publish(new TelnetNegotiatedMessage(handle), ct));
		}

		// The terminal-query plugin counts itself negotiated as soon as it is registered — it is not a
		// telnet option and nothing is exchanged — so it says nothing about whether the client speaks telnet.
		private bool AnyOptionNegotiated() =>
			_telnet?.PluginManager?.GetAllPlugins()
				.Any(plugin => plugin.IsNegotiated && plugin is not TerminalQueryProtocol) == true;

		private async ValueTask OnSubmitAsync(string input)
		{
			// By the time a client has sent a line, whatever it was going to negotiate has settled.
			await AnnounceTelnetIfNegotiatedAsync();

			// Held like the negotiation messages: a client that types as soon as it connects can
			// deliver a line before RegisterAsync, and published then it carries no SessionId, so the
			// engine skips its registration wait and parses it for a handle it does not know yet — the
			// line is lost. The SessionId is read when the message goes out, after registration.
			await held.PublishAsync(() => ConnectionInputPublisher.PublishAsync(server._publishEndpoint,
				server._connectionService, server._logger, handle,
				new TelnetInputMessage(handle, input, server._connectionService.Get(handle)?.SessionId), ct),
				// A blank line is still a queued message, so it counts too.
				Math.Max(input.Length, 1));
		}

		private async ValueTask OnTerminalTypesAsync(IReadOnlyList<string> terminalTypes)
		{
			if (_publishedTerminalTypes.SequenceEqual(terminalTypes))
			{
				return;
			}

			var snapshot = terminalTypes.ToArray();
			_publishedTerminalTypes = snapshot;

			// MTTS is the only thing that ever tells us a client can render more than 16 colours.
			// Until it was read, ProtocolCapabilities.SupportsXterm256 sat at its default of
			// false for every telnet connection, and every xterm256 colour the game produced was
			// sent as one of the sixteen — to clients that had said, in the one place there is to
			// say it, that they could display them.
			await held.PublishAsync(async () =>
			{
				server.TryApplyTerminalCapabilities(handle, snapshot);
				await server._publishEndpoint.Publish(
					new TerminalTypeNegotiatedMessage(handle, snapshot), ct);
			});
		}

		private async ValueTask OnEnvironmentVariablesAsync(
			Dictionary<string, string> environment, Dictionary<string, string> user)
		{
			await AnnounceTelnetIfNegotiatedAsync();

			var name = Lookup("CLIENT_NAME");
			var version = Lookup("CLIENT_VERSION");
			if (name is null && version is null) return;

			await held.PublishAsync(
				() => server._publishEndpoint.Publish(new ClientIdentityMessage(handle, name, version), ct));

			// MNES sends these as VAR, but a client that sends them as USERVAR means the same thing.
			string? Lookup(string variable) =>
				environment.TryGetValue(variable, out var value) || user.TryGetValue(variable, out value) ? value : null;
		}

		private async ValueTask OnGmcpNegotiatedAsync(bool agreed)
		{
			await AnnounceTelnetIfNegotiatedAsync();
			await held.PublishAsync(
				() => server._publishEndpoint.Publish(new GMCPNegotiatedMessage(handle, agreed), ct));
		}

		/// <summary>
		/// Answers MSDP, natively or over GMCP. The game reports no MSDP variables of its own: a client is
		/// told the game's name (<c>SERVER_ID</c>), and the client variables it sets are recorded on the
		/// connection.
		/// </summary>
		private MSDPServerHandler CreateMsdpHandler() =>
			new(new MSDPServerModel(_ => ValueTask.CompletedTask)
			{
				Commands = () => ["LIST", "REPORT", "RESET", "SEND", "UNREPORT"],
				Configurable_Variables = () => ["CLIENT_NAME", "CLIENT_VERSION", "PLUGIN_ID"],
				Sendable_Variables = new() { ["SERVER_ID"] = () => server._mssp.Current.Name },
				SetCallbackAsync = OnMsdpVariableSetAsync
			}, server._logger);

		private async ValueTask OnMsdpVariableSetAsync(string variable, string value)
		{
			await AnnounceTelnetIfNegotiatedAsync();
			await held.PublishAsync(() => server._publishEndpoint.Publish(
				new MSDPUpdateMessage(handle, new Dictionary<string, string> { [variable] = value }), ct));
		}

		private async ValueTask OnNawsAsync(int newHeight, int newWidth)
		{
			await AnnounceTelnetIfNegotiatedAsync();
			await held.PublishAsync(() =>
			{
				// Kept here as well as sent to the engine: the renderer lays automatic-width boxes out at it.
				server._connectionService.UpdateCapabilities(handle, current => current with { Width = newWidth });
				return server._publishEndpoint.Publish(new NAWSUpdateMessage(handle, newHeight, newWidth), ct);
			});
		}

		private async ValueTask OnCharsetChangeAsync(Encoding encoding)
		{
			await AnnounceTelnetIfNegotiatedAsync();
			await held.PublishAsync(() =>
			{
				server._connectionService.UpdateCapabilities(handle, current => current with
				{
					Charset = TerminalCharsets.Parse(encoding.WebName) ?? encoding.WebName
				});
				return Task.CompletedTask;
			});
		}

		private async ValueTask OnTerminalReportAsync(TerminalProbeResult answered) =>
			await held.PublishAsync(async () =>
			{
				server._connectionService.UpdateCapabilities(handle, current => current with
				{
					Probe = answered,
					CellWidth = answered.CellWidth,
					CellHeight = answered.CellHeight
				});
				await server._publishEndpoint.Publish(new TerminalReportMessage(handle, answered), ct);
			});

		private async ValueTask OnPuebloEnabledAsync(string clientVersion)
		{
			server._logger.LogDebug("Pueblo negotiated on handle {Handle}", handle);

			if (await server.TryUpdateFormatAsync(handle, OutputFormat.Pueblo, ct))
			{
				server._logger.LogDebug("Updated Pueblo capabilities for handle {Handle}", handle);
			}

			await held.PublishAsync(() => server._publishEndpoint.Publish(
				new PuebloNegotiatedMessage(handle, PuebloProtocol.ClientCommand + clientVersion), ct));
		}

		private async ValueTask OnMxpEnabledAsync()
		{
			await AnnounceTelnetIfNegotiatedAsync();

			if (await server.TryUpdateFormatAsync(handle, OutputFormat.Mxp, ct))
			{
				server._logger.LogDebug("Updated MXP capabilities for handle {Handle}", handle);
				server.StartAskingWhatMxpClientRenders(handle, _telnet, ct);
			}
			else if (!ct.IsCancellationRequested)
			{
				server._logger.LogWarning("MXP negotiated but connection {Handle} not yet registered", handle);
			}

			await held.PublishAsync(
				() => server._publishEndpoint.Publish(new MxpNegotiatedMessage(handle), ct));
		}

		/// <summary>
		/// Registers the connection with the main process, handing it this connection's writers.
		/// </summary>
		public async Task RegisterAsync()
		{
			var telnet = _telnet!;
			var remoteIp = connection.RemoteEndPoint is not IPEndPoint remoteEndpoint
				? "unknown"
				: $"{remoteEndpoint.Address}:{remoteEndpoint.Port}";

			// PennMUSH's CONN_SSL. Kestrel attaches ITlsHandshakeFeature only on an endpoint that actually
			// terminated TLS, so this is the handshake that happened rather than anything the client says
			// about itself — and it is false, correctly, on the plaintext listener.
			var isSecure = connection.Features.Get<ITlsHandshakeFeature>() is not null;

			await server._connectionService.RegisterAsync(
				handle,
				remoteIp,
				connection.RemoteEndPoint?.ToString() ?? remoteIp,
				"telnet",
				WriteOutputAsync,
				WritePromptAsync,
				() => telnet.CurrentEncoding,
				connection.Abort,
				async (module, message) =>
				{
					await telnet.SendGMCPCommand(module, message);
				},
				isSecure: isSecure, cancellationToken: ct);
		}

		private async ValueTask WriteOutputAsync(byte[] data)
		{
			// Write output to the network transport using SendAsync which handles
			// IAC (0xFF) escaping and CRLF line termination internally.
			// Write serialization is handled by the library's internal write lock.
			server._logger.LogTrace("OutputFunction called with {ByteCount} bytes for handle {Handle}", data.Length, handle);
			try
			{
				await _telnet!.SendAsync(data);
				server._logger.LogTrace("Successfully sent {ByteCount} bytes to transport for handle {Handle}",
					data.Length, handle);
			}
			catch (ObjectDisposedException ode)
			{
				server._logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
			}
			catch (Exception ex)
			{
				server._logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
			}
		}

		private async ValueTask WritePromptAsync(byte[] data)
		{
			// Write prompt output using SendPromptAsync which handles IAC (0xFF) escaping
			// and adds the appropriate prompt terminator (EOR, GA, or CRLF) based on
			// what the client has negotiated.
			// Write serialization is handled by the library's internal write lock.
			try
			{
				await _telnet!.SendPromptAsync(data);
			}
			catch (ObjectDisposedException ode)
			{
				server._logger.LogError(ode, "{ConnectionId} Stream has been closed", connection.ConnectionId);
			}
			catch (Exception ex)
			{
				server._logger.LogError(ex, "{ConnectionId} Unexpected Exception occurred", connection.ConnectionId);
			}
		}
	}

	/// <summary>
	/// Holds what a connection publishes until the main process has registered it.
	/// </summary>
	/// <remarks>
	/// Anything that writes connection metadata in the main process, and any line of input, has to arrive after the handle
	/// is registered there, because every one of those consumers gives up on an unregistered handle
	/// after ConnectionRetryPolicy's five 50ms attempts. The read loop starts before RegisterAsync,
	/// so a client that answers TTYPE within one round trip
	/// can outrun its own registration and have its terminal type silently dropped. Widening the
	/// consumers' retry window would only make that less likely; holding the messages here makes the
	/// ordering a fact.
	/// </remarks>
	private sealed class RegistrationHold(long handle, ILogger logger, CancellationToken ct) : IDisposable
	{
		private readonly object _pendingLock = new();
		private readonly SemaphoreSlim _publishGate = new(1, 1);
		// Null once registration has happened, after which publishing is direct.
		private List<Func<Task>>? _pending = [];
		// Input is held too (see OnSubmitAsync), but a client decides how much of it there is, so what it may
		// send before registration is capped rather than buffered for as long as registration takes.
		private int _heldInputChars;
		private bool _heldInputDropped;

		public async ValueTask PublishAsync(Func<Task> publish, int inputChars = 0)
		{
			if (TryHold(publish, inputChars))
			{
				return;
			}

			await _publishGate.WaitAsync(ct);
			try
			{
				await publish();
			}
			finally
			{
				_publishGate.Release();
			}
		}

		/// <summary>
		/// Publishes everything held, and from then on lets every publish straight through.
		/// </summary>
		public async ValueTask ReleaseAsync()
		{
			await _publishGate.WaitAsync(ct);
			try
			{
				// In the order they were produced: a client's terminal-type list is reported once per entry,
				// and the last report is the complete one.
				foreach (var publish in TakeHeld())
				{
					await PublishHeldAsync(publish);
				}
			}
			finally
			{
				_publishGate.Release();
			}
		}

		public void Dispose() => _publishGate.Dispose();

		// True when the publish was held, or dropped as input over the cap; false once registration has happened.
		private bool TryHold(Func<Task> publish, int inputChars)
		{
			lock (_pendingLock)
			{
				if (_pending is null)
				{
					return false;
				}

				if (!IsOverInputCap(inputChars))
				{
					_pending.Add(publish);
				}

				return true;
			}
		}

		private bool IsOverInputCap(int inputChars)
		{
			if (inputChars <= 0 || (_heldInputChars += inputChars) <= MaxInputHeldBeforeRegistration)
			{
				return false;
			}

			if (!_heldInputDropped)
			{
				_heldInputDropped = true;
				logger.LogWarning("Dropping input on handle {Handle}: over {Limit} characters sent before it was registered",
					handle, MaxInputHeldBeforeRegistration);
			}

			return true;
		}

		private Func<Task>[] TakeHeld()
		{
			lock (_pendingLock)
			{
				Func<Task>[] queued = [.. _pending ?? []];
				_pending = null;
				return queued;
			}
		}

		// Each on its own: a negotiation report that fails must not keep the input queued behind it
		// from going out.
		private async Task PublishHeldAsync(Func<Task> publish)
		{
			try
			{
				await publish();
			}
			catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
			{
				logger.LogWarning(ex, "A message held for registration could not be published for handle {Handle}", handle);
			}
		}
	}
}
