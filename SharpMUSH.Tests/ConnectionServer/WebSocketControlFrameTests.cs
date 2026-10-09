using SharpMUSH.SocketServer.ProtocolHandlers;

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

	[Test]
	public async Task TerminalTypesFrameParsesInOrder()
	{
		var ok = WebSocketControlFrame.TryParseTerminalTypes(
			"{\"type\":\"ttype\",\"types\":[\"SHARPMUSH-PORTAL\",\"UTF8\",\"SCREEN_READER\"]}", out var types);
		await Assert.That(ok).IsTrue();
		await Assert.That(types).IsEquivalentTo(["SHARPMUSH-PORTAL", "UTF8", "SCREEN_READER"]);
	}

	[Test]
	[Arguments("{\"type\":\"ttype\",\"types\":[]}")]
	[Arguments("{\"type\":\"ttype\",\"types\":[\"ANSI\",1]}")]
	[Arguments("{\"type\":\"ttype\",\"types\":[\"ANSI\",\" \"]}")]
	[Arguments("{\"type\":\"ttype\",\"types\":\"ANSI\"}")]
	[Arguments("{\"type\":\"naws\",\"cols\":80,\"rows\":24}")]
	[Arguments("look")]
	public async Task MalformedTerminalTypesReturnFalse(string message)
	{
		await Assert.That(WebSocketControlFrame.TryParseTerminalTypes(message, out _)).IsFalse();
	}

	[Test]
	public async Task TerminalTypesOverTheLimitsReturnFalse()
	{
		var tooMany = "{\"type\":\"ttype\",\"types\":[" + string.Join(',', Enumerable.Repeat("\"ANSI\"", 17)) + "]}";
		var tooLong = "{\"type\":\"ttype\",\"types\":[\"" + new string('A', 65) + "\"]}";
		await Assert.That(WebSocketControlFrame.TryParseTerminalTypes(tooMany, out _)).IsFalse();
		await Assert.That(WebSocketControlFrame.TryParseTerminalTypes(tooLong, out _)).IsFalse();
	}
}
