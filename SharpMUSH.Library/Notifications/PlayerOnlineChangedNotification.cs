using Mediator;
using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Published when a player connects (<paramref name="Online"/> true, on every connection, the first and
/// each reconnect) and when their last connection closes (false). It drives the live member lists of the
/// channels they are on (<see cref="Definitions.SharpEvents.ChannelWho"/>), which <c>@channel/who</c> lists
/// a player on while connected.
/// </summary>
public record PlayerOnlineChangedNotification(AnySharpObject Player, bool Online) : INotification;
