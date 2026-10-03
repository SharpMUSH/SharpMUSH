using SharpMUSH.Library.Definitions;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;
using SharpMUSH.Library.Services.Interfaces;

namespace SharpMUSH.Library.Common;

/// <summary>
/// What one viewer may see of another object's flags: PennMUSH's <c>Can_See_Flag</c>
/// (<c>hdrs/mushdb.h:66</c>) and, for CONNECTED, <c>can_see_connected</c> (<c>src/bsd.c:6664</c>).
/// Built once per viewer and reused for every object a listing shows.
/// </summary>
public sealed class FlagView
{
	private readonly bool _god;
	private readonly bool _seeAll;
	private readonly bool _mistrust;
	private readonly DBRef _owner;
	private readonly IConnectionService? _connections;

	private FlagView(bool god, bool seeAll, bool mistrust, DBRef owner, IConnectionService? connections)
		=> (_god, _seeAll, _mistrust, _owner, _connections) = (god, seeAll, mistrust, owner, connections);

	/// <param name="viewer">Whose view this is.</param>
	/// <param name="connections">Where CONNECTED is read from; without it CONNECTED is never shown.</param>
	public static async ValueTask<FlagView> ForAsync(AnySharpObject viewer, IConnectionService? connections)
	{
		var owner = await viewer.Object().Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return new FlagView(viewer.IsGod(), await viewer.IsSee_All(),
			await viewer.IsMistrust(ExecutionBudget.CurrentToken), owner.Object.DBRef, connections);
	}

	/// <summary>
	/// <c>Can_See_Flag</c>'s permission half: a flag that is not dark, mdark, odark or disabled is
	/// public; an odark one is shown to the owner's objects (unless MISTRUST); an mdark one to See_All;
	/// a dark one only to God.
	/// </summary>
	public async ValueTask<bool> CanSeeAsync(SharpObject thing, SharpObjectFlag flag)
	{
		var perms = flag.SetPermissions;
		bool Has(string perm) => perms.Contains(perm, StringComparer.OrdinalIgnoreCase);

		var dark = Has("dark");
		var mdark = Has("mdark");

		if (_god || !(dark || mdark || Has("odark") || flag.Disabled)) return true;
		if (dark || flag.Disabled) return false;
		if (_seeAll) return true;
		if (mdark || _mistrust) return false;

		var thingOwner = await thing.Owner.WithCancellation(ExecutionBudget.CurrentToken);
		return thingOwner.Object.DBRef.Number == _owner.Number;
	}

	/// <summary>
	/// <c>can_see_connected</c>: the player is online (a play connection, as
	/// <see cref="Extensions.ConnectionServiceExtensions.IsOnline"/> counts it) that is not hidden, or the
	/// viewer has Priv_Who.
	/// </summary>
	public async ValueTask<bool> SeesConnectedAsync(SharpObject thing)
		=> _connections is not null
			&& thing.Type.Equals("PLAYER", StringComparison.OrdinalIgnoreCase)
			&& await _connections.Get(thing.DBRef)
				.AnyAsync(c => c.PresenceClass != PresenceClasses.Portal && (!c.IsHidden || _seeAll));
}
