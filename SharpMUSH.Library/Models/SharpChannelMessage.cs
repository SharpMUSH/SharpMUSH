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
}
