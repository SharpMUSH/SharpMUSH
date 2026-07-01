using System.Text;
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class WebTransportTransportTests
{
	[Test]
	public async Task Receives_length_prefixed_frames_then_null_at_eof()
	{
		using var input = new MemoryStream();
		await FrameCodec.WriteFrameAsync(input, Encoding.UTF8.GetBytes("look"), CancellationToken.None);
		await FrameCodec.WriteFrameAsync(input, Encoding.UTF8.GetBytes("say hi"), CancellationToken.None);
		input.Position = 0;
		using var output = new MemoryStream();

		var transport = new WebTransportTransport(input, output, "1.2.3.4", "host", () => { });

		await Assert.That(await transport.ReceiveTextAsync(CancellationToken.None)).IsEqualTo("look");
		await Assert.That(await transport.ReceiveTextAsync(CancellationToken.None)).IsEqualTo("say hi");
		await Assert.That(await transport.ReceiveTextAsync(CancellationToken.None)).IsNull();
	}

	[Test]
	public async Task Sends_frame_as_length_prefixed_and_reports_close()
	{
		using var input = new MemoryStream();
		using var output = new MemoryStream();
		var closed = false;
		var transport = new WebTransportTransport(input, output, "ip", "host", () => closed = true);

		await transport.SendAsync(Encoding.UTF8.GetBytes("hello"), CancellationToken.None);
		await transport.CloseAsync();

		output.Position = 0;
		var frame = await FrameCodec.ReadFrameAsync(output, CancellationToken.None);
		await Assert.That(Encoding.UTF8.GetString(frame!)).IsEqualTo("hello");
		await Assert.That(closed).IsTrue();
	}
}
