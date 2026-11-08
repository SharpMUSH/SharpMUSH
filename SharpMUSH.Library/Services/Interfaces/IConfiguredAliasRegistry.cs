using SharpMUSH.Configuration.Options;

namespace SharpMUSH.Library.Services.Interfaces;

/// <summary>
/// Adds the aliases an <see cref="AliasOptions"/> names to a live command or function table, so an alias the
/// configuration gains while the server runs works at once and a restriction naming it finds it.
/// </summary>
/// <remarks>
/// Adding only: an alias already there for the same built-in is left as it is, one taken by something else is
/// logged and skipped, and an alias the configuration no longer names stays until the server restarts.
/// </remarks>
public interface IConfiguredAliasRegistry
{
	/// <returns>Whether any alias was added.</returns>
	bool RegisterConfiguredAliases(AliasOptions aliases);
}
