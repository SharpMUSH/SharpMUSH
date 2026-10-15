using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Plugins.Discord.Events;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordEventSpecTests
{
	private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
	private static readonly long Tomorrow = Now.AddDays(1).ToUnixTimeSeconds();

	[Test]
	public async Task ANameStartAndLocation_AreEnough_AndTheEventLastsThreeHours()
	{
		var spec = DiscordEventSpec.Parse($$"""{"name":"Harbour Night","start":{{Tomorrow}},"location":"https://game.example/scenes/12"}""", Now)
			.Expect<DiscordEventSpec>();

		await Assert.That(spec.Name).IsEqualTo("Harbour Night");
		await Assert.That(spec.Start).IsEqualTo(Now.AddDays(1));
		await Assert.That(spec.End).IsEqualTo(Now.AddDays(1) + DiscordEventSpec.DefaultLength);
		await Assert.That(spec.Description).IsNull();
		await Assert.That(spec.GuildId).IsNull();
	}

	[Test]
	public async Task TimesMayBeStrings_AndTheServerIsAnId()
	{
		var spec = DiscordEventSpec.Parse(
			$$"""{"name":"N","start":"{{Tomorrow}}","end":"{{Tomorrow + 3600}}","location":"L","server":"1180000000000000001"}""", Now)
			.Expect<DiscordEventSpec>();

		await Assert.That(spec.End - spec.Start).IsEqualTo(TimeSpan.FromHours(1));
		await Assert.That(spec.GuildId).IsEqualTo(1180000000000000001UL);
	}

	[Test]
	public async Task LongTextIsCutToDiscordsLimits()
	{
		var spec = DiscordEventSpec.Parse(
			$$"""{"name":"{{new string('n', 150)}}","start":{{Tomorrow}},"location":"{{new string('l', 150)}}","description":"{{new string('d', 1500)}}"}""", Now)
			.Expect<DiscordEventSpec>();

		await Assert.That(spec.Name.Length).IsEqualTo(DiscordEventSpec.MaxName);
		await Assert.That(spec.Name).EndsWith("…");
		await Assert.That(spec.Location.Length).IsEqualTo(DiscordEventSpec.MaxLocation);
		await Assert.That(spec.Description!.Length).IsEqualTo(DiscordEventSpec.MaxDescription);
	}

	[Test]
	[Arguments("not json", "must be a JSON object")]
	[Arguments("[1]", "must be a JSON object")]
	[Arguments("""{"start":1999999999,"location":"L"}""", "needs a name")]
	[Arguments("""{"name":"N","start":1999999999}""", "needs a location")]
	[Arguments("""{"name":"N","location":"L"}""", "needs a start")]
	[Arguments("""{"name":"N","start":1700000000,"location":"L"}""", "start has passed")]
	[Arguments("""{"name":"N","start":1999999999,"end":1999999000,"location":"L"}""", "end must come after its start")]
	[Arguments("""{"name":"N","start":1999999999,"location":"L","server":"Harbour"}""", "server takes")]
	public async Task WhatDiscordWouldRefuse_IsRefusedFirst(string json, string reason)
	{
		var refused = DiscordEventSpec.Parse(json, Now);

		await Assert.That(refused.Expect<Error<string>>().Value).Contains(reason);
	}
}
