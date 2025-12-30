using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Service for managing channel message recall buffers
/// </summary>
public interface IChannelBufferService
{
	/// <summary>
	/// Adds a message to the channel's recall buffer
	/// </summary>
	/// <param name="message">The message to add</param>
	ValueTask AddMessageAsync(SharpChannelMessage message);
	
	/// <summary>
	/// Gets the most recent messages from a channel's recall buffer
	/// </summary>
	/// <param name="channelId">The channel ID</param>
	/// <param name="count">Number of messages to retrieve</param>
	/// <returns>Messages in reverse chronological order (newest first)</returns>
	IAsyncEnumerable<SharpChannelMessage> GetMessagesAsync(string channelId, int count);
	
	/// <summary>
	/// Clears all messages from a channel's buffer
	/// </summary>
	/// <param name="channelId">The channel ID</param>
	ValueTask ClearBufferAsync(string channelId);
}
