using SharpMUSH.Implementation.Parsing;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Implementation.Visitors;

/// <summary>
/// The built-ins a parse of commands marks: the one <see cref="CommandDispatcher.FindBuiltIn"/> finds for
/// a name, by the way it splits its arguments with the switches written.
/// </summary>
/// <remarks>
/// A guess dispatch checks: a tree is kept for its text alone, so the command it runs may be another one,
/// by then or for the executor running it. Dispatch takes the marked arguments only when they split the
/// way the command it runs splits them.
/// </remarks>
internal sealed class BuiltInCommandHeads(IMUSHCodeParser parser, string? gameDialect) : ICommandHeads
{
	public int SplitToken(string name)
		=> !parser.State.IsEmpty && CommandDispatcher.FindBuiltIn(parser, name, gameDialect) is { Definition: { } definition } builtIn
			? CommandArgumentSplitter.SplitToken(CommandArgumentSplitter.SplitBehavior(definition, builtIn.Switches))
			: 0;
}
