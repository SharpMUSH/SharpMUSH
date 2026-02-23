using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IBooleanExpressionParser
{
	Func<AnySharpObject, AnySharpObject, bool> Compile(string text);
	bool Validate(string text, AnySharpObject lockee);

	/// <summary>
	/// Normalizes a lock expression by converting bare dbrefs to objids.
	/// This ensures locks reference specific object instances and won't match recycled dbrefs.
	/// </summary>
	/// <param name="text">The lock expression to normalize</param>
	/// <returns>The normalized lock expression with objids instead of bare dbrefs</returns>
	string Normalize(string text);
}