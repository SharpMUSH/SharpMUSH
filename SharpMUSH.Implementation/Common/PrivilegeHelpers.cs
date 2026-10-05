using Mediator;
using SharpMUSH.Library;
using SharpMUSH.Library.Commands.Database;
using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Services.Interfaces;

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
	public static async ValueTask StripPrivilegeAsync(IMediator mediator, IFlagAndPowerService flagAndPowerService,
		AnySharpObject executor, AnySharpObject target)
	{
		if (await target.HasFlag("TRUST"))
		{
			await flagAndPowerService.SetOrUnsetFlag(executor, target, "!TRUST", false);
		}

		await mediator.Send(new ClearObjectGrantsCommand(target));
		await flagAndPowerService.ClearAllPowers(executor, target, false);
	}

	/// <summary>
	/// <c>chown_object</c>'s reset for a new owner (<c>src/set.c:333-339</c>): the privilege strip, and the
	/// object is left <c>HALT</c>ed.
	/// </summary>
	public static async ValueTask ResetForNewOwnerAsync(IMediator mediator, IFlagAndPowerService flagAndPowerService,
		AnySharpObject executor, AnySharpObject target)
	{
		await StripPrivilegeAsync(mediator, flagAndPowerService, executor, target);
		await flagAndPowerService.SetOrUnsetFlag(executor, target, "HALT", false);
	}
}
