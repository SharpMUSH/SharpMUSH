using System.Text;
using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class FrameCodecTests
{
	[Test]
	public async Task Roundtrips_two_frames_including_payload_with_newlines()
	{
		using var ms = new MemoryStream();
		await FrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("line1\nline2"), CancellationToken.None);
		await FrameCodec.WriteFrameAsync(ms, Encoding.UTF8.GetBytes("second"), CancellationToken.None);
		ms.Position = 0;

		var f1 = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);
		var f2 = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);
		var f3 = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);

		await Assert.That(Encoding.UTF8.GetString(f1!)).IsEqualTo("line1\nline2");
		await Assert.That(Encoding.UTF8.GetString(f2!)).IsEqualTo("second");
		await Assert.That(f3).IsNull(); // clean EOF at frame boundary
	}

	[Test]
	public async Task Handles_empty_payload_frame()
	{
		using var ms = new MemoryStream();
		await FrameCodec.WriteFrameAsync(ms, ReadOnlyMemory<byte>.Empty, CancellationToken.None);
		ms.Position = 0;

		var frame = await FrameCodec.ReadFrameAsync(ms, CancellationToken.None);

		await Assert.That(frame).IsNotNull();
		await Assert.That(frame!.Length).IsEqualTo(0);
	}

	[Test]
	public async Task Throws_on_truncated_payload()
	{
		using var ms = new MemoryStream();
		// Header claims 10 bytes but only 3 follow.
		ms.Write([0, 0, 0, 10]);
		ms.Write([1, 2, 3]);
		ms.Position = 0;

		await Assert.That(async () => await FrameCodec.ReadFrameAsync(ms, CancellationToken.None))
			.Throws<EndOfStreamException>();
	}
}
