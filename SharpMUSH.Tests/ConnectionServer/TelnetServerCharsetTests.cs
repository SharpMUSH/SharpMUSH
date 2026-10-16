using NSubstitute;
using SharpMUSH.Messaging.Messages;
using SharpMUSH.RenderingWorker.Services;
using SharpMUSH.SocketServer.Models;
using SharpMUSH.SocketServer.Services;
using System.Text;

namespace SharpMUSH.Tests.ConnectionServer;

public partial class TelnetServerNegotiationTests
{
	private const byte CHARSET = 42;
	private const byte REQUEST = 1;
	private const byte ACCEPTED = 2;
	private const byte REJECTED = 3;

	[Test]
	public async Task CharsetOffer_ContainsOnlyRendererEncodingsInPreferenceOrder()
	{
		var (toServer, fromServer, handler, _, cts) = StartServer();
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await WriteAsync(toServer, IAC, DO, CHARSET);
			var offer = await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET, REQUEST) && Contains(bytes, IAC, SE));
			await Assert.That(Contains(offer, [IAC, SB, CHARSET, REQUEST,
				.. ";utf-8;iso-8859-1;us-ascii"u8.ToArray(), IAC, SE])).IsTrue().Because(Describe(offer));
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
			cts.Dispose();
		}
	}

	[Test]
	[Arguments("utf-8", "utf-8", "café", "636166C3A9", false)]
	[Arguments("ISO-8859-1", "latin-1", "café", "636166E9", true)]
	[Arguments("iso-ir-100", "latin-1", "café", "636166E9", false)]
	[Arguments("us-ascii", "ascii", "cafe", "63616665", true)]
	[Arguments("ANSI_X3.4-1968", "ascii", "cafe", "63616665", false)]
	public async Task CharsetAccepted_InputMetadataAndRenderedWireAgree(
		string selected, string canonical, string text, string encodedHex, bool fragmented)
	{
		var capabilities = new ProtocolCapabilities();
		var service = Substitute.For<IConnectionServerService>();
		service.UpdateCapabilities(Arg.Any<long>(), Arg.Any<Func<ProtocolCapabilities, ProtocolCapabilities>>())
			.Returns(call => { capabilities = call.Arg<Func<ProtocolCapabilities, ProtocolCapabilities>>()(capabilities); return true; });
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service);
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await WriteAsync(toServer, IAC, DO, CHARSET);
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET, REQUEST) && Contains(bytes, IAC, SE));
			byte[] accepted = [IAC, SB, CHARSET, ACCEPTED, .. Encoding.ASCII.GetBytes(selected), IAC, SE];
			if (fragmented)
			{
				foreach (var b in accepted) await WriteAsync(toServer, b);
			}
			else await WriteAsync(toServer, accepted);
			await WriteAsync(toServer, [.. Convert.FromHexString(encodedHex), 13, 10]);
			var input = await WaitForPublishedAsync<TelnetInputMessage>(published);
			await Assert.That(input?.Input).IsEqualTo(text);
			await Assert.That(capabilities.Charset).IsEqualTo(canonical);
			await Assert.That(capabilities.OutputCharset).IsEqualTo(canonical);
			var registration = service.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IConnectionServerService.RegisterAsync));
			var encoding = (Func<Encoding>)registration.GetArguments()[6]!;
			await Assert.That(encoding().WebName).IsEqualTo(Encoding.GetEncoding(selected).WebName);
			var output = (Func<byte[], ValueTask>)registration.GetArguments()[4]!;
			await output(OutputTransformService.Transform(Encoding.UTF8.GetBytes(text), capabilities));
			var wire = await ReadUntilAsync(fromServer, bytes => Contains(bytes, Convert.FromHexString(encodedHex)));
			await Assert.That(Contains(wire, Convert.FromHexString(encodedHex))).IsTrue();
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
			cts.Dispose();
		}
	}

	[Test]
	[Arguments("utf-16", "offered", false)]
	[Arguments("unicode", "offered", true)]
	[Arguments("utf-32", "offered", false)]
	[Arguments("iso-8859-1", "unoffered", false)]
	[Arguments("iso-8859-1", "rejected", true)]
	[Arguments("us-ascii", "replayed", false)]
	public async Task CharsetInvalidAcceptance_IsRejectedBeforeBufferedTextDecoding(string name, string state, bool fragmented)
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service);
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			if (state != "unoffered")
			{
				await WriteAsync(toServer, IAC, DO, CHARSET);
				await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET, REQUEST) && Contains(bytes, IAC, SE));
			}
			if (state == "rejected") await WriteAsync(toServer, IAC, SB, CHARSET, REJECTED, IAC, SE);
			if (state == "replayed") await WriteAsync(toServer, [IAC, SB, CHARSET, ACCEPTED, .. "utf-8"u8.ToArray(), IAC, SE]);
			byte[] frame = [IAC, SB, CHARSET, ACCEPTED, .. Encoding.ASCII.GetBytes(name), IAC, SE];
			if (fragmented)
			{
				foreach (var b in frame[..^1]) await WriteAsync(toServer, b);
				await WriteAsync(toServer, [frame[^1], .. "café雪\r\n"u8.ToArray()]);
			}
			else await WriteAsync(toServer, [.. frame, .. "café雪\r\n"u8.ToArray()]);
			var response = await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET, REJECTED, IAC, SE));
			await Assert.That(Contains(response, IAC, SB, CHARSET, REJECTED, IAC, SE)).IsTrue();
			var input = await WaitForPublishedAsync<TelnetInputMessage>(published);
			await Assert.That(input?.Input).IsEqualTo("café雪");
			var registration = service.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IConnectionServerService.RegisterAsync));
			await Assert.That(((Func<Encoding>)registration.GetArguments()[6]!)().WebName).IsEqualTo("utf-8");
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
			cts.Dispose();
		}
	}

	[Test]
	[Arguments(";utf-16;ISO-8859-1", "iso-8859-1")]
	[Arguments(";us-ascii;utf-8;iso-8859-1", "utf-8")]
	[Arguments(";utf-16", null)]
	public async Task CharsetPeerRequest_ChoosesOnlyRendererEncodings(string offered, string? expected)
	{
		var (toServer, fromServer, handler, _, cts) = StartServer();
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await WriteAsync(toServer, IAC, WILL, CHARSET);
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, DO, CHARSET));
			byte[] request = [IAC, SB, CHARSET, REQUEST, .. Encoding.ASCII.GetBytes(offered), IAC, SE];
			foreach (var b in request) await WriteAsync(toServer, b);
			var response = await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET) && Contains(bytes, IAC, SE));
			byte[] expectedFrame = expected is null
				? [IAC, SB, CHARSET, REJECTED, IAC, SE]
				: [IAC, SB, CHARSET, ACCEPTED, .. Encoding.ASCII.GetBytes(expected), IAC, SE];
			await Assert.That(Contains(response, expectedFrame)).IsTrue().Because(Describe(response));
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
			cts.Dispose();
		}
	}

	[Test]
	[Arguments("iso-8859-1")]
	[Arguments("us-ascii")]
	public async Task Gmcp_RemainsUtf8AfterTextCharsetChanges(string selected)
	{
		var service = Substitute.For<IConnectionServerService>();
		var (toServer, fromServer, handler, published, cts) = StartServer(connectionService: service);
		try
		{
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, WILL, CHARSET));
			await WriteAsync(toServer, IAC, DO, CHARSET);
			await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, CHARSET, REQUEST) && Contains(bytes, IAC, SE));
			await WriteAsync(toServer, [IAC, SB, CHARSET, ACCEPTED, .. Encoding.ASCII.GetBytes(selected), IAC, SE,
				IAC, DO, GMCP, IAC, SB, GMCP, .. "Test.Message {\"name\":\"café雪\"}"u8.ToArray(), IAC, SE]);
			var message = await WaitForPublishedAsync<GMCPSignalMessage>(published);
			await Assert.That(message).IsNotNull();
			await Assert.That(message!.Info).Contains("café雪");
			var registration = service.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IConnectionServerService.RegisterAsync));
			var send = (Func<string, string, ValueTask>)registration.GetArguments()[8]!;
			await send("Test.Message", "{\"name\":\"café雪\"}");
			var wire = await ReadUntilAsync(fromServer, bytes => Contains(bytes, IAC, SB, GMCP) && Contains(bytes, IAC, SE));
			await Assert.That(Contains(wire, "Test.Message {\"name\":\"café雪\"}"u8.ToArray())).IsTrue().Because(Describe(wire));
		}
		finally
		{
			await cts.CancelAsync();
			await toServer.CompleteAsync();
			await handler.WaitAsync(Timeout);
			cts.Dispose();
		}
	}
}
