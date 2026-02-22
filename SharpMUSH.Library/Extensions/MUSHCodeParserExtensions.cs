using SharpMUSH.Library.Models;
using SharpMUSH.Library.ParserInterfaces;

namespace SharpMUSH.Library.Extensions;

public static class MUSHCodeParserExtensions
{
	public static TResult With<TResult>(this IMUSHCodeParser parser, Func<ParserState, ParserState> stateTransform, Func<IMUSHCodeParser, TResult> evaluate)
		=> evaluate(parser.Push(stateTransform(parser.CurrentState)));

	/// <summary>
	/// Parses attribute content with appropriate DEBUG/NODEBUG flag handling.
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
		var debugOverride = GetAttributeDebugOverride(attribute);

		return parser.With(
			state => state with { AttributeDebugOverride = debugOverride },
			evaluate);
	}

	/// <summary>
	/// Determines attribute-level debug override from attribute flags.
	/// </summary>
	/// <param name="attribute">The attribute to check</param>
	/// <returns>null (no override), true (DEBUG), or false (NODEBUG takes precedence)</returns>
	private static bool? GetAttributeDebugOverride(SharpAttribute attribute)
	{
		var flags = attribute.Flags.ToList();
		var hasNoDebug = flags.Any(f => f.Name.Equals("no_debug", StringComparison.OrdinalIgnoreCase));
		var hasDebug = flags.Any(f => f.Name.Equals("debug", StringComparison.OrdinalIgnoreCase));

		// NODEBUG takes precedence over DEBUG
		if (hasNoDebug)
		{
			return false;
		}
		else if (hasDebug)
		{
			return true;
		}

		return null;
	}
}