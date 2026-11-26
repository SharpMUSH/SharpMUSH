using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SharpMUSH.Client.Models;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Tests.BUnit.Services;

/// <summary>
/// The tab's character sessions: one on <c>/play</c>, one per character on <c>/client</c>. What every component
/// injects (the play terminal, the comm feed, the member lists) is the focused session's, followed as the focus
/// moves, while the sessions behind it keep their connections.
/// </summary>
public class CharacterSessionRegistrationTests
{
	[Test]
	public async Task What_components_inject_is_the_focused_sessions()
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Substitute.For<Microsoft.JSInterop.IJSRuntime>());
		services.AddTerminalServices();
		await using var provider = services.BuildServiceProvider();

		var registry = provider.GetRequiredService<CharacterSessionRegistry>();
		var session = registry.Focused;

		await Assert.That(registry.Sessions).IsEquivalentTo(new[] { session });
		await Assert.That(provider.GetRequiredService<IPlayTerminalService>()).IsTypeOf<FocusedPlayTerminal>();
		await Assert.That(provider.GetRequiredService<ICommFeed>()).IsTypeOf<FocusedCommFeed>();
		await Assert.That(provider.GetRequiredService<IChannelWho>()).IsTypeOf<FocusedChannelWho>();
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
		var registry = new CharacterSessionRegistry(Session(host));
		var session = registry.Focused;

		await Assert.That(session.Identity).IsEqualTo(new TerminalIdentity("acct", "#5:1"));
		await host.RecreateAsync();

		await Assert.That(ReferenceEquals(registry.Focused, session)).IsTrue();
		await Assert.That(session.Identity).IsEqualTo(new TerminalIdentity("acct", "#6:1"));
	}

	[Test]
	public async Task The_play_terminal_follows_the_focus_and_says_so()
	{
		var (registry, ash, brin) = TwoCharacters();
		var focused = new FocusedPlayTerminal(registry);
		bool? state = null;
		var recreated = 0;
		focused.ConnectionStateChanged += connected => state = connected;
		focused.Recreated += () => recreated++;

		registry.Focus(brin);

		await Assert.That(focused.Identity).IsEqualTo(brin.Identity);
		await Assert.That(state).IsEqualTo(true);
		await Assert.That(recreated).IsEqualTo(1);
		await Assert.That(ash.Terminal.IsConnected).IsTrue();
	}

	[Test]
	public async Task Only_the_focused_terminals_lines_reach_the_facade()
	{
		var ash = Substitute.For<IPlayTerminalService>();
		var brin = Substitute.For<IPlayTerminalService>();
		var registry = new CharacterSessionRegistry(Session(Host(ash)), () => Session(Host(brin)));
		var brinSession = registry.Open();
		var focused = new FocusedPlayTerminal(registry);
		var heard = new List<string>();
		focused.LineReceived += line => heard.Add(line.Text);

		ash.LineReceived += Raise.Event<Action<TerminalLine>>(Line("for ash"));
		registry.Focus(brinSession);
		ash.LineReceived += Raise.Event<Action<TerminalLine>>(Line("ash, behind"));
		brin.LineReceived += Raise.Event<Action<TerminalLine>>(Line("for brin"));

		await Assert.That(heard).IsEquivalentTo(new[] { "for ash", "for brin" });
	}

	[Test]
	public async Task A_session_behind_counts_what_it_hears_until_it_comes_to_the_front()
	{
		var ash = Substitute.For<IPlayTerminalService>();
		var brin = Substitute.For<IPlayTerminalService>();
		var registry = new CharacterSessionRegistry(Session(Host(ash)), () => Session(Host(brin)));
		var brinSession = registry.Open();

		brin.LineReceived += Raise.Event<Action<TerminalLine>>(Line("one"));
		brin.LineReceived += Raise.Event<Action<TerminalLine>>(Line("two"));
		ash.LineReceived += Raise.Event<Action<TerminalLine>>(Line("in front"));

		await Assert.That(brinSession.Unseen).IsEqualTo(2);
		await Assert.That(registry.Focused.Unseen).IsEqualTo(0);

		registry.Focus(brinSession);
		await Assert.That(brinSession.Unseen).IsEqualTo(0);
	}

	[Test]
	public async Task The_comm_feed_follows_the_focus()
	{
		var (registry, _, brin) = TwoCharacters();
		var feed = new FocusedCommFeed(registry);
		var changed = 0;
		feed.Changed += () => changed++;

		registry.Focus(brin);

		await Assert.That(ReferenceEquals(feed.Channels, brin.Comms.Channels)).IsTrue();
		await Assert.That(changed).IsEqualTo(1);
	}

	[Test]
	public async Task Closing_a_session_logs_its_character_out_and_drops_it()
	{
		var brin = Playing("#6:1");
		var registry = new CharacterSessionRegistry(Session(Host(Playing("#5:1"))), () => Session(Host(brin)));
		var session = registry.Open();

		await registry.CloseAsync(session);

		await brin.Received(1).SendAsync("QUIT");
		await brin.Received(1).DisposeAsync();
		await Assert.That(registry.Sessions).DoesNotContain(session);
	}

	[Test]
	public async Task The_focused_session_cannot_be_closed()
	{
		var (registry, ash, _) = TwoCharacters();

		await Assert.That(() => registry.CloseAsync(ash)).Throws<InvalidOperationException>();
		await Assert.That(registry.Sessions).Contains(ash);
	}

	[Test]
	public async Task A_tab_keeps_at_most_the_limit()
	{
		var registry = new CharacterSessionRegistry(Session(Host(Substitute.For<IPlayTerminalService>())),
			() => Session(Host(Substitute.For<IPlayTerminalService>())));
		while (registry.CanOpen) registry.Open();

		await Assert.That(registry.Sessions.Count).IsEqualTo(CharacterSessionRegistry.Limit);
		await Assert.That(() => registry.Open()).Throws<InvalidOperationException>();
	}

	[Test]
	public async Task A_session_is_found_by_the_character_it_plays()
	{
		var (registry, _, brin) = TwoCharacters();

		await Assert.That(registry.For(new TerminalIdentity("acct", "#6:1")) is CharacterSession found && ReferenceEquals(found, brin)).IsTrue();
		await Assert.That(registry.For(new TerminalIdentity("acct", "#9:1")) is SharpMUSH.Library.DiscriminatedUnions.NotFound).IsTrue();
	}

	[Test]
	public async Task Building_the_play_terminal_does_not_build_the_alert_rules()
	{
		// The rules need the account (PlaySettings, AccountAuthService), whose sign-out handler needs the play terminal:
		// resolving them while the terminal is built would be a constructor cycle. They are read on the first line instead.
		var resolved = 0;
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton(Substitute.For<Microsoft.JSInterop.IJSRuntime>());
		services.AddTerminalServices();
		services.AddSingleton<HighlightRules>(_ =>
		{
			resolved++;
			throw new InvalidOperationException("The alert rules were built with the terminal");
		});
		await using var provider = services.BuildServiceProvider();

		provider.AttachPlayTerminalFeeds();

		await Assert.That(provider.GetRequiredService<CharacterSessionRegistry>().Focused.Terminal).IsNotNull();
		await Assert.That(resolved).IsEqualTo(0);
	}

	/// <summary>Ash in front and Brin behind, both connected.</summary>
	private static (CharacterSessionRegistry Registry, CharacterSession Ash, CharacterSession Brin) TwoCharacters()
	{
		var registry = new CharacterSessionRegistry(Session(Host(Playing("#5:1"))), () => Session(Host(Playing("#6:1"))));
		var brin = registry.Open();
		return (registry, registry.Focused, brin);
	}

	private static IPlayTerminalService Playing(string character)
	{
		var terminal = Substitute.For<IPlayTerminalService>();
		terminal.Identity.Returns(new TerminalIdentity("acct", character));
		terminal.IsConnected.Returns(true);
		terminal.OobChannels.Returns(new OobChannelStore());
		return terminal;
	}

	private static PlayTerminalServiceHost Host(IPlayTerminalService terminal) => new(() => terminal);

	private static CharacterSession Session(PlayTerminalServiceHost terminal) =>
		new(terminal, Substitute.For<ICommFeed>(), Substitute.For<IChannelWho>());

	private static TerminalLine Line(string text) => new(DateTime.UnixEpoch, text, TerminalLineSource.Server);
}
