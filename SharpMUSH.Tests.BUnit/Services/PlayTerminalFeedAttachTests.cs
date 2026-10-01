using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The comm feed has to be listening before the play terminal carries anything: the OOB store keeps only
/// the latest <c>comm.message</c>, so a line that arrives before the feed subscribes is gone for good.
/// A singleton factory builds nothing until something resolves it, and nothing on the way to connecting
/// the play terminal does — <c>Program</c> calls <see cref="TerminalServiceCollectionExtensions.AttachPlayTerminalFeeds"/>
/// right after the host is built.
/// </summary>
public class PlayTerminalFeedAttachTests
{
	private const string Channels =
		"""{"v":2,"viewer":{"name":"Ilsa","objid":"#5:1"},"channels":[{"name":"Public","joined":true}]}""";

	private const string First =
		"""{"v":2,"kind":"channel","channel":"Public","to":[],"from":"Wren","fromObjid":"#12:1","text":"one","style":"say","ts":1790780182950}""";

	private const string Second =
		"""{"v":2,"kind":"channel","channel":"Public","to":[],"from":"Dace","fromObjid":"#13:1","text":"two","style":"say","ts":1790780182951}""";

	[Test]
	public async Task An_attached_feed_keeps_every_line_the_play_terminal_received()
	{
		var (services, store) = Build();

		services.AttachPlayTerminalFeeds();
		store.Set(CommPayloadParser.ChannelsPackage, Channels);
		store.Set(CommPayloadParser.MessagePackage, First);
		store.Set(CommPayloadParser.MessagePackage, Second);

		var feed = services.GetRequiredService<ICommFeed>();
		await Assert.That(feed.Messages("Public").Select(m => m.Text)).IsEquivalentTo(new[] { "one", "two" });
		await Assert.That(feed.Channels.Single().Unread).IsEqualTo(2);
	}

	private static (ServiceProvider Services, OobChannelStore Store) Build()
	{
		var store = new OobChannelStore();
		var playTerminal = Substitute.For<IPlayTerminalService>();
		playTerminal.OobChannels.Returns(store);

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddTerminalServices();
		// The real play host opens a websocket; this one wraps a terminal whose OOB store the test fills.
		services.AddSingleton(new PlayTerminalServiceHost(() => playTerminal));
		return (services.BuildServiceProvider(), store);
	}
}
