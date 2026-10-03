using SharpMUSH.Library;
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
	/// <c>do_chzone</c> (<c>src/set.c:477-481</c>).
	/// </summary>
	public static async ValueTask StripPrivilegeAsync(IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor, AnySharpObject target)
	{
		string[] privileged = ["WIZARD", "ROYALTY", "TRUST"];

		foreach (var flag in privileged)
		{
			if (await target.HasFlag(flag))
			{
				await manipulateSharpObjectService.SetOrUnsetFlag(executor, target, $"!{flag}", false);
			}
		}

		await manipulateSharpObjectService.ClearAllPowers(executor, target, false);
	}

	/// <summary>
	/// <c>chown_object</c>'s reset for a new owner (<c>src/set.c:333-339</c>): the privilege strip, and the
	/// object is left <c>HALT</c>ed.
	/// </summary>
	public static async ValueTask ResetForNewOwnerAsync(IManipulateSharpObjectService manipulateSharpObjectService,
		AnySharpObject executor, AnySharpObject target)
	{
		await StripPrivilegeAsync(manipulateSharpObjectService, executor, target);
		await manipulateSharpObjectService.SetOrUnsetFlag(executor, target, "HALT", false);
	}
}
