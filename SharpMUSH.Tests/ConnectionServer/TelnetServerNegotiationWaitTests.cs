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

	[Test]
	public async Task NegotiatingClient_RegistersWithWhatItSaid()
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(WaitsForNegotiation, service);
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
