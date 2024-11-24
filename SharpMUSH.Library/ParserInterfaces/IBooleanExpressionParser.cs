using SharpMUSH.Library.DiscriminatedUnions;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IBooleanExpressionParser
{
		Func<AnySharpObject, AnySharpObject, bool> Compile(string text);
		bool Validate(string text, AnySharpObject lockee);
}