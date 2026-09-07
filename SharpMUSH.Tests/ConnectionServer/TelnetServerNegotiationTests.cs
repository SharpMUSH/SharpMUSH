using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharpMUSH.ConnectionServer.Configuration;
using SharpMUSH.ConnectionServer.Models;
using SharpMUSH.ConnectionServer.ProtocolHandlers;
using SharpMUSH.ConnectionServer.Services;
using SharpMUSH.Messaging.Abstractions;
using SharpMUSH.Messaging.Messages;
using System.IO.Pipelines;
using System.Net;
using System.Text;
using TelnetNegotiationCore.Builders;
using TelnetNegotiationCore.Interpreters;

namespace SharpMUSH.Tests.ConnectionServer;

/// <summary>
/// What the telnet listener actually offers a connecting client, driven over a real duplex pipe
/// rather than a socket. These cover the two options a MUD client is judged by and that SharpMUSH
/// was silently not offering: RFC 1091 terminal type (without which <c>terminfo()</c> can only ever
/// answer "unknown") and MXP (without which no client can turn it on, however capable).
/// </summary>
public class TelnetServerNegotiationTests
{
	private const byte IAC = 255;
	private const byte DO = 253;
	private const byte WILL = 251;
	private const byte SB = 250;
	private const byte SE = 240;
	private const byte IS = 0;
	private const byte SEND = 1;
	private const byte TTYPE = 24;
	private const byte MXP = 91;

	private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

	/// <summary>A builder factory in server mode, standing in for the one DI registers.</summary>
	private sealed class ServerBuilderFactory : ITelnetInterpreterFactory
	{
		public TelnetInterpreterBuilder CreateBuilder() =>
			new TelnetInterpreterBuilder()
				.UseMode(TelnetInterpreter.TelnetMode.Server)
				.UseLogger(NullLogger<TelnetInterpreter>.Instance);
	}

	private sealed class PipeDuplex(PipeReader input, PipeWriter output) : IDuplexPipe
	{
		public PipeReader Input { get; } = input;
		public PipeWriter Output { get; } = output;
	}

	private sealed class FakeConnectionContext(IDuplexPipe transport, CancellationToken closed) : ConnectionContext
	{
		public override IDuplexPipe Transport { get; set; } = transport;
		public override string ConnectionId { get; set; } = "test-connection";
		public override IFeatureCollection Features { get; } = new FeatureCollection();
		public override IDictionary<object, object?> Items { get; set; } = new Dictionary<object, object?>();
		public override CancellationToken ConnectionClosed { get; set; } = closed;
		public override EndPoint? RemoteEndPoint { get; set; } = new IPEndPoint(IPAddress.Loopback, 4321);
	}

	/// <summary>
	/// Runs the handler against a pipe pair and returns the client half: what to write to the server,
	/// what the server wrote back, and the handler's own task.
	/// </summary>
	private static (PipeWriter ToServer, PipeReader FromServer, Task Handler, List<object> Published, CancellationTokenSource Cts)
		StartServer(ConnectionServerOptions? options = null, IConnectionServerService? connectionService = null)
	{
		var clientToServer = new Pipe();
		var serverToClient = new Pipe();
		var cts = new CancellationTokenSource();

		var published = new List<object>();
		var bus = Substitute.For<IMessageBus>();
		bus.Publish(Arg.Any<object>(), Arg.Any<CancellationToken>())
			.ReturnsForAnyArgs(call =>
			{
				lock (published) published.Add(call[0]);
				return Task.CompletedTask;
			});

		var descriptors = Substitute.For<IDescriptorGeneratorService>();
		descriptors.GetNextTelnetDescriptor().Returns(42L);

		var server = new TelnetServer(
			NullLogger<TelnetServer>.Instance,
			connectionService ?? Substitute.For<IConnectionServerService>(),
			bus,
			descriptors,
			new ServerBuilderFactory(),
			options ?? new ConnectionServerOptions());

		var context = new FakeConnectionContext(
			new PipeDuplex(clientToServer.Reader, serverToClient.Writer), cts.Token);

		return (clientToServer.Writer, serverToClient.Reader, server.OnConnectedAsync(context), published, cts);
	}

	/// <summary>Reads from the server until <paramref name="predicate"/> accepts what has arrived so far.</summary>
	private static async Task<byte[]> ReadUntilAsync(PipeReader reader, Func<byte[], bool> predicate)
	{
		var seen = new List<byte>();
		using var cts = new CancellationTokenSource(Timeout);

		while (!predicate([.. seen]))
		{
			var result = await reader.ReadAsync(cts.Token);
			foreach (var segment in result.Buffer)
			{
				seen.AddRange(segment.ToArray());
			}

			reader.AdvanceTo(result.Buffer.End);

			if (result.IsCompleted && !predicate([.. seen]))
			{
				throw new InvalidOperationException(
					$"Server closed the pipe before the expected bytes arrived. Saw: {Describe(seen)}");
			}
		}

		return [.. seen];
	}

	private static string Describe(IEnumerable<byte> bytes) => string.Join(" ", bytes.Select(b => b.ToString()));

	private static bool Contains(byte[] haystack, params byte[] needle)
	{
		for (var i = 0; i + needle.Length <= haystack.Length; i++)
		{
			if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
			{
				return true;
			}
		}

		return false;
	}

	private static async Task WriteAsync(PipeWriter writer, params byte[] bytes)
	{
		await writer.WriteAsync(bytes);
		await writer.FlushAsync();
	}

	private static async Task<T?> WaitForPublishedAsync<T>(List<object> published) where T : class
	{
		var deadline = DateTimeOffset.UtcNow + Timeout;
		while (DateTimeOffset.UtcNow < deadline)
		{
			lock (published)
			{
				if (published.OfType<T>().FirstOrDefault() is { } found)
				{
					return found;
				}
			}

			await Task.Delay(25);
		}

		return null;
	}

	[Test]
	public async Task ServerOffersTerminalTypeNegotiation()
	{
		var (toServer, fromServer, handler, _, cts) = StartServer();
		try
		{
			var negotiation = await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, DO, TTYPE));
			await Assert.That(Contains(negotiation, IAC, DO, TTYPE)).IsTrue()
				.Because("terminfo() can only name a client that was asked to name itself (RFC 1091)");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task ServerOffersMxpByDefault()
	{
		var (toServer, fromServer, handler, _, cts) = StartServer();
		try
		{
			var negotiation = await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, WILL, MXP));
			await Assert.That(Contains(negotiation, IAC, WILL, MXP)).IsTrue()
				.Because("a client cannot turn MXP on unless the server offers telnet option 91");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task ServerOmitsMxp_WhenDisabled()
	{
		var (toServer, fromServer, handler, _, cts) = StartServer(new ConnectionServerOptions { MxpEnabled = false });
		try
		{
			// TTYPE is offered in the same initial burst, so seeing it means MXP's absence is settled
			// rather than merely not having arrived yet.
			var negotiation = await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, DO, TTYPE));
			await Assert.That(Contains(negotiation, IAC, WILL, MXP)).IsFalse()
				.Because("MxpEnabled=false must keep the option off the wire");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task ClientTerminalType_IsPublishedToTheMainProcess()
	{
		var (toServer, fromServer, handler, published, cts) = StartServer();
		try
		{
			await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, DO, TTYPE));
			await WriteAsync(toServer, IAC, WILL, TTYPE);

			// The server answers WILL with IAC SB TTYPE SEND IAC SE, and asks again after each answer
			// until one repeats — RFC 1091's way of saying the list has ended.
			var name = Encoding.ASCII.GetBytes("SharpMUTerm");
			foreach (var _ in Enumerable.Range(0, 2))
			{
				await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, SB, TTYPE, SEND, IAC, SE));
				await WriteAsync(toServer, [IAC, SB, TTYPE, IS, .. name, IAC, SE]);
			}

			var message = await WaitForPublishedAsync<TerminalTypeNegotiatedMessage>(published);

			await Assert.That(message).IsNotNull()
				.Because("the terminal type lives in the main process's connection metadata, so it has to be published");
			await Assert.That(message!.Handle).IsEqualTo(42L);
			await Assert.That(message.TerminalTypes).Contains("SharpMUTerm")
				.Because("terminfo() reports the first reported terminal type as the client");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	/// <summary>
	/// The plaintext listener attaches no ITlsHandshakeFeature, so a connection on it must report
	/// ssl() as false. The claim is made from the handshake that happened, never from the port
	/// number — a TLS endpoint is what makes it true, and there is none here.
	/// </summary>
	[Test]
	public async Task PlaintextListener_DoesNotClaimSsl()
	{
		var connectionService = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, _, cts) = StartServer(connectionService: connectionService);
		try
		{
			await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, DO, TTYPE));

			await connectionService.Received(1).RegisterAsync(
				Arg.Any<long>(), Arg.Any<string>(), Arg.Any<string>(), "telnet",
				Arg.Any<Func<byte[], ValueTask>>(), Arg.Any<Func<byte[], ValueTask>>(),
				Arg.Any<Func<Encoding>>(), Arg.Any<Action>(),
				Arg.Any<Func<string, string, ValueTask>>(),
				Arg.Any<ProtocolCapabilities?>(), Arg.Any<string>(),
				isSecure: false);
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task AnsweringAnOption_MarksTheClientAsSpeakingTelnet()
	{
		var (toServer, fromServer, handler, published, cts) = StartServer();
		try
		{
			await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, DO, TTYPE));
			await WriteAsync(toServer, IAC, WILL, TTYPE);

			// The client need not finish naming itself; agreeing to the option is already proof it
			// speaks telnet, and a crawler that reads the login screen and leaves never sends a line.
			await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, SB, TTYPE, SEND, IAC, SE));

			await Assert.That(await WaitForPublishedAsync<TelnetNegotiatedMessage>(published)).IsNotNull()
				.Because("terminfo() reports \"telnet\" from a negotiated option, so one has to be reported");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}

	[Test]
	public async Task RawSocket_IsNotMarkedAsSpeakingTelnet()
	{
		var (toServer, fromServer, handler, published, cts) = StartServer();
		try
		{
			await ReadUntilAsync(fromServer, seen => Contains(seen, IAC, DO, TTYPE));

			// A bare "nc host port" session: it ignores every option we offered and just types.
			await WriteAsync(toServer, Encoding.ASCII.GetBytes("connect God\r\n"));

			var input = await WaitForPublishedAsync<TelnetInputMessage>(published);
			await Assert.That(input).IsNotNull()
				.Because("the line still has to reach the parser; only the telnet claim is withheld");

			bool claimed;
			lock (published)
			{
				claimed = published.OfType<TelnetNegotiatedMessage>().Any();
			}

			await Assert.That(claimed).IsFalse()
				.Because("arriving on the telnet port is not the same as speaking telnet");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
		}
	}
}
