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
	private static readonly ConnectionServerOptions WaitsForNegotiation = new() { NegotiationWaitMilliseconds = 60_000 };

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

	/// <summary>MXP SUPPORT must be asked and retained before the welcome can render.</summary>
	[Test]
	public async Task MxpSupport_IsRequestedBeforeRegistration()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(WaitsForNegotiation, service);
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
		var (toServer, fromServer, handler, published, cts) = StartServer(WaitsForNegotiation, service);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Encoding.ASCII.GetString(bytes).Contains("\u001b[c"));
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

	/// <summary>Terminal queries are sent while registration is held, and their reply seeds the first render.</summary>
	[Test]
	public async Task GraphicsProbe_PrecedesRegistration()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(WaitsForNegotiation, service);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Encoding.ASCII.GetString(bytes).Contains("\u001b[c"));
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

	[Test]
	public async Task NegotiatingClient_RegistersWithWhatItSaid()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(new ConnectionServerOptions { NegotiationWaitMilliseconds = 500 }, service);
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
	public async Task SilentClient_RegistersWhenItTypesALine()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(WaitsForNegotiation, service);
		using var serverCancellation = cts;
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await Assert.That(Registration(service)).IsNull();
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
