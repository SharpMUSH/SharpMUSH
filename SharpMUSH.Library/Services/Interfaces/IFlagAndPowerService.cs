using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Sets and clears an object's flags and powers, under PennMUSH's <c>can_set_flag</c> and
/// <c>set_power</c> rules.
/// </summary>
public interface IFlagAndPowerService
{
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
}
