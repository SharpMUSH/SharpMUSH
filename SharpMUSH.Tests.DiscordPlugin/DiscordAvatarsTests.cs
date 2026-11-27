using SharpMUSH.Library.Models;
using SharpMUSH.Plugins.Discord.Links;

namespace SharpMUSH.Tests.DiscordPlugin;

public class DiscordAvatarsTests
{
	private static readonly DateTimeOffset Now = new(2026, 11, 27, 3, 0, 0, TimeSpan.Zero);

	private static SharpFeedMessage Message(string style = "say", string display = "", string picture = "",
		ExternalFeedIdentity? external = null, int speaker = 5)
		=> new(1, "discord", "123", Now, new DBRef(speaker, 1700000000000), "Alice", new DBRef(5), "Alice", null, "", style,
			MString.Plain("Hi."), display, ExternalIdentity: external, Picture: picture);

	[Test]
	[Arguments("/api/wiki-assets/a1/face.png", "https://play.example.org", "https://play.example.org/api/wiki-assets/a1/face.png")]
	[Arguments("/api/wiki-assets/a1/face.webp", "https://play.example.org/", "https://play.example.org/pictures/png/api/wiki-assets/a1/face.webp.png")]
	[Arguments("/api/wiki-assets/a1/face.png", "https://play.example.org/portal/", "https://play.example.org/api/wiki-assets/a1/face.png")]
	[Arguments("https://cdn.discordapp.com/avatars/1/a.png", null, "https://cdn.discordapp.com/avatars/1/a.png")]
	[Arguments("http://8.8.8.8/a.png", null, "http://8.8.8.8/a.png")]
	public async Task APicture_GoesOutAsAnAddressDiscordCanFetch(string picture, string? mudUrl, string expected)
		=> await Assert.That(DiscordAvatarUrl.Public(picture, mudUrl)).IsEqualTo(expected);

	[Test]
	[Arguments("/api/wiki-assets/a1/face.png", null)]
	[Arguments("/api/wiki-assets/a1/face.png", "")]
	[Arguments("/api/wiki-assets/a1/face.png", "telnet://play.example.org")]
	[Arguments("/api/wiki-assets/a1/face.png", "http://localhost:8080")]
	[Arguments("/api/wiki-assets/a1/face.png", "https://192.168.1.20")]
	[Arguments("https://mush.local/a.png", null)]
	[Arguments("https://nas.lan/a.png", null)]
	[Arguments("https://intranet/a.png", null)]
	[Arguments("http://127.0.0.1/a.png", null)]
	[Arguments("http://10.0.0.4/a.png", null)]
	[Arguments("http://172.20.1.1/a.png", null)]
	[Arguments("http://100.64.0.1/a.png", null)]
	[Arguments("http://169.254.169.254/a.png", null)]
	[Arguments("http://[::1]/a.png", null)]
	[Arguments("http://[fd00::1]/a.png", null)]
	[Arguments("http://[::ffff:192.168.0.1]/a.png", null)]
	[Arguments("https://user:pass@cdn.example.org/a.png", null)]
	[Arguments("ftp://cdn.example.org/a.png", null)]
	[Arguments("//cdn.example.org/a.png", "https://play.example.org")]
	[Arguments("not a picture", "https://play.example.org")]
	[Arguments("#5:1700000000000", "https://play.example.org")]
	public async Task APictureDiscordCouldNotReach_GoesOutWithNone(string picture, string? mudUrl)
		=> await Assert.That(DiscordAvatarUrl.Public(picture, mudUrl)).IsNull();

	[Test]
	public async Task AnOverlongAddress_GoesOutWithNone()
		=> await Assert.That(DiscordAvatarUrl.Public("https://cdn.example.org/" + new string('a', DiscordAvatarUrl.MaxLength), null)).IsNull();

	[Test]
	[Arguments("say")]
	[Arguments("pose")]
	[Arguments("semipose")]
	public async Task ASpeakersOwnLine_CarriesTheirPortrait(string style)
		=> await Assert.That(DiscordAvatars.Speaker(Message(style))).IsEqualTo("#5:1700000000000");

	[Test]
	[Arguments("emit", "")]
	[Arguments("announce", "")]
	[Arguments("say", "Public")]
	public async Task AnEmitOrALineUnderAnotherName_CarriesNoSpeakersPortrait(string style, string display)
		=> await Assert.That(DiscordAvatars.Speaker(Message(style, display))).IsNull();

	[Test]
	public async Task AnOutsideSpeaker_HasNoPortraitInTheGame()
		=> await Assert.That(DiscordAvatars.Speaker(Message(external: new ExternalFeedIdentity("discord", "1", "ann", "https://cdn.discordapp.com/a.png"), speaker: -1))).IsNull();
}
