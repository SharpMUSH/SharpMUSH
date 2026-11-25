using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The tab's character session is the play connection and the feeds over it, and every part stays resolvable
/// on its own: a component that injects the play terminal or the comm feed gets the same object Play reads
/// through the session.
/// </summary>
public class CharacterSessionRegistrationTests
{
	[Test]
	public async Task The_focused_session_is_made_of_the_registered_play_terminal_and_feeds()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Substitute.For<Microsoft.JSInterop.IJSRuntime>());
		services.AddTerminalServices();
		await using var provider = services.BuildServiceProvider();

		var registry = provider.GetRequiredService<CharacterSessionRegistry>();
		var session = registry.Focused;

		await Assert.That(registry.Sessions).IsEquivalentTo(new[] { session });
		await Assert.That(ReferenceEquals(provider.GetRequiredService<CharacterSession>(), session)).IsTrue();
		await Assert.That(ReferenceEquals(session.Terminal, provider.GetRequiredService<PlayTerminalServiceHost>())).IsTrue();
		await Assert.That(ReferenceEquals(session.Terminal, provider.GetRequiredService<IPlayTerminalService>())).IsTrue();
		await Assert.That(ReferenceEquals(session.Comms, provider.GetRequiredService<ICommFeed>())).IsTrue();
		await Assert.That(ReferenceEquals(session.ChannelWho, provider.GetRequiredService<IChannelWho>())).IsTrue();
		await Assert.That(ReferenceEquals(session.Oob, session.Terminal.OobChannels)).IsTrue();
	}

	[Test]
	public async Task The_session_keeps_its_object_through_a_character_switch()
	{
		var first = Substitute.For<IPlayTerminalService>();
		first.Identity.Returns(new TerminalIdentity("acct", "#5:1"));
		var second = Substitute.For<IPlayTerminalService>();
		second.Identity.Returns(new TerminalIdentity("acct", "#6:1"));
		var host = new PlayTerminalServiceHost(new Queue<IPlayTerminalService>([first, second]).Dequeue);

		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Substitute.For<Microsoft.JSInterop.IJSRuntime>());
		services.AddTerminalServices();
		services.AddSingleton(host);
		await using var provider = services.BuildServiceProvider();
		var session = provider.GetRequiredService<CharacterSessionRegistry>().Focused;

		await Assert.That(session.Identity).IsEqualTo(new TerminalIdentity("acct", "#5:1"));
		await host.RecreateAsync();

		await Assert.That(ReferenceEquals(provider.GetRequiredService<CharacterSessionRegistry>().Focused, session)).IsTrue();
		await Assert.That(session.Identity).IsEqualTo(new TerminalIdentity("acct", "#6:1"));
	}
}
