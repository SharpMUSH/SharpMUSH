using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

public interface IManipulateSharpObjectService
{
	ValueTask<CallState> SetName(AnySharpObject executor, AnySharpObject obj, MString name, bool notify);

	ValueTask<CallState> SetPassword(AnySharpObject executor, SharpPlayer player, string newPassword, bool notify);

	ValueTask<CallState> SetOrUnsetFlag(AnySharpObject executor, AnySharpObject obj, string flagOrFlagAlias, bool notify);

	/// <summary>Resolves a power by its name or one of its aliases, or null when no such power exists.</summary>
	ValueTask<SharpPower?> FindPower(string powerOrPowerAlias);

	/// <summary>
	/// Applies a whole <c>@power</c> right-hand side: wizard-only, one or more space-separated power names,
	/// each optionally prefixed with <c>!</c> to revoke rather than grant.
	/// </summary>
	ValueTask<CallState> SetOrUnsetPowers(AnySharpObject executor, AnySharpObject obj, string powerSpecification,
		bool notify);

	ValueTask<CallState> SetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias, bool notify);

	ValueTask<CallState> UnsetPower(AnySharpObject executor, AnySharpObject obj, string powerOrPowerAlias, bool notify);

	ValueTask<CallState> ClearAllPowers(AnySharpObject executor, AnySharpObject obj, bool notify);

	ValueTask<CallState> SetOwner(AnySharpObject executor, AnySharpObject obj, SharpPlayer newOwner, bool notify);

	ValueTask<CallState> SetParent(AnySharpObject executor, AnySharpObject obj, AnySharpObject newParent, bool notify);

	ValueTask<CallState> UnsetParent(AnySharpObject executor, AnySharpObject obj, bool notify);

	ValueTask<CallState> SetZone(AnySharpObject executor, AnySharpObject obj, AnySharpObject newZone, bool notify);

	ValueTask<CallState> UnsetZone(AnySharpObject executor, AnySharpObject obj, bool notify);
}