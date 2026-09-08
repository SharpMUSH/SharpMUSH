using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Ports PennMUSH's announce_connect/announce_disconnect (src/bsd.c): room/inventory broadcasts,
/// HEAR_CONNECT and SUSPECT/WIZARD broadcasts, and ACONNECT/ADISCONNECT hook dispatch to the
/// player, their room, their zone, and the master room.
/// </summary>
public interface IConnectionAnnounceService
{
	/// <summary>Called once a login completes. <paramref name="connectionCount"/> is the player's total connection count AFTER this one. <paramref name="isHiddenConnection"/> is the connecting socket's own <c>Hidden</c> state (PennMUSH DESC.hide).</summary>
	ValueTask AnnounceConnectAsync(IMUSHCodeParser parser, AnySharpObject player, int connectionCount, bool isHiddenConnection);

	/// <summary>Called once a socket leaves LoggedIn. <paramref name="remainingConnections"/> excludes the disconnecting socket. <paramref name="isHiddenConnection"/> is the disconnecting socket's own <c>Hidden</c> state (PennMUSH DESC.hide).</summary>
	ValueTask AnnounceDisconnectAsync(IMUSHCodeParser parser, AnySharpObject player, int remainingConnections, bool isHiddenConnection);
}
