using SharpMUSH.Library;
using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// Whose context stack code may touch: the player whose command it is running (<c>%#</c>), or a player
/// its object controls. Shared by <c>@context</c> and <c>context()</c>.
/// </summary>
internal static class ContextAuthority
{
	/// <summary>The player <paramref name="name"/> names, when the executor may use their stack; else the error.</summary>
	public static async ValueTask<Result<SharpPlayer>> ResolveAsync(IMUSHCodeParser parser, ILocateService locate,
		IPermissionService permissions, AnySharpObject executor, string name)
	{
		if (await locate.LocateConnectionTarget(parser, executor, executor, name.Trim()) is not AnySharpObject found
			|| found is not SharpPlayer player)
		{
			return new Error<string>(ErrorMessages.Returns.NoSuchPlayer);
		}

		// The whole objid where the enactor carries one: a queued action can outlive its player, whose number may
		// then belong to someone else.
		var isEnactor = parser.CurrentState.Enactor is { } enactor && enactor.SameObjectAs(player.Object.DBRef);
		return isEnactor || await permissions.Controls(executor, found)
			? player
			: new Error<string>(ErrorMessages.Returns.PermissionDenied);
	}
}
