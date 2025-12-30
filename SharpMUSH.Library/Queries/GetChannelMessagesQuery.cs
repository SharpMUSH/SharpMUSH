using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Query to retrieve the most recent channel messages from the recall buffer
/// </summary>
/// <param name="ChannelId">The channel ID to query messages for</param>
/// <param name="Count">Number of messages to retrieve (default: 10)</param>
public record GetChannelMessagesQuery(string ChannelId, int Count = 10) : IStreamQuery<SharpChannelMessage>;
