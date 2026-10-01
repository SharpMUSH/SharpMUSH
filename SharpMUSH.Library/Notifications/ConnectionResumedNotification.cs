using Mediator;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.Notifications;

/// <summary>
/// Published when the connection server rebound a socket to a session that was still logged in as
/// <paramref name="Player"/>: a page reload, or a dropped connection coming back within the grace
/// period. The client may have none of the state pushed before (a reload starts empty), so the
/// connect-time snapshots are sent again.
/// </summary>
/// <param name="Handle">The resumed connection.</param>
/// <param name="Player">The player it is logged in as.</param>
public record ConnectionResumedNotification(long Handle, DBRef Player) : INotification;
