using OneOf;
using SharpMUSH.Library.Models;

namespace SharpMUSH.Library.ParserInterfaces;

public interface IBooleanExpressionParser
{
	Func<OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing>, bool> Compile(string text);
	bool Validate(string text, OneOf<SharpPlayer, SharpRoom, SharpExit, SharpThing> lockee);
}