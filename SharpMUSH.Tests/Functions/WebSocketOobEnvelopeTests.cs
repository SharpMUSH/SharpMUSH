using SharpMUSH.Library.Services;

namespace SharpMUSH.Tests.Functions;

public class WebSocketOobEnvelopeTests
{
	[Test]
	[Arguments("room.contents", "{\"who\":[\"#5\"]}", "{\"type\":\"oob\",\"package\":\"room.contents\",\"data\":{\"who\":[\"#5\"]}}")]
	[Arguments("room.exits", "north", "{\"type\":\"oob\",\"package\":\"room.exits\",\"data\":\"north\"}")]
	[Arguments("x", "", "{\"type\":\"oob\",\"package\":\"x\",\"data\":null}")]
	public async Task Build_ProducesEnvelope(string package, string message, string expected)
	{
		await Assert.That(WebSocketOobEnvelope.Build(package, message)).IsEqualTo(expected);
	}

	[Test]
	public async Task Build_NamesTheSender()
	{
		await Assert.That(WebSocketOobEnvelope.Build("myapp.event", "{\"n\":1}", "#12:1700000000000"))
			.IsEqualTo("{\"type\":\"oob\",\"package\":\"myapp.event\",\"data\":{\"n\":1},\"from\":\"#12:1700000000000\"}");
	}
}
