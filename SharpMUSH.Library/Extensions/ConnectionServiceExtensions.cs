using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Extensions;

public static class ConnectionServiceExtensions
{
	public static bool IsConnected(this IConnectionService connectionService, AnySharpObject obj) =>
		connectionService.Get(obj.Object().DBRef).Any();
}
