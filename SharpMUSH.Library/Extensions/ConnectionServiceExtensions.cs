using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Extensions;

public static class ConnectionServiceExtensions
{
	/// <summary>True if the object has any live connection (a socket), including a background portal one.
	/// Use for lifecycle/technical checks; use <see cref="IsOnline"/> for presence.</summary>
	public static async ValueTask<bool> IsConnected(this IConnectionService connectionService, AnySharpObject obj) =>
		await connectionService.Get(obj.Object().DBRef).AnyAsync();

	/// <summary>True if the object has a present connection (<see cref="IConnectionService.ConnectionData.IsPresent"/>).
	/// Portal-class background connections (the web client's system terminal) and sessions held after their
	/// client dropped don't count, so a character only browsing the portal is offline for presence (WHO,
	/// room contents, CONNECTED).</summary>
	public static async ValueTask<bool> IsOnline(this IConnectionService connectionService, AnySharpObject obj) =>
		await connectionService.Get(obj.Object().DBRef)
			.Where(c => c.IsPresent)
			.AnyAsync();
}
