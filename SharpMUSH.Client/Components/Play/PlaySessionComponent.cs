using Microsoft.AspNetCore.Components;
using SharpMUSH.Client.Services;

namespace SharpMUSH.Client.Components.Play;

/// <summary>
/// A component that reads a character's play session: its connection, its room and other out-of-band
/// payloads, its channels and pages, and its channel member lists.
/// </summary>
/// <remarks>
/// Inside Play the session is the page's own, which Play cascades. On <c>/client</c> another character can
/// come to the front while a page is still shown (its switch connects first), and a component that read the
/// tab's focused session would show that character in the page still drawn for the one before. Placed
/// anywhere else, it reads the focused session, followed as the focus moves.
/// </remarks>
public abstract class PlaySessionComponent : ComponentBase
{
	/// <summary>The page's session, which Play cascades; null outside Play.</summary>
	[CascadingParameter] private CharacterSession? PageSession { get; set; }

	/// <summary>Resolved on first use, so a component that reads only one part needs only that one registered.</summary>
	[Inject] private IServiceProvider SessionServices { get; set; } = null!;

	/// <summary>The session's play connection.</summary>
	protected IPlayTerminalService PlayTerminal => PageSession?.Terminal ?? SessionServices.GetRequiredService<IPlayTerminalService>();

	/// <summary>The session's channels and conversations.</summary>
	protected ICommFeed Feed => PageSession?.Comms ?? SessionServices.GetRequiredService<ICommFeed>();

	/// <summary>The session's channel member lists.</summary>
	protected IChannelWho Who => PageSession?.ChannelWho ?? SessionServices.GetRequiredService<IChannelWho>();

	/// <summary>
	/// The objid of the character the session plays, to pin a portal command to (<c>GameCommandService.RunAsync</c>):
	/// the server refuses it once the tab acts as another, rather than running it as that one. Null for a guest or a
	/// typed login.
	/// </summary>
	protected string? PlayCharacter => (PageSession?.Terminal ?? SessionServices.GetService<IPlayTerminalService>())?.Identity?.Character;

	/// <summary>Whether the component is inside a Play page, whose session stays the same for the component's life.</summary>
	protected bool OnPlayPage => PageSession is not null;
}
