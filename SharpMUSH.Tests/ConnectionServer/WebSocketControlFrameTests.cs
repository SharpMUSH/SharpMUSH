using SharpMUSH.ConnectionServer.ProtocolHandlers;

namespace SharpMUSH.Tests.ConnectionServer;

public class WebSocketControlFrameTests
{
	[Test]
	public async Task ValidNawsFrameParsesAndClamps()
	{
		var ok = WebSocketControlFrame.TryParseNaws("{\"type\":\"naws\",\"cols\":120,\"rows\":40}", out var cols, out var rows);
		await Assert.That(ok).IsTrue();
		await Assert.That(cols).IsEqualTo(120);
		await Assert.That(rows).IsEqualTo(40);
	}

	[Test]
	public async Task OversizeClampsToThousand()
	{
		var ok = WebSocketControlFrame.TryParseNaws("{\"type\":\"naws\",\"cols\":99999,\"rows\":0}", out var cols, out var rows);
		await Assert.That(ok).IsTrue();
		await Assert.That(cols).IsEqualTo(1000);
		await Assert.That(rows).IsEqualTo(1);
	}

	[Test]
	[Arguments("look north")]
	[Arguments("{not json")]
	[Arguments("{\"type\":\"chat\",\"msg\":\"hi\"}")]
	[Arguments("{\"hello\":1}")]
	[Arguments("{\"type\":\"naws\",\"cols\":1.5,\"rows\":40}")]
	public async Task NonNawsReturnsFalse(string message)
	{
		await Assert.That(WebSocketControlFrame.TryParseNaws(message, out _, out _)).IsFalse();
	}
}
