using SharpMUSH.Library.DiscriminatedUnions;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library;

/// <summary>
/// Global mail aliases (<c>@malias</c>). Names are one case-insensitive namespace; every permission rule
/// lives above the store, in the command layer, as it does in PennMUSH's <c>malias.c</c>.
/// </summary>
public interface IMailAliasStore
{
	/// <summary>Every alias, in the order it was created (PennMUSH lists its array in order).</summary>
	IAsyncEnumerable<SharpMailAlias> GetMailAliasesAsync(CancellationToken cancellationToken = default);

	/// <summary>Stores a new alias. <c>Error</c> when its name is taken.</summary>
	ValueTask<Result<SharpMailAlias>> CreateMailAliasAsync(SharpMailAlias alias, CancellationToken cancellationToken = default);

	/// <summary>
	/// Replaces the alias named <paramref name="name"/> with <paramref name="alias"/>, keeping its place in
	/// the list; a different name renames it. <c>Error</c> when there is no such alias or the new name is taken.
	/// </summary>
	ValueTask<Result<SharpMailAlias>> UpdateMailAliasAsync(string name, SharpMailAlias alias,
		CancellationToken cancellationToken = default);

	ValueTask<Found<None>> DeleteMailAliasAsync(string name, CancellationToken cancellationToken = default);

	ValueTask DeleteAllMailAliasesAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// PennMUSH <c>malias_cleanup</c>: a destroyed player leaves every alias it was on, and each alias it
	/// owned passes to <paramref name="newOwner"/>. One write, so no alias is seen half-cleaned.
	/// </summary>
	ValueTask ReleaseMailAliasesAsync(int player, int newOwner, CancellationToken cancellationToken = default);
}
