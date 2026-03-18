using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Extensions;

public static class MUSHCodeParserExtensions
{
	public static TResult With<TResult>(this IMUSHCodeParser parser, Func<ParserState, ParserState> stateTransform, Func<IMUSHCodeParser, TResult> evaluate)
		=> evaluate(parser.Push(stateTransform(parser.CurrentState)));

	/// <summary>
	/// Parses attribute content with appropriate DEBUG/NODEBUG flag handling.
	/// Sets <see cref="ParserStateFlags.Debug"/> (≙ PennMUSH <c>QUEUE_DEBUG</c>) or
	/// <see cref="ParserStateFlags.NoDebug"/> (≙ PennMUSH <c>QUEUE_NODEBUG</c>) on the parser state
	/// based on the attribute's flags.
	/// </summary>
	/// <param name="parser">The parser instance</param>
	/// <param name="attribute">The attribute being evaluated</param>
	/// <param name="evaluate">Function to evaluate with the configured parser</param>
	/// <returns>Result of the evaluation</returns>
	public static TResult WithAttributeDebug<TResult>(
		this IMUSHCodeParser parser,
		SharpAttribute attribute,
		Func<IMUSHCodeParser, TResult> evaluate)
	{
		var flagDelta = GetAttributeDebugFlags(attribute);

		return parser.With(
			state => state with { Flags = (state.Flags & ~(ParserStateFlags.Debug | ParserStateFlags.NoDebug)) | flagDelta },
			evaluate);
	}

	/// <summary>
	/// Determines the <see cref="ParserStateFlags"/> Debug/NoDebug bits to apply for a given attribute.
	/// NODEBUG takes precedence over DEBUG, matching PennMUSH's <c>QUEUE_NODEBUG</c> / <c>QUEUE_DEBUG</c> priority.
	/// </summary>
	/// <param name="attribute">The attribute to check</param>
	/// <returns>
	/// <see cref="ParserStateFlags.NoDebug"/> if the attribute has NO_DEBUG,
	/// <see cref="ParserStateFlags.Debug"/> if it has DEBUG,
	/// <see cref="ParserStateFlags.None"/> if neither.
	/// </returns>
	private static ParserStateFlags GetAttributeDebugFlags(SharpAttribute attribute)
	{
		var flags = attribute.Flags.ToList();
		var hasNoDebug = flags.Any(f => f.Name.Equals("no_debug", StringComparison.OrdinalIgnoreCase));
		var hasDebug = flags.Any(f => f.Name.Equals("debug", StringComparison.OrdinalIgnoreCase));

		// NODEBUG takes precedence over DEBUG (matching PennMUSH QUEUE_NODEBUG priority)
		if (hasNoDebug)
			return ParserStateFlags.NoDebug;
		if (hasDebug)
			return ParserStateFlags.Debug;

		return ParserStateFlags.None;
	}
}