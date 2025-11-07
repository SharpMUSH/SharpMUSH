using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Queries;

/// <summary>
/// Query to check if an object (or its owner) is a member of a specific channel.
/// Used by channel locks (channel^name) in the Boolean Expression Parser.
/// </summary>
/// <param name="Object">The object to check for channel membership</param>
/// <param name="ChannelName">The name of the channel to check</param>
public record IsOnChannelQuery(AnySharpObject Object, string ChannelName) : IQuery<bool>;
