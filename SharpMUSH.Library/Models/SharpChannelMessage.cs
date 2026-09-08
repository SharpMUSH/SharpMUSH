namespace SharpMUSH.Library.Models;

/// <summary>
/// Represents a message sent on a channel for recall buffer purposes
/// </summary>
public class SharpChannelMessage
{
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
	/// The message type (Say, Pose, Emit, etc.)
	/// </summary>
	public required string MessageType { get; set; }

	/// <summary>
	/// PennMUSH's <c>CBTYPE_SEEALL</c> buffer tag (src/extchat.c:3970): the line was delivered only to
	/// See_All members, so recall must hide it from everyone else as well - otherwise
	/// <c>@channel/recall</c> hands back the hidden-connect line that the live broadcast withheld.
	/// </summary>
	public bool SeeAllOnly { get; set; }
}
