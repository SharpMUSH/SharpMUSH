using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Query to retrieve the most recent channel messages from the recall buffer
/// </summary>
/// <param name="ChannelId">The channel ID to query messages for</param>
/// <param name="Count">Number of messages to retrieve (default: 10)</param>
public record GetChannelMessagesQuery(string ChannelId, int Count = 10) : IStreamQuery<SharpChannelMessage>;

/// <summary>
/// How many messages a channel's recall buffer holds, without reading them back. PennMUSH keeps the
/// figure on the channel as <c>ChanNumMsgs(c)</c>; <c>@channel/list</c> and <c>@channel/what</c> print it
/// for every visible channel at once.
/// </summary>
public record CountChannelMessagesQuery(string ChannelId) : IQuery<int>;
