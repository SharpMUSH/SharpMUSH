using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Queries.Database;

namespace SharpMUSH.Implementation.Common;

/// <summary>
/// The privilege reset an object takes when it changes hands or zones, shared by <c>@chown</c>,
/// <c>@chownall</c>, <c>@chzone</c>, <c>@chzoneall</c> and <c>zone()</c>.
/// </summary>
public static class PrivilegeHelpers
{
	/// <summary>
	/// <c>clear_flag_internal</c> on <c>WIZARD</c>, <c>ROYALTY</c> and <c>TRUST</c>, then the whole
	/// power bitmask: the common core of PennMUSH's <c>chown_object</c> (<c>src/set.c:333-339</c>) and
	/// <c>do_chzone</c> (<c>src/set.c:477-481</c>). WIZARD, ROYALTY and the built-in powers are the
	/// object's roles and overrides, so every one of those comes off, unconditionally as in PennMUSH.
	/// </summary>
	public static async ValueTask StripPrivilegeAsync(IMediator mediator, AnySharpObject target)
	{
		if (await target.HasFlag("TRUST") && await mediator.Send(new GetObjectFlagQuery("TRUST")) is { } trust)
		{
			await mediator.Send(new UnsetObjectFlagCommand(target, trust));
		}

		await mediator.Send(new ClearObjectGrantsCommand(target));
		// Materialized: the loop removes powers from the collection being read.
		foreach (var power in await target.Object().Powers.Value.ToArrayAsync())
		{
			await mediator.Send(new UnsetObjectPowerCommand(target, power));
		}
	}

	/// <summary>
	/// <c>chown_object</c>'s reset for a new owner (<c>src/set.c:333-339</c>): the privilege strip, and the
	/// object is left <c>HALT</c>ed. It is <c>clear_flag_internal</c> and <c>set_flag_internal</c>, so it asks no
	/// permission: the chown already passed, and its executor may control neither the object nor its new owner.
	/// </summary>
	public static async ValueTask ResetForNewOwnerAsync(IMediator mediator, AnySharpObject target)
	{
		await StripPrivilegeAsync(mediator, target);
		if (await mediator.Send(new GetObjectFlagQuery("HALT")) is { } halt)
		{
			await mediator.Send(new SetObjectFlagCommand(target, halt));
		}
	}
}
