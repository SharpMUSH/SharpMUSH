using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IBooleanExpressionParser
{
	Func<AnySharpObject, AnySharpObject, bool> Compile(string text);
	bool Validate(string text, AnySharpObject lockee);
	void InvalidateCache(string? text = null);

	/// <summary>
	/// Normalizes a lock expression to canonical form, resolving object names to dbrefs.
	/// PennMUSH resolves all lock targets (bare names, carry, owner, exact, indirect) to
	/// dbrefs at @lock time. The executor provides context for name resolution.
	/// </summary>
	/// <param name="text">The lock expression to normalize</param>
	/// <param name="executor">The object setting the lock, used as context for name resolution. If null, name resolution is skipped.</param>
	/// <returns>The normalized lock expression with names resolved to dbrefs</returns>
	string Normalize(string text, AnySharpObject? executor = null);
}
