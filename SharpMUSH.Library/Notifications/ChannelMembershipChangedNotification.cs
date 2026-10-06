using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Published after a write that changes which channels <paramref name="Member"/> is on, or how: a join,
/// a leave, a change to their own channel flags, or the rename or deletion of a channel they are on.
/// </summary>
/// <param name="Member">The object whose channel list changed.</param>
/// <param name="ChannelName">The channel's name, after a rename.</param>
/// <param name="Cause"><c>join</c>, <c>leave</c>, <c>status</c>, <c>rename</c> or <c>delete</c>.</param>
/// <param name="PreviousStatus">For <c>leave</c> and <c>status</c>, the member's flags on the channel before
/// the write, read before it, so a reader can tell whether they were listed by <c>@channel/who</c>.</param>
public record ChannelMembershipChangedNotification(
	AnySharpObject Member,
	string ChannelName,
	string Cause,
	SharpChannelStatus? PreviousStatus = null) : INotification;
