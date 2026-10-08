namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a message sent on a channel for recall buffer purposes
/// </summary>
public class SharpChannelMessage
{
	/// <summary>
	/// The line's id: what the <c>CHANNEL`MESSAGE</c> event passes on (the <c>comm.message</c> payload's
	/// <c>id</c>) and what the portal's recall endpoint returns, so a pulled and a pushed copy of one line
	/// are known to be one. 0 means none yet, and the buffer gives the line the next one
	/// (<see cref="Services.Interfaces.IChannelMessageIdSource"/>).
	/// </summary>
	public long Id { get; set; }

	/// <summary>
	/// The channel ID this message belongs to
	/// </summary>
	public required string ChannelId { get; set; }

	/// <summary>
	/// When the message was sent
	/// </summary>
	public required DateTimeOffset Timestamp { get; set; }

	/// <summary>
	/// The sender's DBRef
	/// </summary>
	public required DBRef Sender { get; set; }

	/// <summary>
	/// The formatted message content
	/// </summary>
	public required MString Message { get; set; }

	/// <summary>
	/// PennMUSH's <c>CBTYPE_SEEALL</c> buffer tag (src/extchat.c:3970): the line was delivered only to
	/// See_All members, so recall must hide it from everyone else as well - otherwise
	/// <c>@channel/recall</c> hands back the hidden-connect line that the live broadcast withheld.
	/// </summary>
	public bool SeeAllOnly { get; set; }

	/// <summary>
	/// The line's style as <c>CHANNEL`MESSAGE</c> names it — <c>say</c>, <c>pose</c>, <c>semipose</c>,
	/// <c>emit</c> or <c>presence</c> — or empty for a line written straight into the buffer
	/// (<c>cbufferadd()</c>), which has only its <see cref="Message"/>.
	/// </summary>
	public string Style { get; set; } = string.Empty;

	/// <summary>
	/// The speaker's name as the line names them, plain, after the mogrifier; empty when the line names
	/// nobody (an <c>@cemit</c>, or a line written straight into the buffer).
	/// </summary>
	public string SpeakerName { get; set; } = string.Empty;

	/// <summary>The message part alone, plain, after the mogrifier; empty when only <see cref="Message"/> is known.</summary>
	public string MessageText { get; set; } = string.Empty;
}
