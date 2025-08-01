using System.Collections.Immutable;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Extensions;

public static class MUSHCodeParserExtensions
{
	public static TResult With<TResult>(this IMUSHCodeParser parser, Func<ParserState, ParserState> stateTransform, Func<IMUSHCodeParser, TResult> evaluate)
	{
		var tmpParser = parser.Push(stateTransform(parser.CurrentState));
		return evaluate(tmpParser);
	}
}