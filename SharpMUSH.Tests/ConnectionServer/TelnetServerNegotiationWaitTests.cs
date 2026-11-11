using NSubstitute;
using SharpMUSH.Library.Utilities;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.SocketServer.Configuration;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;
using System.Text;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// A new telnet connection registers, and so is sent the connect screen, once the client has said what it can
/// show, not before.
/// </summary>
public partial class TelnetServerNegotiationTests
{

	private static NSubstitute.Core.ICall? Registration(IConnectionServerService service) =>
		service.ReceivedCalls().SingleOrDefault(call => call.GetMethodInfo().Name == nameof(IConnectionServerService.RegisterAsync));

	private static async Task<ProtocolCapabilities> RegisteredCapabilitiesAsync(IConnectionServerService service)
	{
		using var deadline = new CancellationTokenSource(Timeout);
		var call = Registration(service);
		while (call is null)
		{
			await Task.Delay(10, deadline.Token);
			call = Registration(service);
		}

		return (ProtocolCapabilities)call.GetArguments()[9]!;
	}

	[Test]
	public async Task AutoLoginBeforeNegotiation_DoesNotReleaseRegistrationOrInput()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 1000);
		using var cancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, MXP));
			await WriteAsync(toServer, "connect guest\r\nlook\r\n"u8.ToArray());
			// A subsequent protocol round-trip proves the parser has consumed the preceding login.
			await WriteAsync(toServer, IAC, DO, MXP);
			await ReadUntilAsync(fromServer, bytes => Encoding.ASCII.GetString(bytes).Contains("<SUPPORT "));
			await Task.Delay(100);
			await Assert.That(Registration(service)).IsNull();
			TelnetInputMessage[] earlyInput;
			lock (published) earlyInput = [.. published.OfType<TelnetInputMessage>()];
			await Assert.That(earlyInput).IsEmpty();
			await WriteAsync(toServer, "\u001b[1z<SUPPORTS +IMAGE -FRAME>\r\n\u001b[?62;4c"u8.ToArray());
			var capabilities = await RegisteredCapabilitiesAsync(service);
			await Assert.That(capabilities.MxpSupported).Contains("IMAGE");
			await Assert.That(capabilities.Probe?.Sixel).IsTrue();
			var inputs = await WaitForInputsAsync(published, 2);
			await Assert.That(inputs.Length).IsEqualTo(2);
			await Assert.That(inputs[0].Input).IsEqualTo("connect guest");
			await Assert.That(inputs[1].Input).IsEqualTo("look");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task CancelledStartupDoesNotRegisterConnection()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 60_000);
		using var cancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, DO, TTYPE));
			await cts.CancelAsync();
			await handler.WaitAsync(Timeout);
			await Assert.That(Registration(service)).IsNull();
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task DisconnectedStartupDoesNotRegisterOrReplayAutoLogin()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 60_000);
		using var cancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, MXP));
			await WriteAsync(toServer, "connect guest\r\n"u8.ToArray());
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
			await Assert.That(Registration(service)).IsNull();
			await Assert.That(published.OfType<TelnetInputMessage>()).IsEmpty();
		}
		finally
		{
			await cts.CancelAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	/// <summary>MXP SUPPORT must be asked and retained before the welcome can render.</summary>
	[Test]
	public async Task MxpSupport_IsRequestedBeforeRegistration()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 1000);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, MXP));
			await WriteAsync(toServer, IAC, DO, MXP);
			await ReadUntilAsync(fromServer, bytes => Encoding.ASCII.GetString(bytes).Contains("<SUPPORT "));
			await Assert.That(Registration(service)).IsNull();
			await WriteAsync(toServer, Encoding.ASCII.GetBytes("\u001b[1z<SUPPORTS +IMAGE -FRAME>\r\n"));
			await WriteAsync(toServer, "\r\n"u8.ToArray());
			var capabilities = await RegisteredCapabilitiesAsync(service);
			await Assert.That(capabilities.Format).IsEqualTo(OutputFormat.Mxp);
			await Assert.That(capabilities.MxpSupported).Contains("IMAGE");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	/// <summary>Flushing partial reports cannot overwrite the complete snapshot used by the welcome.</summary>
	[Test]
	public async Task HeldTerminalReportsDoNotRegressInitialCapabilities()
	{
		var service = Substitute.For<IConnectionServerService>();
		var reports = new List<TerminalProbeResult>();
		service.UpdateCapabilities(Arg.Any<long>(), Arg.Any<Func<ProtocolCapabilities, ProtocolCapabilities>>())
			.Returns(call =>
			{
				var updated = call.Arg<Func<ProtocolCapabilities, ProtocolCapabilities>>()(new ProtocolCapabilities());
				if (updated.Probe is { } probe) reports.Add(probe);
				return true;
			});
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 1000);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, DO, TTYPE));
			await WriteAsync(toServer, "\u001b[6;17;8t\u001b[?62;4clook\r\n"u8.ToArray());
			await WaitForPublishedAsync<TelnetInputMessage>(published);
			await Assert.That(reports.Count).IsGreaterThanOrEqualTo(2);
			await Assert.That(reports.All(report => report.Sixel == true && report.CellWidth == 8)).IsTrue();
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	/// <summary>
	/// Two NAWS reports before registration: the renderer keeps the last width while the held reports replay,
	/// whether the window grew or shrank, and the engine still hears both sizes in order.
	/// </summary>
	[Test]
	[Arguments(80, 120)]
	[Arguments(120, 80)]
	public async Task HeldNawsReportsDoNotRollBackRendererWidth(int first, int last)
	{
		const byte NAWS = 31;
		var service = Substitute.For<IConnectionServerService>();
		var widths = new List<int>();
		service.UpdateCapabilities(Arg.Any<long>(), Arg.Any<Func<ProtocolCapabilities, ProtocolCapabilities>>())
			.Returns(call =>
			{
				var updated = call.Arg<Func<ProtocolCapabilities, ProtocolCapabilities>>()(new ProtocolCapabilities(Width: -1));
				if (updated.Width != -1) lock (widths) widths.Add(updated.Width);
				return true;
			});
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 1000);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, DO, TTYPE));
			await WriteAsync(toServer, [
				IAC, WILL, NAWS,
				IAC, SB, NAWS, 0, (byte)first, 0, 24, IAC, SE,
				IAC, SB, NAWS, 0, (byte)last, 0, 24, IAC, SE,
				.. "\u001b[?62;4clook\r\n"u8.ToArray()]);
			await WaitForPublishedAsync<TelnetInputMessage>(published);

			await Assert.That((await RegisteredCapabilitiesAsync(service)).Width).IsEqualTo(last);
			int[] seen;
			lock (widths) seen = [.. widths];
			await Assert.That(seen).IsNotEmpty();
			await Assert.That(seen.All(width => width == last)).IsTrue();
			int[] engineWidths;
			lock (published) engineWidths = [.. published.OfType<NAWSUpdateMessage>().Select(message => message.Width)];
			await Assert.That(engineWidths.SequenceEqual([first, last])).IsTrue();
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	/// <summary>A terminal report that arrives while registration is held seeds the first render.</summary>
	[Test]
	public async Task TerminalReportBeforeRegistration_SeedsTheFirstRender()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 1000);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, DO, TTYPE));
			await Assert.That(Registration(service)).IsNull();
			await WriteAsync(toServer, "\u001b[?62;4clook\r\n"u8.ToArray());
			var capabilities = await RegisteredCapabilitiesAsync(service);
			await Assert.That(capabilities.Probe?.Sixel).IsTrue();
			await Assert.That(capabilities.Pins?.Graphics).IsEqualTo(TerminalGraphics.Auto);
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	/// <summary>
	/// Nothing is asked about graphics at connect, whatever the client calls itself: the questions are escape
	/// sequences, and a MUD client prints them. A connection is known by the name it reports.
	/// </summary>
	[Test]
	[Arguments("")]
	[Arguments("XTERM-KITTY")]
	[Arguments("MUDRAMMER,XTERM,ANSI")]
	[Arguments("BEIPMU")]
	[Arguments("ANSI")]
	[Arguments("VT100")]
	[Arguments("XTERM-256COLOR")]
	[Arguments("MUDLET,XTERM,MTTS 2349")]
	public async Task NoClientIsAskedAboutGraphicsAtConnect(string terminalTypeCycle)
	{
		var terminalTypes = terminalTypeCycle.Split(',', StringSplitOptions.RemoveEmptyEntries);
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 500);
		using var serverCancellation = cts;
		try
		{
			var seen = new List<byte>(await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, DO, TTYPE)));
			if (terminalTypes.Length > 0)
			{
				await WriteAsync(toServer, IAC, WILL, TTYPE);
				seen.AddRange(await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, TTYPE, SEND)));
				foreach (var type in terminalTypes.Append(terminalTypes[^1]))
				{
					await WriteAsync(toServer, [IAC, SB, TTYPE, IS, .. Encoding.ASCII.GetBytes(type), IAC, SE]);
				}
			}

			await RegisteredCapabilitiesAsync(service);
			// Refusing an unknown option proves everything the server sent before it has arrived.
			await WriteAsync(toServer, IAC, DO, 200);
			seen.AddRange(await ReadUntilAsync(fromServer,
				bytes => Contains(bytes, IAC, (byte)TelnetNegotiationCore.Models.Trigger.WONT, 200)));
			await Assert.That(Encoding.ASCII.GetString([.. seen])).DoesNotContain("\u001b[c");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task NegotiatingClient_RegistersWithWhatItSaid()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 500);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET) && Contains(bytes, IAC, DO, TTYPE));
			await WriteAsync(toServer, IAC, WILL, TTYPE, IAC, DO, CHARSET);
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET, REQUEST) && Contains(bytes, IAC, SB, TTYPE, SEND));
			await WriteAsync(toServer, [IAC, SB, CHARSET, ACCEPTED, .. "utf-8"u8.ToArray(), IAC, SE]);
			await WriteAsync(toServer, [IAC, SB, TTYPE, IS, .. "MUDLET"u8.ToArray(), IAC, SE]);
			await WriteAsync(toServer, [IAC, SB, TTYPE, IS, .. "MTTS 13"u8.ToArray(), IAC, SE]);
			await Assert.That(Registration(service)).IsNull();
			await WriteAsync(toServer, [IAC, SB, TTYPE, IS, .. "MTTS 13"u8.ToArray(), IAC, SE]);

			var capabilities = await RegisteredCapabilitiesAsync(service);
			await Assert.That(capabilities.OutputCharset).IsEqualTo(TerminalCharsets.Utf8);
			await Assert.That(capabilities.SupportsXterm256).IsTrue();
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task ClientWithoutCapabilityReplies_KeepsInputQueued()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 60_000);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await Assert.That(Registration(service)).IsNull();
			await WriteAsync(toServer, "look\r\n"u8.ToArray());

			// Refusing an unknown option proves the preceding command was consumed without
			// answering any capability probes or completing initial negotiation.
			await WriteAsync(toServer, IAC, DO, 200);
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, (byte)TelnetNegotiationCore.Models.Trigger.WONT, 200));
			await Assert.That(Registration(service)).IsNull();
			TelnetInputMessage[] earlyInput;
			lock (published) earlyInput = [.. published.OfType<TelnetInputMessage>()];
			await Assert.That(earlyInput).IsEmpty();
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task SilentClient_ReleasesQueuedInputAtDeadline()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service, negotiationWaitMilliseconds: 1000);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await WriteAsync(toServer, "look\r\n"u8.ToArray());
			var input = await WaitForPublishedAsync<TelnetInputMessage>(published);
			await Assert.That(input?.Input).IsEqualTo("look");
			await Assert.That((await RegisteredCapabilitiesAsync(service)).OutputCharset).IsEqualTo(TerminalCharsets.Ascii);
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}
}
