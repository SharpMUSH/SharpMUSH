namespace SharpMUSH.Client.Services;

/// <summary>
/// One character's play session in this tab: its play connection and what is read off it, the out-of-band
/// store, the channels and pages filed from it, and the channel member lists.
/// </summary>
/// <remarks>
/// <para>Play holds one, the tab's (<see cref="CharacterSessionRegistry.Focused"/>), and follows it through a
/// character switch: the terminal behind it is rebuilt (<see cref="TerminalServiceHost.RecreateAsync"/>), the
/// session object stays. The command terminal is not part of a session: it is the tab's portal connection,
/// whichever character it acts as.</para>
/// <para>The longer log (<see cref="TerminalLog"/>) and resume points (<see cref="TerminalResumeStore"/>) stay
/// shared services: both are kept per <see cref="Identity"/> already.</para>
/// </remarks>
public sealed class CharacterSession(IPlayTerminalService terminal, ICommFeed comms, IChannelWho channelWho)
{
	/// <summary>The character's play connection.</summary>
	public IPlayTerminalService Terminal { get; } = terminal;

	/// <summary>The channels and conversations filed from <see cref="Terminal"/>'s out-of-band store.</summary>
	public ICommFeed Comms { get; } = comms;

	/// <summary>Channel member lists, from the same store.</summary>
	public IChannelWho ChannelWho { get; } = channelWho;

	/// <summary>The latest out-of-band payloads on <see cref="Terminal"/>, the room's among them.</summary>
	public IOobChannelStore Oob => Terminal.OobChannels;

	/// <summary>Who the connection plays as, when it logged in as an account's character; null for a guest or a typed login.</summary>
	public TerminalIdentity? Identity => Terminal.Identity;

	/// <summary>A new play connection, not yet connected: what a session is built over.</summary>
	public static PlayTerminalServiceHost NewTerminal(IServiceProvider services) => new(
		() => new PlayTerminalService(
			ActivatorUtilities.CreateInstance<PlayWebSocketClientService>(services),
			services.GetRequiredService<ILogger<TerminalService>>(),
			services.GetRequiredService<ITerminalLoginTokens>(),
			services.GetRequiredService<TerminalLog>(),
			// Its own, so one connection's frames claim alerts apart from another's; the rules are the account's. Read
			// on the first line, not here: the rules need the account (PlaySettings, AccountAuthService), whose sign-out
			// handler needs this terminal.
			services.GetService<IServiceProviderIsService>()?.IsService(typeof(HighlightRules)) is true
				? new TerminalHighlights(
					new Lazy<IHighlightMatcherSource>(services.GetRequiredService<HighlightRules>, LazyThreadSafetyMode.PublicationOnly),
					new Lazy<TerminalAlerts>(services.GetRequiredService<TerminalAlerts>, LazyThreadSafetyMode.PublicationOnly))
				: null));

	/// <summary>
	/// A session over <paramref name="terminal"/>, with its own feeds. They are built here, not on first use, so
	/// they listen before the connection carries anything (see
	/// <see cref="TerminalServiceCollectionExtensions.AttachPlayTerminalFeeds"/>).
	/// </summary>
	public static CharacterSession Over(IPlayTerminalService terminal, IServiceProvider services)
	{
		var history = services.GetService<ICommHistory>();
		// The play connection too: a reconnect logs in again without replaying what was missed, so the feed pulls
		// it from the server.
		var comms = new OobCommFeed(terminal.OobChannels, history: history, connection: terminal);
		var who = new ChannelWhoFeed(terminal.OobChannels, history);
		return new CharacterSession(terminal, comms, who);
	}
}

/// <summary>
/// The tab's character sessions. Play is the registry with one entry, <see cref="Focused"/>: the character
/// the tab acts as.
/// </summary>
public sealed class CharacterSessionRegistry(CharacterSession focused)
{
	/// <summary>The session the tab acts as, and the one Play shows.</summary>
	public CharacterSession Focused { get; } = focused;

	/// <summary>Every session in the tab, <see cref="Focused"/> first.</summary>
	public IReadOnlyList<CharacterSession> Sessions { get; } = [focused];
}
