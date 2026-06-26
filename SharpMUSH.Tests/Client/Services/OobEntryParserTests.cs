using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.Client.Services;

public class OobEntryParserTests
{
	[Test]
	public async Task ParsesObjectEntries()
	{
		var json = "{\"who\":[{\"dbref\":\"#5\",\"name\":\"Bob\",\"cmd\":\"look #5\"}]}";
		var entries = OobEntryParser.Parse(json, "who");
		await Assert.That(entries.Count).IsEqualTo(1);
		await Assert.That(entries[0].Name).IsEqualTo("Bob");
		await Assert.That(entries[0].Cmd).IsEqualTo("look #5");
	}

	[Test]
	public async Task ParsesBareStringEntries()
	{
		var entries = OobEntryParser.Parse("{\"exits\":[\"north\",\"south\"]}", "exits");
		await Assert.That(entries.Count).IsEqualTo(2);
		await Assert.That(entries[1].Name).IsEqualTo("south");
	}

	[Test]
	[Arguments(null)]
	[Arguments("not json")]
	[Arguments("{\"who\":\"oops\"}")]
	public async Task MalformedReturnsEmpty(string? json)
	{
		await Assert.That(OobEntryParser.Parse(json, "who").Count).IsEqualTo(0);
	}
}
