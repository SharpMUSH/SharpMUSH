using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Extensions;

public static class ConnectionServiceExtensions
{
	public static async ValueTask<bool> IsConnected(this IConnectionService connectionService, AnySharpObject obj) =>
		await connectionService.Get(obj.Object().DBRef).AnyAsync();
}
