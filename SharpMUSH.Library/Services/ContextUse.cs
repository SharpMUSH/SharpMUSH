using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Extensions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Services;

/// <summary>
/// Recent use on the context stack: after a line a player typed runs a <c>$</c>-command, or an
/// <c>@input</c> session takes a line, the system of each object that answered is pushed onto the
/// player's stack, so its palette entries rise.
/// </summary>
public sealed class ContextUse(IContextStack stack, ISystemNames systems)
{
	/// <summary>Pushes the systems of <paramref name="answered"/> for <paramref name="player"/>, when it is a player.</summary>
	public async ValueTask RecordAsync(AnySharpObject player, IEnumerable<DBRef> answered,
		CancellationToken cancellationToken = default)
	{
		if (!player.IsPlayer) return;
		var character = player.Object().DBRef;
		var pushed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var obj in answered)
		{
			var system = await systems.SystemOfAsync(obj, cancellationToken);
			if (pushed.Add(system)) stack.Push(character, system);
		}
	}
}
