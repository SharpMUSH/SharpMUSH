using System.Text;
using SharpMUSH.AnalysisProtocol;

namespace SharpMUSH.Tests.Integration.AnalysisTransport;

public class LspFrameTests
{
	[Test]
	public async Task StdioFraming_CountsUtf8BytesAndKeepsConsecutiveMessages()
	{
		using var stream = new MemoryStream();
		var first = Encoding.UTF8.GetBytes("{\"text\":\"😀\"}");
		await LspFrames.WriteStdioAsync(stream, first);
		await LspFrames.WriteStdioAsync(stream, Encoding.UTF8.GetBytes("{}"));
		stream.Position = 0;
		await Assert.That((await LspFrames.ReadStdioAsync(stream, 1024))!.SequenceEqual(first)).IsTrue();
		await Assert.That(Encoding.UTF8.GetString((await LspFrames.ReadStdioAsync(stream, 1024))!)).IsEqualTo("{}");
		await Assert.That(await LspFrames.ReadStdioAsync(stream, 1024)).IsNull();
	}

	[Test]
	[Arguments("Content-Length: 999999999\r\n\r\n", false)]
	[Arguments("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}", false)]
	[Arguments("Content-Length: -2\r\n\r\n", false)]
	[Arguments("Content-Length: 2\r\n\r\n{", true)]
	public async Task StdioFraming_RejectsInvalidLengthsAndTruncation(string input, bool truncated)
	{
		using var stream = new MemoryStream(Encoding.ASCII.GetBytes(input));
		if (truncated) await Assert.That(async () => await LspFrames.ReadStdioAsync(stream, 1024)).Throws<EndOfStreamException>();
		else await Assert.That(async () => await LspFrames.ReadStdioAsync(stream, 1024)).Throws<InvalidDataException>();
	}

	[Test]
	public async Task StdioFraming_RejectsUnterminatedOversizeHeader()
	{
		using var stream = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 2048)));
		await Assert.That(async () => await LspFrames.ReadStdioAsync(stream, 1024)).Throws<InvalidDataException>();
	}
}
