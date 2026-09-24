namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Applies the configured <c>command_restrictions</c> to the live command table.
/// </summary>
/// <remarks>
/// PennMUSH runs the <c>restrict_command</c> lines of <c>mush.cnf</c> through the same
/// <c>restrict_command()</c> that <c>@command/restrict</c> calls (the <c>restrict_command</c> branch
/// of <c>config_set</c>, <c>src/conf.c</c>). The translation from restriction words to a lock and
/// behaviour bits is private to the command implementation, and the attribute instances it mutates
/// belong to the command table, so the command library is the only thing that can apply them — this
/// is what lets a host say when, without knowing how.
/// </remarks>
public interface ICommandRestrictionApplier
{
	/// <param name="restrictions">Command name to its restriction words, as <c>command_restrictions</c> holds them.</param>
	ValueTask ApplyConfiguredRestrictionsAsync(IReadOnlyDictionary<string, string[]> restrictions);
}
